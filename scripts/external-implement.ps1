<#
.SYNOPSIS
    Runs one task's (or one fix's) implementer on the local OpenCode install, in a worktree this
    script creates, with the brief the main session filled from build-process.md Appendix A.

.DESCRIPTION
    The main session fills Appendix A for the task (the entry pasted in full, review URLs of an
    earlier round if any) and passes it as -BriefFile, exactly as it would to a Claude implementer.
    This script then:
      1. creates the worktree and branch (task/T<nn>-<slug> under ic2-work\T<nn>, or
         fix/<issue>-<slug> under ic2-work\fix-<issue>), from origin/main, and pushes the branch;
         an existing remote branch is resumed, never recreated;
      2. copies assets.local.ini into the worktree for a -LocalOnly task;
      3. runs `opencode run` there with the .opencode/agents/external-implementer.md agent and
         the chosen model, feeding it the brief plus the run rules. The run is watched
         (scripts/Invoke-OpenCodeWatched.ps1) with stdin closed, because `opencode run` waits for
         stdin's end-of-file before it creates a session (the cause of the 2026-09-28 hangs). If
         OpenCode creates no session within -StartupTimeoutSec, its session makes no progress
         for -IdleTimeoutSec, or it does not finish within -TotalTimeoutSec, its process tree is
         killed;
      4. checks the outcome: a PR exists for the branch, the worktree is clean and pushed, and
         it is detached so the branch is free for the reviewer; saves the run's output next to
         the worktree as <name>.implementer.log and prints its tail.
    With -Model auto (the default) the models form a chain: deepseek-flash, then mimo-flash-free,
    then glm, each tried once. The next model runs ONLY on an infrastructure failure (no session
    in time, an idle session, no exit in time, a run that exits without a session, a non-zero exit, the
    fallback-to-default-agent guard), and only when the failed run left nothing behind: no new
    commit, locally or on origin, and no new PR. Otherwise the script exits 1 and the main session
    decides. Two consecutive attempts failing with the same cause (Get-OpenCodeFailureClass: two
    startup hangs, two idle kills, ...) stop the chain early. When every model fails, the chain
    stops that way, or OpenCode is not installed, it exits 3 ("OpenCode unavailable: ..."), and the task falls back to the catalogue's Claude model (operating-guide
    §3). An implementer that stops and reports exits 0: it has not failed and is never retried.
    The last line names the model that ran ("implemented by: <name>"); the review passes it to
    external-review.ps1 as -ExcludeModel, so the reviewer is never the implementer's model.
    The script never merges, labels or reviews; the main session does those (Appendix C).

    Model names -> OpenCode model ids (`opencode models` lists what this machine has). The
    cheap tier is the default (the user's decision of 2026-09-28).

.PARAMETER Task
    T<nn>, for a task. Mutually exclusive with -Fix.
.PARAMETER Fix
    The bug issue number, for a fix-lane run (build-process.md §4.10).
.PARAMETER Slug
    The branch slug: task/T<nn>-<slug> or fix/<issue>-<slug>.
.PARAMETER Issue
    The task's issue number (for the log and the PR check).
.PARAMETER BriefFile
    The filled Appendix A brief.
.PARAMETER Model
    auto (default: the chain deepseek-flash, then mimo-flash-free, then glm), or one model alone:
    deepseek-flash (DeepSeek V4.1 Flash at max, index 39, proven on this repository in #279),
    mimo-flash-free (index 38, free; the endpoint's limits are unknown), mimo-pro, mimo-flash,
    glm, or luna.
.PARAMETER LocalOnly
    Copy assets.local.ini from the main checkout into the worktree.
.PARAMETER FixturesDir
    A clone of ic2-test-fixtures, exported as IC2_FIXTURES_DIR for the run.
.PARAMETER StartupTimeoutSec
    How long a run may take to create its OpenCode session before it is killed (default 180).
.PARAMETER TotalTimeoutSec
    How long a run may take in all before it is killed (default 10800).
.PARAMETER IdleTimeoutSec
    How long the run's session may go without its `updated` time advancing before the run is
    killed (default 900; 0 disables). `updated` advances at each step boundary, not while a tool
    runs or a reply streams, so this must exceed the longest single step (a long generation).
.PARAMETER ModelIds
    Overrides of the model name -> model id map, e.g. @{ 'deepseek-flash' = 'opencode/deepseek-v4.2-flash' },
    for when `opencode models` shows a different id (or, in a test, a bad id to exercise the chain).

.EXAMPLE
    pwsh scripts/external-implement.ps1 -Task T71 -Slug persistence-hardening -Issue 308 -BriefFile C:\tmp\T71-brief.md
.EXAMPLE
    pwsh scripts/external-implement.ps1 -Fix 346 -Slug migration-message -BriefFile C:\tmp\346-brief.md -Model mimo-flash-free
#>
[CmdletBinding()]
param(
    [string] $Task,
    [int] $Fix,
    [Parameter(Mandatory)] [string] $Slug,
    [int] $Issue,
    [Parameter(Mandatory)] [string] $BriefFile,
    [ValidateSet('auto', 'deepseek-flash', 'mimo-flash-free', 'mimo-pro', 'mimo-flash', 'glm', 'luna')] [string] $Model = 'auto',
    [switch] $LocalOnly,
    [string] $FixturesDir,
    [int] $StartupTimeoutSec = 180,
    [int] $TotalTimeoutSec = 10800,
    [int] $IdleTimeoutSec = 900,
    [hashtable] $ModelIds
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-OpenCodeWatched.ps1')

# Chosen on 2026-09-28 from the OpenCode Go table and the comparison of the plan's own models:
# DeepSeek V4.1 Flash at max effort (index 39, $0.27, the implementer of the #279 rehearsal) is the
# default; MiMo-V2.6-Flash (38, free endpoint, limits unknown) is the second cheap option; GLM-5.3
# at max effort (up to 45) implements a High-effort entry and is the escalation after a failed rework
# round; Luna (29-37) is the cheap routine option and never the escalation; MiMo Pro (46, $0.13)
# becomes the default the day the plan lists it. Confirm the ids with `opencode models` on first use.
# On 2026-09-28 the desktop's `opencode models` listed mimo-v2.6-flash-free, glm-5.3 and gpt-6-luna,
# but neither mimo-v2.6-pro nor mimo-v2.6-flash; those two stay in the table for the day the plan
# lists them, and mimo-flash-free is the default until then.
$models = @{
    'deepseek-flash'  = 'opencode/deepseek-v4.1-flash'
    'mimo-flash-free' = 'opencode/mimo-v2.6-flash-free'
    'mimo-pro'        = 'opencode/mimo-v2.6-pro'
    'mimo-flash'      = 'opencode/mimo-v2.6-flash'
    'glm'             = 'opencode/glm-5.3'
    'luna'            = 'opencode/gpt-6-luna'
}
if ($ModelIds) { foreach ($k in $ModelIds.Keys) { $models[$k] = $ModelIds[$k] } }
$variants = @{ 'mimo-flash-free' = ''; 'deepseek-flash' = 'max'; 'mimo-pro' = ''; 'mimo-flash' = ''; 'glm' = 'max'; 'luna' = 'high' }
# The fallback chain (the user's decision of 2026-09-28): each model once, the next only on an
# infrastructure failure. An explicit -Model runs that model alone.
$chain = if ($Model -eq 'auto') { @('deepseek-flash', 'mimo-flash-free', 'glm') } else { @($Model) }

if (-not $Task -and -not $Fix) { throw 'Give -Task T<nn> or -Fix <issue>.' }
if ($Task -and $Fix) { throw '-Task and -Fix are mutually exclusive.' }
if ($Task -and $Task -notmatch '^T\d{2,3}$') { throw "-Task must look like T24; got $Task" }
if (-not (Test-Path $BriefFile)) { throw "Brief not found: $BriefFile" }
# OpenCode not installed or not found: the same exit 3 as every model failing.
try { $null = Resolve-OpenCodeExe } catch {
    if (-not (Test-OpenCodeInfraFailure $_)) { throw }
    [Console]::Error.WriteLine("OpenCode unavailable: $($_.Exception.Message) The task falls back to the catalogue's Claude model (operating-guide §3).")
    exit 3
}
foreach ($tool in 'gh', 'git') { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not on PATH." } }

$repo = (Resolve-Path (git rev-parse --show-toplevel)).Path
# ic2-work sits beside the MAIN checkout, even when this script runs from a worktree.
$commonDir = (git -C $repo rev-parse --path-format=absolute --git-common-dir).Trim()
$mainRoot = Split-Path $commonDir -Parent
$workRoot = Join-Path (Split-Path $mainRoot -Parent) 'ic2-work'
$agentFile = Join-Path $repo '.opencode/agents/external-implementer.md'
if (-not (Test-Path -LiteralPath $agentFile)) { throw "Agent file not found: $agentFile" }
if ($Task) { $name = $Task; $branch = "task/$Task-$Slug" } else { $name = "fix-$Fix"; $branch = "fix/$Fix-$Slug"; if (-not $Issue) { $Issue = $Fix } }
$worktree = Join-Path $workRoot $name
$log = Join-Path $workRoot "$name.implementer.log"

# 1. The worktree and branch. Resume a pushed branch; otherwise start from origin/main and push.
git -C $repo fetch -q origin
$remoteHas = (git -C $repo ls-remote --heads origin $branch)
if (Test-Path $worktree) {
    $wtBranch = git -C $worktree rev-parse --abbrev-ref HEAD
    if ($wtBranch -ne $branch) { git -C $worktree checkout -q $branch }
} elseif ($remoteHas) {
    git -C $repo worktree add $worktree $branch | Out-Null
} else {
    git -C $repo worktree add -b $branch $worktree origin/main | Out-Null
    git -C $worktree push -q -u origin $branch
}
if ($remoteHas) { git -C $worktree merge -q --ff-only "origin/$branch" }
if ($LocalOnly) {
    $ini = Join-Path $repo 'assets.local.ini'
    if (-not (Test-Path $ini)) { throw "-LocalOnly, but $ini is missing." }
    Copy-Item $ini (Join-Path $worktree 'assets.local.ini') -Force
}
Write-Host "worktree: $worktree on $branch"
# OpenCode silently falls back to its default, full-permission agent when --agent names one it
# cannot find, and a branch cut from a main that predates this file does not carry it. Copy the
# agent into the worktree, and keep the copy out of git so the implementer cannot commit it.
$agentRel = '.opencode/agents/external-implementer.md'
if (-not (git -C $worktree ls-files -- $agentRel)) {
    $agentDir = Join-Path $worktree '.opencode/agents'
    New-Item -ItemType Directory -Force -Path $agentDir | Out-Null
    Copy-Item -LiteralPath $agentFile -Destination (Join-Path $agentDir 'external-implementer.md') -Force
    $exclude = Join-Path $commonDir 'info/exclude'
    if (-not (Test-Path $exclude) -or -not (Select-String -LiteralPath $exclude -SimpleMatch $agentRel -Quiet)) {
        Add-Content -LiteralPath $exclude -Value $agentRel
    }
}

# 3. The run.
$brief = Get-Content -Raw -LiteralPath $BriefFile
$rules = @"

---
RUN RULES (from scripts/external-implement.ps1; they override the brief where they conflict):
- Your worktree is $worktree on branch $branch, already created and pushed. Skip the brief's
  worktree and branch creation; never run git worktree. Pass git -C "$worktree" explicitly.
- Everything else in the brief is binding: Owns, Done-when, the rules for engine code, the PR
  body, the detach at the end, and the report.
- The PR body's "Closes #$Issue" is the only place a closing keyword may precede #<n>.
"@
$prompt = $brief + $rules
if ($FixturesDir) { $env:IC2_FIXTURES_DIR = $FixturesDir }
# The run is watched (scripts/Invoke-OpenCodeWatched.ps1). It starts OpenCode with stdin CLOSED
# (an empty file): `opencode run` creates no session until stdin's end-of-file, so an inherited
# pipe hung both 2026-09-28 runs. No session within StartupTimeoutSec, a session idle for
# IdleTimeoutSec, or no exit within TotalTimeoutSec kills its process tree. The helper reads the
# output as UTF-8.
# The next model runs only on an infrastructure failure (no session, idle, no exit, an exit
# without a session, a non-zero exit, the fallback-agent guard), and only when the failed run left nothing
# behind: no new commit, locally or on origin, and no new PR. Its uncommitted edits are discarded.
# An implementer that stops and reports exits 0: that is not a failure, and it is never retried
# on another model.
# "Nothing behind" is judged against the state before the first attempt, so a resumed branch
# (unpushed local commits, or a rework round whose PR is already open) can still fall back.
$startSha = git -C $worktree rev-parse HEAD
$startRemote = git -C $repo rev-parse "origin/$branch" 2>$null
$startPr = gh pr list --head $branch --state open --json number --jq '.[0].number' 2>$null
$failures = @()
$output = $null
$lastClass = $null
$sameCause = $null
$implementedBy = $null
[System.IO.File]::WriteAllText($log, '')
foreach ($m in $chain) {
    $ocArgs = @('run', '--dir', $worktree, '--agent', 'external-implementer', '--model', $models[$m])
    if ($variants[$m]) { $ocArgs += @('--variant', $variants[$m]) }
    Write-Host "attempt: $m ($($models[$m]))"
    $reason = $null
    try {
        $run = Invoke-OpenCodeWatched -Arguments $ocArgs -Prompt $prompt -WorkDir $worktree -Title "ic2-$name-$m" `
            -StartupTimeoutSec $StartupTimeoutSec -TotalTimeoutSec $TotalTimeoutSec -IdleTimeoutSec $IdleTimeoutSec
        $output = $run.Output
        if ($run.ExitCode -ne 0) { $reason = "exit $($run.ExitCode)" }
        elseif ($output -match 'Falling back to default agent') { $reason = 'fell back to the default agent' }
    } catch {
        # Only OpenCode's own failures advance the chain; anything else is rethrown (exit 1).
        if (-not (Test-OpenCodeInfraFailure $_)) { throw }
        $reason = $_.Exception.Data['Reason']; $output = $_.Exception.Message
    }
    Add-Content -LiteralPath $log -Value "=== $m ($($models[$m])): $(if ($reason) { "failed: $reason" } else { 'ran' }) ===`n$output" -Encoding utf8
    if (-not $reason) { $implementedBy = $m; break }
    Write-Warning "$m failed: $reason"
    $failures += "${m}: $reason"
    git -C $repo fetch -q origin
    $prNow = gh pr list --head $branch --state open --json number --jq '.[0].number' 2>$null
    $leftWork = (git -C $worktree rev-parse HEAD) -ne $startSha -or
        ((git -C $repo rev-parse "origin/$branch" 2>$null) -ne $startRemote) -or
        ($prNow -and $prNow -ne $startPr)
    if ($leftWork) { [Console]::Error.WriteLine("$m failed ($reason) after committing, pushing or opening a PR on $branch; not retrying on another model. The main session decides. Log: $log"); exit 1 }
    git -C $worktree reset -q --hard $startSha
    git -C $worktree clean -q -fd
    # Two consecutive attempts failing with one cause (two startup hangs, two idle kills) mean
    # OpenCode itself is the problem, not the model: stop the chain (operating-guide §3).
    $class = Get-OpenCodeFailureClass $reason
    if ($class -eq $lastClass) { $sameCause = $class; break }
    $lastClass = $class
}
Write-Host "run output: $log"
if (-not $implementedBy) {
    # Not Write-Error: under ErrorActionPreference Stop it would end the script with exit 1, not 3.
    $why = if ($sameCause) { "same failure twice: $sameCause ($($failures -join '; '))" } else { $failures -join '; ' }
    [Console]::Error.WriteLine("OpenCode unavailable: $why. The task falls back to the catalogue's Claude model (operating-guide §3). Log: $log")
    exit 3
}
if ($failures) { Write-Host "fell back: $($failures -join '; ')" }
# The reviewer must not be this model: pass it to external-review.ps1 as -ExcludeModel.
Write-Host "implemented by: $implementedBy ($($models[$implementedBy]))"

# 4. The outcome. The PR is the deliverable; a clean, pushed, detached worktree is the handover.
git -C $repo fetch -q origin
$pr = gh pr list --head $branch --state open --json number,url --jq '.[0]'
$dirty = git -C $worktree status --porcelain
$local = git -C $worktree rev-parse HEAD
$remote = git -C $repo rev-parse "origin/$branch" 2>$null
if ($dirty) { Write-Warning "uncommitted changes remain in $worktree" }
if ($local -ne $remote) { Write-Warning "the worktree's HEAD ($local) is not pushed (origin/$branch is $remote)" }
git -C $worktree checkout -q --detach
Write-Host '--- tail of the run ---'
($output -split "`r?`n" | Select-Object -Last 40) -join "`n" | Write-Host
if (-not $pr) { Write-Error "No open PR for $branch. Read $log; resume with the same command once the cause is known."; exit 1 }
Write-Host "PR: $pr"
