// Learn more about F# at http://fsharp.org

open System
open Fake
open Newtonsoft.Json
open System.Net.Http
open System.Net
open System.Text
open PublicModel
open PublicModel.AccountManagement
open System.Net.Http
open DataClasses
open Fake
open Fake.Core.StringBuilder
open PublicModel.ProjectManagement
open Fake.Core
open Fake.IO
open Newtonsoft.Json.Linq
open System.Collections.Generic
open Fake.Core.String
open PublicModel.WorkerModel
open System.Diagnostics
open PublicModel.PerfReportModel

type FableHacksJsonConverter() = class
    inherit JsonConverter()

    override x.CanRead = true
    override x.CanWrite = true
    override x.CanConvert(t: Type) =
        t = typeof<TimeSpan>;

    override x.WriteJson(writer: JsonWriter, value: obj, serializer: JsonSerializer) =
        let number : TimeSpan = unbox value
        writer.WriteValue(number.TotalMilliseconds)
        ()

    override x.ReadJson(reader: JsonReader, t: Type, existingValue: obj, serializer: JsonSerializer) =
        let time = JValue.ReadFrom(reader).Value<float>()
        time * float TimeSpan.TicksPerMillisecond |> int64 |> TimeSpan |> box
end

let converters = [|
        FableHacksJsonConverter() :> JsonConverter
        Fable.JsonConverter() :> JsonConverter
    |]

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
        request.Content <- content

    let result = client.SendAsync(request).Result

    if result.StatusCode = HttpStatusCode.OK then
        Ok (result.Content.ReadAsStringAsync().Result)
    else
        Error result

let sendJsonRequest config auth url data =
    let content =
        if box data :? IO.Stream then
            new StreamContent(box data :?> IO.Stream) :> HttpContent
        else
            let json = Newtonsoft.Json.JsonConvert.SerializeObject(data, converters)
            printfn "Sending %s <- %s" url json
            new StringContent(json, Encoding.UTF8) :> HttpContent

    let result = sendRequest config url auth (Some content)
    result |> Result.map (fun rjson ->
        Newtonsoft.Json.JsonConvert.DeserializeObject(rjson, converters)
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
        let args = sprintf "msbuild %s /p:Configuration=%s \"/p:AdditionalBenchmarkConstants=\\\"%s\\\"\"" project configuration defineConstants
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
    Attachements: (string * Guid * string[]) array
    ResultLegend: Map<string, string>
    // LocalAttachements: Map<string, string>
    Data: WorkerSubmission []
}

let getFileTags (file: string) =
    let specialType =
        if file.EndsWith(".stacks") || file.EndsWith(".stacks.gz") then "_stacks"
        else ""

    printf "Uploading file%s %s: " specialType file

    let tags =
        [|
            (if file.EndsWith(".json") then Some "json" else None)
            (if file.EndsWith(".json") then Some "json" else None)
            (if file.EndsWith(".xml") then Some "xml" else None)
            // (if file.EndsWith("-report.json") && file.Contains "BenchmarkDotNet.Artifacts" then Some "BDN_json" else None)
            (if file.EndsWith(".log") then Some "log" else None)
            (if file.EndsWith(".html") then Some "html" else None)
            (if file.EndsWith(".csv") then Some "csv" else None)
            (if specialType <> "" then Some <| specialType.TrimStart('_') else None)
        |] |> Array.choose id
    tags

let benchmarkDotNet_parseJson emptySubmission filePath : BenchmarkData =
    let globalJson = JObject.Parse(IO.File.ReadAllText(filePath))


    let files = ResizeArray<(string * Guid * string[])>()
    let parseTestResult (json:JObject) =
        let results = ResizeArray<(string * TestResultValue)>()
        let environment = ResizeArray<(string * string)>()
        let knownFields = set [| "Memory.BytesAllocatedPerOperation" |]
        let testid = sprintf "%s.%s.%s" (json.["Namespace"].Value<string>()) (json.["Type"].Value<string>()) (json.["Method"].Value<string>())

        let unknownFields = json.Descendants() |> Linq.Enumerable.OfType<JObject> |> Seq.collect (fun x -> x.Properties()) |> Seq.filter (fun p -> not <| Set.contains p.Name knownFields)
        let getPath (j:JToken) =
            Seq.unfold (fun (j:JToken) ->
                if j = (json :> JToken) || isNull j.Parent then
                    None
                else match j with
                     | :? JProperty as prop -> Some (Some prop.Name, j.Parent :> JToken)
                     | _ -> Some(None, j.Parent :> JToken))
                j
                |> Seq.choose id
                |> Seq.rev
                |> (fun d -> String.Join(".", d))

        // printfn "%A" unknownFields
        for f in unknownFields do
            let path = getPath f
            if not <| path.StartsWith("Columns.") && f.Value :? JValue then
                if f.Value.Type <> JTokenType.String && path.Contains "Statistics." && not <| (path.EndsWith ".N" || path.EndsWith ".Kurtosis" || path.EndsWith ".Skewness") then
                    results.Add (path, (f.Value.Value<float>() / 100.0) |> int64 |> TimeSpan |> TestResultValue.Time)
                else if f.Value.Type = JTokenType.String then
                    results.Add (path, TestResultValue.Anything <| f.Value.Value<string>())
                else if f.Value.Type = JTokenType.Boolean then
                    results.Add (path, TestResultValue.Anything <| f.Value.Value<bool>().ToString())
                else if f.Value.Type = JTokenType.Integer || f.Value.Type = JTokenType.Float then
                    results.Add (path, TestResultValue.Number <| (f.Value.Value<float>(), None))
            ()

        results.Add("Memory.BytesAllocatedPerOperation", json.["Memory"].["BytesAllocatedPerOperation"].Value<float>() |> TestResultValue.ByteSize)
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
                    if propName.Contains "TimeFraction" then
                        let isPercent = legend.["Legend"].Value<string>().Contains "%"
                        let value = (v.Value<string>() |> Double.Parse) / (if isPercent then 100.0 else 1.0)
                        results.Add("Columns." + propName, TestResultValue.Fraction (value, Some "Statistics.Mean"))
                    if legend.["IsNumeric"].Value<bool>() then
                        results.Add("Columns." + propName, TestResultValue.Number (v.Value<string>() |> Double.Parse, None))
                    else if legend.["IsFileName"].Value<bool>() then
                        let fileGuid = Guid.NewGuid()
                        let fileName = v.Value<string>()
                        let tags = getFileTags fileName
                        files.Add (fileName, fileGuid, tags)
                        results.Add("Columns." + propName, TestResultValue.AttachedFile (fileGuid, tags))
                    else
                        results.Add("Columns." + propName, TestResultValue.Anything <| v.Value<string>())
                | "TimeUnit" ->
                    let micros = v.Value<string>() |> Double.Parse
                    results.Add("Columns." + propName, TestResultValue.Time <| TimeSpan(micros * 10.0 |> int64))
                | "SizeUnit" ->
                    let bytes = v.Value<string>() |> Double.Parse
                    results.Add("Columns." + propName, TestResultValue.ByteSize bytes)
                | _ -> failwith ""

        let parameters =
            match json.["Parameters"] with
            | :? JObject as paramsObject -> paramsObject.ToObject<Map<string, string>>()
            | _ -> Map.empty

        { emptySubmission with TaskName = testid; Environment = Map.ofSeq environment; Results = Map.ofSeq results; TaskParameters = parameters }

    let testData = globalJson.["Benchmarks"] :?> JArray |> Seq.map (fun x -> parseTestResult (x :?> JObject)) |> Seq.toArray

    let legend = globalJson.["Columns"] :?> JObject :> seq<KeyValuePair<string, JToken>> |> Seq.map (fun (KeyValue (propName, j)) -> ("Columns." + propName, j.["Legend"].Value<string>()))

    {
        BenchmarkData.Attachements = files.ToArray()
        Data = testData
        ResultLegend = Map.ofSeq legend
    }

let benchmarkDotNet_processStuff emptyReport (extract: BenchmarkDotNetLogExtraction) =
    let jsonFile = extract.OutFiles |> Seq.tryFind (fun f -> f.EndsWith ".json")
    let result = jsonFile |> Option.map (benchmarkDotNet_parseJson emptyReport) |> Option.defaultValue { BenchmarkData.Data = [||]; Attachements = [||]; ResultLegend = Map.empty }

    { result with Attachements = Array.concat [ result.Attachements; extract.OutFiles |> Seq.map (fun f -> f, Guid.NewGuid(), (Array.append (getFileTags f) [| "global"; "BdnReport" |])) |> Seq.toArray ] }

let benchmarkDotNet_logparser applicationWD =
    let extract = { BenchmarkDotNetLogExtraction.OutFiles = ResizeArray() }
    let mutable exportSection = false
    let fn (line: string) =
        let lineT = line.Trim()
        if lineT = "// * Export *" then
            exportSection <- true

        else if exportSection then
            if String.IsNullOrEmpty lineT then
                exportSection <- false
            else
                extract.OutFiles.Add(IO.Path.Combine(applicationWD, lineT))

    extract, fn

let executeTest repoPath projectId (test: TestDefinition) =
    let executable, args = prepareRun repoPath test
    let logFileName = IO.Path.GetTempFileName() + ".log"
    use logFile = IO.File.CreateText logFileName
    let emptySubmission = {
        WorkerSubmission.DefinitionId = projectId
        DateComputed = DateTime.UtcNow
        TaskName = ""
        ProjectVersion = RepoManager.getCurrentCommit (IO.Path.Combine(repoPath, test.ProjectRepoPath))
        ProjectRootCommit = RepoManager.getRootCommit (IO.Path.Combine(repoPath, test.ProjectRepoPath))
        ProjectCloneUrl = RepoManager.getCloneUrl (IO.Path.Combine(repoPath, test.ProjectRepoPath))
        BuildSystemVersion = RepoManager.getCurrentCommit repoPath
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


let executeWork (config:WorkerConfig) (spec:TaskSpecification) =
    printfn "Executing %A" spec
    let repo = RepoManager.prepareRepository config.ClonedRepositories spec
    printfn "%s" repo
    let results, logFile = executeTest repo spec.DefinitionId spec.Definition
    printfn "Logfile is at %s" logFile
    { results with Attachements = Array.append results.Attachements [| logFile, Guid.NewGuid(), [| "global"; "log" |] |] }

let sendResponse (api: IServerFunction) ({ BenchmarkData.Attachements = attachements; Data = results; ResultLegend = _legend}) : PerfReportModel.ImportResult array =
    let results = api.Invoke "pushResults" results |> expectOk |> Seq.toArray

    for (file, fileId, tags) in attachements do
        try
            use stream = IO.File.OpenRead file

            let specialType =
                if Array.contains "stacks" tags then "_stacks"
                else ""

            use stream = if (String.IsNullOrEmpty specialType |> not) && file.EndsWith(".gz") then
                            new IO.Compression.GZipStream(stream, IO.Compression.CompressionMode.Decompress) :> IO.Stream
                         else stream :> IO.Stream

            printf "Uploading file%s %s: " specialType file

            let tagsQS = tags |> Seq.map Uri.EscapeDataString |> Seq.map ((+) "tag=") |> (fun x -> String.Join('&', x))

            api.Invoke (sprintf "pushFile%s/%O?%s" specialType fileId tagsQS) stream |> expectOk

            printfn "OK"

        with error ->
            printfn "Error while uploading: %O" error

    results

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
    let stopWatch = Stopwatch.StartNew()
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

    workItem |> Option.map (fun x ->
        do server.Invoke "pushWorkStatus" ({WorkStatusInfo.LogMessages = [||]; State = WorkState.Done; TaskId = x.Id; TimeFromStart = stopWatch.Elapsed}) |> expectOk
        ()
    ) |> ignore

    printfn "%A" result

    0 // return an integer exit code
