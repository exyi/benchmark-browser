module Global
open Admin.Global

type Page =
  | Home
  | Counter
  | About
  | Admin of AdminPage
  | Dashboard of id: string
  | EnqueueTask of projectId: string

let toHash page =
  match page with
  | About -> "#about"
  | Counter -> "#counter"
  | Home -> "#home"
  | Admin page ->
      match page with
      | AdminPage.EditProject pid -> sprintf "#admin/editProject/%s" pid
      | AdminPage.NewProject -> sprintf "#admin/newProject"
  | Dashboard id -> sprintf "#board/%s" id
  | EnqueueTask projectId -> sprintf "#board/%s/newTask" projectId
