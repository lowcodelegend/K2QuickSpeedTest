param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [string]$Title = $Tag,
    [string]$ZipPath = ".\artifacts\QuickSpeedTest-win-x64.zip"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI is required. Install it from https://cli.github.com/ and run 'gh auth login'."
}

if (-not (Test-Path $ZipPath)) {
    throw "Release zip was not found at '$ZipPath'. Run .\scripts\publish.ps1 first."
}

gh release create $Tag $ZipPath --title $Title --notes "Pre-built Windows x64 release for K2 QuickSpeedTest."
