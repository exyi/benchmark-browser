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

type ServerFunction<'a, 'b> = string -> 'a -> Result<'b, HttpResponseMessage>

let grabSomeWork (server: ServerFunction<_, _>) =
    let work : PublicModel.WorkerModel.WorkerQueueItem option = server "getMeSomeWork" () |> expectOk
    work

let executeWork (item:TaskSpecification) =
    ()

[<EntryPoint>]
let main argv =
    let config = getConfig argv

    printfn "Logging in as <%s>" config.ApiUser
    let (user, token) = login config
    printfn "Logged in with roles %A" user.Roles
    if Array.contains "Worker" user.Roles |> not then
        printfn "Warning: user is not in a 'Worker' role"
    if user.HasTmpPassword then
        printfn "Warning: user has a temptorary password"

    let server = sendJsonRequest config (Some token)

    let workItem = grabSomeWork server
    let result = workItem |> Option.map (fun x -> x.Task) |> Option.map (executeWork)

    printfn "%A" result

    0 // return an integer exit code
