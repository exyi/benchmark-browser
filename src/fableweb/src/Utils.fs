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
open Fable.PowerPack.Fetch


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

[<RequireQualifiedAccessAttribute>]
type RequestResponseForm<'request, 'response> =
    | Request of 'request * error: string
    | Response of 'response
    | Loading of 'request
with
    static member LiftRequest msg =
        UpdateMsg'.lift
            (function | Request (x, _) -> x | Loading x -> x | _ -> failwith "")
            (fun m x -> match m with Request (_, e) -> Request(x, e) | Loading _ -> Loading x | _ -> Request (x, ""))
            msg

module RequestResponseForm' =
    let view (model: RequestResponseForm<'request, 'response>) (dispatch: UpdateMsg<RequestResponseForm<'request, 'response>> -> unit) emptyRequest viewRequest viewResponse apiRequest =
        match model with
        | RequestResponseForm.Request (request, error) ->

            let submitClick (event:MouseEvent) =
                event.preventDefault()
                dispatch (UpdateMsg (fun m ->
                    let apiCmd =
                        Cmd.ofPromise apiRequest request
                            (function
                             | Ok response ->
                                UpdateMsg'.replace (RequestResponseForm.Response response)
                             | Error error ->
                                UpdateMsg'.replace (RequestResponseForm.Request (request, error))
                            )
                            (fun error -> UpdateMsg'.replace (RequestResponseForm.Request (request, error.Message)))
                    RequestResponseForm.Loading request, apiCmd
                ))
            form [ ] [
                viewRequest request (dispatch << RequestResponseForm<'request, 'response>.LiftRequest)

                (if System.String.IsNullOrEmpty error then
                    str ""
                 else
                    div [ Props.ClassName "notification is-danger" ] [
                        str error
                    ])

                div [ Props.ClassName "field" ] [
                    div [ Props.ClassName "control" ] [
                        button [ Props.ClassName "button is-primary"; Props.OnClick submitClick; Props.Type "submit" ] [ str "Ok" ]
                    ]
                ]
            ]
        | RequestResponseForm.Loading request ->
            form [ ] [
                viewRequest request (dispatch << RequestResponseForm<'request, 'response>.LiftRequest)

                div [ Props.ClassName "field" ] [
                    div [ Props.ClassName "control" ] [
                        button [ Props.ClassName "button is-primary is-loading"; Props.Type "submit" ] [ ]
                    ]
                ]
            ]
        | RequestResponseForm.Response response ->
            let againClick (event: MouseEvent) =
                event.preventDefault()
                dispatch (UpdateMsg'.replace (RequestResponseForm.Request (emptyRequest, "")))
            div [ ] [
                viewResponse response
                div [ Props.ClassName "field" ] [
                    div [ Props.ClassName "control" ] [
                        button [ Props.ClassName "button"; Props.OnClick againClick ] [ str "Again" ]
                    ]
                ]
            ]


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
open Fable.Core.JsInterop
open PublicModel.PerfReportModel
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


module Option =
    let bind2 mapping a b =
        match (a, b) with
        | (Some a, Some b) -> mapping a b
        | _ -> None

    let map2 mapping a b =
        match (a, b) with
        | (Some a, Some b) -> mapping a b |> Some
        | _ -> None


let viewCommitInfo (commit: GitCommitInfo) =
    p [] [
        strong [] [ str commit.Author ]
        str " "
        commit.Signature |> Option.map (fun sign -> span [] [ str sign; faIcon "is-small has-text-success" "check" ]) |> Option.defaultValue (str "")
        str " "
        span [ Props.Title (string commit.Time) ] [ str ((commit.Time |> box :?> string |> DateTime.Parse).ToShortDateString()) ]
        str " "
        small [] [ str commit.Hash ]

        br []

        str commit.Subject
    ]

type NonTransparentBox<'a> = {
    MagicHiddenValue: 'a
}
let makeBlackbox a = { MagicHiddenValue = a }
let unblackbox a = a.MagicHiddenValue


module Recharts =
    let LineChart : Fable.Import.React.ComponentClass<obj> = JsInterop.import "LineChart" "recharts"
    let Line : Fable.Import.React.ComponentClass<obj> = JsInterop.import "Line" "recharts"
    let CartesianGrid : Fable.Import.React.ComponentClass<obj> = JsInterop.import "CartesianGrid" "recharts"
    let Tooltip : Fable.Import.React.ComponentClass<obj> = JsInterop.import "Tooltip" "recharts"
    let ResponsiveContainer : Fable.Import.React.ComponentClass<obj> = JsInterop.import "ResponsiveContainer" "recharts"

    let lineChart (data: obj []) (click : Func<unit, unit>) (content: ReactElement list) : ReactElement =
        createElement(ResponsiveContainer,
            createObj [
                "height" ==> 200
                "width" ==> "100%"

            ],
            [
                createElement(LineChart,
                    (createObj [
                        "data" ==> data
                        "onClick" ==> click
                    ]),
                    content
                )
            ]
        )

    let lineComponent (lineType: string) stroke (dataKey: obj -> obj) : ReactElement =
        createElement(Line,
            (createObj [
                "type" ==> lineType
                "dataKey" ==> dataKey
                "stroke" ==> stroke
            ]),
            []
        )
    let cartesianGrid (color: string) : ReactElement =
        createElement(
            CartesianGrid,
            createObj [
                "stroke" ==> color
            ],
            []
        )
    let tooltip (content: obj -> ReactElement) : ReactElement =
        createElement(
            Tooltip,
            createObj [
                "isAnimationActive" ==> false
                "content" ==> content
            ],
            []
        )