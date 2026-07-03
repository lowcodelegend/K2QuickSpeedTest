# K2 QuickSpeedTest

K2 QuickSpeedTest is a small Windows console utility for running repeatable throughput tests against K2/Nintex K2 workflows. It starts multiple process instances across configurable worker threads and records timing through the supplied K2 package and SQL database objects.

This project is intended to be distributed as a pre-built Windows release zip so most users do not need to build the source.

## Quick Start With The Pre-Built Release

1. Download `assets/QuickSpeedTest-win-x64.zip` from this repository, or download the same zip from the GitHub release if one has been published.
2. Extract the zip on a Windows machine that can reach the K2 Host Server.
3. Import `QuickSpeedTest v0.1.kspx` from the extracted folder into the target K2 environment.
4. Run `setup-database.sql` from the extracted folder against SQL Server to create the `QuickSpeedTest` database and stored procedure.
5. Run `K2.QuickSpeedTest.exe`.

If `appSettings.json` is not present, the app prompts for connection details when it starts. To avoid entering them each time, copy `appSettings.example.json` to `appSettings.json` in the extracted release folder and edit it for your environment.

The pre-built executable targets .NET Framework 4.8. This is intentional because the K2 client assemblies used by the tool are .NET Framework-era assemblies. If the app exits with a shutdown-time `SEHException`, make sure you are using the current release zip from `assets/`.

Example `appSettings.json`:

```json
{
  "Host": "k2-server-name",
  "Port": 5252,
  "UserID": "DOMAIN\\username",
  "Password": "your-password",
  "Integrated": false,
  "SecurityLabelName": ""
}
```

`SecurityLabelName` is optional. Leave it blank to let K2 use the environment's default security label. Set it only when your K2 environment requires a specific label.

Do not commit `appSettings.json`. It is ignored by Git because it can contain credentials.

When no `appSettings.json` file exists, the interactive defaults are:

```text
Server: localhost
Username: administrator
Integrated authentication: true
Security label: blank, using K2 default
```

## Running A Test

The app can run interactively:

```powershell
.\K2.QuickSpeedTest.exe
```

Interactive mode asks for:

- **Number of threads**: how many concurrent workers should start workflow instances.
- **Iterations per thread**: how many workflow instances each worker should start.
- **Batch name**: a label used to group this test run in SQL results.
- **Process**: the K2 workflow to start.

The total number of workflow instances started is:

```text
threads x iterations per thread
```

For example, `10` threads and `50` iterations starts `500` workflow instances.

It can also run non-interactively:

```powershell
.\K2.QuickSpeedTest.exe --threads 10 --iterations 50 --batch "baseline-001" --process "Throughput-SQL"
```

When a run starts, the app prints the target server, authentication mode, selected process, batch name, and per-thread connection progress. During larger runs it periodically reports how many process instances have started.

If a thread cannot connect to K2 or a process start fails, the app prints the underlying error message and an error summary instead of only reporting a generic failure. If the failure looks authentication-related and the app is running interactively, it prompts for connection settings again and retries the run.

When `--process` does not contain a folder separator, the app prefixes it with `QuickSpeedTest\`. For example, `Throughput-SQL` becomes `QuickSpeedTest\Throughput-SQL`.

Useful options:

```text
--config, -c       Path to config file. Defaults to appSettings.json.
--threads, -t      Number of concurrent worker threads.
--iterations, -i   Number of process starts per thread.
--batch, -b        Batch name stored with each test result.
--process, -p      K2 process name or full K2 process path.
```

## How The Test Works

QuickSpeedTest measures how quickly K2 can start and complete a simple measured workflow path under concurrent load.

For each requested process instance, the console app:

1. Opens one K2 client connection per worker thread.
2. Creates a K2 process instance.
3. Sets two process data fields:
   - `starttime`: the current UTC time from the client machine, stored as Unix epoch milliseconds.
   - `batchname`: the test run label.
4. Starts the process instance.
5. The supplied `Throughput-SQL` workflow calls `[dbo].[sp_InsertSpeedTestResult]`.
6. SQL Server inserts a result row with the original start time, batch name, and SQL Server's current UTC end time.

The result is a simple end-to-end measurement of workflow start and straight-through execution into SQL.

The test includes:

- K2 client API overhead
- K2 authentication/session setup per worker thread
- process instance creation
- workflow start handling
- data field assignment
- workflow execution through the supplied measured path
- SmartObject/service broker overhead
- SQL stored procedure execution
- SQL insert latency

The test does not measure:

- browser or form load performance
- user task completion time
- isolated SQL Server insert performance
- isolated K2 Host Server performance
- a full business workflow unless you point the tool at one

The console output reports how many process starts succeeded from the client app's perspective. The SQL results show how many workflow instances actually reached the measured SQL logging step. When interpreting a run, treat the SQL row count as the authoritative completion count.

## Choosing Test Values

Start with a small test to confirm the setup is working:

```powershell
.\K2.QuickSpeedTest.exe --threads 1 --iterations 5 --batch "smoke-test" --process "Throughput-SQL"
```

Then increase gradually:

```powershell
.\K2.QuickSpeedTest.exe --threads 5 --iterations 20 --batch "baseline-5x20" --process "Throughput-SQL"
.\K2.QuickSpeedTest.exe --threads 10 --iterations 50 --batch "baseline-10x50" --process "Throughput-SQL"
```

Use consistent batch names when comparing environments or changes. Good batch names include the test purpose, thread count, iteration count, and any environment detail that matters:

```text
dev-sql-5x20
uat-task-10x50-before-index-change
prod-sql-20x100-after-maintenance
```

Avoid starting with very high thread counts. The tool is designed to apply load, so large tests can create a real workload on K2, SQL Server, and downstream systems used by the workflow.

## Reviewing Results

Run `query-results.sql` against SQL Server after the test. In SQL Server Management Studio:

1. Connect to the SQL Server that hosts the `QuickSpeedTest` database.
2. Open `query-results.sql`.
3. Execute the query.
4. Review the row for your batch name.

The query returns one row per batch with:

- total process count
- earliest start timestamp
- latest end timestamp
- total duration in milliseconds
- calculated processes per second

### Result Columns

`BatchName` is the test run label entered in the console or passed with `--batch`. Use this to compare runs.

`TotalProcesses` is the number of rows recorded for the batch. It should match `threads x iterations`. If it is lower than expected, some workflow instances may not have completed the SQL logging step.

`EarliestStart` is the first recorded start timestamp for the batch, stored as Unix epoch milliseconds.

`LatestEnd` is the last recorded end timestamp for the batch, stored as Unix epoch milliseconds.

`TotalDurationMS` is the elapsed time between the first process start and the last process result being inserted.

`ProcessesPerSecond` is the main throughput number. It is calculated as:

```text
TotalProcesses / (TotalDurationMS / 1000)
```

Higher is better when comparing the same process type under similar conditions.

`StartTime` is captured by the client machine running `K2.QuickSpeedTest.exe`. `EndTime` is captured by SQL Server. If those machines have different clocks, very small tests can produce slightly misleading durations. Larger test runs reduce the impact of small clock differences.

### Example Interpretation

If a batch returns:

```text
TotalProcesses: 500
TotalDurationMS: 25000
ProcessesPerSecond: 20
```

That means 500 process instances completed the measured path in 25 seconds, averaging about 20 completed instances per second.

When comparing two runs, compare batches that use the same process, thread count, iteration count, and environment conditions. For example:

```text
baseline-10x50-before-change   18.4 processes/sec
baseline-10x50-after-change    24.1 processes/sec
```

This suggests the second run had better throughput for that specific test shape.

### What To Watch For

- If `TotalProcesses` is lower than expected, check the console output for failed starts and confirm the workflow can reach the SQL stored procedure.
- If `ProcessesPerSecond` drops as thread count increases, the environment may be hitting a bottleneck.
- If results vary heavily between runs, repeat the same batch shape several times and compare averages.
- If `TotalDurationMS` is very small, use more iterations. Tiny tests are useful for smoke testing but not for meaningful throughput comparisons.
- If no rows appear, confirm the K2 package was imported, the SQL setup script was run, and the workflow is writing to the expected database.

### Clearing Or Filtering Results

The results table is append-only by default. Keep old rows if you want long-term comparison data.

To view one batch only:

```sql
SELECT *
FROM [dbo].[SpeedTestResults]
WHERE [BatchName] = 'your-batch-name'
ORDER BY [Id];
```

To remove a test batch:

```sql
DELETE FROM [dbo].[SpeedTestResults]
WHERE [BatchName] = 'your-batch-name';
```

Use deletes carefully if the database is shared with other testers.

## Included Files

- `QuickSpeedTest v0.1.kspx`: K2 package containing the sample workflows.
- `scripts/setup-database.sql`: SQL database/table/procedure setup.
- `scripts/query-results.sql`: throughput summary query.
- `K2.QuickSpeedTest`: source code for the console runner.
- `scripts/publish.ps1`: creates the Windows release zip.

## Building From Source

Most users should use the pre-built zip. Build only if you want to modify the utility.

Prerequisites:

- Windows
- .NET 8 SDK for building
- .NET Framework 4.8 runtime for running the built executable
- K2/Nintex K2 client assemblies installed under `C:\Program Files\K2\Host Server\Bin`

Build:

```powershell
.\scripts\build.ps1
```

Create a release zip:

```powershell
.\scripts\publish.ps1
```

The generated zip is written to `artifacts/QuickSpeedTest-win-x64.zip`.

The repository copy used by end users is stored at `assets/QuickSpeedTest-win-x64.zip`. After publishing a new build, copy the generated zip into `assets/` before committing:

```powershell
Copy-Item .\artifacts\QuickSpeedTest-win-x64.zip .\assets\QuickSpeedTest-win-x64.zip -Force
```

Create a GitHub release with the pre-built zip attached:

```powershell
.\scripts\create-github-release.ps1 -Tag v0.1.0 -Title "K2 QuickSpeedTest v0.1.0"
```

## Security

This project intentionally ships only `appSettings.example.json`. Keep real server names, usernames, and passwords in your local `appSettings.json` only.
