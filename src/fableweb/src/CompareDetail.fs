module CompareDetail
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Utils
open PublicModel.PerfReportModel
open Fable.PowerPack
open System.Globalization
open System
open System.Data.Common
open Fable.Import
open Fable.PowerPack.Json
open System.Collections.Generic
open Elmish
open Fable.Import.React
open Fable.Core.JsInterop
open PublicModel.PerfReportModel

let splitLastSegment (str: string) =
            let lastDot = str.LastIndexOf('.')
            if lastDot > 0 then str.Remove(lastDot), str.Substring(lastDot + 1)
            else "", str

[<RequireQualifiedAccess>]
type GridColumnValueGetter =
    | ResultValue of id: string
    | EnvironmentValue of id: string
    | ParameterValue of id: string
    | TaskName
    | TaskClass
    | TaskMethod
    | ScaledResultValue of baseGetter: GridColumnValueGetter

with
    member x.Eval (s: WorkerSubmission) =
        let someString a = Some (TestResultValue.Anything a)

        match x with
        | ResultValue col -> Map.tryFind col s.Results
        | EnvironmentValue col -> Map.tryFind col s.Environment |> Option.bind someString
        | ParameterValue col -> Map.tryFind col s.TaskParameters |> Option.bind someString
        | TaskName -> s.TaskName |> someString
        | TaskClass -> s.TaskName |> splitLastSegment |> fst |> splitLastSegment |> snd |> someString
        | TaskMethod -> s.TaskName |> splitLastSegment |> snd |> someString
        | ScaledResultValue col ->
            // Option.map2 (fun value scaler -> ) (value.Eval s) (scaler.Eval s)
            let value = col.Eval s
                        |> Option.bind(function | TestResultValue.Fraction (value, Some scaledCol) -> Some (value, scaledCol) | _ -> None)
                        |> Option.bind(fun (fraction, scaledCol) -> Map.tryFind scaledCol s.Results |> Option.bind (TestResultValue.TryScaleBy fraction))
            value

type GridColumnDescriptor = {
    Legend: string
    Title: string
    Getter: GridColumnValueGetter
}

type GridLayoutSettings = {
    Columns: GridColumnDescriptor[]
    InactiveColumns: GridColumnDescriptor[]
}
with
    static member Empty = { Columns = [||]; InactiveColumns = [||] }
    member x.AddColumnsFromData (data: WorkerSubmission[]) =
        let mapMap (ctor: string -> 'b) (theMap: Map<string, 'a>) : seq<string * 'b> =
            theMap |> Map.toSeq |> Seq.map fst |> Seq.distinct |> Seq.map (fun x -> x, ctor x)
        let baseCols = Seq.concat [
                             seq [
                                 "Task name", GridColumnValueGetter.TaskName
                                 "Task method", GridColumnValueGetter.TaskMethod
                                 "Task class", GridColumnValueGetter.TaskClass
                             ]
                             data |> Seq.collect (fun a -> a.Results |> mapMap GridColumnValueGetter.ResultValue)
                             data |> Seq.collect (fun a -> a.Environment |> mapMap GridColumnValueGetter.EnvironmentValue)
                             data |> Seq.collect (fun a -> a.TaskParameters |> mapMap GridColumnValueGetter.EnvironmentValue)
                         ] |> Seq.distinct |> Seq.toArray
        let columns = baseCols |> Seq.collect(fun (colId, getter) -> seq {
            yield { GridColumnDescriptor.Legend = colId; Title = colId; Getter = getter }

            let value = data |> Seq.tryPick getter.Eval
            match value with
            | Some (TestResultValue.Fraction (_, Some column)) ->
                let legend = sprintf "%s scaled by %s" column colId
                yield { GridColumnDescriptor.Legend = legend; Title = "Scaled " + colId; Getter = GridColumnValueGetter.ScaledResultValue getter }
            | _ -> ()
        })

        let thisColMap = Seq.append x.Columns x.InactiveColumns |> Set

        let newCols = columns |> Seq.filter (fun x -> not <| Set.contains x thisColMap) |> Seq.toArray

        { x with Columns = Array.append x.Columns newCols }

type ComparisonData = {
    Comparison: VersionComparisonSummary
    Base: BenchmarkReport []
    Target: BenchmarkReport []
    BaseDescription: ReportGroupDetails
    TargetDescription: ReportGroupDetails
}
with
    static member Load (vA, vB) =
        ApiClient.loadComparison vA vB |> Promise.map (fun (comparison, lbase, ltarget, dbase, dtarget) ->
            { Comparison = comparison; Base = lbase; Target = ltarget; BaseDescription = dbase; TargetDescription = dtarget }
        )

type Model = {
    Data: LoadableData<ComparisonData>
    GridSettings: GridLayoutSettings
}
with
    static member LiftDataMsg = UpdateMsg'.lift (fun x -> x.Data) (fun m x ->
        if m.Data = x then
            m
        else
            let rows =
                match x with
                | LoadableData.Loaded d -> Array.append d.Target d.Base
                | _ -> [||]
            let settings = m.GridSettings.AddColumnsFromData (rows |> Array.map (fun x -> x.Data))
            { m with Data = x; GridSettings = settings })
    static member LiftSettingsMsg = UpdateMsg'.lift (fun x -> x.GridSettings) (fun m x -> { m with GridSettings = x })
module SortableImport =
    let SortableContainer : System.Func<obj -> ReactElement, ComponentClass<obj>> = Fable.Core.JsInterop.import "SortableContainer" "react-sortable-hoc"
    let SortableElement : System.Func<obj -> ReactElement, ComponentClass<obj>> = Fable.Core.JsInterop.import "SortableElement" "react-sortable-hoc"
    let arrayMove : ('a [] -> int -> int -> 'a[])  = Fable.Core.JsInterop.import "arrayMove" "react-sortable-hoc"

open SortableImport
open PublicModel.PerfReportModel
open System.Data.Common
open PublicModel.PerfReportModel

let initState =
    let storedSettings =
        Browser.localStorage.getItem "detail-grid-layout" :?> string
        |> Option.ofObj
        |> Option.map (Fable.Core.JsInterop.ofJson)
        |> Option.defaultValue GridLayoutSettings.Empty
    { Data = LoadableData.Loading; GridSettings = storedSettings }

let viewComparisonSummary (model: VersionComparisonSummary) =
    let colNames = model.SummaryGroups |> Map.toSeq |> Seq.collect (fun (_key, v: PerfSummaryGroup) -> v.ColumnSummary |> Map.toSeq |> Seq.map fst) |> Seq.distinct |> Seq.toArray

    if colNames.Length = 0 then
        str ""
    else
        table [ ClassName "table" ] [
            thead [ ClassName "thead" ] [
                tr [] [
                    yield th [ ClassName "th" ] [ str "Group" ]

                    for col in colNames do
                        yield th [ ClassName "th" ] [ str col ]
                ]
            ]
            tbody [ ClassName "tbody" ] (
                model.SummaryGroups |> Map.toSeq |> Seq.sortBy fst |> Seq.map (fun (name, data) ->
                    tr [] [
                        yield td [ ClassName "td" ] [ str name ]

                        for col in colNames do
                            yield td [ ClassName "td" ] [ data.ColumnSummary.TryFind col |> Option.map (ProjectDashboard.viewRelativePerf) |> Option.defaultValue (str "") ]
                    ]
                ) |> Seq.toList
            )
        ]

let private cmpOptions = ComparisonOptions.Default

let private createMappingKey (s: WorkerSubmission) =
    s.TaskName, s.Environment |> cmpOptions.Environment.FilterMap, s.TaskParameters

let private viewValue =
    function
    | TestResultValue.Anything a -> str a
    | TestResultValue.Number (num, units) -> str (sprintf "%g%s" num (units |> Option.defaultValue ""))
    | x -> str (sprintf "%A" x)

let viewResultsGrid (tuples: (BenchmarkReport * BenchmarkReport) array) (settings: GridLayoutSettings) settingsDispatch =
    let rows = tuples |> Seq.map (fun (a, b) ->
        let cols = settings.Columns |> Array.map (fun c ->
            let colA = c.Getter.Eval a.Data
            let colB = c.Getter.Eval b.Data

            let content =
                match (colA, colB) with
                | (None, None) -> [ str "" ]
                | (Some a, None) -> [ viewValue a; str " (REMOVED)" ]
                | (None, Some a) -> [ viewValue a; str " (ADDED)" ]
                | (Some a, Some b) ->
                    match TestResultValue.GetComparable a, TestResultValue.GetComparable b with
                    | _ when a = b ->
                        [ viewValue a; ]
                    | (Some cmpA, Some cmpB) ->
                        [ viewValue b; str " "; ProjectDashboard.displayPercents (cmpA / cmpB) (sprintf "%g -> %g" cmpA cmpB) ]
                    | _ ->
                        [ viewValue a; str " => "; viewValue b ]
            td [ ClassName "td" ] content
        )

        tr [ ClassName "tr" ] (Seq.toList cols)
    )

    let removeColumn column =
        closeDropdown ()
        settingsDispatch (UpdateMsg (fun m ->
            let cols = m.Columns |> Array.except [ column ]
            { m with Columns = cols; InactiveColumns = Array.append [| column |] m.InactiveColumns }, Cmd.none))
    let swapColumns oldIndex newIndex =
        printfn "Moving from %d to %d" oldIndex newIndex
        closeDropdown ()
        settingsDispatch (UpdateMsg (fun m ->
            let cols = arrayMove m.Columns oldIndex newIndex
            { m with Columns = cols }, Cmd.none))

    let headerMenu (column: GridColumnDescriptor) =
        [
            p [] [ str column.Legend ]
            button [ ClassName "button"; OnClick (fun ev -> removeColumn column; ev.preventDefault()) ] [ str "Remove" ]
        ]

    let sortableHeaderItem =
        let headerItem col =
                span [] [ str col.Title ]
        SortableElement.Invoke(fun o -> headerItem (!!o?value))

    let headerRow (columns: GridColumnDescriptor []) =
        columns |> Seq.mapi (fun index col ->
            th [ ClassName "th"; Title (col.Title + " - " + col.Legend); Style [ TextOverflow "ellipsis" ] ] [
                createElement(
                    sortableHeaderItem,
                    createObj [
                        "key" ==> ("item-" + string index)
                        "value" ==> col
                        "index" ==> index
                    ], [||])
                Utils.dropDownLittleMenu (lazy (headerMenu col))
            ]
        )
        |> Seq.toList
        |> tr [ ClassName "tr" ]

    let sortableHeaders = SortableContainer.Invoke (fun o -> headerRow (!! o?items))

    table [ ClassName "table" ] [
        thead [ ClassName "thead" ] [
            createElement (
                sortableHeaders,
                createObj [
                    "items" ==> settings.Columns
                    "onSortEnd" ==> (fun data _mouseEvent -> swapColumns (!!data?oldIndex) (!!data?newIndex))
                    "lockAxis" ==> "x"
                    "axis" ==> "x"
                ], [||])
        ]
        tbody [ ClassName "tbody" ] (Seq.toList rows)
    ]

let viewSettingsPanel (model: GridLayoutSettings) dispatch =
    let addColumn col _ =
        dispatch (UpdateMsg (fun m ->
            let cols = m.InactiveColumns |> Array.except [ col ]
            if cols.Length = 0 then
                closeDropdown ()
            { m with InactiveColumns = cols; Columns = Array.append [| col |] m.Columns }, Cmd.none))

    let removeColumns filter _ =
        closeDropdown ()
        dispatch (UpdateMsg (fun m ->
            let filtered = m.Columns |> Array.filter filter
            let cols = m.Columns |> Array.except filtered
            { m with Columns = cols; InactiveColumns = Array.append filtered m.InactiveColumns }, Cmd.none))

    let colGroups = lazy [
        yield "All", fun _ -> true
        for c in model.Columns |> Seq.map (fun col -> col.Title |> splitLastSegment |> fst) |> Seq.distinct |> Seq.filter (fun x -> x <> "") do
            yield c + "... columns", fun col -> col.Title.StartsWith(c+".")
    ]

    div [] [
        Utils.dropDownMenu (
            button
                [ ClassName "button"; Disabled (model.InactiveColumns.Length = 0) ]
                [ str "Add column"; Utils.littleDropDownIcon ])
                    (lazy (
                            model.InactiveColumns |> Seq.map (fun col ->
                                button [ ClassName "button is-small"; Title col.Legend; OnClick (addColumn col) ] [ str col.Title ])
                                |> Seq.toList
                    ))
        Utils.dropDownMenu (
            button
                [ ClassName "button "; Disabled (model.Columns.Length = 0) ]
                [ str "Remove columns"; Utils.littleDropDownIcon ])
                    (lazy (
                            colGroups.Value |> Seq.map (fun (name, filter) ->
                                button [ ClassName "button is-small"; OnClick (removeColumns filter) ] [ str name ])
                                |> Seq.toList
                    ))
    ]

let viewGroupDetails =
    function
    | ReportGroupDetails.Commits commits ->
        div [ ClassName "box" ] [
            for commit in commits do
                yield div [ ClassName "content" ] [
                    p [] [
                        strong [] [ str commit.Author ]
                        str " "
                        commit.Signature |> Option.map (fun sign -> span [] [ str sign; faIcon "is-small has-text-success" "check" ]) |> Option.defaultValue (str "")
                        str " "
                        span [ Title (string commit.Time) ] [ str ((commit.Time |> box :?> string |> DateTime.Parse).ToShortDateString()) ]
                        str " "
                        small [] [ str commit.Hash ]

                        br []

                        str commit.Subject
                    ]
                ]
        ]
    | ReportGroupDetails.NoInfo -> div [] []

let viewData (verA, verB) (model: ComparisonData) gridSettings gridSettingsDispatch =
    let aMap = model.Base |> Seq.map (fun x -> createMappingKey x.Data, x) |> Map.ofSeq
    let pairs = model.Target |> Seq.choose (fun x -> Map.tryFind (createMappingKey x.Data) aMap |> Option.map (fun a -> a, x)) |> Seq.toArray
    div [] [
        (
            if model.BaseDescription = model.TargetDescription then
                viewGroupDetails model.BaseDescription
            else
                div [ ClassName "columns" ] [
                    div [ ClassName "column" ] [
                        h4 [ ClassName "subtitletitle is-4" ] [ str "Base" ]
                        viewGroupDetails model.BaseDescription
                    ]
                    div [ ClassName "column" ] [
                        h4 [ ClassName "subtitletitle is-4" ] [ str "Target" ]
                        viewGroupDetails model.TargetDescription
                    ]
                ]
        )

        (if model.BaseDescription <> model.TargetDescription then
            div [ ClassName "level" ] [
                div [ ClassName "level-item" ] [
                    a [ ClassName "button"; Href (Global.toHash (Global.Page.CompareDetail (verA, verA))) ] [ str "Detail" ]
                ]
                div [ ClassName "level-item" ] [
                    a [ ClassName "button"; Href (Global.toHash (Global.Page.CompareDetail (verB, verA))) ] [ str "Swap" ]
                ]
                div [ ClassName "level-item" ] [
                    a [ ClassName "button"; Href (Global.toHash (Global.Page.CompareDetail (verB, verB))) ] [ str "Detail" ]
                ]
            ]
        else
            str ""
        )

        viewComparisonSummary model.Comparison
        viewSettingsPanel gridSettings gridSettingsDispatch

        section [ ClassName "section is-fullwidth"; Style [ MaxWidth "90vw"; OverflowX "scroll" ] ] [
            viewResultsGrid pairs gridSettings gridSettingsDispatch
        ]
    ]

let view (verA, verB) (model: Model) dispatch =
    div [] [
        LoadableData'.display model.Data (Model.LiftDataMsg >> dispatch) (fun data _ -> viewData (verA, verB) data model.GridSettings (Model.LiftSettingsMsg >> dispatch)) (fun _ -> ComparisonData.Load (verA, verB))
    ]