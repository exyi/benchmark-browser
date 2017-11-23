module Home.State

open Elmish
open Types
open Utils

let refreshData = ApiClient.listProjects

let init () : Model * Cmd<UpdateMsg<Model>> =
  let updateCmd = LoadableData'.loadData refreshData
  { Data = LoadableData.Loading }, Cmd.map Model.LiftDataUpdate updateCmd
