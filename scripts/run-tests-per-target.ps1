Param(
    [string]$Configuration = "Debug",
    [string]$RunSettings = "tests.runsettings",
    [string[]]$Projects = @(),
    [string[]]$TargetFrameworks = @(),
    [string]$DotnetFilter,
    [string[]]$ConsoleIncludeTraits = @(),
    [string[]]$ConsoleExcludeTraits = @("category=LongRunning")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).ProviderPath
$runSettingsPath = Join-Path $repoRoot $RunSettings

if ((-not (Test-Path $runSettingsPath)) -and (Test-Path $RunSettings)) {
    $runSettingsPath = (Resolve-Path $RunSettings).ProviderPath
}

$testMatrix = @(
    @{
        Name = "LiteDB.Tests"
        Project = "LiteDB.Tests/LiteDB.Tests.csproj"
        TargetFrameworks = @("net462", "net481", "net8.0")
        UseRunSettings = $true
        UseConsoleRunnerFor = @("net462")
    },
    @{
        Name = "LiteDB.Vector.Tests"
        Project = "LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj"
        TargetFrameworks = @("net8.0")
        UseRunSettings = $false
        UseConsoleRunnerFor = @()
    },
    @{
        Name = "LiteDB.ReproRunner.Tests"
        Project = "LiteDB.ReproRunner.Tests/LiteDB.ReproRunner.Tests.csproj"
        TargetFrameworks = @("net8.0")
        UseRunSettings = $false
        UseConsoleRunnerFor = @()
    }
)

if ($Projects.Count -gt 0) {
    $testMatrix = $testMatrix | Where-Object { $Projects -contains $_.Name }

    if ($testMatrix.Count -eq 0) {
        throw "No matching projects found for selection: $($Projects -join ', ')"
    }
}

function Invoke-Logged {
    param(
        [string]$Command,
        [string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot
    )

    Write-Host ""
    Write-Host ">> $Command $($Arguments -join ' ')" -ForegroundColor Cyan

    Push-Location $WorkingDirectory
    try {
        & $Command @Arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "$Command exited with code $exitCode."
    }
}

function Resolve-XunitRunner {
    $packageRoot = Join-Path $env:USERPROFILE ".nuget\packages\xunit.runner.console"

    if (-not (Test-Path $packageRoot)) {
        throw "xunit.runner.console package not restored. Run 'dotnet restore' for LiteDB.Tests first."
    }

    $latest = Get-ChildItem -Path $packageRoot -Directory |
        Sort-Object { [Version]$_.Name } -Descending |
        Select-Object -First 1

    if (-not $latest) {
        throw "Unable to locate xunit.runner.console under '$packageRoot'."
    }

    $runnerPath = Join-Path $latest.FullName "tools\net452\xunit.console.exe"

    if (-not (Test-Path $runnerPath)) {
        throw "xunit.console runner not found at '$runnerPath'."
    }

    return $runnerPath
}

$consoleRunnerPath = $null
$failures = @()

foreach ($entry in $testMatrix) {
    $projectPath = Join-Path $repoRoot $entry.Project
    if (-not (Test-Path $projectPath)) {
        $failures += "[$($entry.Name)] Project '$($entry.Project)' was not found."
        continue
    }

    $tfmSet = @(
        if ($TargetFrameworks.Count -gt 0) {
            $entry.TargetFrameworks | Where-Object { $TargetFrameworks -contains $_ }
        }
        else {
            $entry.TargetFrameworks
        }
    )
    $tfmSet = @($tfmSet | Where-Object { $_ })

    if ($tfmSet.Count -eq 0) {
        Write-Host ""
        Write-Host "Skipping $($entry.Name) because none of its target frameworks are selected." -ForegroundColor Yellow
        continue
    }

    foreach ($tfm in $tfmSet) {
        Write-Host ""
        Write-Host "==== $($entry.Name) :: $tfm ====" -ForegroundColor Green

        try {
            $useConsole = $entry.UseConsoleRunnerFor -contains $tfm

            if ($useConsole) {
                if (-not $consoleRunnerPath) {
                    $consoleRunnerPath = Resolve-XunitRunner
                }

                Invoke-Logged "dotnet" @("build", $projectPath, "-c", $Configuration, "-f", $tfm, "--nologo")

                $projectDir = Split-Path $entry.Project -Parent
                $dllName = [System.IO.Path]::GetFileNameWithoutExtension($entry.Project)
                $testDll = Join-Path $repoRoot ("{0}\bin\{1}\{2}\{3}.dll" -f $projectDir, $Configuration, $tfm, $dllName)

                if (-not (Test-Path $testDll)) {
                    throw "Expected test assembly '$testDll' not found after build."
                }

                $consoleArgs = @($testDll)
                foreach ($trait in $ConsoleIncludeTraits) {
                    if ([string]::IsNullOrWhiteSpace($trait)) { continue }
                    $consoleArgs += @("-trait", $trait)
                }
                foreach ($trait in $ConsoleExcludeTraits) {
                    if ([string]::IsNullOrWhiteSpace($trait)) { continue }
                    $consoleArgs += @("-notrait", $trait)
                }
                Invoke-Logged $consoleRunnerPath $consoleArgs
            }
            else {
                $args = @("test", $projectPath, "-c", $Configuration, "-f", $tfm, "--nologo")
                if ($entry.UseRunSettings -and (Test-Path $runSettingsPath)) {
                    $args += @("--settings", $runSettingsPath)
                }
                if ($DotnetFilter) {
                    $args += @("--filter", $DotnetFilter)
                }

                Invoke-Logged "dotnet" $args
            }
        }
        catch {
            $failures += "[$($entry.Name) | $tfm] $($_.Exception.Message)"
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Test failures detected:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}
else {
    Write-Host ""
    Write-Host "All requested test projects completed successfully." -ForegroundColor Green
}
