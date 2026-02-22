<#
.SYNOPSIS
    Slices a sprite sheet into individual 64x64 frame PNGs for HuffleDesktopPet.

.DESCRIPTION
    Wrapper around the HuffleDesktopPet.SpriteProcessor console tool.
    Builds the tool on first run, then invokes it with your parameters.

    The sheet must be a grid of equal-sized frames:
      - Columns = frames in one animation cycle
      - Rows    = animation states (e.g. walk on row 1, idle on row 2)

    Output files follow the naming convention used by the pet:
      huffle_{state}_{frame:D2}.png   (e.g. huffle_walk_01.png)

.PARAMETER Sheet
    Path to the sprite sheet PNG (required).

.PARAMETER Cols
    Number of frame columns in the sheet (required).

.PARAMETER Rows
    Number of animation-state rows in the sheet. Default: 1.

.PARAMETER States
    Comma-separated name for each row, matching the order top-to-bottom.
    Example: "walk,idle"  or  "walk,idle,eat,play,clean,sleep"
    Default: state0, state1, ... if not provided.

.PARAMETER Out
    Output directory for the cropped frames.
    Default: a "sprites" folder created next to the sheet file.

.PARAMETER Size
    Output pixel size (square). Default: 64  (produces 64x64 PNGs).

.PARAMETER Pet
    Filename prefix. Default: huffle.

.PARAMETER Padding
    Transparent pixel gap between frames in the sheet. Default: 0.

.PARAMETER Margin
    Transparent border around the entire sheet. Default: 0.

.EXAMPLE
    # One-state sheet: 4 walk frames on a single row
    .\process-sprites.ps1 -Sheet C:\sprites\walk.png -Cols 4 -States walk

.EXAMPLE
    # Two-state sheet: walk on row 1, idle on row 2, 2px padding between frames
    .\process-sprites.ps1 -Sheet C:\sprites\sheet.png -Cols 4 -Rows 2 -States walk,idle -Padding 2

.EXAMPLE
    # Full multi-state sheet, custom output directory
    .\process-sprites.ps1 -Sheet C:\sprites\huffle_full.png `
        -Cols 4 -Rows 8 `
        -States walk,idle,eat,play,clean,sleep,sad,happy `
        -Out C:\Desktop-Pet\HuffleDesktopPet\assets\sprites

#>
param(
    [Parameter(Mandatory)]
    [string]$Sheet,

    [Parameter(Mandatory)]
    [int]$Cols,

    [int]$Rows       = 1,
    [string]$States  = "",
    [string]$Out     = "",
    [int]$Size       = 64,
    [string]$Pet     = "huffle",
    [int]$Padding    = 0,
    [int]$Margin     = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Locate processor project ──────────────────────────────────────────────────

$scriptDir     = Split-Path -Parent $MyInvocation.MyCommand.Path
$processorProj = Resolve-Path (Join-Path $scriptDir "../../src/HuffleDesktopPet.SpriteProcessor/HuffleDesktopPet.SpriteProcessor.csproj")

# ── Build the processor (cached by MSBuild, fast after first run) ─────────────

Write-Host "Building sprite processor..." -ForegroundColor DarkGray
& dotnet build $processorProj --configuration Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to build SpriteProcessor." -ForegroundColor Red
    exit $LASTEXITCODE
}

# ── Assemble arguments ────────────────────────────────────────────────────────

$Sheet = (Resolve-Path $Sheet).Path

$toolArgs = @(
    $Sheet
    "--cols",   $Cols
    "--rows",   $Rows
    "--size",   $Size
    "--pet",    $Pet
    "--padding",$Padding
    "--margin", $Margin
)

if ($States -ne "") { $toolArgs += @("--states", $States) }
if ($Out    -ne "") { $toolArgs += @("--out",    $Out)    }

# ── Run ───────────────────────────────────────────────────────────────────────

& dotnet run --project $processorProj --configuration Release --no-build -- @toolArgs
exit $LASTEXITCODE
