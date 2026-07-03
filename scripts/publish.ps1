param(
    [string]$Configuration = "Release",
    [string]$DotNet = ".\.dotnet\dotnet.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "K2.QuickSpeedTest\K2.QuickSpeedTest.csproj"
$output = Join-Path $root "artifacts\publish\QuickSpeedTest-win-x64"
$zip = Join-Path $root "artifacts\QuickSpeedTest-win-x64.zip"

if (-not (Test-Path $DotNet)) {
    $DotNet = "dotnet"
}

if (Test-Path $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
& $DotNet publish $project --configuration $Configuration --output $output

Copy-Item -LiteralPath (Join-Path $root "K2.QuickSpeedTest\appSettings.example.json") -Destination (Join-Path $output "appSettings.example.json") -Force
Copy-Item -LiteralPath (Join-Path $root "QuickSpeedTest v0.1.kspx") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\setup-database.sql") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\query-results.sql") -Destination $output -Force

if (Test-Path $zip) {
    Remove-Item -LiteralPath $zip -Force
}

Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zip -Force
Write-Host "Created $zip"
