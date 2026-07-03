# K2 QuickSpeedTest

K2 QuickSpeedTest is a small Windows console utility for running repeatable throughput tests against K2/Nintex K2 workflows. It starts multiple process instances across configurable worker threads and records timing through the supplied K2 package and SQL database objects.

This project is intended to be distributed as a pre-built Windows release zip so most users do not need to build the source.

## Quick Start With The Pre-Built Release

1. Download `QuickSpeedTest-win-x64.zip` from the GitHub release. In this prepared working copy, the same zip is generated at `artifacts/QuickSpeedTest-win-x64.zip`.
2. Extract the zip on a Windows machine that can reach the K2 Host Server.
3. Import `QuickSpeedTest v0.1.kspx` from the extracted folder into the target K2 environment.
4. Run `setup-database.sql` from the extracted folder against SQL Server to create the `QuickSpeedTest` database and stored procedure.
5. Copy `appSettings.example.json` to `appSettings.json` in the extracted release folder.
6. Edit `appSettings.json` for your environment.
7. Run `K2.QuickSpeedTest.exe`.

Example `appSettings.json`:

```json
{
  "Host": "k2-server-name",
  "Port": 5252,
  "UserID": "DOMAIN\\username",
  "Password": "your-password",
  "Integrated": false
}
```

Do not commit `appSettings.json`. It is ignored by Git because it can contain credentials.

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

When `--process` does not contain a folder separator, the app prefixes it with `QuickSpeedTest\`. For example, `Throughput-SQL` becomes `QuickSpeedTest\Throughput-SQL`.

Useful options:

```text
--config, -c       Path to config file. Defaults to appSettings.json.
--threads, -t      Number of concurrent worker threads.
--iterations, -i   Number of process starts per thread.
--batch, -b        Batch name stored with each test result.
--process, -p      K2 process name or full K2 process path.
```

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
- .NET 8 SDK
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

Create a GitHub release with the pre-built zip attached:

```powershell
.\scripts\create-github-release.ps1 -Tag v0.1.0 -Title "K2 QuickSpeedTest v0.1.0"
```

## Security

This project intentionally ships only `appSettings.example.json`. Keep real server names, usernames, and passwords in your local `appSettings.json` only.
