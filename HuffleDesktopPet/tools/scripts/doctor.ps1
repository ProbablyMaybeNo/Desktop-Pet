#Requires -Version 5.1
<#
.SYNOPSIS
  Diagnostics: prints versions of all relevant tools and environment info.
.EXAMPLE
  .\doctor.ps1
#>

$ErrorActionPreference = 'Continue'

Write-Host ""
Write-Host "=== HuffleDesktopPet Doctor ===" -ForegroundColor Cyan
Write-Host ""

# PowerShell
Write-Host "PowerShell  : $($PSVersionTable.PSVersion)" -ForegroundColor White

# OS
$os = [System.Environment]::OSVersion
Write-Host "OS          : $($os.VersionString)" -ForegroundColor White

# Git
try   { Write-Host "Git         : $(git --version)" -ForegroundColor White }
catch { Write-Host "Git         : NOT FOUND" -ForegroundColor Red }

# .NET
try {
    $sdks = dotnet --list-sdks
    Write-Host "dotnet SDKs :" -ForegroundColor White
    $sdks | ForEach-Object { Write-Host "              $_" }
} catch {
    Write-Host "dotnet      : NOT FOUND" -ForegroundColor Red
}

# .NET runtimes
try {
    $runtimes = dotnet --list-runtimes
    Write-Host "dotnet RTMs :" -ForegroundColor White
    $runtimes | ForEach-Object { Write-Host "              $_" }
} catch {}

# VS detection (quick)
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsInfo = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property displayName 2>$null
    Write-Host "VS          : $vsInfo" -ForegroundColor White
} else {
    Write-Host "VS          : vswhere not found (VS may not be installed)" -ForegroundColor Yellow
}

# Working directory
Write-Host "WorkDir     : $(Get-Location)" -ForegroundColor White
Write-Host ""
Write-Host "=== End Doctor ===" -ForegroundColor Cyan
Write-Host ""
