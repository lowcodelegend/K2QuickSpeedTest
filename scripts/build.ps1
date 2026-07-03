param(
    [string]$Configuration = "Release",
    [string]$DotNet = ".\.dotnet\dotnet.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "K2.QuickSpeedTest.sln"

if (-not (Test-Path $DotNet)) {
    $DotNet = "dotnet"
}

& $DotNet build $solution --configuration $Configuration
