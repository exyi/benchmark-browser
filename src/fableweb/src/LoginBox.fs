module LoginBox

open Fable.Core
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Elmish
open PublicModel.AccountManagement
open Fable.Import.React
open Fable.Core
open Fable.Core.JsInterop
open Counter.View
open Fable.Helpers.React
open Utils

type DialogModel = LoginData * (LoginResult option)
[<RequireQualifiedAccessAttribute>]
type State =
    | LoggedIn of UserDetails
    | LoggedOut
    | DialogOpen of DialogModel

type Model = {
    State: State
}
with member x.HasRole role =
        match x.State with
        | State.LoggedIn info -> role = "" || Array.contains role info.Roles
        | _ -> false

let rec createDialogMsg (msg: UpdateMsg<DialogModel>) =
    UpdateMsg (fun x ->
        let result, cmd = match x.State with | State.DialogOpen d -> (msg.Invoke d) | _ -> failwith ""
        { x with State = State.DialogOpen result }, (Cmd.map createDialogMsg cmd)
    )

let textBox attrs ttype text (dispatch: (UpdateMsg<'a>) -> unit) (update: 'a -> string -> 'a) =
    let onchange (ev:FormEvent) =
        let value : string = !!ev.target?value
        dispatch (UpdateMsg (fun x -> update x value, Cmd.none))
    input (List.append [HTMLAttr.Value text; HTMLAttr.Type ttype; OnChange onchange] attrs)


let simpleButton btnType txt action dispatch =
    div
        [ ClassName "column is-narrow" ]
        [ button
            [ ClassName "button"
              OnClick (fun _ -> action |> dispatch)
              Type btnType ]
            [ str txt ] ]


let openLogin x =
    { x with State = State.DialogOpen ({ LoginData.Password = ""; Login = ""; Otp = ""; TokenValidity = 1.0 }, None) }, Cmd.none

let initDialogState () =
    let model = match ApiClient.tryGetLoginToken () with
                | Some _token, Some details -> State.LoggedIn details
                | _ -> State.LoggedOut
    model, Cmd.none

let initState () =
    let dialog, dialogCmd = initDialogState ()
    { State = dialog }, (Cmd.map createDialogMsg dialogCmd)

let logout x =
    ApiClient.removeStoredTokens()
    initState()

let tryLogin (data, result) =
        let cmd = Cmd.ofPromise ApiClient.login data
                    (fun result -> UpdateMsg (fun (d, _) -> (d, Some (result)), Cmd.none))
                    (fun error ->
                        printfn "%A" error
                        UpdateMsg (fun x -> x, Cmd.none))
        (data, result), cmd

let renderDialog (logindata, validationResult) (dispatch: UpdateMsg<DialogModel> -> unit) =
    div [ ] [
        form [ OnSubmit (fun ev -> dispatch <| UpdateMsg (tryLogin); ev.preventDefault() ) ] [
            label [] [
                str "Login: "
                textBox [] "text" logindata.Login dispatch (fun (m, b) v -> { m with Login = v }, b)
            ]
            label [] [
                str "Password: "
                textBox [] "password" logindata.Password dispatch (fun (m, b) v -> { m with Password = v }, b)
            ]
            label [] [
                str "TOTP: "
                textBox [] "text" logindata.Otp dispatch (fun (m, b) v -> { m with Otp = v }, b)
            ]
            // label [] [
            //     textBox [] "number" logindata.Otp dispatch (fun (m, b) v -> { m with Otp = v }, b)
            //     str "Permanent login"
            // ]
            simpleButton "submit" "Login" (UpdateMsg'.nop ()) dispatch
        ]
        (match validationResult with
         | None -> str ""
         | Some (LoginResult.Ok { HasTmpPassword = true }) -> span [] [ str (sprintf "You are in, but you password is temporary:"); a [ Href "#account/password" ] [ str "Change it" ] ]
         | Some (LoginResult.Ok _) -> str (sprintf "You are in")
         | Some (LoginResult.WrongPassword) -> span [ ClassName "validation-error" ] [ str "Password is wrong" ]
         | Some (LoginResult.WrongOtp) -> span [ ClassName "validation-error" ] [ str "OTP is wrong" ]
         | Some (LoginResult.OtpRequired) -> span [ ClassName "validation-error" ] [ str "OTP is required" ]
         | Some (LoginResult.UserDoesNotExist) -> span [ ClassName "validation-error" ] [ str "User does not exist" ]
        )
    ]

let root model dispatch =

      match model.State with
        | State.LoggedIn details ->
           span [ ] [
               span [ ClassName "userName" ] [ str details.Email ]
               (if details.HasTmpPassword then (span [ ClassName "tmp-password-warning" ] [ str " Your password is temporary" ]) else str "")
               str " | "
               a [ OnClick (fun _ -> dispatch (UpdateMsg logout)); ClassName "logout" ] [ str "Logout" ]
           ]
        | State.LoggedOut ->
            span [ ] [
                a [ ClassName "display-login"; OnClick (fun x -> dispatch <| UpdateMsg openLogin) ] [ str "Login" ]
            ]
        | State.DialogOpen d ->
            div [ ClassName "login-box" ] [
                renderDialog d (dispatch << createDialogMsg)
                simpleButton "button" "Close" (UpdateMsg (ignore >> initState)) dispatch
            ]
