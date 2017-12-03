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
open PublicModel.ProjectManagement
open PublicModel.ProjectManagement
open System.Threading
open Giraffe.XmlViewEngine
open Marten
open PublicModel.ProjectManagement

type ProjectsQueryItem = {
    Id: int
    RootCommit: string
    TestDefId: Guid
    Repo: ProjectRepositoryCloneUrl
    BenchmarkRepo: string
    Count: int
    VersionsTested: int
}

let private getProjects condition parameters s =
    DbUtils.sqlQuery<ProjectsQueryItem>
        (sprintf
            """
            SELECT * FROM (
                SELECT
                    1 as Id,
                    Count(*) as Count,
                    Count(DISTINCT public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'ProjectVersion') as VersionsTested,
                    public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'ProjectRootCommit' as RootCommit,
                    public.mt_doc_projectmanagement_testdefentity.data->'TestDefinition'->'ProjectRepository' as Repo,
                    public.mt_doc_projectmanagement_testdefentity.data->'TestDefinition'->>'BenchmarksRepository' as BenchmarkRepo,
                    public.mt_doc_projectmanagement_testdefentity.Id as TestDefId

                FROM public.mt_doc_perfreportmodel_benchmarkreport

                JOIN public.mt_doc_projectmanagement_testdefentity
                ON public.mt_doc_projectmanagement_testdefentity.Id = (public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'DefinitionId')::uuid

                GROUP BY RootCommit, Repo, BenchmarkRepo, TestDefId
            ) AS row
            WHERE %s
            """

            condition
        )
        parameters
        s

type TestedProjectVersionInfo = {
    Id: int
    Count: int
    ProjectVersion: string
    TestDefId: Guid
    LastUpdateDate: DateTime
}

let private getTestedVersions (rootCommit: string) s =
    DbUtils.sqlQuery<TestedProjectVersionInfo>
        (sprintf
            """
            SELECT * FROM (
                SELECT
                    1 as Id,
                    Count(*) as Count,
                    public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'ProjectVersion' as ProjectVersion,
                    (public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'DefinitionId')::uuid as TestDefId,
                    MAX((public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'DateComputed')::timestamp) as LastUpdateDate

                FROM public.mt_doc_perfreportmodel_benchmarkreport

                WHERE public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'ProjectRootCommit' = ?

                GROUP BY ProjectVersion, TestDefId
                ORDER BY  LastUpdateDate DESC
            ) AS row
            """

            // condition
        )
        [| rootCommit |]
        s

let private createTestedVersionModel (fn) (a:TestedProjectVersionInfo) =
    {
        TestRunListModel.Date = a.LastUpdateDate
        TaskFriendlyName = fn a.TestDefId
        TaskDefId = a.TestDefId
        Reports = a.Count
        ProjectVersion = a.ProjectVersion
    }

let private createProjectListItems (dbItems: ProjectsQueryItem seq) =
    dbItems
    |> Seq.groupBy (fun p -> p.RootCommit)
    |> Seq.map (fun (key, items) ->
        {
            ResultProjectListItem.RootCommit = key
            CloneUrls = items |> Seq.map (fun p -> match p.Repo with ProjectRepositoryCloneUrl.CloneFrom s -> s | _ -> p.BenchmarkRepo) |> Seq.distinct |> Seq.toArray
            ReportCount = items |> Seq.sumBy (fun i -> i.Count)
            TasksRun = 1
            VersionsTested = items |> Seq.sumBy (fun i -> i.VersionsTested)
            TestDefinitionCount = items |> Seq.length
        }
    )
    |> Seq.toArray

let private createTestDefListItems =
    Seq.map (fun (p: TestDefEntity) ->
       {
           TaskDefListItem.Id = p.Id
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

let getHomeModel (s:IDocumentSession) = task {
    let! testDefinitons =
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

    let testDefinitionList =
        testDefinitons
        |> createTestDefListItems
        |> Seq.toArray

    let! projects =
        getProjects "1=1" [||] s

    let projectList =
        projects |> createProjectListItems

    return
        {
            HomePageModel.TaskDefinitions = testDefinitionList
            Projects = projectList
            FewRecentTestRuns = [| ({ Date = DateTime.UtcNow; TaskFriendlyName = "test task"; TaskDefId = Guid(); Reports = 666; ProjectVersion = "dd" }) |]
        }
}

let getProjectDashboard (uid:Guid) (pid:string) (s: IDocumentSession) = task {
    let! projectRows = getProjects "row.RootCommit = ?" [| pid |] s
    let p = projectRows |> createProjectListItems
    assert (p.Length = 1)
    let! testDefs = s.LoadManyAsync(projectRows |> Seq.map (fun p -> p.TestDefId) |> Seq.toArray)
    let! testedVersions = getTestedVersions (projectRows.[0].RootCommit) s
    return Ok
        {
            DashboardModel.DetailedTestDef = None
            TaskDefinitions = testDefs |> createTestDefListItems |> Seq.toArray
            Projects = p
            FewRecentTestRuns = testedVersions |> Seq.map (createTestedVersionModel (fun id -> (Seq.find (fun (e: TestDefEntity) -> e.Id = id) testDefs).FriendlyId)) |> Seq.toArray
        }
}

let getTestDefDashboard (uid: Guid) (pid:string) (s: IDocumentSession) = task {
    let! p = findDefByFriendlyId pid s
    // let! projects = ...
    match p with
    | Some p ->
        let! projectRows = getProjects "TestDefId = ?" [| p.Id |] s
        let projects = projectRows |> createProjectListItems
        return Ok
            {
                DashboardModel.DetailedTestDef = Some p
                TaskDefinitions = [| p |] |> createTestDefListItems |> Seq.toArray
                Projects = projects
                FewRecentTestRuns = [||] // TODO
            }
    | None -> return Error ("")

}