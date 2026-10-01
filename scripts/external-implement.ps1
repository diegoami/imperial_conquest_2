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
    With -Model auto (the default) the models form a chain: glm-flash, then deepseek-flash, then
    luna, each tried once. Luna is last until the cause of her long-run `Bad Request` on Go is
    understood (issue #551's comments, decided in #554): she failed both of #551's real implementer
    runs once the context grew, and Get-OpenCodeFailureClass puts every non-zero exit in one class,
    so a Luna failure early in the chain could stop it before DeepSeek ever ran. The next model runs
    ONLY on an infrastructure failure (no session
    in time, an idle session, no exit in time, a run that exits without a session, a non-zero exit, the
    fallback-to-default-agent guard, a tool call the permission guard rejected -- issue #501), and only when the failed run left nothing behind: no new
    commit, locally or on origin, and no new PR. Otherwise the script exits 1 and the main session
    decides. Two consecutive attempts failing with the same cause (Get-OpenCodeFailureClass: two
    startup hangs, two idle kills, ...) stop the chain early. When every model fails, the chain
    stops that way, or OpenCode is not installed, it exits 3 ("OpenCode unavailable: ..."), and the task falls back to Claude Sonnet (operating-guide
    §3). An implementer that stops and reports has not failed: its OpenCode run exits 0, so it is
    never retried on another model; the script then exits 1 ("No open PR") and the main session
    reads the report.
    Before the run's tail it prints the model that ran ("implemented by: <name>"); the review passes it to
    external-review.ps1 as -ExcludeModel, so the reviewer is never the implementer's model.
    The script never merges, labels or reviews; the main session does those (Appendix C).

    Model names -> OpenCode model ids (`opencode models` lists what this machine has). The runs
    are on OpenCode Go, `opencode-go/…`, per the user's decision of 2026-10-01 (issue #551).

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
    auto (default: the chain glm-flash, then deepseek-flash, then luna; luna is last until the
    cause of her long-run `Bad Request` on Go is understood, issue #554), or one model alone:
    luna (GPT-6 Luna at high effort), glm-flash (GLM-5.3 Flash at max), glm (GLM-5.3 at max),
    deepseek-flash (DeepSeek V4.1 Flash at max, proven on this repository in #279), mimo-pro, or
    mimo-flash.
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
    Overrides of the model name -> model id map, e.g. @{ 'deepseek-flash' = 'opencode-go/deepseek-v4.2-flash' },
    for when `opencode models` shows a different id (or, in a test, a bad id to exercise the chain).

.EXAMPLE
    pwsh scripts/external-implement.ps1 -Task T71 -Slug persistence-hardening -Issue 308 -BriefFile C:\tmp\T71-brief.md
.EXAMPLE
    pwsh scripts/external-implement.ps1 -Fix 346 -Slug migration-message -BriefFile C:\tmp\346-brief.md -Model luna
#>
[CmdletBinding()]
param(
    [string] $Task,
    [int] $Fix,
    [Parameter(Mandatory)] [string] $Slug,
    [int] $Issue,
    [Parameter(Mandatory)] [string] $BriefFile,
    [ValidateSet('auto', 'luna', 'glm-flash', 'glm', 'deepseek-flash', 'mimo-pro', 'mimo-flash')] [string] $Model = 'auto',
    [switch] $LocalOnly,
    [string] $FixturesDir,
    [int] $StartupTimeoutSec = 180,
    [int] $TotalTimeoutSec = 10800,
    [int] $IdleTimeoutSec = 900,
    [hashtable] $ModelIds
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-OpenCodeWatched.ps1')

# On 2026-10-01 the user moved the OpenCode runs from OpenCode Zen to OpenCode Go (issue #551):
# every id is `opencode-go/…` and no Zen model is used, the free ones included. The chain is
# GLM-5.3 Flash (max), then DeepSeek V4.1 Flash (max), then GPT-6 Luna (high effort), each tried
# once; mimo-flash-free is dropped, Go does not offer it. GLM-5.3 at max effort implements a
# High-effort entry and stays the escalation after a failed rework round; MiMo Pro and MiMo
# Flash stay in the table for the day the plan lists them. Confirm the ids with `opencode models`
# on first use; -ModelIds overrides any of them.
$models = @{
    'luna'            = 'opencode-go/gpt-6-luna'
    'glm-flash'       = 'opencode-go/glm-5.3-flash'
    'glm'             = 'opencode-go/glm-5.3'
    'deepseek-flash'  = 'opencode-go/deepseek-v4.1-flash'
    'mimo-pro'        = 'opencode-go/mimo-v2.6-pro'
    'mimo-flash'      = 'opencode-go/mimo-v2.6-flash'
}
if ($ModelIds) { foreach ($k in $ModelIds.Keys) { $models[$k] = $ModelIds[$k] } }
$variants = @{ 'luna' = 'high'; 'glm-flash' = 'max'; 'glm' = 'max'; 'deepseek-flash' = 'max'; 'mimo-pro' = ''; 'mimo-flash' = '' }
# The fallback chain (the user's decision of 2026-10-01, issue #554, replacing #551's order; luna
# is last until the cause of her long-run `Bad Request` on Go is understood, #551's comments):
# each model once, the next only on an infrastructure failure. An explicit -Model runs that model
# alone.
$chain = if ($Model -eq 'auto') { @('glm-flash', 'deepseek-flash', 'luna') } else { @($Model) }

if (-not $Task -and -not $Fix) { throw 'Give -Task T<nn> or -Fix <issue>.' }
if ($Task -and $Fix) { throw '-Task and -Fix are mutually exclusive.' }
if ($Task -and $Task -notmatch '^T\d{2,3}$') { throw "-Task must look like T24; got $Task" }
if (-not (Test-Path $BriefFile)) { throw "Brief not found: $BriefFile" }
# OpenCode not installed or not found: the same exit 3 as every model failing.
try { $null = Resolve-OpenCodeExe } catch {
    if (-not (Test-OpenCodeInfraFailure $_)) { throw }
    [Console]::Error.WriteLine("OpenCode unavailable: $($_.Exception.Message) The task falls back to Claude Sonnet (operating-guide §3).")
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
  whole setup block (worktree and branch creation, and any assets.local.ini copy: -LocalOnly has
  already copied it); never run git worktree. Pass git -C "$worktree" explicitly.
- Everything else in the brief is binding: Owns, Done-when, the rules for engine code, the PR
  body, the detach at the end, and the report.
- The PR body's "Closes #$Issue" is the only place a closing keyword may precede #<n>.
- Stay inside ${worktree}: never read, list, write or run anything by a path outside it (not TEMP,
  not your home directory, not Program Files, not another worktree). OpenCode rejects such a call
  and the rejection ENDS your run. Scratch files go under rendered/ in the worktree (git-ignored)
  or are deleted before you commit. Invoke tools by name from PATH (dotnet, git, gh, python,
  godot.cmd in PowerShell or godot in bash). A mutation check runs in place, uncommitted.
"@
$prompt = $brief + $rules
if ($FixturesDir) { $env:IC2_FIXTURES_DIR = $FixturesDir }
# The run is watched (scripts/Invoke-OpenCodeWatched.ps1). It starts OpenCode with stdin CLOSED
# (an empty file): `opencode run` creates no session until stdin's end-of-file, so an inherited
# pipe hung both 2026-09-28 runs. No session within StartupTimeoutSec, a session idle for
# IdleTimeoutSec, or no exit within TotalTimeoutSec kills its process tree. The helper reads the
# output as UTF-8.
# The next model runs only on an infrastructure failure (no session, idle, no exit, an exit
# without a session, a non-zero exit, the fallback-agent guard, a permission-guard rejection), and only when the failed run left nothing
# behind: no new commit, locally or on origin, and no new PR. Its uncommitted edits are discarded.
# An implementer that stops and reports is not a failure: its OpenCode run exits 0, so it is never
# retried on another model; the script then exits 1 at "No open PR" and the main session reads the report.
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
$attempt = 0
foreach ($m in $chain) {
    $attempt++
    $ocArgs = @('run', '--dir', $worktree, '--agent', 'external-implementer', '--model', $models[$m])
    if ($variants[$m]) { $ocArgs += @('--variant', $variants[$m]) }
    Write-Host "attempt $attempt/$($chain.Count): $m ($($models[$m]))"
    $reason = $null
    try {
        $run = Invoke-OpenCodeWatched -Arguments $ocArgs -Prompt $prompt -WorkDir $worktree -Title "ic2-$name-$m" `
            -StartupTimeoutSec $StartupTimeoutSec -TotalTimeoutSec $TotalTimeoutSec -IdleTimeoutSec $IdleTimeoutSec
        $output = $run.Output
        if ($run.ExitCode -ne 0) { $reason = "exit $($run.ExitCode)" }
        # OpenCode's own evidence (its stderr warning, the session's recorded agent), never the model's words.
        elseif ($run.AgentFallback) { $reason = 'fell back to the default agent' }
        # A rejected tool call ends the run with exit 0 (issue #501): a failure, named by its path.
        elseif ($run.PermissionRejected) { $reason = "permission rejected: $($run.PermissionRejected)" }
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
    [Console]::Error.WriteLine("OpenCode unavailable: $why. The task falls back to Claude Sonnet (operating-guide §3). Log: $log")
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
