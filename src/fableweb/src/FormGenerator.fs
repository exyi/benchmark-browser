module FormGenerator

open Fable
open Elmish
open Utils
open Microsoft.FSharp.Reflection
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Fable.Import.React
open Fable.Core
open Fable.Core.JsInterop
open Elmish
open System.Reflection
open System.Threading
open Elmish.React.Common
open Fable.Helpers.React.Props

type FormControlDescriptor = {
    IsSingleControl: bool
    Element: Import.React.ReactElement
}
with static member CreateSinleControl (element) = { IsSingleControl = true; Element = element }
     static member CreateMultiControl (element) = { IsSingleControl = false; Element = element }

let isOptionType (t: System.Type) =
    not (FSharpType.IsTuple t) && ((t.GetGenericTypeDefinition()) = typedefof<int option> || t.Name.EndsWith(" option"))

let rec createDefault (modelType: System.Type) =
    let genericTypeDef = modelType.GetGenericTypeDefinition()

    printfn "Type %s %A %s %A" modelType.Name modelType genericTypeDef.Name genericTypeDef
    if modelType = typeof<float> then box 0.0
    else if modelType = typeof<string> then box ""
    else if modelType = typeof<bool> then box false
    else if FSharpType.IsTuple modelType then
        let fields = FSharpType.GetTupleElements modelType |> Array.map createDefault
        FSharpValue.MakeTuple(fields, modelType)
    else if isOptionType modelType then box None
    else if modelType.Name.EndsWith("[]") then box [||]
    else if FSharpType.IsRecord modelType then
        let fields = FSharpType.GetRecordFields(modelType) |> Array.map (fun x -> createDefault x.PropertyType)
        FSharpValue.MakeRecord(modelType, fields)
    else if FSharpType.IsUnion modelType then
        let cases = FSharpType.GetUnionCases(modelType)
        cases
        |> Seq.sortBy(fun c -> c.GetFields().Length)
        |> Seq.choose(fun c ->
            try
                let values = c.GetFields() |> Array.map (fun f -> createDefault f.PropertyType)
                Some (FSharpValue.MakeUnion(c, values))
            with e ->
                None
        )
        |> Seq.tryHead
        |> Option.defaultWith (failwithf "Could not initialize union type %s %A" (modelType.Name))
    else
        failwith "Can not create default of type";


let rec createFormCore (modelType:System.Type) (model: obj) (dispatch: UpdateMsg<obj> -> unit) =

    let onchange (procValue) (ev:FormEvent) =
        let value : obj = !!ev.target?value
        dispatch (UpdateMsg (fun old ->
            (match procValue value with | Some x -> x | None -> old), Cmd.none))

    let genericTypeDef = modelType.GetGenericTypeDefinition()

    let mapInnerForms (name, control) =
        if control.IsSingleControl then
            label [ ClassName "form-element label" ] [
                    str name
                    control.Element
                ]
        else
            div [ ClassName "form-inner" ] [
                    h3 [] [ str name ]
                    control.Element
                ]

    if modelType = typeof<string> then
        input [
            HTMLAttr.ClassName "form-control form-control-string input"
            HTMLAttr.Type "text"
            HTMLAttr.DefaultValue (model.ToString())
            OnChange (onchange Some)
        ] |> FormControlDescriptor.CreateSinleControl

    // else if modelType = typeof<System.Uri> then
    //     input [
    //         HTMLAttr.ClassName "form-control form-control-uri"
    //         HTMLAttr.Type "text"
    //         HTMLAttr.DefaultValue (model.ToString())
    //         OnChange (onchange (fun o -> Some (System.Uri(string o) |> box)))
    //     ] |> FormControlDescriptor.CreateSinleControl

    else if modelType = typeof<bool> then
        let onchange (procValue) (ev:FormEvent) =
            let value : obj = !!ev.target?checked
            dispatch (UpdateMsg (fun _ -> procValue value, Cmd.none))
        input [
            HTMLAttr.ClassName "form-control form-control-bool input"
            HTMLAttr.Type "checkbox"
            HTMLAttr.Value (model.ToString())
            HTMLAttr.Checked (unbox model)
            OnChange (onchange id)
        ] |> FormControlDescriptor.CreateSinleControl

    else if modelType = typeof<float> then
        input [
            HTMLAttr.ClassName "form-control form-control-number input"
            HTMLAttr.Type "text"
            // HTMLAttr.Step "any"
            HTMLAttr.DefaultValue (model.ToString())
            OnChange (onchange (fun str -> match System.Double.TryParse(str.ToString()) with | (false, _) -> None | (true, v) -> Some (box v)))
        ] |> FormControlDescriptor.CreateSinleControl

    else if (FSharpType.IsTuple modelType) then
        let tupleElements = FSharpType.GetTupleElements modelType
        let tupleValues = FSharpValue.GetTupleFields model

        let rec liftUpdateMsg index (update: UpdateMsg<obj>) =
            (UpdateMsg (fun model ->
                let values = FSharpValue.GetTupleFields(model)
                let childObj, childCmd = update.Invoke values.[index]
                values.[index] <- childObj
                FSharpValue.MakeTuple(values, modelType), (Cmd.map (liftUpdateMsg index) childCmd)
            ))

        div [ ClassName "form-inner form-tuple" ] (
                Seq.mapi2 (fun index v (el: System.Type) -> el.Name, createFormCore el v (liftUpdateMsg index >> dispatch)) tupleValues tupleElements |> Seq.map mapInnerForms |> Seq.toList
        ) |> FormControlDescriptor.CreateMultiControl

    else if isOptionType modelType then
        // let rec liftUpdateMsg (update: UpdateMsg<obj>) =
        //     (UpdateMsg (fun model ->
        //         let childObj, childCmd = update.Invoke model
        //         Some childObj :> obj, (Cmd.map liftUpdateMsg childCmd)
        //     ))
        // let e = (model :?> obj option)
        //           |> Option.map (fun v ->
        //              createFormCore
        //                  (v.GetType())
        //                  v
        //                  (liftUpdateMsg >> dispatch)
        //            ) |> Option.defaultValue (str "" |> FormControlDescriptor.CreateSinleControl)
        div [ ClassName "form-option" ] [
            str "Option type ;("
        //     e.Element
        ] |> (fun x -> { IsSingleControl = true; Element = x })

    else if modelType.Name.EndsWith("[]") then
        let values = model :?> obj []
        // let rec liftUpdateMsg propIndex (update: UpdateMsg<obj>) =
        //     (UpdateMsg (fun model ->
        //         let values = FSharpValue.GetRecordFields(model)
        //         let childObj, childCmd = update.Invoke values.[propIndex]
        //         values.[propIndex] <- childObj
        //         FSharpValue.MakeRecord(modelType, values), (Cmd.map (liftUpdateMsg prop) childCmd)
        //     ))

        let controls = values |> Array.mapi (fun index prop ->
            let control = createFormCore (modelType.GenericTypeArguments |> Seq.exactlyOne) prop (ignore)
            sprintf "Array[%d]" index, control
        )
        let elements = controls |> Array.map mapInnerForms
        div [ ClassName "form-record" ] (List.ofArray elements) |> FormControlDescriptor.CreateMultiControl

    else if (FSharpType.IsUnion modelType) then
        let options = FSharpType.GetUnionCases(modelType)
        let case, fields = FSharpValue.GetUnionFields(model, modelType)
        let buttons = options |> Array.map (fun c ->
            let onclick e =
                dispatch (UpdateMsg (fun _ ->
                    let newValues = c.GetFields() |> Array.map (fun f -> createDefault f.PropertyType)
                    let newCase = FSharpValue.MakeUnion(c, newValues)
                    newCase, Cmd.none
                ))
            button [
                ClassName (sprintf "button %s" (if c.Tag = case.Tag then "is-text" else "is-link"))
                HTMLAttr.Type "button"
                OnClick onclick
                ] [ str c.Name ]
        )
        let fieldTypes = case.GetFields()
        let rec liftUpdateMsg prop (update: UpdateMsg<obj>) =
            let propIndex = Array.findIndex ((=) prop) fieldTypes
            (UpdateMsg (fun model ->
                let case2, values = FSharpValue.GetUnionFields(model, modelType)
                if false then
                    model, Cmd.none
                else
                    let childObj, childCmd = update.Invoke values.[propIndex]
                    values.[propIndex] <- childObj
                    FSharpValue.MakeUnion(case, values), (Cmd.map (liftUpdateMsg prop) childCmd)
            ))

        div [ ClassName "form-union" ] [
                div [] (List.ofArray buttons)
                div [] (List.ofArray (Array.map2 (fun x (p:PropertyInfo) -> (createFormCore (p.PropertyType) x (liftUpdateMsg p >> dispatch)).Element) fields fieldTypes))
        ] |> FormControlDescriptor.CreateMultiControl

    else if (FSharpType.IsRecord modelType) then
        let props = FSharpType.GetRecordFields modelType
        let rec liftUpdateMsg prop (update: UpdateMsg<obj>) =
            let propIndex = Array.tryFindIndex ((=) prop) props |> Option.defaultWith (failwithf "%A")
            (UpdateMsg (fun model ->
                let values = FSharpValue.GetRecordFields(model)
                let childObj, childCmd = update.Invoke values.[propIndex]
                values.[propIndex] <- childObj
                FSharpValue.MakeRecord(modelType, values), (Cmd.map (liftUpdateMsg prop) childCmd)
            ))

        let controls = props |> Array.map (fun prop ->
            let value = FSharpValue.GetRecordField(model, prop)
            let control = createFormCore prop.PropertyType value (liftUpdateMsg prop >> dispatch)
            prop.Name, control
        )
        let elements = controls |> Array.map mapInnerForms
        div [ ClassName "form-record" ] (List.ofArray elements) |> FormControlDescriptor.CreateMultiControl
    else
        failwithf "Form of type %A is not supported" (modelType)

[<PassGenericsAttribute>]
let createForm (model: 'a) (dispatch: UpdateMsg<'a> -> unit) =
    let modelType = typeof<'a>
    let rec liftUpdateMsg (msg:UpdateMsg<obj>) =
        (UpdateMsg (fun x ->
            let o, c = msg.Invoke (x :> obj)
            o :?> 'a, Cmd.map liftUpdateMsg c
        ))
    let form = createFormCore modelType (model :> obj) (liftUpdateMsg >> dispatch)
    form.Element