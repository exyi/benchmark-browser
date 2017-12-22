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
open System.Data

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

[<RequireQualifiedAccessAttribute>]
type GridSortType =
    | Ascending
    | Descending
    | AscendingString
    | DescendingString
    | AscendingDifference
    | DescendingDifference
with
    member x.SwapDirection () =
        match x with
        | Ascending -> Descending
        | Descending -> Ascending
        | AscendingString -> DescendingString
        | DescendingString -> AscendingString
        | AscendingDifference -> DescendingDifference
        | DescendingDifference -> AscendingDifference


[<RequireQualifiedAccessAttribute>]
type GridFilterDescriptor =
    | Equals of allowed: TestResultValue [] * available: TestResultValue []
    | Range of float * float
with
    member x.Predicate value =
        match x with
        | Equals (allowed, _) -> Array.contains value allowed
        | Range(a, b) ->
            TestResultValue.GetComparable value |> Option.map (fun x -> a <= x && x <= b) |> Option.defaultValue false

type GridLayoutSettings = {
    Columns: GridColumnDescriptor[]
    InactiveColumns: GridColumnDescriptor[]
    SortOptions: (GridColumnDescriptor * GridSortType) option
    Filters: (GridColumnDescriptor * GridFilterDescriptor) []
}
with
    static member Empty = { Columns = [||]; InactiveColumns = [||]; SortOptions = None; Filters = [||] }
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

let private cmpOptions = ComparisonOptions.Default
let private createMappingKey (s: WorkerSubmission) =
    s.TaskName, s.Environment |> cmpOptions.Environment.FilterMap, s.TaskParameters

type ComparisonData(comparison, b, t, bDesc, tDesc) as x = class
    let preparedPairs = lazy (
        let aMap =
            x.Base
            |> Seq.groupBy (fun x -> createMappingKey x.Data)
            |> Seq.choose (fun (key, values) ->
                if Seq.length values <> 1 then None
                else Some (key, Seq.exactlyOne values)
            )
            |> Map.ofSeq
        x.Target |> Seq.choose (fun x -> Map.tryFind (createMappingKey x.Data) aMap |> Option.map (fun a -> a, x)) |> Seq.toArray
    )


    member x.Comparison: VersionComparisonSummary = comparison
    member x.Base: BenchmarkReport [] = b
    member x.Target: BenchmarkReport [] = t
    member x.BaseDescription: ReportGroupDetails = bDesc
    member x.TargetDescription: ReportGroupDetails = tDesc

    static member Load (vA, vB) =
        ApiClient.loadComparison vA vB |> Promise.map (fun (comparison, lbase, ltarget, dbase, dtarget) ->
            ComparisonData(comparison, lbase, ltarget, dbase, dtarget)
        )

    member x.GetPairs settings =
        preparedPairs.Value
        |> Array.filter (fun (row, _) ->
            settings.Filters |> Seq.forall (fun (col, filter) -> col.Getter.Eval row.Data |> Option.map filter.Predicate |> Option.defaultValue false)
        )
        |> (
            settings.SortOptions |> Option.map (fun (column, sortSettings) ->
                let getKey (rowA: TestResultValue) (rowB: TestResultValue) =
                    match sortSettings with
                    | GridSortType.Ascending | GridSortType.Descending -> TestResultValue.GetComparable rowA |> Option.defaultValue 0.0 :> IComparable
                    | GridSortType.AscendingString | GridSortType.DescendingString -> sprintf "%A" rowA :> IComparable
                    | GridSortType.AscendingDifference | GridSortType.DescendingDifference ->
                        Option.map2 (/) (TestResultValue.GetComparable rowA) (TestResultValue.GetComparable rowB) |> Option.defaultValue 0.0 :> IComparable

                let sortFn =
                    match sortSettings with
                    | GridSortType.Ascending | GridSortType.AscendingDifference | GridSortType.AscendingString -> Array.sortBy
                    | _ -> Array.sortByDescending

                sortFn (fun (rA, rB) -> Option.map2 getKey (column.Getter.Eval rA.Data) (column.Getter.Eval rB.Data))
            ) |> Option.defaultValue id
        )
end


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
open System.Data
open Fable.PowerPack.PromiseSeq

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

let private viewValue =
    let pickUnits (units: string[]) multiplier value =
        units
        |> Seq.mapi (fun index unit -> value / (multiplier ** float index), unit)
        |> Seq.filter (fun (v, _) -> v < 10000.0)
        |> Seq.tryHead
        |> Option.defaultWith (fun _ -> value / (multiplier ** float units.Length), Array.last units)


    function
    | TestResultValue.Anything a -> str a
    | TestResultValue.Number (num, units) -> str (sprintf "%g%s" num (units |> Option.defaultValue ""))
    | TestResultValue.Time (time) ->
        let micros = time.TotalMilliseconds * 1000.0
        let (value, units) = pickUnits [| "µs"; "ms"; "s" |] 1000.0 micros
        str (sprintf "%.4g%s" value units)
    | TestResultValue.ByteSize (bytes) ->
        let (value, units) = pickUnits [| "B"; "kiB"; "MiB"; "GiB"; "TiB" |] 1024.0 bytes
        str (sprintf "%.4g%s" value units)
    | TestResultValue.Fraction (fraction, frOf) ->
        let percent = fraction * 100.0
        span [ Title (sprintf "%.4g%% of %s" percent (frOf |> Option.defaultValue "")) ] [ str (sprintf "%.3g%%" percent) ]
    | x -> str (sprintf "%A" x)

let viewFileColumns isCompare (tuples: (BenchmarkReport * BenchmarkReport) array) =
    let getColumns =
        Seq.collect (fun x -> x.Data.Results |> Map.toSeq)
        >> Seq.filter (fun (_, v) -> match v with TestResultValue.AttachedFile _ -> true | _ -> false)
        >> Seq.groupBy fst
        >> Map.ofSeq
        >> Map.map (fun _k v -> v |> Seq.collect (fun (_, TestResultValue.AttachedFile (_file, tags)) -> tags) |> Seq.distinct)

    let aTags =
        tuples |> Seq.map fst |> getColumns
    let bTags = if isCompare then tuples |> Seq.map snd |> getColumns else Map.empty


    let columns = Seq.append (Map.toSeq aTags) (Map.toSeq bTags) |> Seq.map fst |> Seq.distinct

    let getRows (rows: BenchmarkReport seq) tagFilter column =
        rows
        |> Seq.choose (fun row ->
            match row.Data.Results.TryFind column with
            | Some (TestResultValue.AttachedFile (fileId, tags))
                when Array.except tags tagFilter |> Array.isEmpty ->
                    let bName = row.Data.TaskName
                    Some (bName, fileId)
            | _ -> None)
    let fileButton rows tagFilter column getUrl content =
        let rows = getRows rows tagFilter column |> Seq.toArray
        a [
            classList [
                "button", true
            ]
            Target "_blank"
            Disabled (Array.isEmpty rows)
            Title (sprintf "Aggregated %d files" rows.Length)
            Href (getUrl rows) ]
            content

    let hasTag tag col map =
        Map.tryFind col map
        |> Option.map (Seq.contains tag)
        |> Option.defaultValue false

    div [] [
        for col in columns do
            yield h3 [] [ strong [] [ str col ] ]
            if hasTag "stacks" col aTags then
                yield fileButton (Seq.map fst tuples) [| "stacks" |] col (ApiClient.getFlameGraphLocation << Seq.map snd) [ Utils.faIcon "is-small" "fire"; span [] [ str " Base Flamegraph" ] ]
            if hasTag "stacks" col bTags then
                yield fileButton (Seq.map snd tuples) [| "stacks" |] col (ApiClient.getFlameGraphLocation << Seq.map snd) [ Utils.faIcon "is-small" "fire"; span [] [ str " Target Flamegraph" ] ]
            yield fileButton (Seq.map fst tuples) [||] col (ApiClient.getFileArchiveLocation) [
                Utils.faIcon "is-small" "archive"; span [] [ str " Base Archive" ]
            ]
            yield fileButton (Seq.map snd tuples) [||] col (ApiClient.getFileArchiveLocation) [
                Utils.faIcon "is-small" "archive"; span [] [ str " Target Archive" ]
            ]
    ]

let viewFilterEditor allowDeleteButton filter updateFilter = seq {
    match filter with
    | GridFilterDescriptor.Equals (values, moreValues) ->
        let setFilterEvent value (ev: FormEvent) =
            ev.preventDefault()
            let newFilter =
                if Array.contains value values then
                    GridFilterDescriptor.Equals (Array.except [|value|] values, Array.append [| value |] moreValues)
                else
                    GridFilterDescriptor.Equals (Array.append [|value|] values, Array.except [| value |] moreValues)
            updateFilter (Some newFilter)

        if allowDeleteButton then
            yield button [ ClassName "button is-danger"; OnClick (fun _ -> updateFilter None) ] [ Utils.faIcon "is-small" "times"; str "Remove" ]

        let allValues = Array.append values moreValues |> Array.sortBy (sprintf "%A")
        for v in allValues do
            yield label [ ClassName "checkbox" ] [
                    input [ HTMLAttr.Type "checkbox"; OnChange (setFilterEvent v); Checked (Array.contains v values) ]
                    viewValue v
                ]
    | GridFilterDescriptor.Range _ as f ->
        yield str (sprintf "%A" f)
}

let private setSettingsFilter settingsDispatch column replaceFilter value =
    settingsDispatch (UpdateMsg (fun m ->
        let newFilters = m.Filters |> Array.except [| (column, replaceFilter) |] |> Array.append (match value with Some value -> [| (column, value) |] | _ -> Array.empty)
        if value.IsNone then
            closeDropdown()
        { m with Filters = newFilters }, Cmd.none
    ))

let private cellCache = Dictionary<TestResultValue option * TestResultValue option, ReactElement>()
let mutable private lastUrl = ""
let private renderValue a b =
    match cellCache.TryGetValue((a, b)) with
    | (true, e) -> e
    | (false, _) ->
        let content =
            match (a, b) with
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
        let cell = td [ ClassName "td" ] content
        cellCache.Add((a, b), cell)
        cell

let viewResultsGrid (tuples: (BenchmarkReport * BenchmarkReport) array) (settings: GridLayoutSettings) settingsDispatch =
    if Fable.Import.Browser.location.href <> lastUrl then
        lastUrl <- Fable.Import.Browser.location.href
        cellCache.Clear()
    let gridRow =
        Elmish.React.Common.lazyView (fun (a : BenchmarkReport, b: BenchmarkReport) ->
            let cols = settings.Columns |> Array.map (fun c ->
                let colA = c.Getter.Eval a.Data
                let colB = c.Getter.Eval b.Data
                renderValue colA colB
            )
            tr [ ClassName "tr" ] (Seq.toList cols)
        )

    let rows = tuples |> Seq.map (gridRow)

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
        let canCompare a = column.Getter.Eval a.Data |> Option.bind TestResultValue.GetComparable |> Option.isSome
        let allowDiffSort =
            tuples |> Seq.exists (fun (a, b) -> canCompare a && canCompare b)
        let allowNumSort =
            tuples |> Seq.exists (fun (a, b) -> canCompare a || canCompare b)
        let allowAnySort =
            (tuples |> Seq.map fst |> Seq.distinct |> Seq.length) > 1
        let realFilters = settings.Filters |> Array.filter (fun (col, filter) -> col = column)
        let filterableItems =
            tuples
            |> Array.choose (fun (x, _) -> column.Getter.Eval x.Data)
            |> Array.groupBy id
            |> Array.filter (fun (key, values) -> values.Length > 1)
            |> Array.sortBy (fun (key, _) -> sprintf "%A" key)
            // [|
            //     if filterableItems.Length then
            //         yield GridFilterDescriptor.Equals([||], [|  |])
            // |]
        let viewSortButtons sortUp sortDown =
            let setSortClick stype (ev: MouseEvent) =
                ev.preventDefault()
                closeDropdown()
                settingsDispatch(UpdateMsg (fun m ->
                    { m with SortOptions = Some (column, stype) }, Cmd.none
                ))
            span [ ClassName "buttons has-addons" ] [
                button [ ClassName "button is-small is-rounded is-primary"; OnClick (setSortClick sortDown) ] [ faIcon "" "angle-down" ]
                button [ ClassName "button is-small is-rounded is-primary"; OnClick (setSortClick sortUp) ] [ faIcon "" "angle-up" ]
            ]
        let setFilter = setSettingsFilter settingsDispatch column
        [
            yield p [] [ str column.Legend ]
            yield button [ ClassName "button"; OnClick (fun ev -> removeColumn column; ev.preventDefault()) ] [ str "Remove" ]

            // sort options
            if allowDiffSort then
                yield p [] [ str "Sort by difference: "; viewSortButtons GridSortType.AscendingDifference GridSortType.DescendingDifference ]
            if allowAnySort then
                if allowNumSort then
                    yield p [] [ str "Sort by value: "; viewSortButtons GridSortType.Ascending GridSortType.Descending ]
                else
                    yield p [] [ str "Sort by string: "; viewSortButtons GridSortType.AscendingString GridSortType.DescendingString ]

            // filter options
            if realFilters.Length >= 1 then
                yield p [] [ str "Filter:" ]
                for (_, filter) in realFilters do
                    yield! viewFilterEditor true filter (setFilter filter)

            else if filterableItems.Length > 0 then
                yield p [] [ str "Add Filter: " ]
                let filter = GridFilterDescriptor.Equals ([||], filterableItems |> Array.map fst)
                yield! viewFilterEditor false filter (setFilter filter)
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

let viewSettingsPanel (data: ComparisonData) (model: GridLayoutSettings) dispatch =
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
        yield Utils.dropDownMenu (
            button
                [ ClassName "button"; Disabled (model.InactiveColumns.Length = 0) ]
                [ str "Add column"; Utils.littleDropDownIcon ])
                    (lazy (
                            model.InactiveColumns |> Seq.map (fun col ->
                                button [ ClassName "button is-small"; Title col.Legend; OnClick (addColumn col) ] [ str col.Title ])
                                |> Seq.toList
                    ))
        yield Utils.dropDownMenu (
            button
                [ ClassName "button "; Disabled (model.Columns.Length = 0) ]
                [ str "Remove columns"; Utils.littleDropDownIcon ])
                    (lazy (
                            colGroups.Value |> Seq.map (fun (name, filter) ->
                                button [ ClassName "button is-small"; OnClick (removeColumns filter) ] [ str name ])
                                |> Seq.toList
                    ))

        yield Utils.dropDownMenu
            (button [ ClassName "button" ] [ str "Attached files"; Utils.littleDropDownIcon ])
            (lazy ([viewFileColumns (data.BaseDescription <> data.TargetDescription) (data.GetPairs model)]))

        for (column, filter) in model.Filters do
            yield Utils.dropDownMenu
                (button [ ClassName "button is-link" ] [ str ("Filter " + column.Title); Utils.littleDropDownIcon ])
                (lazy (List.ofSeq <| viewFilterEditor true filter (setSettingsFilter dispatch column filter)))

        match model.SortOptions with
        | None -> ()
        | Some (col, sort) ->
            let swapSortDirection (ev: MouseEvent) =
                dispatch (UpdateMsg (fun m ->
                    { m with SortOptions = Some (col, sort.SwapDirection()) }, Cmd.none
                ))
            let removeSort (ev: MouseEvent) =
                dispatch (UpdateMsg (fun m ->
                    { m with SortOptions = None }, Cmd.none
                ))
            yield span [ ClassName "buttons has-addons" ] [
                span [ ClassName "button is-disabled is-primary"; Title (sprintf "%A" sort) ] [ str col.Title ]
                span [ ClassName "button is-primary"; OnClick swapSortDirection; Title "Swap sort direction" ] [
                    (match sort with
                     | GridSortType.Ascending | GridSortType.AscendingString | GridSortType.AscendingDifference -> faIcon "" "angle-up"
                     | _ -> faIcon "" "angle-down"
                    )
                ]
                span [ ClassName "button is-primary"; OnClick removeSort; Title "Remove sort" ] [
                    faIcon "" "times"
                ]
            ]

    ]

let viewGroupDetails =
    function
    | ReportGroupDetails.Commits commits ->
        div [ ClassName "box" ] [
            for commit in commits do
                yield div [ ClassName "content" ] [
                    viewCommitInfo commit
                ]
        ]
    | ReportGroupDetails.NoInfo -> div [] []

let viewData (verA, verB) (model: ComparisonData) (gridSettings: GridLayoutSettings) gridSettingsDispatch =
    let pairs = model.GetPairs gridSettings
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
                    a [ ClassName "button"; Href (Global.toHash (Global.Page.CompareDetail (verA, verA))); Title "Go to detail of the Base version" ] [ str "Detail" ]
                ]
                div [ ClassName "level-item" ] [
                    a [ ClassName "button"; Href (Global.toHash (Global.Page.CompareDetail (verB, verA))); Title "Swap the Base and Target version" ] [ str "Swap" ]
                ]
                div [ ClassName "level-item" ] [
                    a [ ClassName "button"; Href (Global.toHash (Global.Page.CompareDetail (verB, verB))); Title"Go to detail of the Target version" ] [ str "Detail" ]
                ]
            ]
        else
            str ""
        )

        viewComparisonSummary model.Comparison
        viewSettingsPanel model gridSettings gridSettingsDispatch

        section [ ClassName "section is-fullwidth"; Style [ MaxWidth "90vw"; OverflowX "scroll" ] ] [
            viewResultsGrid pairs gridSettings gridSettingsDispatch
        ]
    ]

let view (verA, verB) (model: Model) dispatch =
    div [] [
        LoadableData'.display model.Data (Model.LiftDataMsg >> dispatch) (fun data _ -> viewData (verA, verB) data model.GridSettings (Model.LiftSettingsMsg >> dispatch)) (fun _ -> ComparisonData.Load (verA, verB))
    ]