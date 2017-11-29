module PublicModel.WorkerModel
open System
open ProjectManagement
open System

[<RequireQualifiedAccessAttribute>]
type WorkState =
    | WorkingOnIt
    | Done
    | Failed of string

[<RequireQualifiedAccessAttribute>]
type LogMessageType = Default | Help | Header | Result | Statistic | Info | Error | Hint

type WorkStatusInfo = {
    TaskId: Guid
    TimeFromStart: TimeSpan
    LogMessages: (string * LogMessageType) array
    State: WorkState
}

type VersionSpecifier =
    | Latest
    | GitVersion of string
with member x.ToVersionString() =
            match x with
            | Latest -> "HEAD"
            | GitVersion str -> str

type WorkerQueueItemFormModel = {
    TestDefId: string
    ProjectVersion: VersionSpecifier
    BenchmarkerVersion: VersionSpecifier
}

type TaskSpecification = {
    // Id: Guid
    DefinitionId: Guid
    Definition: TestDefinition
    BuildScriptVersion: string
    ProjectVersion: string
}

[<CLIMutableAttribute>]
type TaskStatusUpdateEntity = {
    Id: Guid
    Info: WorkStatusInfo
}

[<CLIMutableAttribute>]
type WorkerQueueItem = {
    Id: Guid
    Task: TaskSpecification
    Priority: float
    mutable LastUpdate: DateTime
    mutable IsResolved: bool
}