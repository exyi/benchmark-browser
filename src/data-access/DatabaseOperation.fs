module DataAccess.DatabaseOperation
open Marten
open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Giraffe.Tasks
open PublicModel.ProjectManagement
open PublicModel.PerfReportModel
open Newtonsoft.Json
open Newtonsoft.Json.Linq

// type DbAction = IDocumentSession -> Task

// type DbQuery<'a> = IDocumentSession -> Task<'a>

// let bindAction (a:DbAction) (store:IDocumentStore) =
//     fun () ->
//             use session = store.LightweightSession()
//             (a session).ContinueWith(fun _ -> session.SaveChangesAsync())

// let bindQuery (a:DbAction) (store:IDocumentStore) =
//     fun () ->

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
let advance(reader: JsonReader) =
    reader.Read() |> ignore
let private readElements(reader: JsonReader, itemTypes: Type[], serializer: JsonSerializer) =
    let rec read index acc =
        match reader.TokenType with
        | JsonToken.EndArray -> acc
        | _ ->
            let value = serializer.Deserialize(reader, itemTypes.[index])
            advance reader
            read (index + 1) (acc @ [value])
    if reader.TokenType <> JsonToken.StartArray then failwithf"expected start of array: %A" reader.TokenType
    advance reader
    let x = read 0 List.empty
    if reader.TokenType <> JsonToken.EndArray then failwithf"expected end of array: %A" reader.TokenType
    x

type TestResultValueJsonConverter() = class
    inherit JsonConverter()
    override x.CanRead = true
    override x.CanWrite = true
    override x.CanConvert(t: Type) =
        // printfn "type: %s" t.FullName
        t.FullName.StartsWith(typeof<TestResultValue>.FullName)

    override x.WriteJson(writer: JsonWriter, value: obj, serializer: JsonSerializer) =
        let value = value :?> TestResultValue
        let name, object =
            match value with
            | TestResultValue.Time t -> "Time", box t
            | TestResultValue.Number (n, s) -> "Number", box [|box n; box (Option.toObj s)|]
            | TestResultValue.Fraction (n, s) -> "Fraction", box [|box n; box (Option.toObj s)|]
            | TestResultValue.ByteSize s -> "ByteSize", box s
            | TestResultValue.AttachedFile (id, s) -> "AttachedFile", box [|box id; box s|]
            | TestResultValue.Anything str -> "Anything", box str
        writer.WriteStartObject()
        writer.WritePropertyName name
        serializer.Serialize(writer, object)
        writer.WriteEndObject()

    override x.ReadJson(reader: JsonReader, t: Type, existingValue: obj, serializer: JsonSerializer) =
        if reader.TokenType <> JsonToken.StartObject then failwith""
        reader.Read() |> ignore
        if reader.TokenType <> JsonToken.PropertyName then failwith""
        let name = reader.Value :?> string
        advance reader
        let r =
            match name with
            | "Time" -> TestResultValue.Time (serializer.Deserialize<TimeSpan>(reader))
            | "Number" ->
                let v = readElements(reader, [|typeof<float>; typeof<string>|], serializer)
                TestResultValue.Number(v.[0] :?> float, v.[1] :?> string |> Option.ofObj)
            | "Fraction" ->
                let v = readElements(reader, [|typeof<float>; typeof<string>|], serializer)
                TestResultValue.Fraction(v.[0] :?> float, v.[1] :?> string |> Option.ofObj)
            | "AttachedFile" ->
                let v = readElements(reader, [|typeof<Guid>; typeof<string []>|], serializer)
                TestResultValue.AttachedFile(v.[0] :?> Guid, v.[1] :?> string [])
            | "Anything" -> TestResultValue.Anything (serializer.Deserialize<string>(reader))
            | "ByteSize" -> TestResultValue.ByteSize (serializer.Deserialize<float>(reader))
            | wtf -> failwithf "Can't deserialize bazmek %s" wtf

        advance reader
        if reader.TokenType <> JsonToken.EndObject then failwithf "expected and of object %A" reader.TokenType

        box r


end

let converters = [|
        FableHacksJsonConverter() :> JsonConverter
        TestResultValueJsonConverter() :> JsonConverter
        Fable.JsonConverter() :> JsonConverter
    |]

let execOperationOnStore (store:IDocumentStore) (f: IDocumentSession -> Task<'a>) = task {
    use session = store.LightweightSession()
    let! result = f session
    do! session.SaveChangesAsync()
    return result
}

let execOperation (httpCtx:Microsoft.AspNetCore.Http.HttpContext) (f: IDocumentSession -> Task<'a>) =
    execOperationOnStore (httpCtx.RequestServices.GetRequiredService<IDocumentStore>()) f

let private initTables (store: IDocumentStore) =
    use session = store.LightweightSession()
    session.Query<TestDefEntity>() |> Seq.tryHead |> ignore
    session.Query<BenchmarkReport>() |> Seq.tryHead |> ignore
let connectMarten (connectionString:string) =
    let serializer = Marten.Services.JsonNetSerializer()
    serializer.EnumStorage <- EnumStorage.AsString
    serializer.Customize(fun x ->
        for c in converters do
            x.Converters.Add c
    )
    let store = DocumentStore.For(fun opt ->
        opt.Connection(connectionString)
        opt.Serializer(serializer)
        opt.CreateDatabasesForTenants(fun c ->
            // Specify a db to which to connect in case database needs to be created.
            // If not specified, defaults to 'postgres' on the connection for a tenant.
            c.MaintenanceDatabase connectionString |> ignore
            c.ForTenant()
                .CheckAgainstPgDatabase()
                .WithOwner("postgres")
                .WithEncoding("UTF-8")
                .ConnectionLimit(-1)
                .OnDatabaseCreated(fun _connection -> ())
                |> ignore
        );
    )
    (execOperationOnStore store UserService.seedUsersIfNeeded).Result

    // init tables that are used in SQL queries
    initTables store

    store

let removeEntity (id: Guid) check (s: IDocumentSession) = task {
    let! entity = s.LoadAsync(id)
    let ok = check entity
    if ok then
        s.Delete(id)
    return ok
}