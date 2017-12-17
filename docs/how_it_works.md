## Internals

If you are interested how does this system work, there is a source code in this repository :P And apart of that, there is this document to help you understand the overall architecture.

### Overview
The application has 3 main parts
* Fable Elmish client - the web user interface
* worker - a quite simple app that fetches some work from the queue, executes the tests, gathers results and sends them to server
* api server - a Asp.Net Core HTTP server that collects and aggregates the data

Then there is a PostgreSQL database and only the api server can access it. Plus the system administrator can do so, and it's the recommended UI if something goes wrong... The data schema may seem a bit abused at a first glance, but more on that later:

### Data model

The data are stored in a PostgreSQL database, in a bit unorthodox way - all tables look the same, only have a `id` and `data` columns and the data contains a JSON document. However Postgres has amazing JSON support, so I decided to use it as a document database. A library called [Marten](https://github.com/JasperFx/marten) for Object-Database mapping.

The centre of everything is a `BenchmarkReport` record (mapped to a database using Marten). It is created when the worker submits some results and is later used for all reports. Tested versions and tested projects are not stored anywhere, these are just views on the report table (using a GROUP BY over the version/project).

All other tables are quite intuitive IMO, User is a user, TestDef is a test definiton and Queue is a queue for the worker...

### API

Similarly to the database, HTTP API might seem a bit abused... All requests are `POST` and return `OK 200` with a JSON document (except when the server crashes with an exception, then there is 500 without description). But there is a good reason for that (IMO) - it can be easily mapped into simple F# functions, so the API is very easy to use from the Fable client and F# server. There is a list of endpoints (declared types can be found the `src/public-model` and are serialized using Fable converter):

* `login` : LoginData -> LoginResult * (string option) // the optional string is a API JWT token that is used for bearer authentication. It is returned when the login is successful
* `changePassword` : ChangePasswordRequest -> ChangePasswordResponse // required authentication
* `home` : unit -> HomePageModel // supports GET request
* `project/dashboard` : string -> DashboardModel // the string is project ID (root commit hash)
* `testdef/dashboard` : string -> DashboardModel // the string is task definition (Guid or "user friendly id")
* `getReports` : ReportGroupSelector -> (BenchmarkReport[] * ReportGroupDetails)
* `compareReports` : (ReportGroupSelector * ReportGroupSelector)-> (VersionComparisonSummary * BenchmarkReport[] * BenchmarkReport[] * ReportGroupDetails * ReportGroupDetails)
* `files/<type>?<files>` : unit -> gets the specified files aggregated into archive of `<type>` (supported are `zip` and `flame`graph)
* `pushResults` : WorkerSubmission[] -> ImportResult[] // requires auth with role Worker
* `getMeSomeWork` : unit -> WorkerQueueItem // requires auth with role Worker, dequeues item from the worker queue and allocates that (items are automatically dealocated after a day of inactivity)
* `pushWorkStatus` : WorkStatusInfo -> unit // required auth with role Worker, updates the work status, may mark the queue item as resolved, so it will not be deallocated after a day
* ... then there is a administration api, which is undocumented

### Worker

Worker is a application used to run the tests and collect results. It can currently only gather results from BenchmarkDotNet style exports, but can run any script that will behave like a BenchmarkDotNet process. It is a bit messy thing in the `Program.fs` file, but it seems to work.

### Web client

The web UI is build using Fable Elmish and React, it is based on the fable-elmish-react template and has simmilar structure. It also uses Bulma.css for almost all styling. If you are familiar with Elmish, it should not be much unexpected, I hope. Only "strange" think is the `UpdateMsg` type, that is used everywhere instead of proper actions, because I quickly realized that I'm too lazy to write the actions and updates...

The most complex part is probably the `ComparisonDetail` with the interactive grid, but I think that the structure is quite obvious from model type definitions.


