#Requires -Version 7.0
<#
.SYNOPSIS
    Validates that LiteDB's public API surface no longer exposes vector-specific
    types or members outside the approved allow list.

.DESCRIPTION
    Builds (unless -NoBuild) the LiteDB core project, inspects the compiled
    assemblies for each requested target framework, and reports any exported
    types, methods, properties, fields, or events whose names contain the token
    "Vector". Results are matched against an allow list so the script fails only
    when new vector-facing APIs leak out of the plugin registries.

.EXAMPLE
    ./scripts/verify-vector-api.ps1

.EXAMPLE
    ./scripts/verify-vector-api.ps1 -Configuration Debug -TargetFrameworks net8.0

.EXAMPLE
    ./scripts/verify-vector-api.ps1 -NoBuild -AssemblyPaths artifacts/LiteDB.dll
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,

    [string]$Configuration = "Release",

    [string[]]$TargetFrameworks = @("netstandard2.0", "net8.0"),

    [string]$LiteDbProject = "LiteDB/LiteDB.csproj",

    [switch]$NoBuild,

    [string[]]$AssemblyPaths,

    [string]$AllowListPath = (Join-Path $PSScriptRoot "verify-vector-api.allowlist"),

    [string[]]$AllowPatterns = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName "System.Runtime.Loader"
Add-Type -TypeDefinition @"
using System.Reflection;
using System.Runtime.Loader;

public sealed class VectorApiLoadContext : AssemblyLoadContext
{
    public VectorApiLoadContext() : base(isCollectible: true) { }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        return null;
    }
}
"@

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

    foreach ($entry in $source) {
        if (-not $entry) {
            continue
        }

        $trimmed = $entry.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#")) {
            continue
        }

        $rules += $trimmed
    }

    return $rules
}

function Test-AllowRule {
    param(
        [string]$Identifier,
        [string[]]$Rules
    )

    foreach ($rule in $Rules) {
        if ($Identifier -like $rule) {
            return $true
        }
    }

    return $false
}

function Invoke-Build {
    param(
        [string]$Project,
        [string]$Configuration
    )

    Write-Verbose ("dotnet build {0} ({1})" -f $Project, $Configuration)
    $buildArgs = @(
        "build",
        $Project,
        "-c", $Configuration,
        "--nologo"
    )

    $build = & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE`n$build"
    }
}

function Get-AssemblyTargets {
    param(
        [string]$RepoRoot,
        [string]$Configuration,
        [string[]]$Frameworks,
        [string[]]$ExplicitPaths
    )

    $targets = @()

    if ($ExplicitPaths -and $ExplicitPaths.Count -gt 0) {
        foreach ($path in $ExplicitPaths) {
            $resolved = (Resolve-Path -LiteralPath $path).Path
            $targets += [pscustomobject]@{
                Path = $resolved
                Framework = "custom"
            }
        }

        return $targets
    }

    foreach ($tfm in $Frameworks) {
        $assemblyPath = Join-Path $RepoRoot ("LiteDB/bin/{0}/{1}/LiteDB.dll" -f $Configuration, $tfm)
        if (-not (Test-Path -LiteralPath $assemblyPath)) {
            throw "Assembly not found at '$assemblyPath'. Run without -NoBuild to compile LiteDB first."
        }

        $targets += [pscustomobject]@{
            Path = (Resolve-Path -LiteralPath $assemblyPath).Path
            Framework = $tfm
        }
    }

    return $targets
}

function Get-VectorApiExposures {
    param(
        [string]$AssemblyPath,
        [string]$TargetFramework
    )

    $resolved = (Resolve-Path -LiteralPath $AssemblyPath).Path
    $context = [VectorApiLoadContext]::new()
    $stringComparison = [System.StringComparison]::Ordinal
    $flags = [System.Reflection.BindingFlags] "Public,Instance,Static,DeclaredOnly"
    $results = @()

    try {
        $assembly = $context.LoadFromAssemblyPath($resolved)
        foreach ($type in $assembly.ExportedTypes) {
            $typeName = $type.FullName
            if ($typeName.Contains("Vector", $stringComparison)) {
                $results += [pscustomobject]@{
                    Kind = "type"
                    TypeName = $typeName
                    MemberName = $null
                    Identifier = "type::$typeName"
                    TargetFramework = $TargetFramework
                }
            }

            foreach ($method in $type.GetMethods($flags)) {
                if ($method.IsSpecialName) {
                    continue
                }

                if ($method.Name.Contains("Vector", $stringComparison)) {
                    $results += [pscustomobject]@{
                        Kind = "method"
                        TypeName = $typeName
                        MemberName = $method.Name
                        Identifier = "method::$typeName.$($method.Name)"
                        TargetFramework = $TargetFramework
                    }
                }
            }

            foreach ($property in $type.GetProperties($flags)) {
                if ($property.Name.Contains("Vector", $stringComparison)) {
                    $results += [pscustomobject]@{
                        Kind = "property"
                        TypeName = $typeName
                        MemberName = $property.Name
                        Identifier = "property::$typeName.$($property.Name)"
                        TargetFramework = $TargetFramework
                    }
                }
            }

            foreach ($field in $type.GetFields($flags)) {
                if ($field.IsSpecialName) {
                    continue
                }

                if ($field.Name.Contains("Vector", $stringComparison)) {
                    $results += [pscustomobject]@{
                        Kind = "field"
                        TypeName = $typeName
                        MemberName = $field.Name
                        Identifier = "field::$typeName.$($field.Name)"
                        TargetFramework = $TargetFramework
                    }
                }
            }

            foreach ($event in $type.GetEvents($flags)) {
                if ($event.Name.Contains("Vector", $stringComparison)) {
                    $results += [pscustomobject]@{
                        Kind = "event"
                        TypeName = $typeName
                        MemberName = $event.Name
                        Identifier = "event::$typeName.$($event.Name)"
                        TargetFramework = $TargetFramework
                    }
                }
            }
        }
    }
    finally {
        $context.Unload()
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }

    return $results
}

$repoRoot = Resolve-RepoRoot -RequestedRoot $RepoRoot
Write-Verbose ("Repo root: {0}" -f $repoRoot)

if (-not $AssemblyPaths -and -not $NoBuild) {
    Invoke-Build -Project (Join-Path $repoRoot $LiteDbProject) -Configuration $Configuration
}

$allowRules = Get-AllowRules -InlinePatterns $AllowPatterns -AllowFile $AllowListPath
$targets = Get-AssemblyTargets -RepoRoot $repoRoot -Configuration $Configuration -Frameworks $TargetFrameworks -ExplicitPaths $AssemblyPaths

if (-not $targets -or $targets.Count -eq 0) {
    throw "No assemblies to evaluate. Provide -AssemblyPaths or ensure LiteDB was built."
}

$violations = @()
$allowedCount = 0

foreach ($target in $targets) {
    $exposures = Get-VectorApiExposures -AssemblyPath $target.Path -TargetFramework $target.Framework
    foreach ($entry in $exposures) {
        if (Test-AllowRule -Identifier $entry.Identifier -Rules $allowRules) {
            $allowedCount++
            continue
        }

        $violations += $entry
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("Vector API violations detected: {0}" -f $violations.Count)
    $violations |
        Sort-Object TargetFramework, TypeName, MemberName |
        Format-Table TargetFramework, Kind, TypeName, MemberName -AutoSize |
        Out-String |
        Write-Host
    exit 1
}

Write-Host ("Vector API guard passed: {0} known exposures matched allow list; no new vector-facing APIs detected." -f $allowedCount) -ForegroundColor Green
