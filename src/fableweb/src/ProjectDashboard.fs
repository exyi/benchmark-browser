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
    NewItemModel: WorkerModel.WorkerQueueItemFormModel
}
with static member LiftTestMsg = UpdateMsg'.lift (fun x -> x.Test) (fun m x -> { m with Test = x })
     static member LiftNewItemMsg = UpdateMsg'.lift (fun x -> x.NewItemModel) (fun m x -> { m with NewItemModel = x })

let initNewItemForm = FormGenerator.createDefaultType<WorkerModel.WorkerQueueItemFormModel> ()

let initState = { Test = LoadableData.Loading; NewItemModel = initNewItemForm }

let viewCore roleOracle (model: DashboardModel) dispatch =
    div [] [
        h1 [] [ str (sprintf "Test %s" model.TestDef.Title) ]
        a [ ClassName "button"; Href (sprintf "#board/%s/enqueue" (model.TestDef.Id.ToString())) ] [ str "Enqueue" ] |> adminOnlyElement roleOracle
    ]

let view roleOracle id (model: Model) dispatch =
    LoadableData'.display model.Test (dispatch << Model.LiftTestMsg) (viewCore roleOracle) (fun () -> ApiClient.loadDashboard id |> expectResultPromise)


let viewEnqueueForm id (model: WorkerModel.WorkerQueueItemFormModel) dispatch =
    let submit (ev: FormEvent) =
        dispatch (UpdateMsg (fun model ->
            let cmd = Cmd.ofPromise
                        ApiClient.enqueueTask model
                        (fun x -> UpdateMsg(fun m -> { initNewItemForm with TestDefId = model.TestDefId }, Cmd.none))
                        (fun error -> UpdateMsg(fun m -> Fable.Import.Browser.window.alert (error.ToString()); m, Cmd.none))
            model, cmd
        ))
        ev.preventDefault()
    form [ OnSubmit submit ] [
        FormGenerator.createForm model dispatch
        button [ClassName "button is-primary"] [ str "Ok" ]
        a [ ClassName "button"; Href (sprintf "#board/%s" id) ] [ str "Back" ]
    ]