module PublicModel.PerfReportModel

open System
open System
open System
open System

[<RequireQualifiedAccessAttribute>]
type TestResultValue =
    | Time of TimeSpan
    /// (Memory) size as a number of bytes
    | ByteSize of float
    /// Any number with optional units
    | Number of (float * string option)
    /// Fraction of something, with optional specifier of the target. The 54% of execution time should be represented as (0.54, Some "AvgTime")
    | Fraction of (float * string option)
    | Anything of string
    | AttachedFile of Guid * string

with
    static member GetComparable =
        function
        | Time s -> Some s.TotalMilliseconds
        | ByteSize s -> Some s
        | Number (a, _) -> Some a
        | Fraction (a, _) -> Some a
        | _ -> None

    static member TryScaleBy (num: float) =
        function
        | Time s -> TimeSpan(float s.Ticks * num |> int64) |> Time |> Some
        | ByteSize s -> s * num |> ByteSize |> Some
        | Number (a, units) -> Number (a * num, units) |> Some
        | Fraction (a, frOf) -> Fraction (a * num, frOf) |> Some
        | _ -> None

[<CLIMutableAttribute>]
type FieldExplanationEntity = {
    Id: string
    Legend: string
    Categories: string []
}

/// Data for one test run sumbitted by a worker
type WorkerSubmission = {
    /// Id of the related TaskDefinition
    DefinitionId: Guid
    /// (Approximate) time when the results were computed
    DateComputed: DateTime
    /// Name of the benchmark method, should be unique in one project
    TaskName: string
    /// Parameters of the benchmark
    TaskParameters: Map<string, string>
    /// Git version of the measured repository
    ProjectVersion: string
    /// Git clone url of measuered repository
    ProjectCloneUrl: string
    /// Root commit of the measured repository used as a project identifier
    ProjectRootCommit: string
    /// Git version of build-repository
    BuildSystemVersion: string
    /// Stuff like OS, Hardware, .NET Version and so on
    Environment: Map<string, string>
    /// The measured values
    Results: Map<string, TestResultValue>
}

// type BulkSubmissionType =
//     | BenchmarkDotNetJson

// type BulkWorkerSubmission = {
//     ProjectId: Guid
//     DateComputed: DateTime
//     ProjectVersion: string
//     BuildSystemVersion: string
//     BulkSubmissionType
// }

[<RequireQualifiedAccessAttribute>]
type ImportResult =
    | Ok of Guid
    | ProjectDoesNotExists of Guid
    | SomeError of string

[<CLIMutable>]
type BenchmarkReport = {
    Id: Guid
    DateSubmitted: DateTime
    WorkerId: Guid
    Data: WorkerSubmission
}

type CommitRelativePerformance = {
    AvgTime: float
    MaxTime: float
    MinTime: float
    Count: int
}

type ProjectPerfSummary = {
    // List of name * (graph data = commit * numbers)
    DetailedBranches: (string * (string * CommitRelativePerformance) []) []
    // List of name * commit * numbers
    HeadOnlyBranches: (string * string * CommitRelativePerformance) []
}

type PerfSummaryGroup = {
    ColumnSummary: Map<string, CommitRelativePerformance>
    // IngoredCount: int
}

type VersionComparisonSummary = {
    CommitA: string * int
    CommitB: string * int
    SummaryGroups: Map<string, PerfSummaryGroup>
    NewTests: string []
    RemovedTests: string []
}
with
    static member IdentityCompare commit count = { CommitA = commit, count; CommitB = commit, count; SummaryGroups = Map.ofSeq [ ]; NewTests = [||]; RemovedTests = [||] }

[<RequireQualifiedAccessAttribute>]
type ReportGroupSelector =
    | Version of string

type GitCommitInfo = {
    Hash: string
    Parents: string[]
    Signature: string option
    Author: string
    Time: DateTime
    Subject: string
}

[<RequireQualifiedAccessAttribute>]
type ReportGroupDetails =
    | Commits of GitCommitInfo []
    | NoInfo


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


