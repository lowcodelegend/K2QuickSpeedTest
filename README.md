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

## Reviewing Results

Run `query-results.sql` against SQL Server after the test. The query returns one row per batch with:

- total process count
- earliest start timestamp
- latest end timestamp
- total duration in milliseconds
- calculated processes per second

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
