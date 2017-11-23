module Global
open Admin.Global

type Page =
  | Home
  | Counter
  | About
  | Admin of AdminPage

let toHash page =
  match page with
  | About -> "#about"
  | Counter -> "#counter"
  | Home -> "#home"
  | Admin page ->
      match page with
      | AdminPage.EditProject pid -> sprintf "#admin/editProject/%s" pid
      | AdminPage.NewProject -> sprintf "#admin/newProject"
