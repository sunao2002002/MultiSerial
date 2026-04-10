[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$SkipZip,
    [string]$PackageLabel,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptRoot "SerialApp.Desktop.csproj"
$artifactsRoot = Join-Path $scriptRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "publish"
$packageRoot = Join-Path $artifactsRoot "packages"
$publishDir = Join-Path $publishRoot ("{0}-{1}" -f $Configuration, $Runtime)

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet command was not found. Install .NET SDK 10 first."
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

$restoreArgs = @("restore", $projectPath)
$publishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $(if ($SelfContained) { "true" } else { "false" }),
    "-o", $publishDir
)

Write-Host "Project: $projectPath"
Write-Host "Configuration: $Configuration"
Write-Host "Runtime: $Runtime"
Write-Host "SelfContained: $SelfContained"
Write-Host "PublishDir: $publishDir"

if (-not $NoRestore) {
    Write-Host "Restoring dependencies..."
    & dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }
}

Write-Host "Publishing release output..."
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$zipPath = $null

if (-not $SkipZip) {
    $label = if ([string]::IsNullOrWhiteSpace($PackageLabel)) {
        Get-Date -Format "yyyyMMdd-HHmmss"
    }
    else {
        $PackageLabel
    }

    $zipName = "SerialApp.Desktop-{0}-{1}-{2}.zip" -f $Configuration, $Runtime, $label
    $zipPath = Join-Path $packageRoot $zipName

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Write-Host "Creating zip package..."
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
}

Write-Host "Publish completed."
Write-Host "OutputDir: $publishDir"

if ($zipPath) {
    Write-Host "ZipPath: $zipPath"
}