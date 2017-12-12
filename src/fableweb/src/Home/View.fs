module Home.View

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Types
open PublicModel.ProjectManagement
open PublicModel.ProjectManagement
open PublicModel.ProjectManagement
open PublicModel.ProjectManagement

let viewData (model: HomePageModel) dispatch =

// <div class="tile is-ancestor">
//   <div class="tile is-4">
//     <!-- 1/3 -->
//   </div>
//   <div class="tile">
//     <!-- This tile will take the rest: 2/3 -->
//   </div>
// </div>
  div [ClassName "tile is-ancestor"] [
        div [ClassName "tile is-8 is-vertical is-parent" ] (
            model.Projects |> Seq.map ProjectDashboard.displayProject |> Seq.toList
        )

        div [ClassName "tile is-vertical is-parent"] (
            model.TaskDefinitions |> Seq.map ProjectDashboard.displayTaskDef |> Seq.toList
        )
  ]

let root model dispatch =
  div
    [ ]
    [
      Utils.LoadableData'.display model.Data (Model.LiftDataUpdate >> dispatch) viewData State.refreshData
    ]
