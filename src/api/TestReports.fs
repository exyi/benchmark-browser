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
open Fake.Tools.Git.Repository

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
    printfn "%A" ctx.Request.Query
    let files =
        ctx.Request.Query
        |> Seq.map (fun (KeyValue (name, file)) ->
            let name, values =
                if file.Count = 0 then "", [|name|]
                else name, file.ToArray()

            name, (values |> Array.choose (fun x ->
                match Guid.TryParse x with
                | (true, guid) -> Some guid
                | _ -> None))
        )
        |> Seq.filter (fun (_, a) -> a.Length > 0)
    let parameters =
        ctx.Request.Query
        |> Seq.choose (fun (KeyValue (name, v)) ->
            if name.StartsWith "q_" then
                Some (name.Substring(2), v.ToArray())
            else
                None
        )
        |> Map.ofSeq
    let stream = ctx.Response.Body
    task {
        do! DatabaseOperation.execOperation ctx (FileStorage.dumpFiles archiveType (parameters) files stream)
        return Some ctx
    }