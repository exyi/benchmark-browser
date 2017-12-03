module PublicModel.ProjectManagement
open System

type DisplayProjectInfo = {
    Id: Guid
    Name: string
}

[<RequireQualifiedAccessAttribute>]
type ResultsProcessor =
    | BenchmarkDotNet
    | StdOutJson

[<RequireQualifiedAccessAttribute>]
type ProjectRepositoryCloneUrl =
    | IsSubmodule
    | CloneFrom of string

type BuildOptions = {
    ProjectPath: string
    Configuration: string
    PreprocessorParameters: string
    UseCommitPreprocParams: bool
}

[<RequireQualifiedAccessAttribute>]
type TestExecutionMethod =
    | ExecScript of exec: string
    | DotnetRun of BuildOptions * arguments: string
    // | CargoBench of BuildOptions

type TestDefinition = {
    BenchmarksRepository: string
    /// Command relative to BenchmarksRepository that will launch the run
    BuildScriptCommand: TestExecutionMethod
    ProjectRepository: ProjectRepositoryCloneUrl
    /// Path relative to BenchmarksRepository, where the project should be located
    ProjectRepoPath: string
    ResultsProcessor: ResultsProcessor
}

type TestDefFormModel = {
    Title: string
    FriendlyId: string
    Definition: TestDefinition
}

[<CLIMutableAttribute>]
type TestDefEntity = {
    Id: Guid
    FriendlyId: string
    Title: string
    OwnerId: Guid
    TestDefinition: TestDefinition
}

type TaskDefListItem = {
    Id: Guid
    FriendlyId: string
    Name: string
    ProjectRepo: string
    ReportCount: int
    TasksRun: int
    TasksQueued: int
}

type ResultProjectListItem = {
    RootCommit: string
    CloneUrls: string []
    ReportCount: int
    TasksRun: int
    VersionsTested: int
    TestDefinitionCount: int
}

type TestRunListModel = {
    // QueueItemId: Guid
    Date: DateTime
    TaskFriendlyName: string
    TaskDefId: Guid
    Reports: int
    ProjectVersion: string
}

type DashboardModel = {
    TaskDefinitions: TaskDefListItem []
    DetailedTestDef: TestDefEntity option
    Projects: ResultProjectListItem []
    FewRecentTestRuns: TestRunListModel []
}

type HomePageModel = {
    TaskDefinitions: TaskDefListItem []
    Projects: ResultProjectListItem []
    FewRecentTestRuns: TestRunListModel []
}