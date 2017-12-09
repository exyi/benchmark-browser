module CompareDetail
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Utils
open PublicModel.PerfReportModel
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
        let splitLastSegment (str: string) =
            let lastDot = str.LastIndexOf('.')
            if lastDot > 0 then str.Remove(lastDot), str.Substring(lastDot + 1)
            else "", str

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
    Base: WorkerSubmission []
    Target: WorkerSubmission []
}
with
    static member Load (vA, vB) =
        ApiClient.loadComparison vA vB |> Promise.map (fun (comparison, lbase, ltarget) ->
            { Comparison = comparison; Base = lbase; Target = ltarget }
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
            let settings = m.GridSettings.AddColumnsFromData (rows)
            { m with Data = x; GridSettings = settings })
    static member LiftSettingsMsg = UpdateMsg'.lift (fun x -> x.GridSettings) (fun m x -> { m with GridSettings = x })
module SortableImport = 
    let SortableContainer : System.Func<obj -> ReactElement, ComponentClass<obj>> = Fable.Core.JsInterop.import "SortableContainer" "react-sortable-hoc"
    let SortableElement : System.Func<obj -> ReactElement, ComponentClass<obj>> = Fable.Core.JsInterop.import "SortableElement" "react-sortable-hoc"
    let arrayMove : ('a [] -> int -> int -> 'a[])  = Fable.Core.JsInterop.import "arrayMove" "react-sortable-hoc"

open SortableImport

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

let viewResultsGrid (tuples: (WorkerSubmission * WorkerSubmission) array) (settings: GridLayoutSettings) settingsDispatch =
    let rows = tuples |> Seq.map (fun (a, b) ->
        let cols = settings.Columns |> Array.map (fun c ->
            let colA = c.Getter.Eval a
            let colB = c.Getter.Eval b

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
            th [ ClassName "th"; Title col.Legend ] [
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
                    "lockAxis" ==> true
                    "axis" ==> "x"
                ], [||])
        ]
        tbody [ ClassName "tbody" ] (Seq.toList rows)
    ]

let viewData (model: ComparisonData) gridSettings gridSettingsDispatch =
    let aMap = model.Base |> Seq.map (fun x -> createMappingKey x, x) |> Map.ofSeq
    let pairs = model.Target |> Seq.choose (fun x -> Map.tryFind (createMappingKey x) aMap |> Option.map (fun a -> a, x)) |> Seq.toArray
    div [] [
        viewComparisonSummary model.Comparison
        div [ Style [ MaxWidth "90vw"; OverflowX "scroll" ] ] [
            viewResultsGrid pairs gridSettings gridSettingsDispatch
        ]
    ]

let view (verA, verB) model dispatch =
    let isComparison = verA <> verB

    let displayGroupSpec = function
                           | ReportGroupSelector.Version ver -> str ("Version " + ver)

    div [] [
        h1 [ ClassName "title" ] (
            if isComparison then
                [ str "Comparison of "; displayGroupSpec verA; str " and "; displayGroupSpec verB ]
            else
                [ str "Detail of "; displayGroupSpec verA ]
        )

        LoadableData'.display model.Data (Model.LiftDataMsg >> dispatch) (fun data _ -> viewData data model.GridSettings (Model.LiftSettingsMsg >> dispatch)) (fun _ -> ComparisonData.Load (verA, verB))
    ]