#Requires -Version 7.0
<#
.SYNOPSIS
    Ensures the LiteDB core project stays free of direct "Vector" references outside plugin extension points.

.DESCRIPTION
    Scans the specified source roots (defaults to LiteDB/) for the literal string "Vector" using ripgrep
    and fails when any occurrences remain outside the optional allow list. Use the allow list to suppress
    intentional matches inside shared extension-point definitions (format: pathPattern[::lineRegex]).

.EXAMPLE
    ./scripts/verify-vector-clean.ps1

.EXAMPLE
    ./scripts/verify-vector-clean.ps1 -AllowPatterns 'LiteDB/Plugins/**'
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,

    [string]$AllowListPath = (Join-Path $PSScriptRoot "verify-vector-clean.allowlist"),

    [string[]]$AllowPatterns = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    param([string]$RequestedRoot)

    if ($RequestedRoot) {
        return (Resolve-Path -LiteralPath $RequestedRoot).Path
    }

    $candidate = Split-Path -Path $PSScriptRoot -Parent
    if (-not $candidate) {
        throw "Unable to locate repository root from script path."
    }

    return (Resolve-Path -LiteralPath $candidate).Path
}

function Get-RelativePathNormalized {
    param(
        [string]$Root,
        [string]$FullPath
    )

    $relative = [System.IO.Path]::GetRelativePath($Root, $FullPath)
    return ($relative -replace "\\", "/")
}

function Get-AllowRules {
    param(
        [string[]]$InlinePatterns,
        [string]$AllowFile
    )

    $rules = @()
    $source = @()

    if ($InlinePatterns) {
        $source += $InlinePatterns
    }

    if ($AllowFile -and (Test-Path -LiteralPath $AllowFile)) {
        $source += Get-Content -LiteralPath $AllowFile
    }

    # Patterns are evaluated against repo-relative paths using PowerShell wildcard semantics.
    foreach ($entry in $source) {
        if (-not $entry) {
            continue
        }

        $trimmed = $entry.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#")) {
            continue
        }

        $parts = $trimmed.Split("::", 2)
        $pathPattern = ($parts[0].Trim() -replace "\\", "/")
        if (-not $pathPattern) {
            continue
        }

        $linePattern = $null
        if ($parts.Length -eq 2) {
            $linePattern = $parts[1].Trim()
            if (-not $linePattern) {
                $linePattern = $null
            }
        }

        $rules += [pscustomobject]@{
            PathPattern = $pathPattern
            LinePattern = $linePattern
        }
    }

    return $rules
}

function Test-AllowRule {
    param(
        [pscustomobject]$Match,
        [pscustomobject[]]$Rules
    )

    foreach ($rule in $Rules) {
        if (-not $rule.PathPattern) {
            continue
        }

        if ($Match.RelativePath -like $rule.PathPattern) {
            if (-not $rule.LinePattern) {
                return $true
            }

            if ($Match.Text -match $rule.LinePattern) {
                return $true
            }
        }
    }

    return $false
}

if (-not (Get-Command rg -ErrorAction SilentlyContinue)) {
    throw "ripgrep (rg) is required. Install it (https://github.com/BurntSushi/ripgrep) and ensure it is on PATH."
}

$repoRoot = Resolve-RepoRoot -RequestedRoot $RepoRoot
Write-Verbose ("Repo root: {0}" -f $repoRoot)
$liteDbRoot = Join-Path $repoRoot "LiteDB"
if (-not (Test-Path -LiteralPath $liteDbRoot)) {
    throw "LiteDB project directory not found at '$liteDbRoot'."
}

$searchRoots = @((Resolve-Path -LiteralPath $liteDbRoot).Path)
Write-Verbose ("Using search root: {0}" -f $searchRoots[0])

$allowRules = Get-AllowRules -InlinePatterns $AllowPatterns -AllowFile $AllowListPath
$matches = @()
$violations = @()
$allowedCount = 0

$rgArgs = @("--json", "--line-number", "Vector")
foreach ($root in $searchRoots) {
    $rgArgs += (Resolve-Path -LiteralPath $root).Path
}

Push-Location -Path $repoRoot
try {
    $rgOutput = & rg @rgArgs 2>&1
    $rgExit = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($rgExit -eq 2) {
    $message = ($rgOutput | Select-Object -First 1)
    throw "ripgrep failed: $message"
}

foreach ($line in $rgOutput) {
    if (-not $line) {
        continue
    }

    $parsed = $null
    try {
        $parsed = $line | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        continue
    }

    if ($parsed.type -ne "match") {
        continue
    }

    $lineText = $parsed.data.lines.text -replace '\r?\n$', ""
    $relativePath = Get-RelativePathNormalized -Root $repoRoot -FullPath $parsed.data.path.text
    $match = [pscustomobject]@{
        RelativePath = $relativePath
        LineNumber   = [int]$parsed.data.line_number
        Text         = $lineText
    }

    if (Test-AllowRule -Match $match -Rules $allowRules) {
        $allowedCount++
        continue
    }

    $violations += $match
}

if ($violations.Count -gt 0) {
    Write-Error ("Vector references detected in LiteDB core (total: {0}). Remove or allowlist them explicitly." -f $violations.Count)
    $violations |
        Sort-Object RelativePath, LineNumber |
        ForEach-Object {
            $linePreview = $_.Text.Trim()
            Write-Host ("{0}:{1}: {2}" -f $_.RelativePath, $_.LineNumber, $linePreview) -ForegroundColor Red
        }

    exit 1
}

if ($rgExit -eq 1) {
    Write-Host "Vector clean: no references found in the specified roots." -ForegroundColor Green
}
else {
    $totalMatches = $allowedCount
    Write-Host ("Vector clean: all {0} matches were allowlisted." -f $totalMatches) -ForegroundColor Green
}
