<#
    Automates Codex runs against the spatial migration spec until every task in
    `specs/001-spatial-plugin-migration/tasks.md` is closed. Each iteration
    launches a non-interactive Codex session, commits the resulting changes,
    and repeats with a fresh session.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$TaskFile = 'specs/001-spatial-plugin-migration/tasks.md',
    [string]$ProgressFile = 'specs/001-spatial-plugin-migration/progress.md',
    [string]$InitialInstructions = @"
Read github/prompts/speckit.implement.prompt.md and please resume with the spatial plugin migration.
Status: specs/001-spatial-plugin-migration/progress.md - add your progress to it whenever necessary.
Also see specs\001-spatial-plugin-migration\tasks.md
"@,
    [string]$CodexBinary = 'codex',
    [string[]]$CodexOptions = @('--yolo'),
    [string]$CommitPrefix = 'auto: spatial plugin iteration',
    [switch]$DryRun,
    [switch]$SkipCommit,
    [int]$MaxIterations = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-OpenTasks {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Task file not found: $Path"
    }

    $lines = Get-Content -LiteralPath $Path
    $unchecked = @($lines | Where-Object { $_ -match '^\s*-\s*\[\s\]\s+' })
    return (Order-Tasks -Lines $unchecked)
}

function Order-Tasks {
    param(
        [string[]]$Lines
    )

    if (-not $Lines) {
        return @()
    }

    return ($Lines | Sort-Object -Stable -Property @{
            Expression = {
                $match = [regex]::Match($_, 'T(\d{3})')
                if ($match.Success) {
                    return [int]$match.Groups[1].Value
                }

                return [int]::MaxValue
            }
        })
}

function Get-TaskIds {
    param(
        [string[]]$Lines
    )

    if (-not $Lines) {
        return @()
    }

    $ids = @()
    foreach ($line in $Lines) {
        $match = [regex]::Match($line, 'T\d{3}')
        if ($match.Success) {
            $ids += $match.Value
        }
        else {
            $ids += $line.Trim()
        }
    }

    return ($ids | Select-Object -Unique)
}

function Build-CodexPrompt {
    param(
        [string]$Base,
        [string[]]$Remaining,
        [string]$TaskPath,
        [string]$ProgressPath
    )

    $remainingText = if ($Remaining) {
        $Remaining -join "`n"
    }
    else {
        '(all tasks complete)'
    }

    $prompt = @"
$Base

Remaining unchecked tasks (update $TaskPath and $ProgressPath as you work):
$remainingText

Work non-interactively, starting from the lowest-numbered task, complete the highest priority open item(s), mark them as done in the task file, and record progress updates.
"@

    if ($prompt.EndsWith("`n")) {
        return $prompt
    }

    return $prompt + "`n"
}

function Invoke-CodexIteration {
    param(
        [string]$Prompt,
        [string]$RepoRootPath,
        [string]$Binary,
        [string[]]$Options,
        [switch]$Dry
    )

    if ($Dry) {
        Write-Host '[dry-run] Skipping Codex execution.'
        return 0
    }

    $execArgs = @('exec')
    if ($Options) {
        $execArgs += $Options
    }
    $execArgs += @('--cd', $RepoRootPath, '-')

    $null = $Prompt | & $Binary @execArgs
    return $LASTEXITCODE
}

Push-Location -LiteralPath $RepoRoot
try {
    $iteration = 0

    while ($true) {
        $openBefore = Get-OpenTasks -Path $TaskFile

        if (-not $openBefore -or $openBefore.Count -eq 0) {
            Write-Host 'All tasks are complete. Exiting.'
            break
        }

        $iteration++

        if ($MaxIterations -gt 0 -and $iteration -gt $MaxIterations) {
            Write-Host "Reached MaxIterations ($MaxIterations). Exiting."
            break
        }

        Write-Host ("Starting Codex iteration #{0}. Remaining tasks: {1}" -f $iteration, $openBefore.Count)

        $prompt = Build-CodexPrompt -Base $InitialInstructions -Remaining $openBefore -TaskPath $TaskFile -ProgressPath $ProgressFile
        $exitCode = Invoke-CodexIteration -Prompt $prompt -RepoRootPath $RepoRoot -Binary $CodexBinary -Options $CodexOptions -Dry:$DryRun

        if ($exitCode -ne 0) {
            throw "Codex exited with code $exitCode."
        }

        $openAfter = Get-OpenTasks -Path $TaskFile

        $closed = @()
        if ($openBefore) {
            if ($openAfter) {
                $closed = $openBefore | Where-Object { $openAfter -notcontains $_ }
            }
            else {
                $closed = $openBefore
            }
        }

        if (-not $SkipCommit) {
            if ($DryRun) {
                Write-Host '[dry-run] Skipping git commit.'
            }
            else {
                $statusOutput = & git status --porcelain
                if ($LASTEXITCODE -ne 0) {
                    throw 'git status failed.'
                }

                if ($statusOutput) {
                    & git add -A
                    if ($LASTEXITCODE -ne 0) {
                        throw 'git add failed.'
                    }

                    $null = & git diff --cached --quiet
                    $hasStagedChanges = ($LASTEXITCODE -ne 0)

                    if ($hasStagedChanges) {
                        $closedIds = Get-TaskIds -Lines $closed
                        if (-not $closedIds) {
                            $closedIds = @('progress')
                        }

                        $commitMessage = "{0} #{1} ({2})" -f $CommitPrefix, $iteration, ($closedIds -join ', ')

                        & git commit -m $commitMessage
                        if ($LASTEXITCODE -ne 0) {
                            throw 'git commit failed.'
                        }
                        else {
                            Write-Host ("Committed iteration #{0}: {1}" -f $iteration, $commitMessage)
                        }
                    }
                    else {
                        Write-Host 'No staged changes after Codex iteration; skipping commit.'
                    }
                }
                else {
                    Write-Host 'No working tree changes detected; skipping commit.'
                }
            }
        }

        if ($openAfter -and ($openAfter.Count -eq $openBefore.Count) -and (-not $closed)) {
            Write-Warning 'No unchecked tasks were closed during this iteration. Stopping to avoid an infinite loop.'
            break
        }
    }
}
finally {
    Pop-Location
}
