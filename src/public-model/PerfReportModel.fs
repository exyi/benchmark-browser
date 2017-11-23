module PublicModel.PerfReportModel

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

type WorkerSubmission = {
    ProjectId: Guid
    DateComputed: DateTime
    TaskName: string
    TaskParameters: Map<string, string>
    ProjectVersion: string
    BuildSystemVersion: string
    /// Stuff like OS, Hardware, .NET Version and so on
    Environment: Map<string, string>
    Results: Map<string, TestResultValue>
}

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