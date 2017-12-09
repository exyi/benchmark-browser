module TestReports

open System
open Microsoft.AspNetCore.Http
open PublicModel
open DataAccess
open Authentication

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