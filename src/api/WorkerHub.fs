module WorkerHub
open System
open PublicModel
open PublicModel.WorkerModel
open Microsoft.AspNetCore.Http
open Giraffe
open Giraffe.Tasks
open DataAccess
open Authentication

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

let enqueueWorkerTask context (form: WorkerQueueItemFormModel) =
    let uid = getCurrentUserId context
    DatabaseOperation.execOperation context (WorkerTaskService.enqueueWorkerTask uid form)

let pushFile t (fileId: string) (context:HttpContext) =
    let fileId = Guid.Parse fileId
    let tags = context.Request.Query.["tag"] |> Seq.toArray

    DatabaseOperation.execOperation context (fun session ->
        FileStorage.storeFile fileId t tags (context.Request.Body) session
    )
