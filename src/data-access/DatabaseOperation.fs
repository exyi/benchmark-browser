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

let converters = [|
        FableHacksJsonConverter() :> JsonConverter
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