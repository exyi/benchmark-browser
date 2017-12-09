module CompareDetail
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Utils
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open Fable.PowerPack
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open System.Globalization
open PublicModel.PerfReportModel
open System
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open Fable.PowerPack.Experimental.IndexedDB
open System.Data.Common

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
    wtf: string
    Data: LoadableData<ComparisonData>
}
with
    static member LiftDataMsg = UpdateMsg'.lift (fun x -> x.Data) (fun m x -> { m with Data = x })

let initState = { wtf = ""; Data = LoadableData.Loading }

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

let viewRows (tuples: (WorkerSubmission * WorkerSubmission) array) =
    let columns = tuples |> Seq.collect (fun (a, b) -> [a; b]) |> Seq.collect (fun a -> a.Results |> Map.toSeq |> Seq.map fst) |> Seq.distinct |> Seq.toArray
    let rows = tuples |> Seq.map (fun (a, b) ->
        let cols = columns |> Array.map (fun c ->
            let colA = a.Results.TryFind c
            let colB = b.Results.TryFind c

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

        let cols = cols |> Seq.append [
                       th [ ClassName "th" ] [ str a.TaskName ]
                   ]

        tr [ ClassName "tr" ] (Seq.toList cols)
    )

    table [ ClassName "table" ] [
        thead [ ClassName "thead" ] [
            tr [ ClassName "tr" ] [
                yield th [ ClassName "th" ] []
                for col in columns do
                    yield th [ ClassName "th" ] [ str col ]
            ]
        ]
        tbody [ ClassName "tbody" ] (Seq.toList rows)
    ]

let viewData (model: ComparisonData) dispatch =
    let aMap = model.Base |> Seq.map (fun x -> createMappingKey x, x) |> Map.ofSeq
    let pairs = model.Target |> Seq.choose (fun x -> Map.tryFind (createMappingKey x) aMap |> Option.map (fun a -> a, x)) |> Seq.toArray
    div [] [
        viewComparisonSummary model.Comparison
        div [ Style [ MaxWidth "90vw"; OverflowX "scroll" ] ] [
            viewRows pairs
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

        LoadableData'.display model.Data (Model.LiftDataMsg >> dispatch) viewData (fun _ -> ComparisonData.Load (verA, verB))
    ]