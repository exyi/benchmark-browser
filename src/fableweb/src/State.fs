module App.State

open Elmish
open Elmish.Browser.Navigation
open Elmish.Browser.UrlParser
open Fable.Import.Browser
open Global
open Types
open Home.Types
open Admin.Global
open Utils
open PublicModel.PerfReportModel

let pageParser: Parser<Page->Page,Page> =
  oneOf [
    map About (s "about")
    map Counter (s "counter")
    map Home (s "home")
    map (Admin AdminPage.NewTaskDef) (s "admin" </> s "newProject")
    // map (Admin AdminPage.NewProject) (s "newProject")
    map (Admin << AdminPage.EditProject) (s "admin" </> s "editProject" </> str)
    map ProjectDashboard (s "board" </> str)
    map TaskDashboard (s "taskBoard" </> str)
    map EnqueueTask (s "taskBoard" </> str </> s "enqueue")
    map (fun v -> CompareDetail(ReportGroupSelector.Version v, ReportGroupSelector.Version v)) (s "detail" </> s "commit" </> str)
    map (fun vA vB -> CompareDetail(ReportGroupSelector.Version vA, ReportGroupSelector.Version vB)) (s "compare" </> s "commits" </> str </> str)
  ]

let urlUpdate (result: Option<Page>) model =
  match result with
  | None ->
    console.error("Error parsing url")
    model,Navigation.modifyUrl (toHash model.currentPage)
  | Some page ->
      let model  = { model with currentPage = page }

      // Do some special behavior, like loading a model
      match page with
      | EnqueueTask id -> { model with board = { model.board with NewItemModel = ({ ProjectDashboard.initNewItemForm with TestDefId = id }, false) } }, Cmd.none
      | ProjectDashboard id -> {model with board = ProjectDashboard.initState}, Cmd.map Msg.BoardMsg (Cmd.map ProjectDashboard.Model.LiftTestMsg (LoadableData'.loadData (expectResultPromise << ApiClient.loadProjectDashboard) id))
      | TaskDashboard id -> {model with board = ProjectDashboard.initState}, Cmd.map Msg.BoardMsg (Cmd.map ProjectDashboard.Model.LiftTestMsg (LoadableData'.loadData (expectResultPromise << ApiClient.loadTestDefDashboard) id))
      | CompareDetail (vA, vB) ->
          { model with compare = CompareDetail.initState },
          Cmd.map Msg.CompareMsg (Cmd.map CompareDetail.Model.LiftDataMsg (LoadableData'.loadData (CompareDetail.ComparisonData.Load) (vA, vB)))
      | _ -> model, []

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
        admin = Admin.Global.initState ()
        board = ProjectDashboard.initState
        compare = CompareDetail.initState }
  model, Cmd.batch [ cmd
                     Cmd.map CounterMsg counterCmd
                     Cmd.map HomeMsg homeCmd
                     Cmd.map LoginMsg loginCmd  ]


/// These are set to local storage on page unload - just a optimization to avoid serialization after every model update
let dirtyLocalStorage = System.Collections.Generic.Dictionary<string, obj>()

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
  | BoardMsg msg ->
      let (m, cmd) = msg.Invoke model.board
      { model with board = m }, Cmd.map BoardMsg cmd
  | CompareMsg msg ->
      let (m, cmd) = msg.Invoke model.compare
      if m.GridSettings <> model.compare.GridSettings then
          dirtyLocalStorage.["detail-grid-layout"] <- m.GridSettings
            // Fable.Import.Browser.localStorage.setItem("detail-grid-layout", Fable.Core.JsInterop.toJson m.GridSettings)
      { model with compare = m }, Cmd.map CompareMsg cmd
