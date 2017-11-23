module RepoManager
open DataClasses
open Fake.Tools
open System

let getUpToDate (config:WorkerConfig) (cloneUrl:Uri) =
    if IO.Directory.Exists config.ClonedRepositories |> not then ignore <| IO.Directory.CreateDirectory config.ClonedRepositories

    let repoUID = sprintf "%s_%s" (cloneUrl.Segments |> Seq.last) (Git.SHA1.calcSHA1 (cloneUrl.ToString()))
    let repoPath = IO.Path.Combine(config.ClonedRepositories, repoUID)
    if IO.Directory.Exists repoPath then
        if Git.CommandHelper.directRunGitCommand repoPath "pull" |> not then failwithf "Could not pull %s" (cloneUrl.ToString())
        Git.Repository.fullclean repoPath
    else
        IO.Directory.CreateDirectory repoPath |> ignore
        Git.Repository.clone (IO.Path.GetDirectoryName repoPath) (cloneUrl.ToString()) (IO.Path.GetFileName repoPath)

    repoPath

let getSpecificVersion config cloneUrl (version: string) =
    let repoPath = getUpToDate config cloneUrl
    Git.Branches.checkout repoPath false version
    repoPath
