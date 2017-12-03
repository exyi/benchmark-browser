module Admin.Global
open System
open Utils
open Elmish
open PublicModel.ProjectManagement
open PublicModel.ProjectManagement

[<RequireQualifiedAccessAttribute>]
type AdminPage =
    | NewTaskDef
    | EditProject of string

type EditTaskDefModel = {
    Id: Option<Guid>
    Form: PublicModel.ProjectManagement.TestDefFormModel
    UIMessage: UIMessage
}

type AdminModel = {
    NewTaskDef: EditTaskDefModel
    EditProject: Option<EditTaskDefModel>
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
        AdminModel.NewTaskDef =
        {
            EditTaskDefModel.Id = None
            UIMessage = UIMessage.NoMsg
            Form = initEditProjectForm
        }
    }

let liftNewProject = UpdateMsg'.lift (fun x -> x.NewTaskDef) (fun m x -> { m with NewTaskDef = x })
let liftEditProject = UpdateMsg'.lift (fun x -> x.EditProject) (fun m x -> { m with EditProject = x })