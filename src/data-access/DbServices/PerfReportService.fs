module DataAccess.PerfReportService
open PublicModel
open PublicModel.PerfReportModel
open Marten
open Marten.Linq
open System
open System.Linq
open Giraffe.Tasks
open System.Threading.Tasks
open Marten
open PublicModel.ProjectManagement
open Giraffe.XmlViewEngine
open PublicModel.WorkerModel
open UserService
open PublicModel.ProjectManagement

let pushResults userId (importData: PerfReportModel.WorkerSubmission seq) (s: IDocumentSession) = task {
    let usedProjectIds = importData |> Seq.map (fun i -> i.ProjectId) |> Set.ofSeq
    let! existingIds = task {
        let! projects = s.LoadManyAsync(usedProjectIds |> Seq.toArray)
        return Map.ofSeq (Seq.map (fun p -> p.ProjectId, p) projects)
    }

    return
        importData |> Seq.map (fun d ->
            if Map.containsKey d.ProjectId existingIds |> not then ImportResult.ProjectDoesNotExists d.ProjectId
            else
                let entity = { BenchmarkReport.Id = Guid.NewGuid(); Data = d; DateSubmitted = DateTime.UtcNow; WorkerId = userId }
                s.Store(entity)
                ImportResult.Ok entity.Id
        )
}

let listTests (s:IDocumentSession) = task {
    let! projects =
        (query{
            for p in s.Query<TestDefEntity>() do
            select p
        }).ToListAsync()

    // let queueCounts =
    //     (query {
    //         for q in s.Query<WorkerQueueItem>() do
    //         groupBy (q.Task.ProjectId) into g
    //         select g.Key
    //     }).ToList()

    // let reportCounts =
    //     (query {
    //         for q in s.Query<BenchmarkReport>() do
    //         groupBy (q.Data.ProjectId) into g
    //         select g.Key
    //     }).ToList()

    return projects
           |> Seq.map (fun p ->
           {
               ProjectListItem.Id = p.Id
               Name = p.Name
               ProjectRepo =
                   match p.TestDefinition.ProjectRepository with
                   | ProjectRepositoryCloneUrl.CloneFrom url -> url
                   | _ -> p.TestDefinition.BenchmarksRepository
               ReportCount = 0 //reportCounts |> Map.tryFind p.Id |> Option.defaultValue 0
               TasksRun = 0
               TasksQueued = 0 // queueCounts |> Map.tryFind p.Id |> Option.defaultValue 0
           })
}