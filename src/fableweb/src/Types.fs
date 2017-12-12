module App.Types

open Global
open Utils
open Admin.Global

type Msg =
  | PageOptionsMsg of UpdateMsg<PageOptions>
  | CounterMsg of Counter.Types.Msg
  | HomeMsg of UpdateMsg<Home.Types.Model>
  | LoginMsg of UpdateMsg<LoginBox.Model>
  | AdminMsg of UpdateMsg<AdminModel>
  | BoardMsg of UpdateMsg<ProjectDashboard.Model>
  | CompareMsg of UpdateMsg<CompareDetail.Model>

type Model = {
    currentPage: Page
    pageOptions: PageOptions
    counter: Counter.Types.Model
    home: Home.Types.Model
    loginBox: LoginBox.Model
    admin: AdminModel
    board: ProjectDashboard.Model
    compare: CompareDetail.Model
  }
