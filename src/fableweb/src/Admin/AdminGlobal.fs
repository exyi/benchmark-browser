module Admin.Global
open System
open Utils
open Elmish
open PublicModel.ProjectManagement
open PublicModel.AccountManagement
open Fable.Helpers.React

[<RequireQualifiedAccessAttribute>]
type AdminPage =
    | NewTaskDef
    | EditProject of string
    | UpsertUser
    | ChangePassword

type EditTaskDefModel = {
    Id: Option<Guid>
    Form: PublicModel.ProjectManagement.TestDefFormModel
    UIMessage: UIMessage
}

type AdminModel = {
    NewTaskDef: EditTaskDefModel
    EditProject: Option<EditTaskDefModel>
    UpsertUser: Utils.RequestResponseForm<UpsertUserRequest, UpsertUserResponse>
    ChangePassword: Utils.RequestResponseForm<ChangePasswordRequest, ChangePasswordResponse>
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
let initUpsertUser : UpsertUserRequest = FormGenerator.createDefaultType ()
let initChangePassword : ChangePasswordRequest = FormGenerator.createDefaultType ()

let initState () =
    {
        AdminModel.EditProject = None
        UpsertUser = RequestResponseForm.Request (initUpsertUser, "")
        ChangePassword = RequestResponseForm.Request (initChangePassword, "")
        AdminModel.NewTaskDef =
        {
            EditTaskDefModel.Id = None
            UIMessage = UIMessage.NoMsg
            Form = initEditProjectForm
        }
    }

let liftNewProject = UpdateMsg'.lift (fun x -> x.NewTaskDef) (fun m x -> { m with NewTaskDef = x })
let liftEditProject = UpdateMsg'.lift (fun x -> x.EditProject) (fun m x -> { m with EditProject = x })
let liftUpsertUser = UpdateMsg'.lift (fun x -> x.UpsertUser) (fun m x -> { m with UpsertUser = x })
let liftChangePassword = UpdateMsg'.lift (fun x -> x.ChangePassword) (fun m x -> { m with ChangePassword = x })

let viewUpsertUser model dispatch =
    RequestResponseForm'.view model dispatch initUpsertUser
        (fun request dispatch ->
            FormGenerator.createForm request dispatch)
        (fun response ->
            match response with
            | UpsertUserResponse.UserCreated password ->
                div [] [
                    str (sprintf "User was created, password is '%s' (without the quotes)" password)
                ]
            | _ ->
                div [] [
                    str "Done."
                ]
        )
        (ApiClient.upsertUser >> (Fable.PowerPack.Promise.map Ok))

let viewChangePassword model dispatch =
    RequestResponseForm'.view model dispatch initChangePassword
        (fun request dispatch ->
            FormGenerator.createForm request dispatch)
        (fun (response: ChangePasswordResponse) ->
            div [] [
                str (sprintf "Password is changed. Your new OTP key is '%s' (size = %d, step = %ds)" response.Otp.Key response.Otp.Size response.Otp.Step)
            ]
        )
        (ApiClient.changePassword)
