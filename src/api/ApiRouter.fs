module api.ApiRouter
open Giraffe.HttpHandlers
open Giraffe.Middleware
open Giraffe.Tasks
open Giraffe.HttpContextExtensions
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Http
open System.Threading.Tasks
// open Fable.Remoting.Giraffe
open PublicModel
open Newtonsoft.Json
open DataAccess.FileStorage
open System
open DataAccess
open DatabaseOperation

let json (dataObj : obj) : HttpHandler =
    setHttpHeader "Content-Type" "application/json"
    >=> setBodyAsString (Newtonsoft.Json.JsonConvert.SerializeObject(dataObj, converters))

let serveFunction (f: (HttpContext -> 'a -> Task<'b>)) =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        let serializerSettings = JsonSerializerSettings()
        for c in converters do
            serializerSettings.Converters.Add c
        task {
            let! model = ctx.BindJson<'a>(serializerSettings)
            let! result = f ctx model
            return! json result next ctx
        }

let serveGetFunction (f: (HttpContext -> Task<'a>)) =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        let serializerSettings = JsonSerializerSettings()
        for c in converters do
            serializerSettings.Converters.Add c
        task {
            let! result = f ctx
            return! json result next ctx
        }

let accessDenied = setStatusCode 401 >=> text "Access Denied"
let forbidden roles = setStatusCode 403 >=> text (sprintf "Forbidden, you would need to be %A" roles)

// let cpRandom _next (context:HttpContext) =
//     use rng = System.Security.Cryptography.RandomNumberGenerator.Create()
//     let buffer = Array.init 4096 (fun _ -> 0uy);
//     while true do
//         rng.GetBytes(buffer, 0, 4096)
//         context.Response.Body.Write(buffer, 0, 4096)
//     task {
//         return Some context
//     }

let requireAuth roles : HttpHandler =
    requiresAuthentication accessDenied >=> requiresRoleOf roles (forbidden roles)

// let authImpl : AccountManagement.AuthApi = {
//     login = Authentication.login
// }
// type A = HttpFunc
let webApp : HttpHandler =
    choose [
        // routeCi "/favicon.ico" >=> cpRandom
        // FableGiraffeAdapter.httpHandlerFor authImpl
        GET >=>
            choose [
                route "/text" >=> text "Something here"
            ]
        routeCi "/echoLogin" >=> serveFunction (fun _ (a:AccountManagement.LoginData) -> task { return a })
        routeCi "/login" >=> serveFunction Authentication.login
        routeCi "/pushResults" >=> requireAuth ["Worker"; "Valid"] >=> serveFunction (TestReports.pushResults)
        routeCi "/getMeSomeWork" >=> requireAuth ["Worker"; "Valid"] >=> serveFunction (WorkerHub.getMeSomeWork)
        routeCi "/pushWorkStatus" >=> requireAuth ["Worker"; "Valid"] >=> serveFunction (WorkerHub.pushWorkStatus)
        routeCi "/home" >=> serveGetFunction (TestReports.getHomeModel)
        routeCi "/testdef/dashboard" >=> serveFunction (TestReports.dashboard)
        routeCi "/project/dashboard" >=> serveFunction (TestReports.projectDashboard)
        routeCi "/getReports" >=> serveFunction (TestReports.getReportGroup)
        routeCi "/compareReports" >=> serveFunction (TestReports.compareReportGroups)
        routeCi "/enqueueTask" >=> requireAuth ["Admin"] >=> serveFunction (WorkerHub.enqueueWorkerTask)
        routeCif "/pushFile/%s" (fun id -> requireAuth ["Worker"] >=> serveGetFunction (WorkerHub.pushFile StoredFileType.AnyAttachement id))
        routeCif "/pushFile_stacks/%s" (fun id -> requireAuth ["Worker"] >=> serveGetFunction (WorkerHub.pushFile StoredFileType.CollectedStacks_Text id))

        subRouteCi "/admin"
            (requireAuth ["Admin"; "Valid"] >=> choose
            [
                routeCi "/createTest" >=> serveFunction (AdminHub.createTestDefinition)
                routeCi "/removeTest" >=> serveFunction (AdminHub.removeTestDefinition)
            ])

        POST >=>
            choose [ ]
        setStatusCode 404 >=> text "Not Found" ]