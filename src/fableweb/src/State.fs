module App.State

open Elmish
open Elmish.Browser.Navigation
open Elmish.Browser.UrlParser
open Fable.Import.Browser
open Global
open Types
open Home.Types
open Admin.Global

let pageParser: Parser<Page->Page,Page> =
  oneOf [
    map About (s "about")
    map Counter (s "counter")
    map Home (s "home")
    map (Admin AdminPage.NewProject) (s "admin" </> s "newProject")
    // map (Admin AdminPage.NewProject) (s "newProject")
    map (Admin << AdminPage.EditProject) (s "admin" </> s "editProject" </> str)
  ]

let urlUpdate (result: Option<Page>) model =
  match result with
  | None ->
    console.error("Error parsing url")
    model,Navigation.modifyUrl (toHash model.currentPage)
  | Some page ->
      { model with currentPage = page }, []

let init result =
  let (counter, counterCmd) = Counter.State.init()
  let (home, homeCmd) = Home.State.init()
  let (loginBox, loginCmd) = LoginBox.initState()
  let (model, cmd) =
    urlUpdate result
      { currentPage = Home
        counter = counter
        home = home
        loginBox = loginBox
        admin = Admin.Global.initState () }
  model, Cmd.batch [ cmd
                     Cmd.map CounterMsg counterCmd
                     Cmd.map HomeMsg homeCmd
                     Cmd.map LoginMsg loginCmd  ]

let update msg model =
  match msg with
  | CounterMsg msg ->
      let (counter, counterCmd) = Counter.State.update msg model.counter
      { model with counter = counter }, Cmd.map CounterMsg counterCmd
  | HomeMsg msg ->
      let (home, homeCmd) = msg.Invoke model.home
      { model with home = home }, Cmd.map HomeMsg homeCmd
  | LoginMsg msg ->
      let (m, cmd) = msg.Invoke model.loginBox
      { model with loginBox = m }, Cmd.map LoginMsg cmd
  | AdminMsg msg ->
      let (m, cmd) = msg.Invoke model.admin
      { model with admin = m }, Cmd.map AdminMsg cmd
