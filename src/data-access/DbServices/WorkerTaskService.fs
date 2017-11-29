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
open Marten
open PublicModel.ProjectManagement
open Marten
open Giraffe.XmlViewEngine
open System
open System.Threading.Tasks.Dataflow
open PublicModel.ProjectManagement
open UserService
open PublicModel.WorkerModel
open PublicModel.WorkerModel
open PublicModel.PerfReportModel

let optionOfNull o =
    if (obj.ReferenceEquals(o, null)) then None else Some o

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
    let! e = s.LoadAsync<WorkerQueueItem>(item.Id)
    e.LastUpdate <- DateTime.UtcNow
    // s.Patch<WorkerQueueItem>(item.Id).Set("LastUpdate", DateTime.UtcNow)
    s.Store e
}

let pushStateUpdate (info: WorkStatusInfo) (s:IDocumentSession) = task {
    let entity = {
            TaskStatusUpdateEntity.Id = Guid.NewGuid()
            Info = info }
    // s.Patch<WorkerQueueItem>(info.TaskId).Set("LastUpdate", DateTime.UtcNow)
    let! e = s.LoadAsync<WorkerQueueItem>(info.TaskId)
    e.LastUpdate <- DateTime.UtcNow
    s.Store e
    s.Store(entity)
}

let findDefByFriendlyId id (s: IDocumentSession) =
    if String.IsNullOrEmpty id then
        System.Threading.Tasks.Task.FromResult(None)
    else match Guid.TryParse(id) with
         | (true, guid) -> s.LoadAsync(guid) |> liftTask optionOfNull
         | (false, _) -> (query { for t in s.Query<TestDefEntity>() do
                                  where (t.FriendlyId = id)
                                  select t }).FirstOrDefaultAsync() |> liftTask optionOfNull

let createTaskDefinition (userId: Guid) (form: TestDefFormModel) (s: IDocumentSession) = task {
    let entity = {
            TestDefEntity.Id = Guid.NewGuid()
            OwnerId = userId
            Title = form.Title
            FriendlyId = form.FriendlyId
            TestDefinition = form.Definition }
    let! existingEntity = findDefByFriendlyId form.FriendlyId s
    match existingEntity with
    | None ->
        s.Store(entity)
        return Ok entity
    | Some existingEntity -> return Error (sprintf "Entity with id '%s' already exists (%s)" form.FriendlyId existingEntity.Title)
}

let enqueueWorkerTask (userId: Guid) (form: WorkerQueueItemFormModel) (s: IDocumentSession) = task {
    let! projectEntity = findDefByFriendlyId form.TestDefId s
    match projectEntity with
    | None -> return Error (sprintf "Test definition '%s' does not exists" form.TestDefId)
    | Some projectEntity ->
        let queueItem =
            {
                WorkerModel.WorkerQueueItem.Id = Guid.NewGuid()
                Task =
                    {
                        TaskSpecification.ProjectId = projectEntity.Id
                        Definition = projectEntity.TestDefinition
                        BuildScriptVersion = form.BenchmarkerVersion.ToVersionString()
                        ProjectVersion = form.ProjectVersion.ToVersionString()
                    }
                Priority = 1.0
                LastUpdate = DateTime.MinValue
            }
        s.Store(queueItem)
        return Ok (queueItem.Id.ToString())
}

let pushResults userId (importData: PerfReportModel.WorkerSubmission seq) (s: IDocumentSession) = task {
    let usedProjectIds = importData |> Seq.map (fun i -> i.ProjectId) |> Set.ofSeq
    let! existingIds = task {
        let! projects = s.LoadManyAsync(usedProjectIds |> Seq.toArray)
        return Map.ofSeq (Seq.map (fun (p: TestDefEntity) -> p.Id, p) projects)
    }

    return
        importData |> Seq.map (fun d ->
            if Map.containsKey d.ProjectId existingIds |> not then ImportResult.ProjectDoesNotExists d.ProjectId
            else
                let entity = { BenchmarkReport.Id = Guid.NewGuid(); Data = d; DateSubmitted = DateTime.UtcNow; WorkerId = userId }
                s.Store(entity)
                ImportResult.Ok entity.Id
        ) |> Seq.toArray
}
