module DataAccess.PerfReportService
open PublicModel
open PublicModel.PerfReportModel
open Marten
open System
open Giraffe.Tasks
open PublicModel.ProjectManagement
open WorkerTaskService
open System.Linq
open RepoManager
open System.Threading.Tasks
open System.Collections.Generic
open VersionComparer
open UserService
open PublicModel.PerfReportModel
open System.Collections.Generic
open Fake.IO.File
open Fake.Core.Globbing
open Fake.Tools.Git.Rebase

let private repoTmpPath = IO.Path.Combine(IO.Path.GetTempPath(), "benchmark-browser-repositories")

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
    ProjectCloneUrl: string
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
                    public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'ProjectCloneUrl' as ProjectCloneUrl,
                    (public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'DefinitionId')::uuid as TestDefId,
                    MAX((public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'DateComputed')::timestamp) as LastUpdateDate

                FROM public.mt_doc_perfreportmodel_benchmarkreport

                WHERE public.mt_doc_perfreportmodel_benchmarkreport.data->'Data'->>'ProjectRootCommit' = ?

                GROUP BY ProjectVersion, TestDefId, ProjectCloneUrl
                ORDER BY LastUpdateDate DESC
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
        ProjectVersionBranch = ""
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

let private getRepoStructure (items: TestedProjectVersionInfo seq) =
    let cloneUrls = items |> Seq.map (fun x -> x.ProjectCloneUrl) |> Seq.distinct |> Seq.map Uri |> Seq.toArray
    RepoManager.getRepoStructureOfMany repoTmpPath cloneUrls


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
            FewRecentTestRuns = [| |] //({ Date = DateTime.UtcNow; TaskFriendlyName = "test task"; TaskDefId = Guid(); Reports = 666; ProjectVersion = "dd"; ProjectVersionBranch = "master" }) |]
        }
}

let private getVersionData =
    let cache : Collections.Concurrent.ConcurrentDictionary<(string * int), Task<IReadOnlyList<BenchmarkReport>>> = Collections.Concurrent.ConcurrentDictionary()
    fun (commit: string) (reportCount: int option) (session:IDocumentSession) ->
        let query = lazy (query {
                        for d in session.Query<BenchmarkReport>() do
                        where (d.Data.ProjectVersion = commit)
                     })
        let load _ = task {
            let! q = query.Value.ToListAsync()
            return q
        }
        let queryCount _ = task {
            return! query.Value.CountAsync()
        }
        match reportCount with
        | Some reportCount -> cache.GetOrAdd((commit, reportCount), load)
        | None -> task {
            // usually, it does not change and it's much faster to query only the count than load many pretty big json documents
            let! reportCount = queryCount ()
            return! cache.GetOrAdd((commit, reportCount), load)
        }

let private getVersionComparison a b (s:IDocumentSession) = task {
    use s = s.DocumentStore.LightweightSession()
    let load (commit, count) = getVersionData commit count s |> liftTask (Seq.map (fun v -> v.Data))
    let! loadedA = load a
    let! loadedB = load b
    return VersionComparer.compareVersions ComparisonOptions.Default loadedA loadedB
}

/// Does some magic to realistically sort the repository to branches...
let private sortRepoToBranches (repoStructure: CompleteRepoStructure) =
    let getNiceName (head: string) =
        if head.StartsWith "refs/heads/" then head.Substring("refs/heads/".Length)
        else head
    // count how 'popular' each branch is by the number of occurences in merge commits
    let branchesSorted = repoStructure.Heads |> Array.sortByDescending (fun (_commit, n) ->
        let niceName = getNiceName n
        let count = repoStructure.Commits
                    |> Map.toSeq
                    |> Seq.filter (fun (_, commit) ->
                        commit.Parents.Length > 1 && commit.Subject.Contains(sprintf "Merge branch '%s'" niceName))
                    |> Seq.length
        count)

    let result = Collections.Generic.Dictionary<string, string>()

    for (headCommit, name) in branchesSorted do
        let niceName = getNiceName name
        let mutable commit = Some headCommit
        while commit.IsSome && not (result.ContainsKey commit.Value) do
            result.Add(commit.Value, name)
            // follow only the first commit
            let commitInfo = repoStructure.Commits.[commit.Value]

            // this magic trick should eliminate harm from "personal" merge commits
            let skipParent = commitInfo.Parents.Length > 1 && commitInfo.Subject.StartsWith (sprintf "Merge branch '%s'" niceName)

            commit <- repoStructure.Commits.[commit.Value].Parents |> Seq.skip (if skipParent then 1 else 0) |> Seq.tryHead

    // commits that were not in any branch
    let orphanCommits = repoStructure.Commits |> Map.toSeq |> Seq.map fst |> Seq.filter (not << result.ContainsKey) |> HashSet

    // try to resolve some names for the branches from names
    let tryParseMergeName subject =
        let maybeRegexes = [|
            "^Merge branch \\'(?<name>.+)\\'"
            "^Merge pull request \\#(?<prNum>\\d+) from (?<name>.+)$"
        |]
        maybeRegexes |> Array.tryPick (fun r ->
            let m = Text.RegularExpressions.Regex.Match(subject, r)
            if m.Success then
                Some m
            else None
        )
    let orphanMerges =
        repoStructure.Commits
        |> Map.toSeq
        |> Seq.map snd
        |> Seq.filter (fun c -> c.Parents.Length > 1 && c.Parents |> Seq.skip 1 |> Seq.exists orphanCommits.Contains)
        |> Seq.choose (fun c ->
            tryParseMergeName c.Subject
            |> Option.map (fun m ->
                if m.Groups.["prNum"].Success then
                    c, sprintf "maybe/pr-%s-%s" m.Groups.["prNum"].Value m.Groups.["name"].Value
                else
                    c, sprintf "maybe/%s-%s" m.Groups.["name"].Value c.Hash
            )
        )

    // follow the maybe-resolved branches
    for (headCommit, name) in orphanMerges do
        let mutable commit = headCommit.Parents |> Seq.tryFind orphanCommits.Contains
        while commit.IsSome && not (result.ContainsKey commit.Value) do
            result.Add(commit.Value, name)
            commit <- repoStructure.Commits.[commit.Value].Parents |> Seq.tryHead
    result

let private findLongestPath (testedVersions: TestedProjectVersionInfo seq) (repoStructure: CompleteRepoStructure) =
    let lessThan = testedVersions |> Seq.map (fun x -> x.ProjectVersion, HashSet()) |> Map.ofSeq

    for v in testedVersions do
        repoStructure.LogFrom v.ProjectVersion
        |> Seq.skip 1
        |> Seq.choose (fun c -> lessThan.TryFind c.Hash)
        |> Seq.iter (fun m -> m.Add(v.ProjectVersion) |> ignore)

    // let root = lessThan |> Map.toSeq |> Seq.maxBy (fun (_, map) -> map.Count)
    // find the longest path from the root
    let rec findLongestPath =
        let cache = Collections.Concurrent.ConcurrentDictionary<string, string list>()
        (fun a -> cache.GetOrAdd(a, fun from ->
            if Seq.isEmpty lessThan.[from] then
                [ from ]
            else
                let longestPath = lessThan.[from] |> Seq.map findLongestPath |> Seq.maxBy List.length
                from :: longestPath
        ))

    if Seq.isEmpty lessThan then
        []
    else
        let keys = lessThan |> Map.toSeq |> Seq.map fst
        keys |> Seq.map findLongestPath |> Seq.maxBy List.length

let private createPerfSummary testedVersions (repoStructure: CompleteRepoStructure) dbSession =
    let sortedbranches = sortRepoToBranches repoStructure
    let testedHeads = testedVersions |> Seq.groupBy (fun x -> match sortedbranches.TryGetValue(x.ProjectVersion) with (true, b) -> b | _ -> "")
    let mainBranch =
        let versions = testedVersions.ToDictionary(fun x -> x.ProjectVersion)
        findLongestPath testedVersions repoStructure
        |> Seq.map (fun v -> versions.[v], repoStructure.Commits.[v])
        |> Seq.toArray
    // let masterTests =
    //     testedVersions.Join((repoStructure.LogFrom "refs/heads/master"), (fun x -> x.ProjectVersion), (fun x -> x.Hash), (fun a b -> a,b))
    //     |> Seq.sortByDescending (fun (_, c) -> c.Time)
    //     |> Seq.toArray
    let head = mainBranch |> Seq.tryLast |> Option.orElseWith (fun x -> Seq.tryHead testedVersions |> Option.map (fun x -> x, repoStructure.Commits.[x.ProjectVersion]))
    match head with
    | None -> { PerfReportModel.ProjectPerfSummary.DetailedBranches = [||]; HeadOnlyBranches = [||] } |> Task.FromResult
    | Some(head, headC) ->
        let compare (a_versionInfo: TestedProjectVersionInfo) (b_versionInfo) = getVersionComparison (a_versionInfo.ProjectVersion, Some a_versionInfo.Count) (b_versionInfo.ProjectVersion, Some b_versionInfo.Count) dbSession
        let masterComparisons = mainBranch |> Seq.map (fun (t, _) -> compare head t) |> Seq.toArray
        let headsComparisons = testedHeads |> Seq.map (fun (branchName, t) -> compare head (Seq.head t) |> liftTask (fun a -> branchName, a)) |> Seq.toArray

        task {
            let! masterComparisons = Task.WhenAll masterComparisons
            let! headsComparisons = Task.WhenAll headsComparisons
            return
                {
                   ProjectPerfSummary.DetailedBranches = [| "master", (masterComparisons |> Array.map (fun c -> (repoStructure.Commits.[fst c.CommitB]), (c.SummaryGroups.[""].ColumnSummary.["Time"]))) |]
                   HeadOnlyBranches = [| for branch, data in headsComparisons do yield branch, fst data.CommitB, data.SummaryGroups.[""].ColumnSummary.["Time"] |]
                }
        }


let getProjectDashboard (uid:Guid) (pid:string) (s: IDocumentSession) = task {
    let! projectRows = getProjects "row.RootCommit = ?" [| pid |] s
    let p = projectRows |> createProjectListItems
    if p.Length = 0 then
        return Error "Project was not found"
    else
        let! testDefs = s.LoadManyAsync(projectRows |> Seq.map (fun p -> p.TestDefId) |> Seq.toArray)
        let! testedVersions = getTestedVersions (projectRows.[0].RootCommit) s
        let repoStructure = getRepoStructure testedVersions

        let! summary = createPerfSummary testedVersions repoStructure s

        return Ok
            {
                DashboardModel.DetailedTestDef = None
                TaskDefinitions = testDefs |> createTestDefListItems |> Seq.toArray
                Projects = p
                FewRecentTestRuns =
                    testedVersions
                    |> Seq.map (createTestedVersionModel (fun id -> (Seq.find (fun (e: TestDefEntity) -> e.Id = id) testDefs).FriendlyId))
                    |> Seq.map (fun m -> { m with ProjectVersionBranch = Map.tryFind m.ProjectVersion repoStructure.NearesHeads |> Option.defaultValue "" })
                    |> Seq.toArray
                PerfSummary = summary
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
                PerfSummary = { PerfReportModel.ProjectPerfSummary.DetailedBranches = [||]; HeadOnlyBranches = [||] }
            }
    | None -> return Error ("Task definiton not found")
}

let private getCommitDetails cloneUrls commit =
    let repo = RepoManager.getRepoStructureOfMany repoTmpPath cloneUrls
    let repo =
        if repo.Commits.ContainsKey commit |> not then
            for c in cloneUrls do RepoManager.flushRepoCache repoTmpPath d
            RepoManager.getRepoStructureOfMany repoTmpPath cloneUrls
        else repo
    repo.Commits.[commit]

let getReportGroups a session = task {
    match a with
    | ReportGroupSelector.Version commit ->
        let! data = getVersionData commit None session
        let repos = data |> Seq.map (fun x -> x.Data.ProjectCloneUrl) |> Seq.distinct |> Seq.map Uri |> Seq.toArray
        if Array.isEmpty repos then
            failwithf "No data for commit %s" commit
        let commitDetails = getCommitDetails repos commit
        return data, (ReportGroupDetails.Commits [|commitDetails|])
}

let compareGroups a b session = task {
    let! vA, descriptionA = getReportGroups a session
    let! vB, descriptionB = getReportGroups b session
    let cmp = VersionComparer.compareVersions ComparisonOptions.Default (vA |> Seq.map (fun x -> x.Data)) (vB |> Seq.map (fun x -> x.Data))
    return cmp, vA, vB, descriptionA, descriptionB
}