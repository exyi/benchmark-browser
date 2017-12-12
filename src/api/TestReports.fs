module TestReports

open System
open Microsoft.AspNetCore.Http
open PublicModel
open DataAccess
open Authentication
open Giraffe.HttpHandlers
open Giraffe.Middleware
open Giraffe.Tasks
open Giraffe.HttpContextExtensions

let pushResults (context: HttpContext) data =
    let userGuid = Authentication.getCurrentUserId context
    DatabaseOperation.execOperation context (fun session ->
        WorkerTaskService.pushResults userGuid data session
    )

let getHomeModel (context: HttpContext) =
    DatabaseOperation.execOperation context (fun s ->
        PerfReportService.getHomeModel s
    )

let dashboard ctx pid =
    let uid = getCurrentUserId ctx
    DatabaseOperation.execOperation ctx (PerfReportService.getTestDefDashboard uid pid)

let projectDashboard ctx pid =
    let uid = getCurrentUserId ctx
    DatabaseOperation.execOperation ctx (PerfReportService.getProjectDashboard uid pid)

let getReportGroup ctx data =
    DatabaseOperation.execOperation ctx (PerfReportService.getReportGroups data)

let compareReportGroups ctx (a, b) =
    DatabaseOperation.execOperation ctx (PerfReportService.compareGroups a b)


let getFiles (archiveType: string) (next : HttpFunc) (ctx : HttpContext) =
    let mimeType = Map.find archiveType FileStorage.archiveTypes
    ctx.Response.ContentType <- mimeType
    let files = ctx.Request.Query |> Seq.map (fun (KeyValue (name, file)) -> name, (file.ToArray() |> Array.map Guid.Parse))
    let stream = ctx.Response.Body
    task {
        do! DatabaseOperation.execOperation ctx (FileStorage.dumpFiles archiveType files stream)
        return Some ctx
    }