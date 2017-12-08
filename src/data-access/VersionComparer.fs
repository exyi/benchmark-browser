module VersionComparer
open PublicModel.PerfReportModel
open System

[<RequireQualifiedAccessAttribute>]
type IncludeOrExcludeOption =
    | IncludeExcept of string []
    | ExcludeExcept of string []
with
    static member IncludeAll = IncludeExcept [||]
    static member ExcludeAll = ExcludeExcept [||]
    member x.FilterMap map =
        match x with
        // | IncludeOrExcludeOption.ExcludeAll -> Map.empty
        // | IncludeOrExcludeOption.IncludeAll -> map
        | IncludeExcept a -> Map.filter (fun k _ -> not <| Array.contains k a) map
        | ExcludeExcept a -> Map.filter (fun k _ -> Array.contains k a) map
type ComparisonOptions = {
    Environment: IncludeOrExcludeOption
}
with
    static member Default = { ComparisonOptions.Environment = IncludeOrExcludeOption.IncludeExcept [||] }

let private getGroups allPairs =
    let byTestName = allPairs |> Array.groupBy (fun (a, b) -> assert (a.TaskName = b.TaskName); a.TaskName)
    let byTestClass = allPairs |> Array.groupBy (fun (a, _) -> let indexOf = a.TaskName.LastIndexOf '.' in a.TaskName.Remove(indexOf))

    Array.concat [
        [| "", allPairs |]
        byTestName |> Array.map (fun (n, x) -> "Test - " + n, x) |> Array.filter (fun (_, x) -> x.Length > 1)
        byTestClass |> Array.map (fun (n, x) -> "Class - " + n, x)
    ]
    |> Map.ofArray

let compareVersions (options: ComparisonOptions) (a: WorkerSubmission seq) (b: WorkerSubmission seq) =
    let versionA = String.Join("-", a |> Seq.map(fun a -> a.ProjectVersion) |> Seq.distinct), Seq.length a
    let versionB = String.Join("-", b |> Seq.map(fun b -> b.ProjectVersion) |> Seq.distinct |> Seq.toArray), Seq.length b

    let createMappingKey (s: WorkerSubmission) =
        s.TaskName, s.Environment |> options.Environment.FilterMap, s.TaskParameters

    let aMap = a |> Seq.map (fun x -> createMappingKey x, x) |> Map.ofSeq
    let pairs = b |> Seq.choose (fun x -> Map.tryFind (createMappingKey x) aMap |> Option.map (fun a -> a, x)) |> Seq.toArray
    let groups = getGroups pairs

    let computeBasicStats s =
        let s = Seq.toArray s
        { CommitRelativePerformance.AvgTime = Array.average s; MinTime = Array.min s; MaxTime = Array.max s; Count = Array.length s }

    let computeSummary getTheNumber pairs =
        // compute avg of relative change
        pairs |> Seq.choose (fun (a, b) -> Option.map2 (/) (getTheNumber a) (getTheNumber b)) |> computeBasicStats
    let columns =

        let allCols = pairs |> Seq.collect(fun (a, b) -> [a; b]) |> Seq.collect (fun a -> a.Results |> Map.toSeq |> Seq.map fst) |> Seq.distinct |> Seq.filter (fun a -> a.StartsWith("Column.")) |> Seq.toList

        let getResultCol name (s: WorkerSubmission) =
            Map.tryFind name s.Results |> Option.bind (TestResultValue.GetComparable)


        [
            "Time", getResultCol "Statistics.Median"
            "Memory", getResultCol "Memory.BytesAllocatedPerOperation"
        ] |> List.append (allCols |> List.map (fun a -> a, getResultCol a))
    let summaryGroups = groups |> Map.map (fun _groupName pairs ->
            let s = Seq.map (fun (name, fn) -> name, computeSummary fn pairs) columns
            { PerfSummaryGroup.ColumnSummary = s |> Map.ofSeq }
    )

    {
        VersionComparisonSummary.CommitA = versionA
        CommitB = versionB
        SummaryGroups = summaryGroups
        NewTests = b |> Seq.map createMappingKey |> Seq.except (a |> Seq.map createMappingKey) |> Seq.map (sprintf "%A") |> Seq.toArray
        RemovedTests = a |> Seq.map createMappingKey |> Seq.except (b |> Seq.map createMappingKey) |> Seq.map (sprintf "%A") |> Seq.toArray
    }
