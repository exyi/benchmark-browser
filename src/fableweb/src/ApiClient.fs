module ApiClient
open Fable
open Fable.PowerPack
open PublicModel.AccountManagement
open Fable.Import
open Fable.PowerPack.Fetch.Fetch_types
open System.Net
open System.Net.Cache
open Fable.Core
open System
open Fable.Helpers.React
open PublicModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel

let endpoint = "http://localhost:5000"

let removeStoredTokens () =
    Browser.sessionStorage.removeItem "logintoken"
    Browser.localStorage.removeItem "logintoken"
    Browser.localStorage.removeItem "logintoken-info"
    Browser.localStorage.removeItem "logintoken-validity"

let tryGetLoginToken() =
    let findInStorage () =
        let tokenSS : string = (Browser.sessionStorage.getItem "logintoken") :?> string
        let tokenLS : string = (Browser.localStorage.getItem "logintoken") :?> string
        let validity = (Browser.localStorage.getItem "logintoken-validity") :?> Option<string> |> Option.map (System.Double.Parse) |> Option.map (fun x -> DateTime(int64 x)) |> Option.defaultValue (System.DateTime.MinValue)
        if validity < System.DateTime.UtcNow then None
        else if isNull tokenSS && isNull tokenLS then None
        else if isNull tokenSS then
            Browser.sessionStorage.setItem("logintoken", tokenLS)
            Some tokenLS
        else Some tokenSS

    let userInfo : UserDetails option = Browser.localStorage.getItem("logintoken-info") :?> Option<string> |> Option.map Fable.Core.JsInterop.ofJson

    findInStorage (), userInfo
    // findInStorage () |> Option.bind (fun token ->
    //     let decocoded = (token.Split('.').[1]) |> System.Convert.FromBase64String |> Fable.PowerPack.Json.ofString

    //     Some token
    // )

[<PassGenericsAttribute>]
let execApiRequest url data list =
    let absUrl = endpoint + "/" + url
    let contentTypeHeader =
        Fetch.requestHeaders (
            List.concat [
                [ HttpRequestHeaders.Accept "application/json"; HttpRequestHeaders.ContentType "application/json" ]
                (match tryGetLoginToken () with | Some token, _ -> [HttpRequestHeaders.Authorization ("Bearer " + token) ] | _ -> [])
            ])
    let promise = Fetch.postRecord absUrl data (List.append list [ contentTypeHeader; RequestProperties.Mode RequestMode.Cors ])
    promise |> Promise.catch (fun e -> failwithf "Request to %s has failed: %s" absUrl (Fable.Core.JsInterop.toJson e)) |> ignore
    promise |> Promise.map (fun e -> printf "Request to %s succeded: %s" absUrl (Fable.Core.JsInterop.toJson e)) |> ignore
    promise |> Promise.bind (fun r -> r.text()) |> Promise.map Fable.Core.JsInterop.ofJson

let login (data: LoginData) : (LoginResult) JS.Promise =
    let prom = execApiRequest "login" data [ ]
    prom |> Promise.map (fun (result, token) ->
        Browser.sessionStorage.setItem("logintoken", token)
        if data.TokenValidity > 1.0 then
            Browser.localStorage.setItem("logintoken", token)
        Browser.localStorage.setItem("logintoken-validity", ((System.DateTime.UtcNow.AddDays data.TokenValidity).Ticks.ToString()))

        match result with
        | LoginResult.Ok userData -> Browser.localStorage.setItem("logintoken-info", userData |> JsInterop.toJson)
        | _ -> ()
        result
    )

let createTaskDefinition (form:PublicModel.ProjectManagement.TestDefFormModel) : (Result<PublicModel.ProjectManagement.TestDefEntity, string>) JS.Promise =
    execApiRequest "admin/createTest" form []

let getHomeModel () : JS.Promise<PublicModel.ProjectManagement.HomePageModel> =
    execApiRequest "home" () []

let loadTestDefDashboard (id: string) : JS.Promise<Result<PublicModel.ProjectManagement.DashboardModel, string>> =
    execApiRequest "testdef/dashboard" id []

let loadProjectDashboard (id: string) : JS.Promise<Result<PublicModel.ProjectManagement.DashboardModel, string>> =
    execApiRequest "project/dashboard" id []

let enqueueTask (data: WorkerModel.WorkerQueueItemFormModel) : JS.Promise<Result<string, string>> =
    execApiRequest "enqueueTask" data []


let loadComparison (verA: ReportGroupSelector) verB : JS.Promise<VersionComparisonSummary * BenchmarkReport[] * BenchmarkReport[] * ReportGroupDetails * ReportGroupDetails> =
    if verA = verB then
        let data = execApiRequest "getReports" verA []
        data |> Promise.map (fun (d, details) -> VersionComparisonSummary.IdentityCompare "" (Array.length d), d, d, details, details)
    else
        execApiRequest "compareReports" (verA, verB) []