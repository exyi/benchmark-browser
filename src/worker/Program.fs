// Learn more about F# at http://fsharp.org

open System
open Fake
open Newtonsoft.Json
open System.Net.Http
open System.Net
open System.Text
open PublicModel
open PublicModel.AccountManagement
open PublicModel.AccountManagement
open PublicModel.WorkerModel
open System.Net.Http
open PublicModel.WorkerModel
open PublicModel.WorkerModel
open DataClasses
open PublicModel.ProjectManagement
open Fake
open Fake.Core.StringBuilder
open PublicModel.ProjectManagement
open Fake.Core
open PublicModel.PerfReportModel
open Fake.IO
open Newtonsoft.Json.Linq
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open Newtonsoft.Json.Linq
open PublicModel.PerfReportModel
open PublicModel.PerfReportModel
open Newtonsoft.Json.Linq
open PublicModel.PerfReportModel
open System.Collections.Generic

let getConfig (args: string array) =
    let file = args.[0]
    let config = IO.File.ReadAllText(file)
    Newtonsoft.Json.JsonConvert.DeserializeObject<WorkerConfig>(config)

let sendRequest config (url:string) auth content =
    use client = new HttpClient()
    use request = new HttpRequestMessage();// .CreateHttp(Uri(Uri(config.ApiEndpoint), url))
    request.RequestUri <- Uri(Uri(config.ApiEndpoint), url)

    match auth with
    | None -> ()
    | Some token -> request.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", token)

    match content with
    | None ->
        request.Method <- HttpMethod.Get
    | Some content ->
        request.Method <- HttpMethod.Post
        request.Content <- new StringContent(content, Encoding.UTF8)

    let result = client.SendAsync(request).Result

    if result.StatusCode = HttpStatusCode.OK then
        Ok (result.Content.ReadAsStringAsync().Result)
    else
        Error result

let sendJsonRequest config auth url data =
    let json = Newtonsoft.Json.JsonConvert.SerializeObject(data, Fable.JsonConverter())
    let result = sendRequest config url auth (Some json)
    result |> Result.map (fun rjson ->
        Newtonsoft.Json.JsonConvert.DeserializeObject(rjson, Fable.JsonConverter())
    )

let login config : (UserDetails * string) =
    let otp = match config.ApiOtp with
              | None -> ""
              | Some otp ->
                   let hasher = OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(otp.Key), step = otp.Step, totpSize = otp.Size)
                   hasher.ComputeTotp()

    let loginResult = sendJsonRequest config None "login" ({ LoginData.Login = config.ApiUser; TokenValidity = 7.0; Password = config.ApiPassword; Otp = otp })
    match loginResult with
    | Error e -> failwithf "Error on login attempt: %A" e
    | Ok (LoginResult.Ok lol, Some token) -> lol, token
    | Ok (error, _) -> failwithf "Login error %A." error

let expectOk = function
               | Ok data -> data
               | Error error -> failwithf "Error %A" error

type IServerFunction =
    abstract member Invoke<'a> : string -> obj -> Result<'a, HttpResponseMessage>

let createServerFunction config auth =
    { new IServerFunction
      with member _x.Invoke url value = sendJsonRequest config auth url value }

let grabSomeWork (server: IServerFunction) =
    let work : PublicModel.WorkerModel.WorkerQueueItem option = server.Invoke "getMeSomeWork" () |> expectOk
    work


let prepareRun repoPath test =
    // let argList2 name values =
    //     values
    //     |> Seq.collect (fun v -> ["--" + name; sprintf @"""%s""" v])
    //     |> String.concat " "
    // let buildConfigurationArg (param: BuildConfiguration) =
    //     sprintf "--configuration %s"
    //         (match param with
    //         | Debug -> "Debug"
    //         | Release -> "Release"
    //         | Custom config -> config)
    // let buildBuildArgs (param: DotNetBuildOptions) =
    //     [
    //         buildConfigurationArg param.Configuration
    //         param.Framework |> Option.toList |> argList2 "framework"
    //         param.Runtime |> Option.toList |> argList2 "runtime"
    //         param.BuildBasePath |> Option.toList |> argList2 "build-base-path"
    //         param.OutputPath |> Option.toList |> argList2 "output"
    //         (if param.Native then "--native" else "")
    //     ] |> Seq.filter (not << String.IsNullOrEmpty) |> String.concat " "

    let execDotnetBuild (commitHashes: string [] Lazy) (options : BuildOptions) =
        let project = IO.Path.Combine(repoPath, options.ProjectPath)
        let configuration =
            if String.IsNullOrWhiteSpace options.Configuration then "Release"
            else options.Configuration
        let preprocessorVars = Seq.append (if false then (commitHashes.Value |> Seq.map (fun c -> "C_" + c)) else seq []) [ options.PreprocessorParameters ]
        let defineConstants = String.Join(";", preprocessorVars)
        assert (defineConstants.Contains " " |> not)
        let args = sprintf "msbuild %s /p:Configuration=%s /p:AdditionalBenchmarkConstants=%s" project configuration defineConstants
        let dotnetParam = { Fake.DotNet.Cli.DotnetOptions.Default with DotnetCliPath = "dotnet"; WorkingDirectory = repoPath }
        Trace.tracefn "Restoring packages at %s" project
        Fake.DotNet.Cli.DotnetRestore (fun opt -> { opt with Common = dotnetParam; }) project
        Trace.tracefn "Executing dotnet %s" args
        let result = Fake.DotNet.Cli.Dotnet dotnetParam args
        if not result.OK then failwithf "dotnet build failed with code %i: %s" result.ExitCode (String.Join ("\n", result.Errors))

        let outputPathRegex = Text.RegularExpressions.Regex(@"^\s(?<projectName>[^:()]+) -> (?<outPath>[^:()]+)$")
        result.Messages |> Seq.map (outputPathRegex.Match) |> Seq.filter (fun m -> m.Success) |> Seq.map (fun m -> (m.Groups.["projectName"].Value, m.Groups.["outPath"].Value)) |> Seq.toArray

    let subRepoPath = IO.Path.Combine(repoPath, test.ProjectRepoPath)
    let commitHashes = lazy (Array.append (RepoManager.listAllCommits true subRepoPath) Array.empty)//(RepoManager.listAllCommits false subRepoPath))

    match test.BuildScriptCommand with
    | TestExecutionMethod.DotnetRun (options, args) ->
        let buildPaths = execDotnetBuild commitHashes options
        // target is build as the last projects
        let (_projectName, outPath) = buildPaths |> Seq.last
        "dotnet", (sprintf "%s %s" outPath args)
    | TestExecutionMethod.ExecScript script ->
        script, ""

type BenchmarkDotNetLogExtraction = {
    OutFiles: string ResizeArray
}

type BenchmarkData = {
    Attachements: string array
    ResultLegend: Map<string, string>
    Data: WorkerSubmission []
}

let benchmarkDotNet_parseJson emptySubmission filePath : BenchmarkData =
    let globalJson = JObject.Parse(IO.File.ReadAllText(filePath))


    let parseTestResult (json:JObject) =
        let results = ResizeArray<(string * TestResultValue)>()
        let environment = ResizeArray<(string * string)>()
        let knownFields = set [| "Memory.BytesAllocatedPerOperation" |]
        let testid = sprintf "%s.%s.%s" (json.["Namespace"].Value<string>()) (json.["Type"].Value<string>()) (json.["Method"].Value<string>())

        let unknownFields = json.Descendants() |> Linq.Enumerable.OfType<JObject> |> Seq.collect (fun x -> x.Properties()) |> Seq.filter (fun p -> not <| Set.contains p.Name knownFields)
        let getPath (j:JToken) =
            Seq.unfold (fun (j:JToken) ->
                if j.Parent = (json :> JContainer) || isNull j.Parent then
                    None
                else match j with
                     | :? JProperty as prop -> Some (Some prop.Name, j.Parent :> JToken)
                     | _ -> Some(None, j.Parent :> JToken))
                j
                |> Seq.choose id
                |> (fun d -> String.Join(".", d))

        // printfn "%A" unknownFields
        for f in unknownFields do
            let path = getPath f
            if not <| path.StartsWith("Columns.") && f.Value :? JValue then
                if f.Value.Type <> JTokenType.String && path.Contains ".Statistics." && not <| (path.EndsWith ".N" || path.EndsWith ".Kurtosis" || path.EndsWith ".Skewness") then
                    results.Add (path, (f.Value.Value<float>() / 1000.0) |> TimeSpan.FromMilliseconds |> TestResultValue.Time)
                else if f.Value.Type = JTokenType.String then
                    results.Add (path, TestResultValue.Anything <| f.Value.Value<string>())
                else if f.Value.Type = JTokenType.Boolean then
                    results.Add (path, TestResultValue.Anything <| f.Value.Value<bool>().ToString())
                else if f.Value.Type = JTokenType.Integer || f.Value.Type = JTokenType.Float then
                    results.Add (path, TestResultValue.Number <| (f.Value.Value<float>(), None))
            ()

        results.Add("Memory.BytesAllocatedPerOperation", json.["Memory"].["BytesAllocatedPerOperation"].Value<int64>() |> TestResultValue.ByteSize)
        results.Add("Memory.Gen0Per1k", TestResultValue.Number(json.["Memory"].["Gen0Collections"].Value<float>(), None))
        results.Add("Memory.Gen1Per1k", TestResultValue.Number(json.["Memory"].["Gen1Collections"].Value<float>(), None))
        results.Add("Memory.Gen2Per1k", TestResultValue.Number(json.["Memory"].["Gen2Collections"].Value<float>(), None))

        let columns = json.["Columns"] :?> JObject
        for KeyValue (propName, v) in (columns) do
            if propName.StartsWith "Job." then
                environment.Add (propName, v.Value<string>())
            else
                let legend = globalJson.["Columns"].[propName] :?> JObject
                match legend.["UnitType"].Value<string>() with
                | "Dimensionless" ->
                    if legend.["IsNumeric"].Value<bool>() then
                        results.Add("Columns." + propName, TestResultValue.Number (v.Value<string>() |> Double.Parse, None))
                    else
                        results.Add("Columns." + propName, TestResultValue.Anything <| v.Value<string>())
                | "TimeUnit" ->
                    let micros = v.Value<string>() |> Double.Parse
                    results.Add("Columns." + propName, TestResultValue.Time <| TimeSpan.FromMilliseconds(micros / 1000.0))
                | "SizeUnit" ->
                    let bytes = v.Value<string>() |> Int64.Parse
                    results.Add("Columns." + propName, TestResultValue.ByteSize bytes)
                | _ -> failwith ""

        let parameters =
            match json.["Parameters"] with
            | :? JObject as paramsObject -> paramsObject.ToObject<Map<string, string>>()
            | _ -> Map.empty

        { emptySubmission with TaskName = testid; Environment = Map.ofSeq environment; Results = Map.ofSeq results; TaskParameters = parameters }

    let testData = globalJson.["Benchmarks"] :?> JArray |> Seq.map (fun x -> parseTestResult (x :?> JObject))

    let legend = globalJson.["Columns"] :?> JObject :> seq<KeyValuePair<string, JToken>> |> Seq.map (fun (KeyValue (propName, j)) -> ("Columns." + propName, j.["Legend"].Value<string>()))

    {
        BenchmarkData.Attachements = [| |]
        Data = Seq.toArray testData
        ResultLegend = Map.ofSeq legend
    }

let benchmarkDotNet_processStuff emptyReport (extract: BenchmarkDotNetLogExtraction) =
    let jsonFile = extract.OutFiles |> Seq.tryFind (fun f -> f.EndsWith ".json")
    let result = jsonFile |> Option.map (benchmarkDotNet_parseJson emptyReport) |> Option.defaultValue { BenchmarkData.Data = [||]; Attachements = [||]; ResultLegend = Map.empty }

    { result with Attachements = Array.concat [ result.Attachements; extract.OutFiles.ToArray() ] }

let benchmarkDotNet_logparser applicationWD =
    let extract = { BenchmarkDotNetLogExtraction.OutFiles = ResizeArray() }
    let mutable exportSection = false
    let fn (line: string) =
        let lineT = line.Trim()
        if lineT = "// * Export *" then
            exportSection <- true

        if exportSection then
            if String.IsNullOrEmpty lineT then
                exportSection <- false
            else
                extract.OutFiles.Add(IO.Path.Combine(applicationWD, lineT))

    extract, fn

let executeTest repoPath projectId (test: TestDefinition) =
    let executable, args = prepareRun repoPath test
    let logFileName = IO.Path.GetTempFileName() + ".log"
    use logFile = IO.File.CreateText logFileName;
    let emptySubmission = {
        WorkerSubmission.ProjectId = projectId
        DateComputed = DateTime.UtcNow
        TaskName = ""
        ProjectVersion = Fake.Tools.Git.Branches.getSHA1 "HEAD" (IO.Path.Combine(repoPath, test.ProjectRepoPath))
        BuildSystemVersion = Fake.Tools.Git.Branches.getSHA1 "HEAD" repoPath
        TaskParameters = Map.empty
        Results = Map.empty
        Environment = Map.empty }
    let (writeStdOut, captureStdOut, stdOutFunction, finalizeFunction) =
        match test.ResultsProcessor with
        | ResultsProcessor.BenchmarkDotNet ->
            let bdnExtract, fn = benchmarkDotNet_logparser repoPath
            (true, true, fn, fun () -> benchmarkDotNet_processStuff emptySubmission bdnExtract)
        | ResultsProcessor.StdOutJson -> (false, true, ignore, failwithf "Not implemented %A")

    let exitCode =
        Fake.Core.Process.ExecProcessWithLambdas
            (fun i ->
                { i with Arguments = args; WorkingDirectory = repoPath; FileName = executable }
            )
            (TimeSpan.FromDays 3.0)
            true // silent
            (Fake.Core.Trace.traceError)
            (fun msg ->
                if writeStdOut then Fake.Core.Trace.trace msg
                if captureStdOut then logFile.WriteLine msg
                stdOutFunction msg)

    if exitCode <> 0 then
        Fake.Core.Trace.traceError (sprintf "%s Exitted with exit code %d" executable exitCode)
    finalizeFunction (), logFileName


let executeWork config (spec:TaskSpecification) =
    printfn "Executing %A" spec
    let repo = RepoManager.prepareRepository config spec
    printfn "%s" repo
    let results, logFile = executeTest repo spec.ProjectId spec.Definition
    printfn "Logfile is at %s" logFile
    { results with Attachements = Array.append results.Attachements [| logFile |] }

let sendResponse (api: IServerFunction) ({ BenchmarkData.Attachements = attachements; Data = results; ResultLegend = _legend}) : PerfReportModel.ImportResult seq =
    api.Invoke "pushResults" results |> expectOk

[<EntryPoint>]
let main argv =
    // let emptySubmission = {
    //     WorkerSubmission.ProjectId = Guid()
    //     DateComputed = DateTime.UtcNow
    //     TaskName = ""
    //     ProjectVersion = ""//Fake.Tools.Git.Information.getVersion (IO.Path.Combine(repoPath, test.ProjectRepoPath))
    //     BuildSystemVersion = ""//Fake.Tools.Git.Information.getVersion repoPath
    //     TaskParameters = Map.empty
    //     Results = Map.empty
    //     Environment = Map.empty }
    // let test = benchmarkDotNet_parseJson emptySubmission "/home/exyi/code/dotvvm-benchmarks/BenchmarkDotNet.Artifacts/results/DotvvmSynthTestBenchmark-report.json"
    // printfn "%s" (JsonConvert.SerializeObject(test))

    // TODO
    // Fake.Core.CoreTracing.addListener
    // enable perf map in .NET Core
    Environment.SetEnvironmentVariable("COMPlus_PerfMapEnabled", "1")

    let config = getConfig argv

    printfn "Logging in as <%s>" config.ApiUser
    let (user, token) = login config
    printfn "Logged in with roles %A" user.Roles
    if Array.contains "Worker" user.Roles |> not then
        printfn "Warning: user is not in a 'Worker' role"
    if user.HasTmpPassword then
        printfn "Warning: user has a temptorary password"

    let server = createServerFunction config (Some token)

    let workItem = grabSomeWork server
    let result = workItem |> Option.map (fun x -> x.Task) |> Option.map (executeWork config)

    let response = result |> Option.map (fun result ->
        let importResults = sendResponse server result

        importResults |> Seq.zip result.Data |> Seq.iter (fun (data, import) ->
                              match import with
                              | ImportResult.Ok _guid -> ()
                              | ImportResult.ProjectDoesNotExists pid -> printfn "Ahh, the specified project does not exists (%O).\n" pid
                              | ImportResult.SomeError error -> printfn "Error importing %s" error
        )
        importResults
    )

    printfn "%A" result

    0 // return an integer exit code
