module Admin.EditProject
open Fable.Import.React
open Elmish.React
open Fable.Helpers.React
open Utils
open Admin.Global
open Fable.Helpers.React.Props
open Elmish
open System

let renderView (model: EditTaskDefModel) (dispatch: UpdateMsg<EditTaskDefModel> -> unit) =
    let submit (ev: FormEvent) =
        let updateMsg msg : UpdateMsg<EditTaskDefModel> = UpdateMsg (fun m -> { m with UIMessage = msg }, Cmd.none)
        let clearFormMsg = UpdateMsg (fun m -> { m with Form = initEditProjectForm }, Cmd.none)

        let error, cmd =
            if model.Id.IsSome then
                UIMessage.Error "edit unsupported", Cmd.none
            else if String.IsNullOrWhiteSpace model.Form.Title then
                UIMessage.Error "name is required", Cmd.none
            else
                UIMessage.Info "Request sent", Cmd.ofPromise
                    ApiClient.createTaskDefinition model.Form
                    (fun a ->
                        match a with
                        | Error e -> UIMessage.Error (sprintf "Server says no: %s" e) |> updateMsg
                        | Ok o -> UIMessage.Success (sprintf "Ok, test %s added." model.Form.Title) |> updateMsg |> UpdateMsg'.combine clearFormMsg
                    )
                    (updateMsg << UIMessage.Error << (fun e ->
                        sprintf "Hmm, error %A" e))
        dispatch (UpdateMsg'.combine (updateMsg error) (UpdateMsg'.execCmd cmd))
        ev.preventDefault()

    form [ HTMLAttr.ClassName "form-editproject"; OnSubmit submit ] [
        FormGenerator.createForm model.Form (UpdateMsg'.lift (fun m -> m.Form) (fun m x -> { m with Form = x }) >> dispatch)

        button [ HTMLAttr.Type "submit"; ClassName "button is-primary" ] [ str "Ok" ]

        displayUIMsg model.UIMessage
    ]
