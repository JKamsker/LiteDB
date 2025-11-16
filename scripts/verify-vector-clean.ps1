<#
.SYNOPSIS
    Verifies that the core LiteDB library does not contain vector-specific code outside of extension points.

.DESCRIPTION
    This script searches the LiteDB core library for references to "Vector" and related terms,
    excluding legitimate extension points, documentation, and test files. It helps ensure
    that vector functionality remains isolated to the LiteDB.Vector plugin.

.PARAMETER Strict
    If specified, enables stricter validation including case-sensitive matching.

.PARAMETER ExcludeComments
    If specified, attempts to exclude matches found only in comments and strings (default: true).

.EXAMPLE
    .\verify-vector-clean.ps1
    Runs standard validation

.EXAMPLE
    .\verify-vector-clean.ps1 -Strict
    Runs strict validation with additional checks

.NOTES
    Exit code 0: No vector references found (success)
    Exit code 1: Vector references found outside extension points (failure)
    Exit code 2: Script error
#>

[CmdletBinding()]
param(
    [switch]$Strict,
    [switch]$ExcludeComments = $true
)

$ErrorActionPreference = "Stop"

# Configuration
$rootPath = Split-Path -Parent $PSScriptRoot
$liteDbPath = Join-Path $rootPath "LiteDB"

# Patterns to search for
$searchPatterns = @(
    "Vector",
    "HNSW",
    "Embedding"
)

# Allowed extension point files and patterns
# These files are allowed to contain "Vector" as they define extension points
$allowedExtensionPoints = @(
    "*/Plugins/**",
    "**/ILitePlugin.cs",
    "**/ILitePluginContext.cs",
    "**/DefaultPluginContext.cs",
    "**/IPageFactoryRegistry.cs",
    "**/IBsonTypeRegistry.cs",
    "**/IIndexRegistry.cs",
    "**/IExpressionRegistry.cs",
    "**/IQueryPlannerRegistry.cs"
)

# File extensions to check
$codeExtensions = @("*.cs")

# Patterns to exclude from search
$excludePatterns = @(
    "*/bin/*",
    "*/obj/*",
    "*/.vs/*",
    "*/TestResults/*",
    "**/AssemblyInfo.cs"
)

function Write-ColorOutput {
    param(
        [string]$Message,
        [ConsoleColor]$ForegroundColor = [ConsoleColor]::White
    )

    $oldColor = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    Write-Output $Message
    $host.UI.RawUI.ForegroundColor = $oldColor
}

function Test-IsExtensionPoint {
    param([string]$FilePath)

    foreach ($pattern in $allowedExtensionPoints) {
        if ($FilePath -like $pattern) {
            return $true
        }
    }

    return $false
}

function Test-IsExcluded {
    param([string]$FilePath)

    foreach ($pattern in $excludePatterns) {
        if ($FilePath -like $pattern) {
            return $true
        }
    }

    return $false
}

function Get-CodeFiles {
    $files = @()

    foreach ($ext in $codeExtensions) {
        $found = Get-ChildItem -Path $liteDbPath -Filter $ext -Recurse -File
        $files += $found
    }

    # Filter out excluded paths
    $files = $files | Where-Object { -not (Test-IsExcluded $_.FullName) }

    return $files
}

function Test-FileForVectorReferences {
    param(
        [System.IO.FileInfo]$File,
        [string]$Pattern
    )

    $content = Get-Content -Path $File.FullName -Raw

    if ([string]::IsNullOrWhiteSpace($content)) {
        return $null
    }

    # If excluding comments, try to strip them (simple approach)
    if ($ExcludeComments) {
        # Remove single-line comments
        $content = $content -replace '//.*$', '' -replace '/\*[\s\S]*?\*/', ''

        # Remove XML doc comments
        $content = $content -replace '///.*$', ''

        # Remove string literals (simple approach - may have edge cases)
        $content = $content -replace '"(?:[^"\\]|\\.)*"', '""'
        $content = $content -replace '@"(?:[^"]|"")*"', '""'
    }

    # Search for pattern
    $matchOptions = if ($Strict) { [System.Text.RegularExpressions.RegexOptions]::None } else { [System.Text.RegularExpressions.RegexOptions]::IgnoreCase }

    $regex = [regex]::new("\b$Pattern\b", $matchOptions)
    $matches = $regex.Matches($content)

    if ($matches.Count -gt 0) {
        # Get line numbers
        $lines = Get-Content -Path $File.FullName
        $results = @()

        foreach ($match in $matches) {
            # Find line number (approximate, as we've stripped comments)
            $lineNumber = 1
            $position = 0

            foreach ($line in $lines) {
                if ($line -match "\b$Pattern\b") {
                    # Double-check this line isn't a comment
                    $trimmed = $line.Trim()
                    if ($ExcludeComments -and ($trimmed.StartsWith("//") -or $trimmed.StartsWith("///") -or $trimmed.StartsWith("*"))) {
                        $lineNumber++
                        continue
                    }

                    $results += @{
                        Line = $lineNumber
                        Content = $line.Trim()
                    }
                }
                $lineNumber++
            }
        }

        return $results
    }

    return $null
}

# Main execution
try {
    Write-ColorOutput "Verifying vector isolation in LiteDB core library..." -ForegroundColor Cyan
    Write-ColorOutput "Root path: $rootPath" -ForegroundColor Gray
    Write-ColorOutput "LiteDB path: $liteDbPath" -ForegroundColor Gray
    Write-ColorOutput ""

    if (-not (Test-Path $liteDbPath)) {
        Write-ColorOutput "ERROR: LiteDB directory not found at: $liteDbPath" -ForegroundColor Red
        exit 2
    }

    # Get all code files
    Write-ColorOutput "Scanning code files..." -ForegroundColor Gray
    $codeFiles = Get-CodeFiles
    Write-ColorOutput "Found $($codeFiles.Count) files to check" -ForegroundColor Gray
    Write-ColorOutput ""

    # Track violations
    $violations = @()

    # Check each file
    $checkedCount = 0
    $extensionPointCount = 0

    foreach ($file in $codeFiles) {
        $checkedCount++
        $relativePath = $file.FullName.Substring($rootPath.Length + 1)

        # Skip extension point files
        if (Test-IsExtensionPoint $relativePath) {
            $extensionPointCount++
            Write-Verbose "Skipping extension point: $relativePath"
            continue
        }

        # Check for each pattern
        foreach ($pattern in $searchPatterns) {
            $matches = Test-FileForVectorReferences -File $file -Pattern $pattern

            if ($matches) {
                foreach ($match in $matches) {
                    $violations += @{
                        File = $relativePath
                        Pattern = $pattern
                        Line = $match.Line
                        Content = $match.Content
                    }
                }
            }
        }
    }

    Write-ColorOutput "Checked $checkedCount files ($extensionPointCount skipped as extension points)" -ForegroundColor Gray
    Write-ColorOutput ""

    # Report results
    if ($violations.Count -eq 0) {
        Write-ColorOutput "SUCCESS: No vector references found outside extension points!" -ForegroundColor Green
        Write-ColorOutput ""
        Write-ColorOutput "The core LiteDB library is clean and does not contain vector-specific code." -ForegroundColor Green
        exit 0
    }
    else {
        Write-ColorOutput "FAILURE: Found $($violations.Count) vector reference(s) outside extension points:" -ForegroundColor Red
        Write-ColorOutput ""

        # Group by file
        $violationsByFile = $violations | Group-Object -Property File

        foreach ($group in $violationsByFile) {
            Write-ColorOutput "  $($group.Name)" -ForegroundColor Yellow

            foreach ($violation in $group.Group) {
                Write-ColorOutput "    Line $($violation.Line): $($violation.Pattern) - $($violation.Content)" -ForegroundColor Red
            }

            Write-ColorOutput ""
        }

        Write-ColorOutput "Vector references must be removed from the core library or the files must be added to the allowed extension points list." -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-ColorOutput "ERROR: Script execution failed" -ForegroundColor Red
    Write-ColorOutput $_.Exception.Message -ForegroundColor Red
    Write-ColorOutput $_.ScriptStackTrace -ForegroundColor Gray
    exit 2
}
