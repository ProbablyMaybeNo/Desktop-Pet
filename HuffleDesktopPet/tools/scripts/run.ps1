#Requires -Version 5.1
<#
.SYNOPSIS
  Builds and launches the HuffleDesktopPet WPF application.
.EXAMPLE
  .\run.ps1
  .\run.ps1 -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\..\.."

Write-Host "Building HuffleDesktopPet ($Configuration)..." -ForegroundColor Cyan
dotnet build "$root\HuffleDesktopPet.sln" -c $Configuration --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    exit 1
}

Write-Host "Launching..." -ForegroundColor Green
dotnet run --project "$root\src\HuffleDesktopPet.App\HuffleDesktopPet.App.csproj" -c $Configuration --no-build
