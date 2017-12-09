module ProjectDashboard
open Utils
open PublicModel
open PublicModel.ProjectManagement
open Fable.Helpers
open Fable.Helpers.React
open PublicModel.AccountManagement
open Fable.Helpers.React.Props
open Fable.Import.React
open Elmish
open PublicModel.PerfReportModel
open Fable.Core
open Fable.Core.JsInterop
open PublicModel.PerfReportModel

// [<Fable.Core.>]
// type TrendControl = class inherit Component<obj, obj> end

[<RequireQualifiedAccessAttribute>]
type TrendProp =
    | Data of obj array
    | Gradient of string array
    | Smooth of bool
    | Radius of int
    | AutoDraw of bool
    | AutoDrawDuration of float
    | AutoDrawEasing of string

    interface IProp

let trendControl (props: IProp list) =
    React.createElement (JsInterop.importDefault "react-trend", JsInterop.keyValueList Fable.Core.CaseRules.LowerFirst props, [])

type Model = {
    Test: LoadableData<DashboardModel>
    NewItemModel: WorkerModel.WorkerQueueItemFormModel * bool
}
with static member LiftTestMsg = UpdateMsg'.lift (fun x -> x.Test) (fun m x -> { m with Test = x })
     static member LiftNewItemMsg = UpdateMsg'.lift (fun x -> x.NewItemModel) (fun m x -> { m with NewItemModel = x })

let initNewItemForm = FormGenerator.createDefaultType<WorkerModel.WorkerQueueItemFormModel> ()

let initState = { Test = LoadableData.Loading; NewItemModel = initNewItemForm, false }

let viewTestDef roleOracle (model: DashboardModel) _dispatch =
    Option.map (fun d ->
        div [] [
            h1 [] [ str (sprintf "Test %s" d.Title) ]
            a [ ClassName "button"; Href (sprintf "#taskBoard/%s/enqueue" (d.Id.ToString())) ] [ str "Enqueue" ] |> adminOnlyElement roleOracle
        ]
    ) model.DetailedTestDef

let viewProjectHeader (model: DashboardModel) =
    let project  = model.Projects |> Seq.head
    div [] [
            h1 [] [ str "Project "; Utils.displayStrings project.CloneUrls ]
            // a [ ClassName "button"; Href (sprintf "#taskBoard/%s/enqueue" (d.Id.ToString())) ] [ str "Enqueue" ] |> adminOnlyElement roleOracle
        ]

let displayTaskDef (model: TaskDefListItem) =
    div [ ClassName "box tile is-child" ] [
        h3 [] [ a [ Href ("#taskBoard/" + model.FriendlyId) ] [ str model.Name ] ]
        div [ ] [ str model.ProjectRepo ]
        div [ ] [ str <| sprintf "%d tasks | %d reports | %d queued" model.TasksRun model.ReportCount model.TasksQueued ]
      ]

let displayProject (model: ResultProjectListItem) =
    div [ ClassName "box tile is-child" ] [
        h3 [] [ a [ Href ("#board/" + model.RootCommit) ] [ displayStrings model.CloneUrls ] ]
        div [ ] [ str model.RootCommit ]
        div [ ] [ str <| sprintf "%d tasks | %d reports | %d definitions | %d versions" model.TasksRun model.ReportCount model.TestDefinitionCount model.VersionsTested ]
      ]

let displayPercents (num: float) title =
        if num > 1.001 then
            span [ Style [ Color "hsl(141, 71%, 48%)"]; Title title ] [ str (sprintf "%.3g%%" (num * 100.0 - 100.0)) ]
        else if num < 0.999 then
            span [ Style [ Color "hsl(348, 100%, 61%)"]; Title title ] [ str (sprintf "%.3g%%" (num * 100.0 - 100.0)) ]
        else
            span [ Title title] [ str "~" ]

let viewRelativePerf (model: CommitRelativePerformance) =
    span [] [
        displayPercents model.MinTime "Min"
        str " / "
        displayPercents model.AvgTime "Avg"
        str " / "
        displayPercents model.MaxTime "Max"

        str (sprintf " (%d tests)" model.Count)
    ]

let viewTestedHeads masterCommit (model: (string * string * CommitRelativePerformance) array) =
    table [ ClassName "table is-narrow is-stripped" ] [
            thead [ ClassName "thead" ] [
                tr [ ClassName "tr" ] [
                    th [ClassName "td"] [ str "Branch" ]
                    th [ClassName "td"] [ str "Commit" ]
                    th [ClassName "td"] [ str "Min / Avg / Max" ]
                ]
            ]
            tbody [ClassName "tbody"] [
                for (name, commit, perf) in model do
                    yield tr [ ClassName "tr" ] [
                        td [ ClassName "td" ] [ str name ]
                        td [ ClassName "td" ] [ a [ Href (sprintf "#compare/commits/%s/%s" masterCommit commit); Title "Compare with master" ] [ str commit ] ]
                        td [ ClassName "td" ] [ viewRelativePerf perf ]
                    ]
            ]
        ]

let viewPerfData (model: ProjectPerfSummary) =
    let viewBranchTrend (name, data : _ array) =
        div [ ClassName "box" ] [
            span [ ClassName "title is-4" ] [ str name ]
            span [] [ str (sprintf " - %d tests" data.Length) ]
            trendControl [
                TrendProp.Data (data |> Array.map (fun (_name, p) -> box p.AvgTime))
                TrendProp.AutoDraw true
                TrendProp.AutoDrawDuration 500.0
                TrendProp.AutoDrawEasing "ease-in"
                TrendProp.Smooth true
                TrendProp.Radius 20
                TrendProp.Gradient [| "#00c6ff"; "#F0F"; "#FF0" |]
            ]
        ]

    div [] [
        for i in model.DetailedBranches do
            yield viewBranchTrend i

        if model.DetailedBranches.Length > 0 then
            yield viewTestedHeads (model.DetailedBranches |> Seq.head |> snd |> Seq.head |> fst) model.HeadOnlyBranches
    ]


let viewTestReport (model: TestRunListModel) =
    tr [ ClassName "tr" ] [
        td [ClassName "td"] [ (model.Date |> string |> System.DateTime.Parse).ToShortDateString() |> str ]
        td [ClassName "td"] [ a [ Href (sprintf "#taskBoard/%s" (string model.TaskDefId)) ] [ model.TaskFriendlyName |> str ] ]
        td [ClassName "td"] [ model.Reports |> string |> str ]
        td [ClassName "td"] [ a [ Href ("#detail/commit/" + model.ProjectVersion); Title "View detail of commit" ] [ str model.ProjectVersion ] ]
    ]

let viewCore roleOracle (model: DashboardModel) dispatch =
    let header =
        viewTestDef roleOracle model dispatch
        |> Option.defaultWith (fun _ -> viewProjectHeader model)

    div [] [
        header
        div [] (
            match model.DetailedTestDef with
            | Some _ -> model.Projects |> Seq.map displayProject |> Seq.toList
            | None -> model.TaskDefinitions |> Seq.map displayTaskDef |> Seq.toList
        )

        viewPerfData model.PerfSummary


        table [ ClassName "table is-narrow is-stripped" ] [
            thead [ ClassName "thead" ] [
                tr [ ClassName "tr" ] [
                    th [ClassName "td"] [ str "Date" ]
                    th [ClassName "td"] [ str "Task" ]
                    th [ClassName "td"] [ str "Report Count" ]
                    th [ClassName "td"] [ str "Version" ]
                ]
            ]
            tbody [ClassName "tbody"] (model.FewRecentTestRuns |> Seq.map viewTestReport |> Seq.toList)
        ]
    ]
let viewTask roleOracle id (model: Model) dispatch =
    LoadableData'.display model.Test (dispatch << Model.LiftTestMsg) (viewCore roleOracle) (fun () -> ApiClient.loadTestDefDashboard id |> expectResultPromise)

let viewProject roleOracle id (model: Model) dispatch =
    LoadableData'.display model.Test (dispatch << Model.LiftTestMsg) (viewCore roleOracle) (fun () -> ApiClient.loadProjectDashboard id |> expectResultPromise)



let viewEnqueueForm id (model: WorkerModel.WorkerQueueItemFormModel, isLoading: bool) dispatch =
    let submit (ev: FormEvent) =
        dispatch (UpdateMsg (fun (model, _isLoading) ->
            let cmd = Cmd.ofPromise
                        ApiClient.enqueueTask model
                        (fun x -> UpdateMsg(fun (m,_) -> (m, false), Cmd.none))
                        (fun error -> UpdateMsg(fun (m,_) -> Fable.Import.Browser.window.alert (error.ToString()); (m, false), Cmd.none))
            (model, true), cmd
        ))
        ev.preventDefault()
    form [ OnSubmit submit ] [
        FormGenerator.createForm model (dispatch << UpdateMsg'.lift fst (fun (_, isLoading) x -> (x, isLoading)))
        button [ClassName (sprintf "button is-primary %s" (if isLoading then "is-loading" else ""))] [ str "Ok" ]
        a [ ClassName "button"; Href (sprintf "#taskBoard/%s" id) ] [ str "Back" ]
    ]