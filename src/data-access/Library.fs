module DataAccess.DbUtils
open Marten
open System.Threading
let sqlQuery<'a> sql parameters (session: IDocumentSession) =
    session.QueryAsync<'a>(sprintf "SELECT row_to_json(foo) as data FROM (%s) AS foo" sql, CancellationToken.None, parameters)
