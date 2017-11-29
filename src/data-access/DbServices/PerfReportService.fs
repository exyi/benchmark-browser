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
open DataAccess.WorkerTaskService
open Giraffe.XmlViewEngine

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
               FriendlyId = if String.IsNullOrWhiteSpace p.FriendlyId then p.Id.ToString() else p.FriendlyId
               Name = p.Title
               ProjectRepo =
                   match p.TestDefinition.ProjectRepository with
                   | ProjectRepositoryCloneUrl.CloneFrom url -> url
                   | _ -> p.TestDefinition.BenchmarksRepository
               ReportCount = 0 //reportCounts |> Map.tryFind p.Id |> Option.defaultValue 0
               TasksRun = 0
               TasksQueued = 0 // queueCounts |> Map.tryFind p.Id |> Option.defaultValue 0
           })
}

let getDashboard (uid: Guid) (pid:string) (s: IDocumentSession) = task {
    let! p = findDefByFriendlyId pid s
    match p with
    | Some p ->
        return Ok
            {
                DashboardModel.TestDef = p
            }
    | None -> return Error ("")

}