#Requires -Version 5.1
<#
.SYNOPSIS
  Checks all development dependencies for HuffleDesktopPet.
  Prints actionable guidance if anything is missing.
.EXAMPLE
  .\bootstrap.ps1
#>

$ErrorActionPreference = 'Stop'
$ok = $true

function Test-Tool {
    param([string]$Name, [scriptblock]$Check, [string]$InstallHint)
    try {
        $result = & $Check
        Write-Host "  [OK]  $Name  $result" -ForegroundColor Green
    } catch {
        Write-Host "  [!!]  $Name not found." -ForegroundColor Red
        Write-Host "        $InstallHint" -ForegroundColor Yellow
        $script:ok = $false
    }
}

Write-Host ""
Write-Host "=== HuffleDesktopPet Bootstrap Check ===" -ForegroundColor Cyan
Write-Host ""

# Git
Test-Tool "Git" {
    (git --version) -replace 'git version ', ''
} "Install from https://git-scm.com/download/win"

# .NET SDK 8
Test-Tool ".NET 8 SDK" {
    $v = dotnet --version
    if ($v -notmatch '^8\.') { throw "Expected 8.x, got $v" }
    $v
} "Install from https://dotnet.microsoft.com/en-us/download/dotnet/8.0"

# dotnet workload for WPF (desktop)
Test-Tool ".NET Desktop Workload" {
    $info = dotnet --info
    if ($info -notmatch 'Microsoft.NET.Sdk.WindowsDesktop') { throw "Desktop workload missing" }
    "present"
} "Run: dotnet workload install microsoft-net-desktop (or install Visual Studio 2022 with '.NET desktop development')"

# PowerShell 5+
Test-Tool "PowerShell" {
    "v$($PSVersionTable.PSVersion)"
} "Already running in PowerShell, this should never fail."

Write-Host ""
if ($ok) {
    Write-Host "All checks passed. You're ready to build!" -ForegroundColor Green
} else {
    Write-Host "Fix the issues above, then re-run this script." -ForegroundColor Red
    exit 1
}
