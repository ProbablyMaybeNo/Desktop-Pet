#Requires -Version 5.1
<#
.SYNOPSIS
  Runs the xUnit unit test suite for HuffleDesktopPet.Core.
.EXAMPLE
  .\test.ps1
  .\test.ps1 -Verbose
#>
param(
    [switch]$Verbose,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\..\.."

$args = @(
    'test',
    "$root\src\HuffleDesktopPet.Tests\HuffleDesktopPet.Tests.csproj",
    '--configuration', 'Debug',
    '--logger', 'console;verbosity=normal',
    '--no-restore'
)

if ($NoBuild) { $args += '--no-build' }
if ($Verbose)  { $args += '--logger'; $args += 'console;verbosity=detailed' }

Write-Host "Running unit tests..." -ForegroundColor Cyan
dotnet @args

if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests FAILED." -ForegroundColor Red
    exit 1
}

Write-Host "All tests passed." -ForegroundColor Green
