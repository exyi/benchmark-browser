module Home.View

open Fable.Core
open Fable.Core.JsInterop
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Types
open PublicModel.ProjectManagement

let viewData (model: ProjectListItem []) dispatch =
  div [] (
    model |> Seq.map (fun x ->
      div [ ClassName "box" ] [
        h3 [] [ a [ Href ("#board/" + x.FriendlyId) ] [ str x.Name ] ]
        div [ ] [ str x.ProjectRepo ]
        div [ ] [ str <| sprintf "%d tasks | %d reports | %d queued" x.TasksRun x.ReportCount x.TasksQueued ]
      ]
    ) |> Seq.toList
  )

let root model dispatch =
  div
    [ ]
    [
      h1 [] [ str "Tasks" ]
      Utils.LoadableData'.display model.Data (Model.LiftDataUpdate >> dispatch) viewData State.refreshData
    ]
