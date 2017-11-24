module TestReports

open System
open Microsoft.AspNetCore.Http
open PublicModel
open DataAccess
open Authentication

let pushResults (context: HttpContext) data =
    let userGuid = Authentication.getCurrentUserId context
    DatabaseOperation.execOperation context (fun session ->
        PerfReportService.pushResults userGuid data session
    )

let listProjects (context: HttpContext) =
    DatabaseOperation.execOperation context (fun s ->
        PerfReportService.listTests s
    )

let dashboard ctx pid =
    let uid = getCurrentUserId ctx
    DatabaseOperation.execOperation ctx (PerfReportService.getDashboard uid pid)