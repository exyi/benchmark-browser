module Utils

open Fable
open System
open Elmish
open System.Reflection.Emit
open Fable.Core
open Fable.Helpers.React
open System.Threading
open Fable.Import.RemoteDev.MsgTypes
open Fable.Import.React
open Fable.PowerPack
open System.Text.RegularExpressions


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

    let loadData refreshFunction arg =
        (Cmd.ofPromise
            refreshFunction arg
            (UpdateMsg'.replace << LoadableData.Loaded)
            (fun e ->
                System.Diagnostics.Debugger.Break();
                UpdateMsg'.replace <| LoadableData.Error (sprintf "Loading error %s" (e.Message))))

    let display model dispatch viewData refreshFunction =
        let refreshDispatch () =
            dispatch (loadData refreshFunction () |> UpdateMsg'.execCmd)

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


let elementIf condition el =
    if condition then el
    else str ""

let adminOnlyElement roleOracle = elementIf (roleOracle "Admin")


let expectResultPromise (a:Fable.Import.JS.Promise<Result<'a, 'b>>) =
    a |> Promise.bind (function
                       | Ok a -> Promise.create(fun resolve _reject -> resolve a)
                       | Error e -> Promise.create(fun _resolve reject -> reject (System.Exception(e.ToString()))))

let displayStrings a =
    if Array.length a > 1 then
        span [ Props.HTMLAttr.Title (String.Join(" | ", a |> Seq.ofArray)) ] [ str (a.[0] + ", ...") ]
    else
        str a.[0]

let (closeDropdown, registerDropDown, removeDropDown) =
    let mutable queue : (unit -> unit) list = []
    let add handler = queue <- handler :: queue
    let pop () =
        match queue with
        | head :: tail -> queue <- tail; Some head
        | _ -> None
    let remove (handler: unit -> unit) = queue <- List.filter (fun x -> obj.ReferenceEquals(x, handler)) queue

    (
        (fun () -> (pop ()) |> Option.map (fun x -> x ()) |> ignore),
        add,
        remove
    )

module Components =
    open Fable.Import.React
    open Fable.Helpers.React
    [<Pojo>]
    type DropDownProps = {
        Title: ReactElement
        Body: Lazy<ReactElement list>
        InvalidationGuid: Guid
    }
    [<Pojo>]
    type DropDownState = {
        Open: bool
    }
    type DropDown(props) as this =
        inherit Component<DropDownProps,DropDownState>(props)

        let closeHandler = (fun () -> this.setState({Open = false}))
        member this.render () =
            let state : DropDownState = this.state |> box |> Option.ofObj |> Option.defaultValue(box {Open = false}) |> unbox

            let click (ev: MouseEvent) =
                if state.Open then
                    // closing
                    removeDropDown closeHandler
                else
                    registerDropDown closeHandler
                this.setState( {Open = state.Open |> not} )
                ev.preventDefault()

            div [Props.HTMLAttr.ClassName ("dropdown " + (if state.Open then "is-active" else ""))] [
                div [Props.HTMLAttr.ClassName "dropdown-trigger"; Props.DOMAttr.OnClick click ] [
                    this.props.Title
                ]
                div [Props.HTMLAttr.ClassName "dropdown-menu"] [
                    div [Props.HTMLAttr.ClassName "dropdown-content"]
                        (if state.Open then
                             (this.props.Body.Value |> List.map (fun e -> div [ Props.HTMLAttr.ClassName "dropdown-item" ] [ e ]))
                         else [])
                ]
            ]

    // [<Pojo>]
    // type WidableProps = {
    //     Body: ReactElement list
    // }
    // [<Pojo>]
    // type WidableState = {
    //     Open: bool
    // }
    // type Widable(props) as this =
    //     inherit Component<WidableProps,WidableState>(props)

    //     let closeHandler = (fun () -> this.setState({Open = false}))
    //     member this.render () =
    //         let state : DropDownState = this.state |> box |> Option.ofObj |> Option.defaultValue(box {Open = false}) |> unbox

    //         let click (ev: MouseEvent) =
    //             if state.Open then
    //                 // closing
    //                 removeDropDown closeHandler
    //             else
    //                 registerDropDown closeHandler
    //             this.setState( {Open = state.Open |> not} )
    //             ev.preventDefault()

    //         div [Props.HTMLAttr.ClassName ("widable-panel " + (if state.Open then "is-active" else ""))] [
    //             div [Props.HTMLAttr.ClassName "widable-left"; Props.DOMAttr.OnClick click ] [
    //                 str "<"
    //             ]
    //             div [Props.HTMLAttr.ClassName "widable-content"] this.props.Body
    //         ]


open Components
let dropDownMenu title body =
    let props = { DropDownProps.Title = title; Body = body; InvalidationGuid = Guid() }
    createElement(typedefof<DropDown>, props, [])

let faIcon size icon = span [ Props.HTMLAttr.ClassName ("icon " + size) ] [ i [ Props.HTMLAttr.ClassName ("fa fa-" + icon) ] [] ]

let littleDropDownIcon =
    // button [ Props.HTMLAttr.ClassName "button is-small" ] [
        faIcon "is-small" "angle-down"
    // ]
let dropDownLittleMenu body =
    dropDownMenu (littleDropDownIcon) body