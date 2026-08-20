# Squint - copied-link safety checker for Windows.
# Copyright (C) 2026 milkmade
# SPDX-License-Identifier: GPL-3.0-or-later

<#
.SYNOPSIS
    Builds dist\Squint-Setup.exe - the single file you hand to someone else.

.DESCRIPTION
    Publishes the app self-contained (the .NET runtime is bundled in, so the target machine
    needs nothing preinstalled), renders the wizard artwork, then compiles the Inno Setup
    script into one installer executable.

    Needs on THIS machine: the .NET SDK, and Inno Setup 6 (winget install JRSoftware.InnoSetup).
    Needs on THEIRS: nothing.

.EXAMPLE
    .\tools\build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = '1.3.0',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # Runtime packs only live on nuget.org; passed per-invocation so the global config is untouched.
    [string]$NugetSource = 'https://api.nuget.org/v3/index.json',

    # Leaves the published payload in place so reproducibility can be diffed layer by layer.
    [switch]$KeepPayload,

    # Every source file's modification time is recorded inside the installer, so a rebuilt
    # payload would change the output. Pinning it is what makes the build reproducible.
    # Override via SOURCE_DATE_EPOCH if you want to match some other build exactly.
    [datetime]$SourceDate = [datetime]::SpecifyKind('2020-01-01T00:00:00', 'Utc'),

    # Point at a specific ISCC.exe. Reproducing a published release needs the same Inno
    # version it was built with, which may not be the one on your PATH.
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'
$root        = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project     = Join-Path $root 'src\Squint\Squint.csproj'
$assets      = Join-Path $root 'src\Squint\Assets'
$installerDir = Join-Path $root 'installer'
$outDir      = Join-Path $root 'dist'
# Fixed, not $PID-suffixed: the path leaks into the publish output, so varying it makes the
# build unreproducible.
$payload     = Join-Path $env:TEMP 'Squint-payload'

function Step($m) { Write-Host "  $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "  $m" -ForegroundColor Green }

Write-Host "`n  Building Squint $Version installer ($Runtime)`n" -ForegroundColor White

# ---------------------------------------------------------------- tools
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is required to build. https://dotnet.microsoft.com/download'
}

if ($Iscc) {
    if (-not (Test-Path $Iscc)) { throw "No ISCC.exe at $Iscc" }
    $iscc = $Iscc
}
else {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $iscc) {
    throw 'Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup'
}

# ---------------------------------------------------------------- publish
Step 'Publishing self-contained (takes a minute)...'
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue

# PathMap rewrites the repo's absolute path to a fixed prefix inside the compiled assembly.
# Without it a checkout at C:\dev\squint and one at D:\squint\squint produce different
# bytes, which is exactly why a local build did not match CI.
#
# Single file: the whole app plus the .NET runtime collapse into one Squint.exe.
# Compression keeps the installed size sane; it self-extracts natives on first run.
& dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:Version=$Version `
    -p:Deterministic=true `
    -p:ContinuousIntegrationBuild=true `
    "-p:PathMap=$root\=/_/" `
    --source $NugetSource `
    -o $payload --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE)." }

$produced = @(Get-ChildItem $payload -Recurse -File)
$appExe = Join-Path $payload 'Squint.exe'

if (-not (Test-Path $appExe)) { throw 'Publish did not produce Squint.exe.' }

# Anything left beside the exe means it isn't actually single-file.
$strays = @($produced | Where-Object { $_.Name -ne 'Squint.exe' })
if ($strays.Count -gt 0) {
    throw "Publish left $($strays.Count) file(s) beside the exe: $($strays.Name -join ', ')"
}

$exeMb = [math]::Round((Get-Item $appExe).Length / 1MB, 1)
Ok "Published a single $exeMb MB Squint.exe (.NET bundled in)."

# ---------------------------------------------------------------- wizard artwork
# Inno's image control reads BMP only, and BMP has no alpha, so flatten onto the wizard's
# white background at the size the page draws them.
Step 'Rendering wizard artwork...'
Add-Type -AssemblyName System.Drawing

foreach ($name in 'verified', 'caution', 'suspect') {
    $src = [System.Drawing.Image]::FromFile((Join-Path $assets "$name.png"))
    $bmp = New-Object System.Drawing.Bitmap(80, 80)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($src, 0, 0, 80, 80)
    $g.Dispose()
    $bmp.Save((Join-Path $installerDir "$name.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose(); $src.Dispose()
}
Ok 'Artwork written.'

# ---------------------------------------------------------------- pin timestamps
# Inno stores the last-write time of every file it packs. Left alone, each rebuild produces
# a different installer even when every byte of input is otherwise identical.
if ($env:SOURCE_DATE_EPOCH) {
    $SourceDate = [datetimeoffset]::FromUnixTimeSeconds([long]$env:SOURCE_DATE_EPOCH).UtcDateTime
}

Step "Pinning input timestamps to $($SourceDate.ToString('u'))"
$stamped = 0
foreach ($file in @(Get-ChildItem $payload -Recurse -File) + @(Get-ChildItem $installerDir -File -Filter *.bmp)) {
    $file.LastWriteTimeUtc = $SourceDate
    $file.CreationTimeUtc = $SourceDate
    $stamped++
}
Ok "Pinned $stamped file(s)."

# ---------------------------------------------------------------- compile
Step 'Compiling installer...'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $iscc `
    "/DPayloadDir=$payload" `
    "/DOutputDir=$outDir" `
    "/DIconFile=$(Join-Path $assets 'app.ico')" `
    "/DAppVersion=$Version" `
    (Join-Path $installerDir 'Squint.iss')

if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)." }

if (-not $KeepPayload) { Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue }

$exe = Join-Path $outDir 'Squint-Setup.exe'
$mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host "`n  $exe" -ForegroundColor Green
Write-Host "  $mb MB - one file, nothing else needed.`n" -ForegroundColor Gray
