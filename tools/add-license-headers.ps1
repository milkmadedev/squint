<#
.SYNOPSIS
    Prepends the GPL SPDX notice to every source file. Idempotent - safe to re-run.

.NOTES
    Uses .NET file APIs with an explicit UTF-8 (no BOM) encoding on both read and write.
    Get-Content/Set-Content would round-trip through the ANSI codepage on Windows PowerShell
    and mangle every non-ASCII character in the sources.
#>
[CmdletBinding()]
param(
    [string]$Holder = 'milkmade',
    [string]$Year = '2026'
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$lines = @(
    'Squint - copied-link safety checker for Windows.'
    "Copyright (C) $Year $Holder"
    'SPDX-License-Identifier: GPL-3.0-or-later'
)

# Extension -> line comment marker.
$markers = @{
    '.cs'  = '//'
    '.ps1' = '#'
    '.iss' = ';'
}

$changed = 0
$skipped = 0

Get-ChildItem $root -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|dist|\.git)\\' -and $markers.ContainsKey($_.Extension) } |
    ForEach-Object {
        $marker = $markers[$_.Extension]
        $text = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)

        if ($text -match 'SPDX-License-Identifier') { $skipped++; return }

        $header = ($lines | ForEach-Object { "$marker $_" }) -join "`r`n"
        [System.IO.File]::WriteAllText($_.FullName, "$header`r`n`r`n$text", $utf8NoBom)

        Write-Host "  + $($_.FullName.Replace("$root\", ''))" -ForegroundColor Green
        $changed++
    }

Write-Host "`n  $changed file(s) stamped, $skipped already had a notice.`n"
