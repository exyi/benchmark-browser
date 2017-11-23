module Utils

open Fable
open Elmish
open System.Reflection.Emit
open Fable.Core
open Fable.Helpers.React
open System.Threading
open Fable.Import.RemoteDev.MsgTypes
open Fable.Import.React


type UpdateMsg<'a> = | UpdateMsg of ('a -> 'a * Cmd<UpdateMsg<'a>>)
with member x.Invoke a = match x with UpdateMsg lol -> lol a


module UpdateMsg' =
    let execCmd cmd = UpdateMsg (fun x -> x, cmd)

    let replace model = UpdateMsg (fun _ -> model, Cmd.none)

    let combine (a: UpdateMsg<'a>) (b: UpdateMsg<'a>) =
        UpdateMsg (fun model ->
            let model1, cmd1 = a.Invoke model
            let model2, cmd2 = b.Invoke model1
            model2, Cmd.batch [ cmd1; cmd2 ]
        )

    let nop () = UpdateMsg (fun x -> x, Cmd.none)

    let rec lift (getter: 'b -> 'a) (setter: 'b -> 'a -> 'b) (msg: UpdateMsg<'a>) : UpdateMsg<'b> =
        UpdateMsg (fun model ->
            let childModel, childCmd = msg.Invoke (getter model)
            setter model childModel, Cmd.map (lift getter setter) childCmd
        )
    let liftSome () : (UpdateMsg<'a> -> UpdateMsg<'a option>) = lift (fun g -> match g with | Some x -> x | None -> failwith "Hey, can't dispatch on None") (fun _ x -> Some x)

[<RequireQualifiedAccessAttribute>]
type UIMessage =
    | Error of string
    | Info of string
    | Success of string
    | NoMsg

let displayUIMsg =
    function
    | UIMessage.Error msg -> div [ Props.HTMLAttr.ClassName "notification is-danger" ] [ str msg ]
    | UIMessage.Info msg -> div [ Props.HTMLAttr.ClassName "notification is-info" ] [ str msg ]
    | UIMessage.Success msg -> div [ Props.HTMLAttr.ClassName "notification is-success" ] [ str msg ]
    | UIMessage.NoMsg -> str ""

[<RequireQualifiedAccessAttribute>]
type LoadableData<'a> =
    | Loading
    | Loaded of 'a
    | Error of string

module LoadableData' =

    let loadData refreshFunction =
        (Cmd.ofPromise
            refreshFunction ()
            (UpdateMsg'.replace << LoadableData.Loaded)
            (fun e -> UpdateMsg'.replace <| LoadableData.Error (sprintf "Loading erro %s" (e.ToString()))))

    let display model dispatch viewData refreshFunction =
        let refreshDispatch () =
            dispatch (loadData refreshFunction |> UpdateMsg'.execCmd)

        match model with
        | LoadableData.Loading ->
            div [ Props.HTMLAttr.ClassName "notification is-info loading-msg" ]
                [
                    // button [ Props.HTMLAttr.ClassName "button refresh"; Onclick ] [ str "Try again" ]
                    str "loading data..."; ]
        | LoadableData.Loaded data ->
            let loadedGetter = function | LoadableData.Loaded d -> d | _ -> data
            viewData data (dispatch << (UpdateMsg'.lift loadedGetter (fun _m x -> LoadableData.Loaded x)))
        | LoadableData.Error error ->
            div [ Props.HTMLAttr.ClassName "notification is-danger network-error" ] [ str error ]