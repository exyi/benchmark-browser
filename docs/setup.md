## How to launch the application

### Prerequisites

To build and run the application, you will need Dotnet Core 2.0, Mono, Yarn, PostgreSQL, perl, git and maybe something else. It is known to run on Arch Linux, and it will maybe work on other systems without any problems.


### Web site

To run the website, you will need a PostgreSQL database. No initialization is needed, it's not automatically by the server (so it will need rights for creating tables in the DB). Then clone the repository and open the `src/api/appsettings.json` file. You need to change the `ApiPrivateKey` to a randomly generated key, it's used to encrypt authentication tokens. And you will probably also need to change the `ConnectionString` field to be able to connect to a database.

Then run the `./launch.fish` script - it should build the Fable app and start the Asp.Net Core server on port 5000.

Then navigate to the website, log in as `admin@whatever.com` with password `test-password` and change the passoword before opening the port on firewall. Note that without changing the password, all operating should be forbidden. Now, you can create new users and tasks.

### Worker

To get some test result you will need a worker.

Create (and activate) a new user with a `Worker` role. You can use the admin user, but you should not :)

Open the `src/worker/config.json` file and configure that.

Now, it should be enough to run the `./run-worker.fish` script. It's recommended to run it on a separate mashine to get deterministic results - docker container on a not-very-bussy mashine should be quite OK, vut VM's in a cloud does not seem to be a good choice.

You can run multiple worker for one server, but they should run on the same type of hardware (and software), as comparing results from different mashine does not make much sense.

Now, the only think missing are the benchmarks to run, the worker can currently only gather results from BenchmarkDotNet style exports, but can run any script that will behave like a BenchmarkDotNet process. Also check out a [json exporter used at dotvvm-benchmarks](https://github.com/riganti/dotvvm-benchmarks/blob/1934e7d3c6f1f313baab8097c694a40dc906ea59/DotVVM.Benchmarks/MyJsonExporter.cs), it exports a bit more info (includes columns in the export). You may also find the diagnosers the are used here interesting.

