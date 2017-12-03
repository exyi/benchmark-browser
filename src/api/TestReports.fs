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