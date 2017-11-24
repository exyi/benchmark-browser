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

let menuItem label page currentPage =
    li
      [ ]
      [ a
          [ classList [ "is-active", page = currentPage ]
            Href (toHash page) ]
          [ str label ] ]

let menu currentPage userRoleOracle =
  aside
    [ ClassName "menu" ]
    [ p
        [ ClassName "menu-label" ]
        [ str "General" ]
      ul
        [ ClassName "menu-list" ]
        ([ menuItem "Home" Home currentPage
           menuItem "Counter sample" Counter currentPage
           menuItem "About" Page.About currentPage
        ]
        |> List.append
             (if userRoleOracle "Admin" then [
               menuItem "Create Project" (Admin AdminPage.NewProject) currentPage
             ] else [])
        ) ]

let root model dispatch =

  let pageHtml =
    function
    | Page.About -> Info.View.root
    | Counter -> Counter.View.root model.counter (CounterMsg >> dispatch)
    | Home -> Home.View.root model.home (HomeMsg >> dispatch)
    | Admin AdminPage.NewProject -> Admin.EditProject.renderView model.admin.NewProject (liftNewProject >> AdminMsg >> dispatch)
    | Admin (AdminPage.EditProject pid) ->
          match model.admin.EditProject with
          | Some (m) when m.Id.IsSome && m.Id.Value.ToString() = pid ->
               Admin.EditProject.renderView m (UpdateMsg'.liftSome () >> liftEditProject >> AdminMsg >> dispatch)
          | _ -> str "Loading project..."
    | Dashboard id -> ProjectDashboard.view model.loginBox.HasRole id model.board (BoardMsg >> dispatch)
    | EnqueueTask id -> ProjectDashboard.viewEnqueueForm id model.board.NewItemModel (ProjectDashboard.Model.LiftNewItemMsg >> BoardMsg >> dispatch)

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
            [ ClassName "container" ]
            [ div
                [ ClassName "columns" ]
                [ div
                    [ ClassName "column is-3" ]
                    [ menu model.currentPage (model.loginBox.HasRole) ]
                  div
                    [ ClassName "column" ]
                    [ pageHtml model.currentPage ] ] ] ] ]
                    // [ FormGenerator.createForm model.loginBox (dispatch << LoginMsg) ] ] ] ] ]

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
