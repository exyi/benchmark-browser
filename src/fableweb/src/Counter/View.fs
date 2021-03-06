module Counter.View

open Fable.Core
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Types
open Elmish

let simpleButton txt action dispatch =
  div
    [ ClassName "column is-narrow" ]
    [ a
        [ ClassName "button"
          OnClick (fun _ -> action |> dispatch) ]
        [ str txt ] ]

let root model dispatch =
  let user = PublicModel.AccountManagement.LoginResult.OtpRequired

  div
    [ ClassName "columns is-vcentered" ]
    [ div [ ClassName "column" ] [ ]
      div
        [ ClassName "column is-narrow"
          Style
            [ CSSProp.Width "170px" ] ]
        [ str (sprintf "Counter value: %i" model) ]
      simpleButton "+1" Increment dispatch
      simpleButton "-1" Decrement dispatch
      simpleButton "Reset" Reset dispatch
      sprintf "kokos %A" user |> str
      div [ ClassName "column" ] [ ] ]
