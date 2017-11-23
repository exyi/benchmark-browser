namespace api
open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.JwtBearer
open Microsoft.Extensions.DependencyInjection
open Giraffe.HttpHandlers
open Giraffe.Middleware
open Microsoft.IdentityModel.Tokens


type Startup() =
    member x.ConfigureServices(services: IServiceCollection) =
        let key = SymmetricSecurityKey(Text.Encoding.UTF8.GetBytes("lkasjdlkadjlkdfjglasjkfdsa")) :> SecurityKey
        services.AddSingleton({ Authentication.SecretKey.Key = key }) |> ignore
        services.AddDataProtection() |> ignore
        services.AddCors() |> ignore
        let auth = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        auth.AddJwtBearer(fun opt ->
            // opt.TokenValidationParameters <- new IdentityModel.Tokens.TokenValidationParameters()
            opt.TokenValidationParameters.ValidateActor <- true
            opt.TokenValidationParameters.ValidateAudience <- false
            opt.TokenValidationParameters.ValidateIssuer <- false
            opt.TokenValidationParameters.ValidateIssuerSigningKey <- true
            opt.TokenValidationParameters.ValidateLifetime <- true
            // opt.TokenValidationParameters.ValidIssuer <- "S"
            opt.TokenValidationParameters.IssuerSigningKey <- key
        ) |> ignore

        services.AddSingleton<Marten.IDocumentStore>(DataAccess.DatabaseOperation.connectMarten "host=localhost;database=perf-tests") |> ignore

        ()

    member x.Configure(app: IApplicationBuilder, env: IHostingEnvironment) =
        if env.IsDevelopment() then app.UseDeveloperExceptionPage() |> ignore

        app.UseCors(fun b ->
            b.AllowAnyOrigin() |> ignore
            b.AllowAnyMethod() |> ignore
            b.AllowAnyHeader() |> ignore
            b.AllowCredentials() |> ignore
            ()
        ) |> ignore

        app.UseAuthentication() |> ignore

        app.UseGiraffe(ApiRouter.webApp)

        app.Run(fun context -> context.Response.WriteAsync("Hello World!"))

        ()