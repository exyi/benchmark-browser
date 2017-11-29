module PublicModel.PerfReportModel

open System
open System
open System
open System

[<RequireQualifiedAccessAttribute>]
type TestResultValue =
    | Time of TimeSpan
    /// (Memory) size as a number of bytes
    | ByteSize of int64
    /// Any number with optional units
    | Number of (float * string option)
    /// Fraction of something, with optional specifier of the target. The 54% of execution time should be represented as (0.54, Some "AvgTime")
    | Fraction of (float * string option)
    | Anything of string
    | AttachedFile of Guid * string

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