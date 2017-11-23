module Counter.State

open Elmish
open Types
open Fable.PowerPack

let init () : Model * Cmd<Msg> =
  0, []

let sendReq (data:string) =
  let dd = Fetch.fetch "http://localhost:5000/text" [ Fetch.Fetch_types.RequestProperties.Mode Fetch.Fetch_types.RequestMode.Cors ]
  dd |> Promise.bind (fun r -> r.text())
let update msg model =
  match msg with
  | Increment ->
      model + 1, Cmd.ofPromise sendReq "kokos" (fun e -> printfn "%s" e; Decrement) (fun e -> printfn "%A" e; Reset)
  | Decrement ->
      model - 1, []
  | Reset ->
      0, []
