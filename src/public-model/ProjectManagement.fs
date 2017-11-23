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

type TestExecutionMethod =
    | ExecScript of exec: string
    | DotnetRun of BuildOptions * arguments: string
    | CargoBench of BuildOptions

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
    Name: string
    Definition: TestDefinition
}

[<CLIMutableAttribute>]
type TestDefEntity = {
    Id: Guid
    Name: string
    OwnerId: Guid
    TestDefinition: TestDefinition
}

type ProjectListItem = {
    Id: Guid
    Name: string
    ProjectRepo: string
    ReportCount: int
    TasksRun: int
    TasksQueued: int
}