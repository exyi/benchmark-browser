module PublicModel.WorkerModel
open System
open ProjectManagement

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

type TaskSpecification = {
    Id: Guid
    ProjectId: Guid
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
    LastUpdate: DateTime
}