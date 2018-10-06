namespace api

open System
open System.Collections.Generic
open System.IO
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open System.Net

module Program =
    let exitCode = 0

    let BuildWebHost args =
        WebHost
            .CreateDefaultBuilder(args)
            .UseStartup<Startup>()
            // .UseUrls("http://*:5000")
            .UseKestrel(fun options ->
                options.Listen(IPAddress.Any, 5000)
                if IO.File.Exists "certificate.pfx" then
                    printfn "Running https on port 5433"
                    options.Listen(IPAddress.Any, 5443, fun loptions ->
                        loptions.UseHttps("certificate.pfx", "123") |> ignore
                        ()
                    )

            )
            .Build()

    [<EntryPoint>]
    let main args =
        BuildWebHost(args).Run()

        exitCode
