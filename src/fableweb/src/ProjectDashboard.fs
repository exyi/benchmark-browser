module ProjectDashboard
open Utils
open PublicModel
open PublicModel.ProjectManagement
open Fable.Helpers.React
open PublicModel.AccountManagement
open Fable.Helpers.React.Props
open Fable.Import.React
open Elmish

type Model = {
    Test: LoadableData<DashboardModel>
    NewItemModel: WorkerModel.WorkerQueueItemFormModel * bool
}
with static member LiftTestMsg = UpdateMsg'.lift (fun x -> x.Test) (fun m x -> { m with Test = x })
     static member LiftNewItemMsg = UpdateMsg'.lift (fun x -> x.NewItemModel) (fun m x -> { m with NewItemModel = x })

let initNewItemForm = FormGenerator.createDefaultType<WorkerModel.WorkerQueueItemFormModel> ()

let initState = { Test = LoadableData.Loading; NewItemModel = initNewItemForm, false }

let viewTestDef roleOracle (model: DashboardModel) _dispatch =
    Option.map (fun d ->
        div [] [
            h1 [] [ str (sprintf "Test %s" d.Title) ]
            a [ ClassName "button"; Href (sprintf "#taskBoard/%s/enqueue" (d.Id.ToString())) ] [ str "Enqueue" ] |> adminOnlyElement roleOracle
        ]
    ) model.DetailedTestDef

let viewProjectHeader (model: DashboardModel) =
    let project  = model.Projects |> Seq.head
    div [] [
            h1 [] [ str "Project "; Utils.displayStrings project.CloneUrls ]
            // a [ ClassName "button"; Href (sprintf "#taskBoard/%s/enqueue" (d.Id.ToString())) ] [ str "Enqueue" ] |> adminOnlyElement roleOracle
        ]

let displayTaskDef (model: TaskDefListItem) =
    div [ ClassName "box tile is-child" ] [
        h3 [] [ a [ Href ("#taskBoard/" + model.FriendlyId) ] [ str model.Name ] ]
        div [ ] [ str model.ProjectRepo ]
        div [ ] [ str <| sprintf "%d tasks | %d reports | %d queued" model.TasksRun model.ReportCount model.TasksQueued ]
      ]

let displayProject (model: ResultProjectListItem) =
    div [ ClassName "box tile is-child" ] [
        h3 [] [ a [ Href ("#board/" + model.RootCommit) ] [ displayStrings model.CloneUrls ] ]
        div [ ] [ str model.RootCommit ]
        div [ ] [ str <| sprintf "%d tasks | %d reports | %d definitions | %d versions" model.TasksRun model.ReportCount model.TestDefinitionCount model.VersionsTested ]
      ]


let viewTestReport (model: TestRunListModel) =
    tr [ ClassName "tr" ] [
        td [ClassName "td"] [ (model.Date |> string |> System.DateTime.Parse).ToShortDateString() |> str ]
        td [ClassName "td"] [ a [ Href (sprintf "#taskBoard/%s" (string model.TaskDefId)) ] [ model.TaskFriendlyName |> str ] ]
        td [ClassName "td"] [ model.Reports |> string |> str ]
        td [ClassName "td"] [ model.ProjectVersion |> str ]
    ]

let viewCore roleOracle (model: DashboardModel) dispatch =
    let header =
        viewTestDef roleOracle model dispatch
        |> Option.defaultWith (fun _ -> viewProjectHeader model)

    div [] [
        header
        div [] (
            match model.DetailedTestDef with
            | Some _ -> model.Projects |> Seq.map displayProject |> Seq.toList
            | None -> model.TaskDefinitions |> Seq.map displayTaskDef |> Seq.toList
        )

        table [ ClassName "table is-narrow is-stripped" ] [
            thead [ ClassName "thead" ] [
                tr [ ClassName "tr" ] [
                    th [ClassName "td"] [ str "Date" ]
                    th [ClassName "td"] [ str "Task" ]
                    th [ClassName "td"] [ str "Report Count" ]
                    th [ClassName "td"] [ str "Version" ]
                ]
            ]
            tbody [ClassName "tbody"] (model.FewRecentTestRuns |> Seq.map viewTestReport |> Seq.toList)
        ]
    ]
let viewTask roleOracle id (model: Model) dispatch =
    LoadableData'.display model.Test (dispatch << Model.LiftTestMsg) (viewCore roleOracle) (fun () -> ApiClient.loadTestDefDashboard id |> expectResultPromise)

let viewProject roleOracle id (model: Model) dispatch =
    LoadableData'.display model.Test (dispatch << Model.LiftTestMsg) (viewCore roleOracle) (fun () -> ApiClient.loadProjectDashboard id |> expectResultPromise)



let viewEnqueueForm id (model: WorkerModel.WorkerQueueItemFormModel, isLoading: bool) dispatch =
    let submit (ev: FormEvent) =
        dispatch (UpdateMsg (fun (model, _isLoading) ->
            let cmd = Cmd.ofPromise
                        ApiClient.enqueueTask model
                        (fun x -> UpdateMsg(fun (m,_) -> (m, false), Cmd.none))
                        (fun error -> UpdateMsg(fun (m,_) -> Fable.Import.Browser.window.alert (error.ToString()); (m, false), Cmd.none))
            (model, true), cmd
        ))
        ev.preventDefault()
    form [ OnSubmit submit ] [
        FormGenerator.createForm model (dispatch << UpdateMsg'.lift fst (fun (_, isLoading) x -> (x, isLoading)))
        button [ClassName (sprintf "button is-primary %s" (if isLoading then "is-loading" else ""))] [ str "Ok" ]
        a [ ClassName "button"; Href (sprintf "#board/%s" id) ] [ str "Back" ]
    ]