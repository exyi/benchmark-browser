module WorkerHub
open System
open PublicModel
open PublicModel.WorkerModel
open Microsoft.AspNetCore.Http
open Giraffe
open Giraffe.Tasks
open DataAccess

let getMeSomeWork (context:HttpContext) () =
    DatabaseOperation.execOperation context (fun s -> task {
        let! q = WorkerTaskService.findItemsInQueue 1 s
        match q |> Seq.tryHead with
        | None -> return None
        | Some item ->
            do! WorkerTaskService.allocateItem item s
            return Some item
    })


let pushWorkStatus (context:HttpContext) (status: WorkStatusInfo) =
    DatabaseOperation.execOperation context (WorkerTaskService.pushStateUpdate status)
