<#
.SYNOPSIS
    Publishes HuffleDesktopPet as a self-contained, single-file Windows executable.

.DESCRIPTION
    Produces a standalone HuffleDesktopPet.exe in the publish/ directory.
    No .NET runtime installation required on the target machine.

.PARAMETER Runtime
    Windows target RID. Defaults to win-x64. Use win-arm64 for ARM devices.

.PARAMETER OutputDir
    Output directory for the published files. Defaults to publish/ at the solution root.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Runtime win-arm64

#>
param(
    [string]$Runtime   = "win-x64",
    [string]$OutputDir = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Resolve paths ─────────────────────────────────────────────────────────────

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Resolve-Path (Join-Path $scriptDir "../../..")
$slnDir    = Join-Path $repoRoot "HuffleDesktopPet"
$appCsproj = Join-Path $slnDir  "src/HuffleDesktopPet.App/HuffleDesktopPet.App.csproj"

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "publish"
}

Write-Host ""
Write-Host "=== HuffleDesktopPet — Publish ===" -ForegroundColor Cyan
Write-Host "  Runtime   : $Runtime"
Write-Host "  Output    : $OutputDir"
Write-Host ""

# ── Clean output directory ────────────────────────────────────────────────────

if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}

# ── Publish ───────────────────────────────────────────────────────────────────

$publishArgs = @(
    "publish"
    $appCsproj
    "--configuration", "Release"
    "--runtime",       $Runtime
    "--self-contained", "true"
    "--output",        $OutputDir
    "-p:PublishSingleFile=true"
    "-p:PublishReadyToRun=true"
    "-p:IncludeNativeLibrariesForSelfExtract=true"
    "--nologo"
)

Write-Host "Running: dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
Write-Host ""

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: publish failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

# ── Report ────────────────────────────────────────────────────────────────────

$exe = Get-ChildItem $OutputDir -Filter "*.exe" | Select-Object -First 1

Write-Host ""
Write-Host "=== Publish complete ===" -ForegroundColor Green

if ($exe) {
    $sizeMb = [math]::Round($exe.Length / 1MB, 1)
    Write-Host "  Executable : $($exe.FullName)"
    Write-Host "  Size       : $sizeMb MB"
} else {
    Write-Host "  Output dir : $OutputDir"
}

Write-Host ""
Write-Host "To enable 'Start with Windows', launch the app once and toggle it from the tray icon." -ForegroundColor Yellow
Write-Host ""
