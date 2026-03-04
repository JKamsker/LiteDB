#Requires -Version 7.0
<#
.SYNOPSIS
    Ensures the LiteDB core project stays free of direct "Vector" references outside plugin extension points.

.DESCRIPTION
    Scans compiled LiteDB sources (*.cs under LiteDB/, excluding LiteDB/Plugins) for the literal string "Vector"
    using ripgrep and fails when any occurrences remain outside the optional allow list. Matches that appear inside
    comments, string literals, or documentation blocks are ignored automatically so only executable code is flagged.
    Use the allow list to suppress intentional matches inside other shared extension-point definitions
    (format: pathPattern[::lineRegex]).

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

$script:CodeSegmentCache = @{}

function Get-CodeAnalyzer {
    param([string]$FilePath)

    if (-not $script:CodeSegmentCache.ContainsKey($FilePath)) {
        $script:CodeSegmentCache[$FilePath] = Analyze-CodeSegments -FilePath $FilePath
    }

    return $script:CodeSegmentCache[$FilePath]
}

function Analyze-CodeSegments {
    param([string]$FilePath)

    $segmentMap = @{}

    try {
        $lines = [System.IO.File]::ReadAllLines($FilePath)
    }
    catch {
        return $null
    }

    $inBlockComment = $false
    $inVerbatimString = $false
    $inString = $false
    $inCharLiteral = $false

    for ($lineIndex = 0; $lineIndex -lt $lines.Length; $lineIndex++) {
        $line = $lines[$lineIndex]
        $lineLength = $line.Length
        $segments = [System.Collections.Generic.List[object]]::new()
        $segmentStart = -1
        $index = 0

        while ($index -lt $lineLength) {
            if ($inBlockComment) {
                $endComment = $line.IndexOf("*/", $index)
                if ($endComment -eq -1) {
                    $index = $lineLength
                    break
                }

                $inBlockComment = $false
                $index = $endComment + 2
                continue
            }

            if ($inVerbatimString) {
                $closeQuote = $line.IndexOf('"', $index)
                if ($closeQuote -eq -1) {
                    $index = $lineLength
                    break
                }

                if ($closeQuote + 1 -lt $lineLength -and $line[$closeQuote + 1] -eq '"') {
                    $index = $closeQuote + 2
                    continue
                }

                $inVerbatimString = $false
                $index = $closeQuote + 1
                continue
            }

            if ($inString) {
                if ($line[$index] -eq '\') {
                    $index = [Math]::Min($lineLength, $index + 2)
                }
                elseif ($line[$index] -eq '"') {
                    $inString = $false
                    $index++
                }
                else {
                    $index++
                }

                continue
            }

            if ($inCharLiteral) {
                if ($line[$index] -eq '\') {
                    $index = [Math]::Min($lineLength, $index + 2)
                }
                elseif ($line[$index] -eq "'") {
                    $inCharLiteral = $false
                    $index++
                }
                else {
                    $index++
                }

                continue
            }

            if ($segmentStart -lt 0) {
                $segmentStart = $index
            }

            $ch = $line[$index]
            $next = if ($index + 1 -lt $lineLength) { $line[$index + 1] } else { [char]0 }
            $next2 = if ($index + 2 -lt $lineLength) { $line[$index + 2] } else { [char]0 }

            if ($ch -eq '/' -and $next -eq '/') {
                if ($segmentStart -ge 0 -and $index -gt $segmentStart) {
                    $segments.Add([pscustomobject]@{ Start = $segmentStart; End = $index })
                }

                $segmentStart = -1
                break
            }

            if ($ch -eq '/' -and $next -eq '*') {
                if ($segmentStart -ge 0 -and $index -gt $segmentStart) {
                    $segments.Add([pscustomobject]@{ Start = $segmentStart; End = $index })
                }

                $segmentStart = -1
                $inBlockComment = $true
                $index += 2
                continue
            }

            $startedVerbatim = $false
            if ($ch -eq '$' -and $next -eq '@' -and $next2 -eq '"') {
                $startedVerbatim = $true
                $advance = 3
            }
            elseif ($ch -eq '@' -and $next -eq '$' -and $next2 -eq '"') {
                $startedVerbatim = $true
                $advance = 3
            }
            elseif ($ch -eq '@' -and $next -eq '"') {
                $startedVerbatim = $true
                $advance = 2
            }

            if ($startedVerbatim) {
                if ($segmentStart -ge 0 -and $index -gt $segmentStart) {
                    $segments.Add([pscustomobject]@{ Start = $segmentStart; End = $index })
                }

                $segmentStart = -1
                $inVerbatimString = $true
                $index += $advance
                continue
            }

            $startedInterpolated = $false
            if ($ch -eq '$' -and $next -eq '"') {
                $startedInterpolated = $true
                $advance = 2
            }

            if ($startedInterpolated) {
                if ($segmentStart -ge 0 -and $index -gt $segmentStart) {
                    $segments.Add([pscustomobject]@{ Start = $segmentStart; End = $index })
                }

                $segmentStart = -1
                $inString = $true
                $index += $advance
                continue
            }

            if ($ch -eq '"') {
                if ($segmentStart -ge 0 -and $index -gt $segmentStart) {
                    $segments.Add([pscustomobject]@{ Start = $segmentStart; End = $index })
                }

                $segmentStart = -1
                $inString = $true
                $index++
                continue
            }

            if ($ch -eq "'") {
                if ($segmentStart -ge 0 -and $index -gt $segmentStart) {
                    $segments.Add([pscustomobject]@{ Start = $segmentStart; End = $index })
                }

                $segmentStart = -1
                $inCharLiteral = $true
                $index++
                continue
            }

            $index++
        }

        if ($segmentStart -ge 0 -and $lineLength -gt $segmentStart) {
            $segments.Add([pscustomobject]@{ Start = $segmentStart; End = $lineLength })
        }

        if ($segments.Count -gt 0) {
            $segmentMap[$lineIndex + 1] = $segments
        }
    }

    return [pscustomobject]@{
        Segments = $segmentMap
    }
}

function Test-IsCodeContext {
    param([pscustomobject]$Match)

    $analyzer = Get-CodeAnalyzer -FilePath $Match.FullPath
    if (-not $analyzer) {
        return $true
    }

    $segments = $analyzer.Segments[$Match.LineNumber]
    if (-not $segments) {
        return $false
    }

    foreach ($segment in $segments) {
        if ($Match.Column -ge $segment.Start -and ($Match.Column + $Match.Length) -le $segment.End) {
            return $true
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
$filteredContextCount = 0

$rgArgs = @("--json", "--line-number", "--glob", "*.cs", "--glob", "!Plugins/**", "Vector")
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
    $fullPath = $parsed.data.path.text
    $searchCursor = 0

    foreach ($subMatch in $parsed.data.submatches) {
        $matchText = $subMatch.match.text
        if (-not $matchText) {
            continue
        }

        $column = $lineText.IndexOf($matchText, $searchCursor, [System.StringComparison]::Ordinal)
        if ($column -eq -1) {
            $column = [int]$subMatch.start
        }

        $searchCursor = [Math]::Max($column + $matchText.Length, $searchCursor + 1)

        $match = [pscustomobject]@{
            RelativePath = $relativePath
            FullPath     = $fullPath
            LineNumber   = [int]$parsed.data.line_number
            Column       = $column
            Length       = $matchText.Length
            Text         = $lineText
        }

        if (-not (Test-IsCodeContext -Match $match)) {
            $filteredContextCount++
            continue
        }

        if (Test-AllowRule -Match $match -Rules $allowRules) {
            $allowedCount++
            continue
        }

        $violations += $match
    }
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
    Write-Host "Vector clean: no references found in compiled LiteDB sources." -ForegroundColor Green
}
else {
    $message = "Vector clean"
    $details = @()

    if ($allowedCount -gt 0) {
        $details += ("allowlisted={0}" -f $allowedCount)
    }

    if ($filteredContextCount -gt 0) {
        $details += ("ignored-comments/strings={0}" -f $filteredContextCount)
    }

    if ($details.Count -gt 0) {
        $message += (" ({0})" -f ($details -join ", "))
    }

    Write-Host $message -ForegroundColor Green
}
