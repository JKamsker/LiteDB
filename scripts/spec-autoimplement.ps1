<#
    Automates Codex runs against the vectorsearch migration spec until every task in
    `specs/001-vector-core-cleanup/tasks.md` is closed. Each iteration
    launches a non-interactive Codex session, commits the resulting changes,
    and repeats with a fresh session.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$TaskFile = 'specs/001-vector-core-cleanup/tasks.md',
    [string]$ProgressFile = 'specs/001-vector-core-cleanup/progress.md',
    [string]$InitialInstructions = @"
Read github/prompts/speckit.implement.prompt.md and please resume with the vectorsearch plugin migration.
Status: specs/001-vector-core-cleanup/progress.md - add your progress to it whenever necessary.
Also see specs\001-vector-core-cleanup\tasks.md
"@,
    [string]$CodexBinary = 'codex',
    [string[]]$CodexOptions = @('--yolo'),
    [string]$CommitPrefix = 'auto: vectorsearch plugin iteration',
    [switch]$DryRun,
    [switch]$Simulate,
    [switch]$SkipCommit,
    [int]$MaxIterations = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$dryRunMode = $DryRun -or $Simulate

function Get-OpenTasks {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Task file not found: $Path"
    }

    return @(
        Get-Content -LiteralPath $Path |
        Where-Object { $_ -match '^\s*-\s*\[\s\]\s+' }
    )
}

function Get-OpenTasksByPhase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Task file not found: $Path"
    }

    $phase = 'Uncategorized'
    $results = @()

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*##\s+(?<phase>.+)$') {
            $phase = $Matches['phase'].Trim()
            continue
        }

        if ($line -match '^\s*-\s*\[\s\]\s+') {
            $results += [pscustomobject]@{
                Phase = $phase
                Line  = $line
            }
        }
    }

    return $results
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
        [string]$ProgressPath,
        [string]$PhaseName
    )

    $remainingText = if ($Remaining) {
        $Remaining -join "`n"
    }
    else {
        '(all tasks complete)'
    }

    $phaseLabel = if ($PhaseName) {
        "Remaining unchecked tasks for $PhaseName"
    }
    else {
        'Remaining unchecked tasks'
    }

    $prompt = @"
$Base

$phaseLabel (update $TaskPath and $ProgressPath as you work):
$remainingText

Work non-interactively, choose the next task to tackle, mark it as done in the task file, and record progress updates.
"@

    if ($prompt.EndsWith("`n")) {
        return $prompt
    }

    return $prompt + "`n"
}

function Start-CodexSession {
    param(
        [string]$Prompt,
        [string]$RepoRootPath,
        [string]$Binary,
        [string[]]$Options,
        [switch]$Dry
    )

    if ($Dry) {
        Write-Host '[dry-run] Skipping Codex session start.'
        return [pscustomobject]@{
            ExitCode  = 0
            Output    = @()
            SessionId = $null
        }
    }

    $execArgs = @('exec')
    if ($Options) {
        $execArgs += $Options
    }
    $execArgs += @('--cd', $RepoRootPath, '-')

    $captured = [System.Collections.Generic.List[string]]::new()
    $Prompt | & $Binary @execArgs 2>&1 | ForEach-Object {
        Write-Host $_
        $null = $captured.Add($_)
    }
    $output = $captured.ToArray()
    $exitCode = $LASTEXITCODE

    $sessionId = $null
    if ($output) {
        $outputText = ($output -join "`n")
        $match = [regex]::Match($outputText, 'session id:\s*([0-9a-fA-F-]+)')
        if ($match.Success) {
            $sessionId = $match.Groups[1].Value
        }
    }

    return [pscustomobject]@{
        ExitCode  = $exitCode
        Output    = $output
        SessionId = $sessionId
    }
}

function Resume-CodexSession {
    param(
        [string]$SessionId,
        [string]$Prompt,
        [string]$RepoRootPath,
        [string]$Binary,
        [string[]]$Options,
        [switch]$Dry
    )

    if ($Dry) {
        Write-Host ("[dry-run] Skipping Codex resume for session {0}." -f $SessionId)
        return [pscustomobject]@{
            ExitCode = 0
            Output   = @()
        }
    }

    $resumeArgs = @('exec')
    if ($Options) {
        $resumeArgs += $Options
    }
    $resumeArgs += @('--cd', $RepoRootPath, 'resume', $SessionId, '-')

    $captured = [System.Collections.Generic.List[string]]::new()
    $Prompt | & $Binary @resumeArgs 2>&1 | ForEach-Object {
        Write-Host $_
        $null = $captured.Add($_)
    }
    $output = $captured.ToArray()
    $exitCode = $LASTEXITCODE

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output   = $output
    }
}

Push-Location -LiteralPath $RepoRoot
try {
    $iteration = 0
    $sessionMap = @{}
    $simulatedClosedTasks = [System.Collections.Generic.HashSet[string]]::new()

    while ($true) {
        $openBeforeDetailed = @(Get-OpenTasksByPhase -Path $TaskFile)
        if ($Simulate) {
            $openBeforeDetailed = @($openBeforeDetailed | Where-Object { -not $simulatedClosedTasks.Contains($_.Line) })
        }
        Write-Host '--- Iteration plan ---'
        $openBefore = @($openBeforeDetailed | ForEach-Object { $_.Line })

        if (-not $openBefore -or $openBefore.Count -eq 0) {
            Write-Host 'All tasks are complete. Exiting.'
            break
        }

        if ($openBeforeDetailed) {
            Write-Host 'Remaining phases and open task counts:'
            foreach ($group in ($openBeforeDetailed | Group-Object -Property Phase)) {
                Write-Host ("  - {0}: {1}" -f $group.Name, $group.Count)
            }
        }
        else {
            Write-Host 'No open tasks detected in any phase.'
        }

        $iteration++

        if ($MaxIterations -gt 0 -and $iteration -gt $MaxIterations) {
            Write-Host "Reached MaxIterations ($MaxIterations). Exiting."
            break
        }

        $activePhase = $null
        if ($openBeforeDetailed) {
            $activePhase = ($openBeforeDetailed | Select-Object -First 1).Phase
        }

        $phaseTasks = if ($activePhase) {
            $openBeforeDetailed | Where-Object { $_.Phase -eq $activePhase }
        }
        else {
            @()
        }

        $phaseTaskLines = if ($phaseTasks) { $phaseTasks | ForEach-Object { $_.Line } } else { $openBefore }
        $phaseTaskCount = @($phaseTaskLines).Count
        $phaseLabel = if ($activePhase) { $activePhase } else { 'Uncategorized' }

        Write-Host ("Step 1: Focusing on phase '{0}'." -f $phaseLabel)
        Write-Host ("Starting Codex iteration #{0}. Active phase: {1}. Tasks in phase: {2}" -f $iteration, $phaseLabel, $phaseTaskCount)

        $prompt = Build-CodexPrompt -Base $InitialInstructions -Remaining $phaseTaskLines -TaskPath $TaskFile -ProgressPath $ProgressFile -PhaseName $phaseLabel
        Write-Host 'Step 2: Prompt will include the following tasks:'
        foreach ($taskLine in $phaseTaskLines) {
            Write-Host ("  - {0}" -f $taskLine)
        }

        $simulatedThisIteration = @()
        if ($Simulate) {
            $taskToSimulate = $phaseTaskLines | Where-Object { -not $simulatedClosedTasks.Contains($_) } | Select-Object -First 1
            if ($taskToSimulate) {
                Write-Host ("Step 3: Simulating completion of task: {0}" -f $taskToSimulate)
                $null = $simulatedClosedTasks.Add($taskToSimulate)
                $simulatedThisIteration = @($taskToSimulate)
            }
            else {
                Write-Host ("Step 3: No remaining tasks to simulate in phase '{0}'." -f $phaseLabel)
            }
        }
        else {
            $sessionId = $null
            if ($phaseLabel -and $sessionMap.ContainsKey($phaseLabel)) {
                $sessionId = $sessionMap[$phaseLabel]
            }

            $result = $null
            if (-not $sessionId) {
                Write-Host ("Step 3: Starting new Codex session for phase '{0}'." -f $phaseLabel)
                $result = Start-CodexSession -Prompt $prompt -RepoRootPath $RepoRoot -Binary $CodexBinary -Options $CodexOptions -Dry:$dryRunMode
                if ($result.ExitCode -ne 0) {
                    throw "Codex exited with code $($result.ExitCode)."
                }

                if (-not $dryRunMode) {
                    if (-not $result.SessionId) {
                        throw 'Failed to capture Codex session id from Codex output.'
                    }

                    $sessionMap[$phaseLabel] = $result.SessionId
                    Write-Host ("Initialized Codex session for {0}: {1}" -f $phaseLabel, $result.SessionId)
                }
            }
            else {
                Write-Host ("Step 3: Resuming Codex session {0} for phase '{1}'." -f $sessionId, $phaseLabel)
                $result = Resume-CodexSession -SessionId $sessionId -Prompt $prompt -RepoRootPath $RepoRoot -Binary $CodexBinary -Options $CodexOptions -Dry:$dryRunMode
                if ($result.ExitCode -ne 0) {
                    throw "Codex resume exited with code $($result.ExitCode)."
                }
            }
        }

        Write-Host 'Step 4: Refreshing task list to determine progress.'
        $openAfterDetailed = @(Get-OpenTasksByPhase -Path $TaskFile)
        if ($Simulate) {
            $openAfterDetailed = @($openAfterDetailed | Where-Object { -not $simulatedClosedTasks.Contains($_.Line) })
        }
        $openAfter = @($openAfterDetailed | ForEach-Object { $_.Line })

        $closed = @()
        if ($openBefore) {
            if ($openAfter) {
                $closed = @($openBefore | Where-Object { $openAfter -notcontains $_ })
            }
            else {
                $closed = @($openBefore)
            }
        }

        if ($Simulate -and $simulatedThisIteration) {
            $closed = @($simulatedThisIteration)
        }

        if ($closed.Count -gt 0) {
            Write-Host 'Step 5: Closed tasks this iteration:'
            foreach ($item in $closed) {
                Write-Host ("  - {0}" -f $item)
            }
        }
        else {
            Write-Host 'Step 5: No tasks were closed during this iteration.'
        }

        if (-not $SkipCommit) {
            if ($dryRunMode) {
                $dryLabel = if ($Simulate) { 'dry-run/simulate' } else { 'dry-run' }
                Write-Host ("[{0}] Skipping git commit." -f $dryLabel)
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

        if (($openAfter.Count -gt 0) -and ($openAfter.Count -eq $openBefore.Count) -and ($closed.Count -eq 0)) {
            Write-Warning 'No unchecked tasks were closed during this iteration. Stopping to avoid an infinite loop.'
            break
        }
    }
}
finally {
    Pop-Location
}
