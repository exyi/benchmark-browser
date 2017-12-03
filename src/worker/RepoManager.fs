module RepoManager
open DataClasses
open Fake.Tools
open System
open PublicModel.WorkerModel
open PublicModel.ProjectManagement

let cloneRecursive workingDir repoUrl toPath = Git.CommandHelper.gitCommand workingDir (sprintf "clone --recursive %s %s" repoUrl toPath)

let cloneOrPullRepository repoPath (cloneUrl:Uri) =
    if IO.Directory.Exists repoPath then
        printfn "Fetching %s -> %s" (string cloneUrl) repoPath
        if Git.CommandHelper.directRunGitCommand repoPath "fetch --all" |> not then failwithf "Could not fetch %s" (cloneUrl.ToString())
        Git.FileStatus.cleanWorkingCopy repoPath
    else
        printfn "Cloning %s -> %s" (string cloneUrl) repoPath
        IO.Directory.CreateDirectory repoPath |> ignore
        cloneRecursive (IO.Path.GetDirectoryName repoPath) (cloneUrl.ToString()) (IO.Path.GetFileName repoPath)

let getUpToDate (config:WorkerConfig) (cloneUrl:Uri) =
    if IO.Directory.Exists config.ClonedRepositories |> not then ignore <| IO.Directory.CreateDirectory config.ClonedRepositories

    let repoUID = sprintf "%s_%s" (cloneUrl.Segments |> Seq.last) (Git.SHA1.calcSHA1 (cloneUrl.ToString()))
    let repoPath = IO.Path.Combine(config.ClonedRepositories, repoUID)

    cloneOrPullRepository repoPath cloneUrl

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
    let repoPath = getUpToDate config cloneUrl
    checkoutVersion repoPath version
    repoPath

let prepareRepository config (spec: TaskSpecification) =
    let repoPath = getSpecificVersion config (Uri spec.Definition.BenchmarksRepository) spec.BuildScriptVersion

    let subrepo = IO.Path.Combine (repoPath, spec.Definition.ProjectRepoPath)
    match spec.Definition.ProjectRepository with
    | ProjectRepositoryCloneUrl.IsSubmodule ->
        if not <| IO.Directory.Exists subrepo then failwithf "Subrepo %s does not exists" spec.Definition.ProjectRepoPath

    | ProjectRepositoryCloneUrl.CloneFrom cloneUrl ->
        cloneOrPullRepository subrepo (Uri cloneUrl)

    if Git.Information.getCurrentSHA1 repoPath = Git.Information.getCurrentSHA1 subrepo then
        failwithf "Subrepo %s has the same version as benchmarker repo, so it's not a submodule as claimed." spec.Definition.ProjectRepoPath

    checkoutVersion subrepo spec.ProjectVersion

    repoPath

let listAllCommits (abbreviate: bool) repoPath =
    let list = Git.CommandHelper.getGitResult repoPath (sprintf "log --pretty=format:\"%s\"" (if abbreviate then "%h" else "%H"))
    list.ToArray()

let getCurrentCommit repoPath = Fake.Tools.Git.Branches.getSHA1 repoPath "HEAD"

let getRootCommit repoPath =
    let success, lines, _err = Git.CommandHelper.runGitCommand repoPath "git rev-list --max-parents=0 HEAD"
    if not success then failwith ""
    String.Join("-", lines |> Seq.map(fun x -> x.Trim()))