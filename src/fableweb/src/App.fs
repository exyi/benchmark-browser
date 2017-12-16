module App.View

open Elmish
open Elmish.Browser.Navigation
open Elmish.Browser.UrlParser
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.Browser
open Types
open App.State
open Global
open Admin.Global

importAll "../sass/main.sass"

open Fable.Helpers.React
open Fable.Helpers.React.Props
open Utils
open Admin

let menuItem label page currentPage =
    a
      [ classList [ "is-link", page = currentPage; "button", true ]
        Href (toHash page) ]
      [ str label ]

let menu currentPage userRoleOracle (options: PageOptions) optionsDispatch =
  nav
    [ ClassName "breadcrumb has-bullet-separator" ]
    [ div [ ClassName "field is-grouped" ]
        [
           span [ ClassName "buttons has-addons" ] [
             yield menuItem "Home" Home currentPage
             yield menuItem "About" Page.About currentPage
             if userRoleOracle "Admin" then
                 yield menuItem "Create Task Definition" (Admin AdminPage.NewTaskDef) currentPage
                 yield menuItem "Create/Update User" (Admin AdminPage.UpsertUser) currentPage
             if userRoleOracle "" then
                 yield menuItem "Change Password" (Admin AdminPage.ChangePassword) currentPage
           ]
           span [ Style [Width "20px"] ] []
           a [
               classList [ "is-link", options.IsFullWidth; "button", true ]
               Href "#"
               OnClick (fun e ->
                  e.preventDefault()
                  optionsDispatch (UpdateMsg (fun x -> { x with IsFullWidth = not options.IsFullWidth }, Cmd.none))
               )
              ] [
                str "Full width"
              ]
        ]
    ]

let root model dispatch =

  let pageHtml =
    function
    | Page.About -> Info.View.root
    | Counter -> Counter.View.root model.counter (CounterMsg >> dispatch)
    | Home -> Home.View.root model.home (HomeMsg >> dispatch)
    | Admin AdminPage.NewTaskDef -> Admin.EditProject.renderView model.admin.NewTaskDef (liftNewProject >> AdminMsg >> dispatch)
    | Admin (AdminPage.EditProject pid) ->
          match model.admin.EditProject with
          | Some (m) when m.Id.IsSome && m.Id.Value.ToString() = pid ->
               Admin.EditProject.renderView m (UpdateMsg'.liftSome () >> liftEditProject >> AdminMsg >> dispatch)
          | _ -> str "Loading project..."
    | Admin AdminPage.ChangePassword ->
        Admin.Global.viewChangePassword model.admin.ChangePassword (dispatch << AdminMsg << Admin.Global.liftChangePassword)
    | Admin AdminPage.UpsertUser ->
        Admin.Global.viewUpsertUser model.admin.UpsertUser (dispatch << AdminMsg << Admin.Global.liftUpsertUser)
    | ProjectDashboard id -> ProjectDashboard.viewProject model.loginBox.HasRole id model.board (BoardMsg >> dispatch)
    | TaskDashboard id -> ProjectDashboard.viewTask model.loginBox.HasRole id model.board (BoardMsg >> dispatch)
    | EnqueueTask id -> ProjectDashboard.viewEnqueueForm id model.board.NewItemModel (ProjectDashboard.Model.LiftNewItemMsg >> BoardMsg >> dispatch)
    | CompareDetail (v1, v2) -> CompareDetail.view (v1, v2) model.compare (CompareMsg >> dispatch)

  div
    []
    [ div
        [ ClassName "navbar-bg" ] [
            div [ ClassName "columns" ] [
              div [ ClassName "column" ] [ h1 [ ClassName "title" ] [ str "Benchmark browser" ] ]
              div [ ClassName "column login" ] [ LoginBox.root model.loginBox (dispatch << LoginMsg) ]
            ]
        ]
      div
        [ ClassName "section" ]
        [ div
            [ classList [ "container", not model.pageOptions.IsFullWidth] ]
            [
                menu model.currentPage (model.loginBox.HasRole) model.pageOptions (dispatch << PageOptionsMsg)
                pageHtml model.currentPage ] ] ]
                    // [ FormGenerator.createForm model.loginBox (dispatch << LoginMsg) ] ] ] ] ]

let w = Browser.window
w.addEventListener_unload (fun event ->
  for (KeyValue (key, value)) in State.dirtyLocalStorage do
    Browser.localStorage.setItem(key, Fable.Core.JsInterop.toJson value)
  obj()
)

open Elmish.React
open Elmish.Debug
open Elmish.HMR

// App
Program.mkProgram init update root
|> Program.toNavigable (parseHash pageParser) urlUpdate
#if DEBUG
// |> Program.withDebugger
// |> Program.withHMR
#endif
|> Program.withReact "elmish-app"
|> Program.run
