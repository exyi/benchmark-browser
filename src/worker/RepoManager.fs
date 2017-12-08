module RepoManager
// open DataClasses
open Fake.Tools
open System
open PublicModel.WorkerModel
open PublicModel.ProjectManagement
open System.Collections.Concurrent
open Microsoft.Extensions.Caching.Memory
open System.Security.Claims
open System.Reflection
open Fake.Core.String

let hackSetSomeInternalShit () =
    let moduleType = typeof<Fake.Core.Context.FakeExecutionContext>.DeclaringType
    let daField = (moduleType.GetProperties(BindingFlags.NonPublic ||| BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.Instance) |> Seq.find (fun m -> m.Name = "fake_data"))

    let asyncLocal = daField.GetValue(null) :?> Threading.AsyncLocal<ConcurrentDictionary<string,obj>>

    if asyncLocal.Value |> isNull then
        printfn "Doing some hack with async locals in Fake.Core.Context"
        asyncLocal.Value <- ConcurrentDictionary()

Fake.Core.Context.setExecutionContext (Fake.Core.Context.RuntimeContext.Unknown)
// Fake.Core.Context.forceFakeContext () |> ignore
let cloneRecursive workingDir repoUrl toPath = Git.CommandHelper.gitCommand workingDir (sprintf "clone --recursive %s %s" repoUrl toPath)
let lastRepoFetch : ConcurrentDictionary<string, DateTime> = ConcurrentDictionary()

let private cloneOrPullRepository repoPath (cloneUrl:Uri) (maxAge: TimeSpan) =
    if IO.Directory.Exists repoPath then
        match lastRepoFetch.TryGetValue(repoPath) with
        | (true, date) when (date + maxAge) > DateTime.UtcNow -> ()
        | _ ->
            printfn "Fetching %s -> %s" (string cloneUrl) repoPath
            if Git.CommandHelper.directRunGitCommand repoPath "fetch --all" |> not then failwithf "Could not fetch %s" (cloneUrl.ToString())
            Git.FileStatus.cleanWorkingCopy repoPath

            lastRepoFetch.[repoPath] <- DateTime.UtcNow
    else
        printfn "Cloning %s -> %s" (string cloneUrl) repoPath
        IO.Directory.CreateDirectory repoPath |> ignore
        cloneRecursive (IO.Path.GetDirectoryName repoPath) (cloneUrl.ToString()) (IO.Path.GetFileName repoPath)

let getUpToDate (tmpPath: string) (cloneUrl:Uri) (maxAge: TimeSpan) =
    if IO.Directory.Exists tmpPath |> not then ignore <| IO.Directory.CreateDirectory tmpPath

    let repoUID = sprintf "%s_%s" (cloneUrl.Segments |> Seq.last) (Git.SHA1.calcSHA1 (cloneUrl.ToString()))
    let repoPath = IO.Path.Combine(tmpPath, repoUID)

    cloneOrPullRepository repoPath cloneUrl maxAge

    repoPath

let checkoutVersion repoPath version =
    let likeReallyCorrectCheckout version =
        Git.CommandHelper.gitCommandf repoPath "checkout -f %s" version
        Git.CommandHelper.gitCommandf repoPath "submodule init"
        Git.CommandHelper.gitCommandf repoPath "submodule update"

    let checkoutLatestUpstreamVersion repoPath =
        let ok, lines, errors = Git.CommandHelper.runGitCommand repoPath "remote show origin"
        if not ok then failwithf "Could not run 'git show remote origin' : %s" errors

        let mainBranchLine = lines |> Seq.map(fun l -> l.Trim()) |> Seq.find(fun l -> l.StartsWith("HEAD branch:"))
        let mainBranch = mainBranchLine.Substring(mainBranchLine.IndexOf(':') + 1).Trim()

        likeReallyCorrectCheckout ("origin/" + mainBranch)

    printfn "Repo %s checkout %s" repoPath version
    if version = "HEAD" then
        checkoutLatestUpstreamVersion repoPath
    else
        likeReallyCorrectCheckout version


let getSpecificVersion config cloneUrl version =
    let repoPath = getUpToDate config cloneUrl TimeSpan.Zero
    checkoutVersion repoPath version
    repoPath

let prepareRepository config (spec: TaskSpecification) =
    let repoPath = getSpecificVersion config (Uri spec.Definition.BenchmarksRepository) spec.BuildScriptVersion

    let subrepo = IO.Path.Combine (repoPath, spec.Definition.ProjectRepoPath)
    match spec.Definition.ProjectRepository with
    | ProjectRepositoryCloneUrl.IsSubmodule ->
        if not <| IO.Directory.Exists subrepo then failwithf "Subrepo %s does not exists" spec.Definition.ProjectRepoPath

    | ProjectRepositoryCloneUrl.CloneFrom cloneUrl ->
        cloneOrPullRepository subrepo (Uri cloneUrl) TimeSpan.Zero

    if Git.Information.getCurrentSHA1 repoPath = Git.Information.getCurrentSHA1 subrepo then
        failwithf "Subrepo %s has the same version as benchmarker repo, so it's not a submodule as claimed." spec.Definition.ProjectRepoPath

    checkoutVersion subrepo spec.ProjectVersion

    repoPath

let listAllCommits (abbreviate: bool) repoPath =
    let list = Git.CommandHelper.getGitResult repoPath (sprintf "log --pretty=format:\"%s\"" (if abbreviate then "%h" else "%H"))
    list.ToArray()

let getCurrentCommit repoPath = Fake.Tools.Git.Branches.getSHA1 repoPath "HEAD"

let getRootCommit repoPath =
    let success, lines, err = Git.CommandHelper.runGitCommand repoPath "rev-list --max-parents=0 HEAD"
    if not success then failwithf "Git error: %s" err
    String.Join("-", lines |> Seq.map(fun x -> x.Trim()))

let getCloneUrl repoPath =
    Git.CommandHelper.runSimpleGitCommand repoPath "config --get remote.origin.url"

type GitCommitInfo = {
    Hash: string
    Parents: string[]
    Signature: string option
    Author: string
    Time: DateTime
    Subject: string
}

let private computeChildMap commits =
    commits
    |> Map.toSeq
    |> Seq.collect (fun (_, commit) -> commit.Parents |> Seq.map (fun a -> a, commit))
    |> Seq.groupBy (fun (p, _) -> p)
    |> Map.ofSeq
    |> Map.map (fun _ -> (Seq.map snd) >> Seq.toArray)

let private computeNearestHeads commits heads =
    let q = Collections.Generic.Queue()
    for i in heads do q.Enqueue i

    let result = Collections.Generic.Dictionary()

    while q.Count > 0 do
        let (chash, name) = q.Dequeue()

        if not <| result.ContainsKey chash then
            result.[chash] <- name

            let c = Map.find chash commits
            for p in c.Parents do
                q.Enqueue (p, name)

    result |> Seq.map (|KeyValue|) |> Map.ofSeq

type CompleteRepoStructure = {
    /// List of commit-hash * head-name tuples
    Heads: (string * string) array
    Commits: Map<string, GitCommitInfo>
    ChildrenMap: Map<string, GitCommitInfo[]>
    NearesHeads: Map<string, string>
}
with static member Create commits heads =
      { CompleteRepoStructure.Heads = heads; Commits = commits; ChildrenMap = computeChildMap commits; NearesHeads = computeNearestHeads commits heads}
     member x.LogFrom root = seq {
        let root =
            if Map.containsKey root x.Commits then root
            else fst <| Array.find (fun (_, x) -> x = root) x.Heads
        let doneMap = Collections.Generic.HashSet()
        let q = Collections.Generic.Queue()
        q.Enqueue root

        while q.Count > 0 do
            let c = q.Dequeue ()
            if doneMap.Add c then
                let c = Map.find c x.Commits
                yield c

                for i in c.Parents do q.Enqueue i
     }


let repoStructureCache =
    let opt = MemoryCacheOptions()
    opt.ExpirationScanFrequency <- TimeSpan.FromHours(1.5)
    opt.CompactionPercentage <- 1.0
    new MemoryCache(opt)

let getRepoStructure tmpPath cloneUrl =
    hackSetSomeInternalShit()
    let repo = getUpToDate tmpPath cloneUrl (TimeSpan.FromHours 1.5)
    repoStructureCache.GetOrCreate(repo, fun _ ->
        let success, ``commits_lines``, err = Git.CommandHelper.runGitCommand repo ("rev-list --remotes --pretty=\"format:%H|%P|%G? - %GK - %GS|%ae <%an>|%ai|%s\"")
        if not success then failwithf "Git error: %s" err

        let success, ``branches_lines``, err = Git.CommandHelper.runGitCommand repo "ls-remote --heads origin"
        if not success then failwithf "Git error: %s" err

        let branchHeads =
            ``branches_lines`` |> Seq.map (fun a ->
                let [| commit; name |] = a.Split('\t')
                commit.Trim(), name.Trim()
            ) |> Seq.toArray

        let commits =
            commits_lines |> Seq.filter (fun l -> l.StartsWith "commit " |> not) |> Seq.map (fun l ->
                let [| hash; parents; gpg; author; date; subject |] = l.Split([| '|' |], 6)
                hash, {
                    GitCommitInfo.Hash = hash.Trim()
                    Parents = parents.Trim().Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                    Signature =
                        if gpg.StartsWith("N") then
                            None
                        else
                            gpg.Substring(1).Trim() |> Some
                    Author = author
                    Time = DateTimeOffset.Parse(date).UtcDateTime
                    Subject = subject
                }
            ) |> Map.ofSeq

        CompleteRepoStructure.Create commits branchHeads
    )

let private mergeStructures (a: CompleteRepoStructure array) =
    let allCommits = a |> Seq.collect (fun m -> m.Commits |> Map.toSeq) |> Map.ofSeq // map ofSeq implicitly ignores duplicates
    let allHeads = a |> Array.collect (fun m -> m.Heads) |> Array.distinct
    CompleteRepoStructure.Create allCommits allHeads


let getRepoStructureOfMany tmpPath (cloneUrls: Uri[]) =
    let structures = Array.map (getRepoStructure tmpPath) cloneUrls
    mergeStructures structures