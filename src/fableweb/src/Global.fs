module Global
open Admin.Global

type Page =
  | Home
  | Counter
  | About
  | Admin of AdminPage
  | ProjectDashboard of id: string
  | TaskDashboard of id: string
  | EnqueueTask of projectId: string

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
