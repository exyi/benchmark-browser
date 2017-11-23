module DataAccess.WorkerTaskService
open Marten
open System
open PublicModel
open PublicModel.WorkerModel
open Giraffe.Tasks
open Microsoft.FSharp.Linq.RuntimeHelpers
open PublicModel.WorkerModel
open PublicModel.ProjectManagement
open ProjectManagement
open System.Reflection.Metadata

let findItemsInQueue (limit:int) (s:IDocumentSession) = task {
    let limitDate = System.DateTime.UtcNow.AddDays(-1.0)
    let! queue = (query {
        for i in s.Query<WorkerQueueItem>() do
        where (i.LastUpdate < limitDate)
        sortByDescending i.Priority
        take limit }).ToListAsync()

    return queue |> Seq.toArray
}

let allocateItem (item:WorkerQueueItem) (s:IDocumentSession) = task {
    s.Patch<WorkerQueueItem>(item.Id).Set("LastUpdate", DateTime.UtcNow)
}

let pushStateUpdate (info: WorkStatusInfo) (s:IDocumentSession) = task {
    let entity = {
            TaskStatusUpdateEntity.Id = Guid.NewGuid()
            Info = info }
    s.Patch<WorkerQueueItem>(info.TaskId).Set("LastUpdate", DateTime.UtcNow)
    s.Store(entity)
}

let createTaskDefinition (userId: Guid) (form: TestDefFormModel) (s: IDocumentSession) = task {
    let entity = {
            TestDefEntity.Id = Guid.NewGuid()
            OwnerId = userId
            Name = form.Name
            TestDefinition = form.Definition }
    s.Store(entity)
    return Ok entity
}
