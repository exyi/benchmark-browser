module Admin.Global
open System
open Utils
open Elmish
open PublicModel.ProjectManagement
open PublicModel.ProjectManagement

[<RequireQualifiedAccessAttribute>]
type AdminPage =
    | NewProject
    | EditProject of string

type EditProjectModel = {
    Id: Option<Guid>
    Form: PublicModel.ProjectManagement.TestDefFormModel
    UIMessage: UIMessage
}

type AdminModel = {
    NewProject: EditProjectModel
    EditProject: Option<EditProjectModel>
}

let initEditProjectForm =
    {
        Title = ""
        FriendlyId = ""
        Definition =
        {
            BenchmarksRepository = "https://github.com/myname/my-benchmarks"
            BuildScriptCommand = TestExecutionMethod.ExecScript "./run.sh"
            ProjectRepository = ProjectRepositoryCloneUrl.IsSubmodule
            ProjectRepoPath = "./path/to/project/submodule"
            ResultsProcessor = ResultsProcessor.BenchmarkDotNet
        }
    }

let initState () =
    {
        AdminModel.EditProject = None
        AdminModel.NewProject =
        {
            EditProjectModel.Id = None
            UIMessage = UIMessage.NoMsg
            Form = initEditProjectForm
        }
    }

let liftNewProject = UpdateMsg'.lift (fun x -> x.NewProject) (fun m x -> { m with NewProject = x })
let liftEditProject = UpdateMsg'.lift (fun x -> x.EditProject) (fun m x -> { m with EditProject = x })