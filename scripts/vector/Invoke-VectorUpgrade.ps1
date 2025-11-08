#Requires -Version 7.0
<#
.SYNOPSIS
    Orchestrates vector upgrade steps driven by specs/001-resolve-vector-findings.

.DESCRIPTION
    Loads scripts/vector/MigrationHelpers.cs, runs helper-backed migration operations, executes validation commands
    defined in specs/001-resolve-vector-findings/migration/upgrade-manifest.json, and writes an optional Markdown report.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DatabasePath,

    [string]$ManifestPath,

    [string]$Configuration = "Release",

    [string]$TargetFramework = "net8.0",

    [switch]$DryRun,

    [switch]$SkipValidations,

    [string[]]$StepId,

    [switch]$NoBackup,

    [switch]$OverwriteBackup,

    [switch]$NoRebuild,

    [string]$BackupDirectory,

    [string]$ReportPath,

    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-AssemblyPath {
    param(
        [string]$RepoRoot,
        [string]$Project,
        [string]$AssemblyName,
        [string[]]$Configurations,
        [string[]]$Frameworks
    )

    foreach ($config in $Configurations) {
        foreach ($framework in $Frameworks) {
            $candidate = Join-Path $RepoRoot "$Project/bin/$config/$framework/$AssemblyName"
            if (Test-Path -LiteralPath $candidate) {
                return (Resolve-Path $candidate).Path
            }
        }
    }

    throw "Unable to locate $AssemblyName for project '$Project'. Build the solution (dotnet build LiteDB.sln -c $($Configurations[0])) before running this script."
}

function Resolve-ReferenceAssemblyPath {
    param(
        [string]$AssemblyName,
        [string[]]$PreferredTfms
    )

    $dotnetRoot = $env:DOTNET_ROOT
    if (-not $dotnetRoot) {
        $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($dotnetCmd) {
            $dotnetRoot = Split-Path -Parent $dotnetCmd.Source
        }
    }

    if (-not $dotnetRoot) {
        $dotnetRoot = "C:\Program Files\dotnet"
    }

    $packRoot = Join-Path $dotnetRoot "packs/Microsoft.NETCore.App.Ref"
    if (-not (Test-Path -LiteralPath $packRoot)) {
        return $null
    }

    $versionDirectories = Get-ChildItem -LiteralPath $packRoot -Directory | Sort-Object { [Version]$_.Name } -Descending
    foreach ($versionDirectory in $versionDirectories) {
        foreach ($tfm in $PreferredTfms) {
            $candidate = Join-Path $versionDirectory.FullName "ref/$tfm/$AssemblyName.dll"
            if (Test-Path -LiteralPath $candidate) {
                return (Resolve-Path $candidate).Path
            }
        }
    }

    return $null
}

function Initialize-MigrationHelpers {
    param(
        [string]$RepoRoot,
        [string[]]$Configurations,
        [string[]]$Frameworks
    )

    if ([Type]::GetType("LiteDB.Vector.Tools.MigrationHelpers", $false)) {
        return
    }

    $liteDbAssembly = Resolve-AssemblyPath -RepoRoot $RepoRoot -Project "LiteDB" -AssemblyName "LiteDB.dll" -Configurations $Configurations -Frameworks $Frameworks
    $vectorAssembly = Resolve-AssemblyPath -RepoRoot $RepoRoot -Project "LiteDB.Vector" -AssemblyName "LiteDB.Vector.dll" -Configurations $Configurations -Frameworks $Frameworks

    $sourcePath = Join-Path $RepoRoot "scripts/vector/MigrationHelpers.cs"
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Migration helper source file not found at '$sourcePath'."
    }

    $referenceAssemblies = @($liteDbAssembly, $vectorAssembly)
    $preferredTfms = @("net8.0", "net7.0")
    $frameworkAssemblies = @(
        "System.Runtime",
        "System.Linq",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Reflection"
    )

    foreach ($assemblyName in $frameworkAssemblies) {
        $resolved = Resolve-ReferenceAssemblyPath -AssemblyName $assemblyName -PreferredTfms $preferredTfms
        if ($resolved) {
            $referenceAssemblies += $resolved
        }
        else {
            $referenceAssemblies += $assemblyName
        }
    }

    $loadedAssemblies = [System.AppDomain]::CurrentDomain.GetAssemblies()

    if (-not $loadedAssemblies.Where({ $_.Location -eq $liteDbAssembly }))
    {
        [System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($liteDbAssembly) | Out-Null
    }

    if (-not $loadedAssemblies.Where({ $_.Location -eq $vectorAssembly }))
    {
        [System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($vectorAssembly) | Out-Null
    }

    Add-Type -Path $sourcePath -ReferencedAssemblies $referenceAssemblies -CompilerOptions "/nowarn:1701" -IgnoreWarnings:$true | Out-Null
}

function Expand-Template {
    param(
        [string]$Template,
        [hashtable]$Context
    )

    if (-not $Template) {
        return ""
    }

    return ([System.Text.RegularExpressions.Regex]::Replace(
        $Template,
        "{{(.*?)}}",
        {
            param($match)
            $key = $match.Groups[1].Value.Trim()
            if ($Context.ContainsKey($key) -and $Context[$key]) {
                return $Context[$key]
            }

            return $match.Value
        }))
}

function ConvertTo-Boolean {
    param(
        [string]$Value,
        [bool]$Default
    )

    if (-not $Value) {
        return $Default
    }

    switch ($Value.Trim().ToLowerInvariant()) {
        "true" { return $true }
        "1" { return $true }
        "false" { return $false }
        "0" { return $false }
        default { return $Default }
    }
}

function New-MigrationOptions {
    param(
        [pscustomobject]$Runtime,
        [hashtable]$Overrides
    )

    $options = [LiteDB.Vector.Tools.VectorMigrationOptions]::new()
    $options.DatabasePath = $Runtime.DatabasePath
    $options.CreateBackup = $Runtime.CreateBackup
    $options.OverwriteBackup = $Runtime.OverwriteBackup
    $options.RunRebuild = $Runtime.RunRebuild
    $options.DryRun = $Runtime.DryRun
    if ($Runtime.BackupDirectory) {
        $options.BackupDirectory = $Runtime.BackupDirectory
    }

    foreach ($key in $Overrides.Keys) {
        $value = $Overrides[$key]
        switch ($key) {
            "dryrun" { $options.DryRun = ConvertTo-Boolean -Value $value -Default $options.DryRun }
            "createbackup" { $options.CreateBackup = ConvertTo-Boolean -Value $value -Default $options.CreateBackup }
            "overwritedbackup" { $options.OverwriteBackup = ConvertTo-Boolean -Value $value -Default $options.OverwriteBackup }
            "runrebuild" { $options.RunRebuild = ConvertTo-Boolean -Value $value -Default $options.RunRebuild }
            "backupdirectory" {
                if ($value) {
                    $options.BackupDirectory = [System.IO.Path]::GetFullPath($value)
                }
            }
        }
    }

    return $options
}

function Format-VectorIndexOutput {
    param(
        [System.Collections.IEnumerable]$Indexes
    )

    if (-not $Indexes -or $Indexes.Count -eq 0) {
        return @("No vector indexes found in the current database.")
    }

    $lines = @("Discovered vector indexes:")
    foreach ($index in $Indexes) {
        $lines += ("  - {0}.{1} (Expr: {2}, Dim: {3}, Metric: {4}, Slot: {5})" -f `
            $index.Collection, $index.Name, $index.Expression, $index.Dimensions, $index.MetricName, $index.Slot)
    }

    return $lines
}

function Format-MigrationReport {
    param(
        $Report
    )

    if (-not $Report) {
        return @("Migration helper did not return a report.")
    }

    $backupPath = if ($Report.BackupPath) { $Report.BackupPath } else { "<not created>" }

    $lines = @(
        ("Migration completed at {0:o}" -f $Report.TimestampUtc),
        ("Rebuild executed: {0} (pages rebuilt: {1})" -f $Report.RebuildExecuted, $Report.PagesRebuilt),
        ("Backup path: {0}" -f $backupPath),
        ("Plugin registered: {0}" -f $Report.PluginValidation.PluginRegistered),
        ("Default strategy registered: {0}" -f $Report.PluginValidation.DefaultStrategyRegistered)
    )

    if ($Report.PluginValidation.RegisteredStrategies.Count -gt 0) {
        $lines += ("Registered strategies: {0}" -f ($Report.PluginValidation.RegisteredStrategies -join ", "))
    }

    if ($Report.Actions.Count -gt 0) {
        $lines += "Actions:"
        foreach ($action in $Report.Actions) {
            $lines += "  - $action"
        }
    }

    if ($Report.Warnings.Count -gt 0) {
        $lines += "Warnings:"
        foreach ($warning in $Report.Warnings) {
            $lines += "  - $warning"
        }
    }

    return $lines
}

function Invoke-ExternalCommand {
    param(
        [string]$CommandText
    )

    if (-not $CommandText) {
        throw "Manifest step command cannot be empty."
    }

    Write-Verbose ("Executing command: {0}" -f $CommandText)
    $output = Invoke-Expression $CommandText 2>&1
    $nativeExit = $LASTEXITCODE
    $success = $?

    if ((-not $success) -or ($nativeExit -and $nativeExit -ne 0)) {
        throw "Command '$CommandText' failed with exit code $nativeExit."
    }

    if ($output) {
        return $output | ForEach-Object { $_.ToString() }
    }

    return @()
}

function Invoke-HelperCommand {
    param(
        [string]$Payload,
        [pscustomobject]$Runtime,
        [hashtable]$State
    )

    $segments = $Payload -split "\?", 2
    $name = $segments[0].Trim().ToLowerInvariant()
    $options = @{}
    if ($segments.Length -gt 1 -and $segments[1]) {
        foreach ($pair in ($segments[1] -split "&")) {
            if (-not $pair) {
                continue
            }

            $kv = $pair -split "=", 2
            if ($kv.Length -eq 2) {
                $options[$kv[0].Trim().ToLowerInvariant()] = $kv[1].Trim()
            }
        }
    }

    switch ($name) {
        "capture" {
            $indexes = [LiteDB.Vector.Tools.MigrationHelpers]::CaptureVectorIndexes($Runtime.DatabasePath)
            $State["LastCapture"] = $indexes

            return [pscustomobject]@{
                Output = Format-VectorIndexOutput -Indexes $indexes
                Data   = $indexes
            }
        }
        "relocate" {
            $optionsInstance = New-MigrationOptions -Runtime $Runtime -Overrides $options
            $report = [LiteDB.Vector.Tools.MigrationHelpers]::RelocateVectorMetadata($optionsInstance)
            $State["MigrationReport"] = $report

            return [pscustomobject]@{
                Output = Format-MigrationReport -Report $report
                Data   = $report
            }
        }
        default {
            throw "Unknown helper command '$name'."
        }
    }
}

function Invoke-ManifestStep {
    param(
        [string]$Command,
        [pscustomobject]$Runtime,
        [hashtable]$State
    )

    if ($Command.StartsWith("helpers:", [System.StringComparison]::OrdinalIgnoreCase)) {
        $payload = $Command.Substring(8)
        return Invoke-HelperCommand -Payload $payload -Runtime $Runtime -State $State
    }

    $output = Invoke-ExternalCommand -CommandText $Command
    return [pscustomobject]@{
        Output = $output
    }
}

function Write-UpgradeReport {
    param(
        [string]$Path,
        $Manifest,
        $Summary
    )

    $builder = [System.Text.StringBuilder]::new()
    $builder.AppendLine("# Vector Upgrade Report ($($Manifest.manifestId))") | Out-Null
    $builder.AppendLine() | Out-Null
    $builder.AppendLine("| Field | Value |") | Out-Null
    $builder.AppendLine("| --- | --- |") | Out-Null
    $builder.AppendLine(("| Database | `{0}` |" -f $Summary.DatabasePath)) | Out-Null
    $builder.AppendLine(("| Manifest | `{0}` |" -f $Summary.ManifestPath)) | Out-Null
    $builder.AppendLine(("| Supports In-Place | {0} |" -f $Summary.SupportsInPlace)) | Out-Null
    $builder.AppendLine(("| Dry Run | {0} |" -f $Summary.DryRun)) | Out-Null
    $builder.AppendLine(("| Timestamp (UTC) | {0:o} |" -f $Summary.TimestampUtc)) | Out-Null

    $builder.AppendLine() | Out-Null
    $builder.AppendLine("## Steps") | Out-Null
    $builder.AppendLine("| Step | Category | Status | Notes |") | Out-Null
    $builder.AppendLine("| --- | --- | --- | --- |") | Out-Null
    foreach ($step in $Summary.Steps) {
        $notes = if ($step.Error) {
            $step.Error
        }
        elseif ($step.Notes) {
            $step.Notes
        }
        elseif ($step.Output -and $step.Output.Count -gt 0) {
            ($step.Output -join "<br />")
        }
        else {
            ""
        }

        $builder.AppendLine(("| {0} | {1} | {2} | {3} |" -f $step.Id, $step.Category, $step.Status, ($notes -replace "\|", "\|"))) | Out-Null
    }

    if ($Summary.MigrationReport) {
        $report = $Summary.MigrationReport
        $builder.AppendLine() | Out-Null
        $builder.AppendLine("## Migration Report") | Out-Null
        $backupLine = if ($report.BackupPath) { $report.BackupPath } else { "<not created>" }
        $builder.AppendLine(('- Backup Path: `{0}`' -f $backupLine)) | Out-Null
        $builder.AppendLine(('- Pages Rebuilt: {0}' -f $report.PagesRebuilt)) | Out-Null
        $builder.AppendLine(('- Plugin Registered: {0}' -f $report.PluginValidation.PluginRegistered)) | Out-Null
        $builder.AppendLine(('- Default Strategy Registered: {0}' -f $report.PluginValidation.DefaultStrategyRegistered)) | Out-Null

        if ($report.PluginValidation.RegisteredStrategies.Count -gt 0) {
            $builder.AppendLine(('- Registered Strategies: {0}' -f ($report.PluginValidation.RegisteredStrategies -join ", "))) | Out-Null
        }

        if ($report.Actions.Count -gt 0) {
            $builder.AppendLine() | Out-Null
            $builder.AppendLine("### Actions") | Out-Null
            foreach ($action in $report.Actions) {
                $builder.AppendLine(("- {0}" -f $action)) | Out-Null
            }
        }

        if ($report.Warnings.Count -gt 0) {
            $builder.AppendLine() | Out-Null
            $builder.AppendLine("### Warnings") | Out-Null
            foreach ($warning in $report.Warnings) {
                $builder.AppendLine(("- {0}" -f $warning)) | Out-Null
            }
        }
    }

    [System.IO.File]::WriteAllText($Path, $builder.ToString(), [System.Text.Encoding]::UTF8)
}

# -----------------
# Main script logic
# -----------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $repoRoot "specs/001-resolve-vector-findings/migration/upgrade-manifest.json"
}

$resolvedManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $resolvedManifestPath)) {
    throw "Manifest file not found at '$resolvedManifestPath'."
}

$resolvedDatabasePath = [System.IO.Path]::GetFullPath($DatabasePath)
if (-not (Test-Path -LiteralPath $resolvedDatabasePath)) {
    throw "Database file not found at '$resolvedDatabasePath'."
}

$resolvedBackupDirectory = $null
if ($BackupDirectory) {
    $resolvedBackupDirectory = [System.IO.Path]::GetFullPath($BackupDirectory)
    [System.IO.Directory]::CreateDirectory($resolvedBackupDirectory) | Out-Null
}

$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json -Depth 8
$steps = @($manifest.steps)
if (-not $steps -or $steps.Count -eq 0) {
    throw "Manifest '$resolvedManifestPath' does not contain any steps."
}

$tokenContext = @{
    RepoRoot     = $repoRoot
    DatabasePath = $resolvedDatabasePath
    ManifestPath = $resolvedManifestPath
    ManifestId   = $manifest.manifestId
}

$runtimeContext = [pscustomobject]@{
    DatabasePath    = $resolvedDatabasePath
    CreateBackup    = -not $NoBackup.IsPresent
    OverwriteBackup = $OverwriteBackup.IsPresent
    RunRebuild      = -not $NoRebuild.IsPresent
    DryRun          = $DryRun.IsPresent
    BackupDirectory = $resolvedBackupDirectory
}

$configCandidates = @($Configuration, "Release", "Debug") | Where-Object { $_ } | Select-Object -Unique
$frameworkCandidates = @($TargetFramework, "net8.0", "netstandard2.0") | Where-Object { $_ } | Select-Object -Unique

Initialize-MigrationHelpers -RepoRoot $repoRoot -Configurations $configCandidates -Frameworks $frameworkCandidates

$state = @{}
$stepResults = @()
$stepFilter = @()
if ($StepId) {
    $stepFilter = @($StepId | ForEach-Object { $_.ToString().ToLowerInvariant() })
}

$summary = $null
Push-Location -Path $repoRoot
try {
    for ($i = 0; $i -lt $steps.Count; $i++) {
        $step = $steps[$i]
        $stepId = if ($step.PSObject.Properties.Name -contains "id" -and $step.id) { $step.id } else { "step-$i" }

        if ($stepFilter.Count -gt 0 -and -not ($stepFilter -contains $stepId.ToLowerInvariant())) {
            continue
        }

        $category = if ($step.PSObject.Properties.Name -contains "category" -and $step.category) { $step.category } else { "migration" }
        $continueOnError = if ($step.PSObject.Properties.Name -contains "continueOnError") { [bool]$step.continueOnError } else { $false }
        $commandValue = if ($step.PSObject.Properties.Name -contains "command" -and $step.command) { $step.command } else { "" }
        $commandText = Expand-Template -Template $commandValue -Context $tokenContext

        $result = [ordered]@{
            Id          = $stepId
            Description = $step.description
            Category    = $category
        Command     = $commandText
        Status      = "Pending"
        Output      = @()
        Notes       = $null
        Error       = $null
        }

        $skipStep = $false
        if ($SkipValidations.IsPresent -and $category -eq "validation") {
            $skipStep = $true
            $result.Status = "Skipped"
            $result.Notes = "Skipped via -SkipValidations."
        }

        if (-not $skipStep) {
            if (-not $Quiet.IsPresent) {
                Write-Host ""
                Write-Host ("==> [{0}] {1}" -f $stepId, $step.description) -ForegroundColor Cyan
            }

            try {
                $execution = Invoke-ManifestStep -Command $commandText -Runtime $runtimeContext -State $state
                $result.Status = "Passed"
                if ($execution.Output) {
                    $result.Output = $execution.Output
                    if (-not $Quiet.IsPresent) {
                        $execution.Output | ForEach-Object { Write-Host $_ }
                    }
                }

                if ($execution.Data) {
                    $result.Data = $execution.Data
                }
            }
            catch {
                $result.Status = "Failed"
                $result.Error = $_.Exception.Message
                if (-not $Quiet.IsPresent) {
                    Write-Error ("Step {0} failed: {1}" -f $stepId, $_.Exception.Message)
                }

                if (-not $continueOnError) {
                    throw
                }
            }
        }

        $stepResults += [pscustomobject]$result
    }

    $summary = [pscustomobject]@{
        ManifestId      = $manifest.manifestId
        ManifestPath    = $resolvedManifestPath
        SupportsInPlace = [bool]$manifest.supportsInPlace
        DatabasePath    = $resolvedDatabasePath
        DryRun          = $runtimeContext.DryRun
        Steps           = $stepResults
        MigrationReport = if ($state.ContainsKey("MigrationReport")) { $state.MigrationReport } else { $null }
        TimestampUtc    = [DateTimeOffset]::UtcNow
    }

    if ($ReportPath) {
        $resolvedReportPath = [System.IO.Path]::GetFullPath($ReportPath)
        $reportDirectory = Split-Path -Path $resolvedReportPath -Parent
        if ($reportDirectory) {
            [System.IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
        }

        Write-UpgradeReport -Path $resolvedReportPath -Manifest $manifest -Summary $summary
        if (-not $Quiet.IsPresent) {
            Write-Host ""
            Write-Host ("Report written to {0}" -f $resolvedReportPath) -ForegroundColor Green
        }
    }
}
finally {
    Pop-Location
}

return $summary
