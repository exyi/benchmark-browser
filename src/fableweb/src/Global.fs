module Global
open Admin.Global
open PublicModel.PerfReportModel

type Page =
  | Home
  | Counter
  | About
  | Admin of AdminPage
  | ProjectDashboard of id: string
  | TaskDashboard of id: string
  | EnqueueTask of projectId: string
  | CompareDetail of versionA: ReportGroupSelector * versionB: ReportGroupSelector

let toHash page =
  match page with
  | About -> "#about"
  | Counter -> "#counter"
  | Home -> "#home"
  | Admin page ->
      match page with
      | AdminPage.EditProject pid -> sprintf "#admin/editProject/%s" pid
      | AdminPage.NewTaskDef -> sprintf "#admin/newProject"
  | ProjectDashboard id -> sprintf "#board/%s" id
  | EnqueueTask taskId -> sprintf "#taskBoard/%s/newTask" taskId
  | TaskDashboard taskId -> sprintf "#taskBoard/%s" taskId
  | CompareDetail (ReportGroupSelector.Version version, ReportGroupSelector.Version versionB) when version = versionB ->
       sprintf "#detail/commit/%s" version
  | CompareDetail (ReportGroupSelector.Version versionA, ReportGroupSelector.Version versionB) ->
       sprintf "#compare/commits/%s/%s" versionA versionB
