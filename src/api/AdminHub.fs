module AdminHub
open Microsoft.AspNetCore.Http
open DataAccess
open PublicModel.ProjectManagement

let createTestDefinition (ctx: HttpContext) form =
    let userId = Authentication.getCurrentUserId ctx
    DatabaseOperation.execOperation ctx (WorkerTaskService.createTaskDefinition userId form)

let removeTestDefinition (ctx: HttpContext) id =
    let userId = Authentication.getCurrentUserId ctx

    DatabaseOperation.execOperation ctx
        (DatabaseOperation.removeEntity id
            (fun (e: TestDefEntity) -> e.OwnerId = userId))