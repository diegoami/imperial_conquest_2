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
         (scripts/Invoke-OpenCodeWatched.ps1) with the prompt on stdin, from a file, never on the
         command line (Windows caps it at 32,767 characters); the file's end is the end-of-file
         `opencode run` waits for before it creates a session (the 2026-09-28 hangs). If
         OpenCode creates no session within -StartupTimeoutSec, its session makes no progress
         for -IdleTimeoutSec, or it does not finish within -TotalTimeoutSec, its process tree is
         killed;
      4. checks the outcome: a PR exists for the branch, the worktree is clean and pushed, and
         it is detached so the branch is free for the reviewer; saves the run's output next to
         the worktree as <name>.implementer.log and prints its tail.
    With -Model auto (the default) the chain is deepseek-flash (on OpenCode Go, or on the Alibaba
    Token Plan when quota-tracker's /avoid lists opencode_go), then qwen-flash (Qwen3.8 Flash, Alibaba),
    then the main session runs Claude Sonnet (the user's decision of 2026-10-05, which extends issue
    #575's one OpenCode model per role). A chain model none of whose routes has quota is skipped. GLM left the
    implementer side on 2026-10-01 (issue #573): GLM-5.3 ended T99's implementer run early,
    mid-exploration, with no error (#557), while DeepSeek V4.1 Flash implemented T97 in one go.
    glm, glm-flash and luna stay valid as explicit -Model values, and no default path picks them.
    The next model runs ONLY on an infrastructure failure (no session
    in time, an idle session, no exit in time, a run that exits without a session, a non-zero exit, the
    fallback-to-default-agent guard, a tool call the permission guard rejected -- issue #501), and only when the failed run left nothing behind: no new
    commit, locally or on origin, and no new PR. Otherwise the script exits 1 and the main session
    decides. Two consecutive attempts failing with the same cause (Get-OpenCodeFailureClass: two
    startup hangs, two idle kills, ...) stop the chain early. When every model fails, the chain
    stops that way, or OpenCode is not installed, it exits 3 ("OpenCode unavailable: ..."), and the task falls back to Claude Sonnet (operating-guide
    §3). An implementer that stops and reports has not failed: its OpenCode run exits 0, so it is
    never retried on another model; the script then exits 1 ("No open PR") and the main session
    reads the report. Once every chain model has failed, the script asks Choose-Model.ps1 for an
    implementer outside the failed families (Done-when 2) and runs it once before exit 3; it writes
    the run's attempts and the allowed outside paths it touched into the PR body (Done-when 2, 3).
    Before that, it copies -BriefFile into the worktree at rendered/brief.md and feeds the copy
    (Done-when 1), so the prompt names no path outside the worktree.
    Before the run's tail it prints the model that ran ("implemented by: <name>"); the review passes it to
    external-review.ps1 as -ExcludeModel, so the reviewer is never the implementer's model.
    The script never merges, labels or reviews; the main session does those (Appendix C).
    It runs on OpenCode 1.x (the npm CLI, the default) or 2.x (the desktop app's CLI, opt-in through
    IC2_OPENCODE_EXE): see
    scripts/Invoke-OpenCodeWatched.ps1 for which one runs and how its arguments differ.
    The same agent file (.opencode/agents/external-implementer.md) serves both.

    Model names -> OpenCode model ids (`opencode models` lists what this machine has). The runs
    are on OpenCode Go, `opencode-go/…`, per the user's decision of 2026-10-01 (issue #551), except
    luna: the direct OpenAI route, `openai/gpt-5.6-luna`, via the machine's OpenAI login (issue #575).
    The Alibaba Token Plan (`alibaba-token-plan/…`, the user's decision of 2026-10-05) carries
    qwen-flash and a second route for deepseek-flash and glm (-Route). Its key is the user variable
    ALIBABA_TOKEN_PLAN_API_KEY, loaded into this process when missing and never printed; "Invalid
    API-key" or "Provider not found" from it stops the script (exit 1) with the cause, never a retry.

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
    auto (default: deepseek-flash, then qwen-flash, then the main session runs Claude Sonnet), or
    one model alone: qwen-flash (Qwen3.8 Flash at medium, `alibaba-token-plan/qwen3.8-flash`; it
    offers low, medium and xhigh), luna (GPT-5.6 Luna at
    high effort, `openai/gpt-5.6-luna`, direct OpenAI via the machine's OpenAI login), glm-flash
    (GLM-5.3 Flash at high), glm (GLM-5.3 at low, a heavy model run light; only selected explicitly), deepseek-flash (DeepSeek V4.1 Flash
    at high, proven on this repository in #279), mimo-pro, or mimo-flash.
.PARAMETER Route
    Which provider deepseek-flash and glm run through: auto (the default) takes the usual one
    (OpenCode Go, Z.AI) unless quota-tracker's /avoid lists it, then the Alibaba Token Plan's id for
    the same model (`deepseek-v4.1-flash`, `glm-5.3`); when the tracker does not answer, the usual
    route. go, zai or alibaba force one: an explicit -Model that route does not serve is refused (exit
    1), and an auto chain drops such a model. The route is printed, logged and named on the
    "implemented by:" line.
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
.PARAMETER WhatIf
    Print each chain model's OpenCode argument line (the CLI's version decides its syntax), print the
    destination the brief would be copied to inside the worktree (Done-when 1), and exit 0, without
    creating a worktree, starting a run or billing a model (it does start `opencode --version`, in its own scratch directories).
.PARAMETER SimulateFailed
    With -WhatIf: the implementer model names (comma-separated or repeated) to treat as failed for
    the check. The script prints the substitute Choose-Model.ps1 would pick with those models'
    families excluded, and why, without running anything (Done-when 2's check).
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
    [ValidateSet('auto', 'luna', 'glm-flash', 'glm', 'deepseek-flash', 'qwen-flash', 'mm-m3', 'mm-m2.7', 'ali-deepseek-flash', 'ali-deepseek-pro', 'ali-glm', 'mimo-pro', 'mimo-flash')] [string] $Model = 'auto',
    [ValidateSet('auto', 'go', 'zai', 'alibaba')] [string] $Route = 'auto',
    [switch] $LocalOnly,
    [string] $FixturesDir,
    [int] $StartupTimeoutSec = 180,
    [int] $TotalTimeoutSec = 10800,
    [int] $IdleTimeoutSec = 900,
    [switch] $WhatIf,
    [string[]] $SimulateFailed,
    [hashtable] $ModelIds
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-OpenCodeWatched.ps1')
# The Alibaba Token Plan's key comes from the user environment when this process predates it (never printed).
$null = Import-AlibabaTokenPlanKey

function Get-SubstituteModel {
    # T150 Done-when 2: once every model of the chain has failed, ask Choose-Model for an implementer
    # outside those families. Returns the model name, or $null; -Why is filled either way. Run in
    # process: Choose-Model returns before the rest of the script when dot-sourced, but here it is
    # called normally, prints the name with -Pick (its `exit` ends only that script) and sets
    # $LASTEXITCODE.
    param([string[]] $ExcludeFamilies, [ref] $Why)
    $chooser = Join-Path $PSScriptRoot 'Choose-Model.ps1'
    if (-not (Test-Path -LiteralPath $chooser)) { $Why.Value = "Choose-Model.ps1 not found at $chooser"; return $null }
    try { $out = & $chooser -Role implementer -ExcludeFamily $ExcludeFamilies -Pick 2>&1 }
    catch { $Why.Value = "Choose-Model.ps1 failed: $($_.Exception.Message)"; return $null }
    if ($LASTEXITCODE -ne 0) { $Why.Value = "Choose-Model.ps1 found no implementer outside $($ExcludeFamilies -join ', ')"; return $null }
    $name = @($out) | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ } | Select-Object -Last 1
    if (-not $name) { $Why.Value = 'Choose-Model.ps1 printed no model name'; return $null }
    $Why.Value = "-ExcludeFamily $($ExcludeFamilies -join ',') on the implementer ranking picked $name"
    return $name
}

function Set-PullRequestSection {
    # Appends (or replaces) one "## <Heading>" section in the PR body (Done-when 2 and 3 write the
    # "Implementer attempts" and "Outside paths touched" sections). The body is edited with
    # gh pr edit, through a scratch file inside the worktree (a path outside it is refused).
    param([int] $Pr, [string] $Heading, [string] $Body, [string] $Worktree)
    $marker = "## $Heading"
    $current = gh pr view $Pr --json body --jq '.body' 2>$null
    if ($null -eq $current) { Write-Warning "could not read PR #$Pr's body; not adding '$Heading'"; return }
    $lines = @($current -split "`r?`n")
    $kept = New-Object System.Collections.Generic.List[string]
    $skip = $false
    foreach ($line in $lines) {
        if ($line -match '^##\s') { $skip = ($line.Trim() -eq $marker) }
        if (-not $skip) { $kept.Add($line) }
    }
    $newBody = (($kept -join "`n").TrimEnd()) + "`n`n$marker`n`n$Body`n"
    $dir = Join-Path $Worktree 'rendered'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $file = Join-Path $dir 'pr-body.md'
    [System.IO.File]::WriteAllText($file, $newBody, [System.Text.UTF8Encoding]::new($false))
    gh pr edit $Pr --body-file $file | Out-Null
}

# On 2026-10-01 the user moved the OpenCode runs from OpenCode Zen to OpenCode Go (issue #551):
# every id is `opencode-go/…` and no Zen model is used, the free ones included. The one exception
# is luna: the direct OpenAI route, `openai/gpt-5.6-luna`, via the machine's OpenAI login (issue
# #575). The default chain is DeepSeek V4.1 Flash (high effort) alone (issue #575: one OpenCode
# model per role before Claude), then the main session runs Claude Sonnet. GLM left the
# implementer side in #573 (GLM-5.3 ended T99's run early, mid-exploration, with no error, #557)
# and luna is no longer on the default path, but glm, glm-flash and luna stay valid as explicit
# -Model values.
# mimo-flash-free is dropped, Go does not offer it; MiMo Pro and MiMo Flash stay in the table for
# the day the plan lists them. Confirm the ids with `opencode models` on first use; -ModelIds
# overrides any of them.
$models = @{
    'luna'            = 'openai/gpt-5.6-luna'
    'glm-flash'       = 'zai-coding-plan/glm-5.3-flash'
    'glm'             = 'zai-coding-plan/glm-5.3'
    'deepseek-flash'  = 'opencode-go/deepseek-v4.1-flash'
    'mimo-pro'        = 'opencode-go/mimo-v2.6-pro'
    'mimo-flash'      = 'opencode-go/mimo-v2.6-flash'
    # Qwen3.8 Flash on the Alibaba Token Plan (the user's decision of 2026-10-05).
    'qwen-flash'      = 'alibaba-token-plan/qwen3.8-flash'
    # MiniMax (minimax.io Token Plan, provider `minimax`) and the Alibaba Token Plan's DeepSeek and GLM
    # under names of their own (the owner's decision of 2026-10-06); explicit -Model values only.
    'mm-m3'              = 'minimax/MiniMax-M3'
    'mm-m2.7'            = 'minimax/MiniMax-M2.7'
    'ali-deepseek-flash' = 'alibaba-token-plan/deepseek-v4.1-flash'
    'ali-deepseek-pro'   = 'alibaba-token-plan/deepseek-v4-pro-0813'
    'ali-glm'            = 'alibaba-token-plan/glm-5.3'
}
# The Alibaba Token Plan route of the same models (-Route; the user's decision of 2026-10-05).
$alibabaIds = @{
    'deepseek-flash' = 'alibaba-token-plan/deepseek-v4.1-flash'
    'glm'            = 'alibaba-token-plan/glm-5.3'
}
if ($ModelIds) { foreach ($k in $ModelIds.Keys) { $models[$k] = $ModelIds[$k] } }
# Provider-specific variant. Invoke-OpenCodeWatched passes it by the CLI's major version (1.x
# `--variant v`, 2.x the model's `#v` suffix). Empty means none. Effort is `high` everywhere (issue #575: `max`
# is overkill); luna was already high.
# Heavy models run light (the user's decision of 2026-10-05): glm at low (GLM-5.3 has no medium).
# qwen-flash at medium: Qwen3.8 Flash offers low, medium and xhigh, no high.
$variants = @{ 'luna' = 'high'; 'glm-flash' = 'high'; 'glm' = 'low'; 'deepseek-flash' = 'high'; 'qwen-flash' = 'medium'; 'mm-m3' = 'thinking'; 'mm-m2.7' = ''; 'ali-deepseek-flash' = 'high'; 'ali-deepseek-pro' = 'high'; 'ali-glm' = 'low'; 'mimo-pro' = ''; 'mimo-flash' = '' }
# The fallback chain (the user's decision of 2026-10-05, after issue #575's of 2026-10-01): DeepSeek
# V4.1 Flash (Go, or Alibaba when Go is avoided), then Qwen3.8 Flash, then the main session runs
# Claude Sonnet. An explicit -Model runs that model alone.
$chain = if ($Model -eq 'auto') { @('deepseek-flash', 'qwen-flash') } else { @($Model) }
# Each chain model's route: quota-tracker's /avoid is read once (-Route auto); a silent tracker keeps
# the usual route. An auto chain drops a model with no route that has quota, or one an explicit
# -Route does not serve; an explicit -Model the route does not serve is refused.
$quota = Get-QuotaAvoid
$resolved = @{}
$kept = @()
# Done-when 2: every model this run tried, with its route and its failure class, for the PR body's
# "Implementer attempts" section. A model an auto chain drops for quota is recorded here too.
$attempts = @()
foreach ($m in $chain) {
    $r = Resolve-OpenCodeRoute -Usual $models[$m] -Alibaba $alibabaIds[$m] -Route $Route -Answered $quota.Answered -Avoid $quota.Providers
    if ($r.Refused -and $Model -ne 'auto') { [Console]::Error.WriteLine("Refused: ${m}: $($r.Why)."); exit 1 }
    if ($r.Refused -or ($r.Avoided -and $Model -eq 'auto')) {
        Write-Host "skipped: $m ($($r.Why))"
        $attempts += [pscustomobject]@{ Model = $m; Route = '-'; Class = 'quota-skip'; Why = $r.Why }
        continue
    }
    if ($r.Avoided) { Write-Warning "${m}: $($r.Why); it runs because -Model names it (the main session's choice)." }
    $resolved[$m] = $r
    $kept += $m
    Write-Host "route: $m on $($r.Route) ($($r.Model)): $($r.Why)"
}
$chain = $kept

if (-not $Task -and -not $Fix) { throw 'Give -Task T<nn> or -Fix <issue>.' }
if ($Task -and $Fix) { throw '-Task and -Fix are mutually exclusive.' }
if ($Task -and $Task -notmatch '^T\d{2,3}$') { throw "-Task must look like T24; got $Task" }
if (-not (Test-Path $BriefFile)) { throw "Brief not found: $BriefFile" }
# OpenCode not installed or not found, or a major version this script has no arguments for (only 1.x
# and 2.x): the same exit 3 as every model failing. The version is read once (T98).
try { $cli = Get-OpenCodeCli } catch {
    if (-not (Test-OpenCodeInfraFailure $_)) { throw }
    [Console]::Error.WriteLine("OpenCode unavailable: $($_.Exception.Message) The task falls back to Claude Sonnet (operating-guide §3).")
    exit 3
}
if (-not $chain) {
    [Console]::Error.WriteLine("OpenCode unavailable: no chain model has a route with quota (quota-tracker /avoid: $($quota.Providers -join ', ')). The task falls back to Claude Sonnet (operating-guide §3).")
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
# Done-when 1: the brief lives in the worktree. Its destination is fixed before the run, so -WhatIf
# can print it and the real run copies it there before OpenCode starts.
$briefPath = Join-Path $worktree 'rendered/brief.md'

if ($WhatIf) {
    # No worktree, no OpenCode, no billing: the argument line each chain model would get, and where
    # the brief would be copied (Done-when 1).
    Write-Host "brief: $BriefFile -> $briefPath"
    if ($SimulateFailed) {
        # Done-when 2's check: treat the named models as failed and show the substitute, without
        # running anything. Commas are split because `pwsh -File ... -SimulateFailed a,b` binds the
        # whole list as one string.
        $failed = @($SimulateFailed | ForEach-Object { @([string]$_) -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $why = $null
        $sub = Get-SubstituteModel -ExcludeFamilies $failed -Why ([ref]$why)
        Write-Host "simulate failed: $($failed -join ', ')"
        if ($sub) { Write-Host "would substitute: $sub ($why)" }
        else { Write-Host "would substitute: none ($why)" }
    }
    $whatIfPrompt = Get-Content -Raw -LiteralPath $BriefFile
    foreach ($m in $chain) {
        Write-Host "would attempt: $m"
        $null = Invoke-OpenCodeWatched -WhatIf -Agent 'external-implementer' -Model $resolved[$m].Model -Variant $variants[$m] -Prompt $whatIfPrompt `
            -WorkDir (Get-Location).Path -Title "ic2-$(if ($Task) { $Task } else { "fix-$Fix" })-$m"
    }
    exit 0
}

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

# 3. The run. Done-when 1: copy the brief into the worktree (rendered/ is git-ignored there) and
# feed the copy, so the prompt never names a path outside the worktree. An absolute -BriefFile in
# the main checkout is replaced by the copy's path if the brief text named it.
$renderedDir = Join-Path $worktree 'rendered'
New-Item -ItemType Directory -Force -Path $renderedDir | Out-Null
Copy-Item -LiteralPath $BriefFile -Destination $briefPath -Force
$brief = Get-Content -Raw -LiteralPath $briefPath
if ($brief.Contains($BriefFile)) { $brief = $brief.Replace($BriefFile, $briefPath) }
$rules = @"

---
RUN RULES (from scripts/external-implement.ps1; they override the brief where they conflict):
- Your worktree is $worktree on branch $branch, already created and pushed. Skip the brief's
  whole setup block (worktree and branch creation, and any assets.local.ini copy: -LocalOnly has
  already copied it); never run git worktree. Pass git -C "$worktree" explicitly.
- Your brief is copied into your worktree at $briefPath (git-ignored). Read it there, never the
  main checkout's copy.
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
# The run is watched (scripts/Invoke-OpenCodeWatched.ps1). It starts OpenCode with the prompt on
# stdin, from a file (never the command line, capped at 32,767 characters): `opencode run` creates
# no session until stdin's end-of-file, which the file's end gives, where an inherited pipe hung
# both 2026-09-28 runs. No session within StartupTimeoutSec, a session idle for
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
# The finishing run's object, for the "Outside paths touched" section (Done-when 3).
$finalRun = $null
[System.IO.File]::WriteAllText($log, '')
$attempt = 0
foreach ($m in $chain) {
    $attempt++
    Write-Host "attempt $attempt/$($chain.Count): $m ($($resolved[$m].Model), route $($resolved[$m].Route))$(if ($variants[$m]) { " with variant $($variants[$m])" }), OpenCode $($cli.Version)"
    $reason = $null
    try {
        $run = Invoke-OpenCodeWatched -Agent 'external-implementer' -Model $resolved[$m].Model -Variant $variants[$m] -Prompt $prompt -WorkDir $worktree -Title "ic2-$name-$m" `
            -StartupTimeoutSec $StartupTimeoutSec -TotalTimeoutSec $TotalTimeoutSec -IdleTimeoutSec $IdleTimeoutSec
        $output = $run.Output
        # A rejected tool call ends the run (issue #501): exit 0 under 1.x, exit 1 under 2.x. It is
        # named by its path, and checked first so the 2.x exit 1 does not hide it as a plain "exit 1".
        if ($run.PermissionRejected) { $reason = "permission rejected: $($run.PermissionRejected)" }
        elseif ($run.ExitCode -ne 0) { $reason = "exit $($run.ExitCode)" }
        # OpenCode's own evidence (its stderr warning, the session's recorded agent), never the model's words.
        elseif ($run.AgentFallback) { $reason = 'fell back to the default agent' }
    } catch {
        # Only OpenCode's own failures advance the chain; anything else is rethrown (exit 1).
        if (-not (Test-OpenCodeInfraFailure $_)) { throw }
        $reason = $_.Exception.Data['Reason']; $output = $_.Exception.Message
    }
    Add-Content -LiteralPath $log -Value "=== $m ($($resolved[$m].Model), route $($resolved[$m].Route)): $(if ($reason) { "failed: $reason" } else { 'ran' }) ===`n$output" -Encoding utf8
    if (-not $reason) {
        $implementedBy = $m
        $attempts += [pscustomobject]@{ Model = $m; Route = $resolved[$m].Route; Class = 'ran'; Why = '' }
        $finalRun = $run
        break
    }
    Write-Warning "$m failed: $reason"
    $failures += "${m}: $reason"
    # Two consecutive attempts failing with one cause (two startup hangs, two idle kills) mean
    # OpenCode itself is the problem, not the model: stop the chain (operating-guide §3).
    $class = Get-OpenCodeFailureClass $reason
    $attempts += [pscustomobject]@{ Model = $m; Route = $resolved[$m].Route; Class = $class; Why = $reason }
    git -C $repo fetch -q origin
    $prNow = gh pr list --head $branch --state open --json number --jq '.[0].number' 2>$null
    $leftWork = (git -C $worktree rev-parse HEAD) -ne $startSha -or
        ((git -C $repo rev-parse "origin/$branch" 2>$null) -ne $startRemote) -or
        ($prNow -and $prNow -ne $startPr)
    if ($leftWork) { [Console]::Error.WriteLine("$m failed ($reason) after committing, pushing or opening a PR on $branch; not retrying on another model. The main session decides. Log: $log"); exit 1 }
    git -C $worktree reset -q --hard $startSha
    git -C $worktree clean -q -fd
    if ($class -eq $lastClass) { $sameCause = $class; break }
    $lastClass = $class
}
Write-Host "run output: $log"
# Done-when 2: every model of the chain failed (or was skipped for quota), so ask Choose-Model for
# an implementer of another family and run it once. Nothing has been left behind by the failed runs
# (checked above), so the worktree is at the starting commit. If Choose-Model returns nothing, exit 3
# is unchanged.
if (-not $implementedBy) {
    $failedFamilies = @($attempts | Where-Object { $_.Class -ne 'ran' } | Select-Object -ExpandProperty Model -Unique)
    $subWhy = $null
    $substitute = Get-SubstituteModel -ExcludeFamilies $failedFamilies -Why ([ref]$subWhy)
    if (-not $substitute) {
        Write-Host "the chain is spent; no substitute: $subWhy"
    } else {
        Write-Host "the chain is spent; substitute: $subWhy"
        $sr = Resolve-OpenCodeRoute -Usual $models[$substitute] -Alibaba $alibabaIds[$substitute] -Route $Route -Answered $quota.Answered -Avoid $quota.Providers
        if ($sr.Refused) {
            Write-Warning "substitute $substitute refused: $($sr.Why)"
            $attempts += [pscustomobject]@{ Model = $substitute; Route = '-'; Class = 'quota-skip'; Why = $sr.Why }
        } else {
            if ($sr.Avoided) { Write-Warning "${substitute}: $($sr.Why); it runs anyway as the substitute." }
            $attempt++
            Write-Host "attempt $attempt (substitute): $substitute ($($sr.Model), route $($sr.Route))$(if ($variants[$substitute]) { " with variant $($variants[$substitute])" }), OpenCode $($cli.Version)"
            $reason = $null
            try {
                $run = Invoke-OpenCodeWatched -Agent 'external-implementer' -Model $sr.Model -Variant $variants[$substitute] -Prompt $prompt -WorkDir $worktree -Title "ic2-$name-$substitute" `
                    -StartupTimeoutSec $StartupTimeoutSec -TotalTimeoutSec $TotalTimeoutSec -IdleTimeoutSec $IdleTimeoutSec
                $output = $run.Output
                if ($run.PermissionRejected) { $reason = "permission rejected: $($run.PermissionRejected)" }
                elseif ($run.ExitCode -ne 0) { $reason = "exit $($run.ExitCode)" }
                elseif ($run.AgentFallback) { $reason = 'fell back to the default agent' }
            } catch {
                if (-not (Test-OpenCodeInfraFailure $_)) { throw }
                $reason = $_.Exception.Data['Reason']; $output = $_.Exception.Message
            }
            Add-Content -LiteralPath $log -Value "=== $substitute ($($sr.Model), route $($sr.Route)): $(if ($reason) { "failed: $reason" } else { 'ran' }) ===`n$output" -Encoding utf8
            if (-not $reason) {
                $implementedBy = $substitute
                $resolved[$substitute] = $sr
                $attempts += [pscustomobject]@{ Model = $substitute; Route = $sr.Route; Class = 'ran'; Why = '' }
                $finalRun = $run
            } else {
                Write-Warning "$substitute failed: $reason"
                $failures += "${substitute}: $reason"
                $attempts += [pscustomobject]@{ Model = $substitute; Route = $sr.Route; Class = (Get-OpenCodeFailureClass $reason); Why = $reason }
                git -C $repo fetch -q origin
                $prNow = gh pr list --head $branch --state open --json number --jq '.[0].number' 2>$null
                $leftWork = (git -C $worktree rev-parse HEAD) -ne $startSha -or
                    ((git -C $repo rev-parse "origin/$branch" 2>$null) -ne $startRemote) -or
                    ($prNow -and $prNow -ne $startPr)
                if ($leftWork) { [Console]::Error.WriteLine("$substitute failed ($reason) after committing, pushing or opening a PR on $branch; the main session decides. Log: $log"); exit 1 }
                git -C $worktree reset -q --hard $startSha
                git -C $worktree clean -q -fd
            }
        }
    }
}
if (-not $implementedBy) {
    # Not Write-Error: under ErrorActionPreference Stop it would end the script with exit 1, not 3.
    $why = if ($sameCause) { "same failure twice: $sameCause ($($failures -join '; '))" } else { $failures -join '; ' }
    [Console]::Error.WriteLine("OpenCode unavailable: $why. The task falls back to Claude Sonnet (operating-guide §3). Log: $log")
    exit 3
}
if ($failures) { Write-Host "fell back: $($failures -join '; ')" }
# The reviewer must not be this model: pass it to external-review.ps1 as -ExcludeModel.
Write-Host "implemented by: $implementedBy ($($resolved[$implementedBy].Model), route $($resolved[$implementedBy].Route))"

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

# Done-when 2 and 3: the two sections this run writes into the PR body, once it exists.
$prNumber = 0
try { $prNumber = [int](($pr | ConvertFrom-Json).number) } catch { }
if ($prNumber -gt 0) {
    $attemptLines = @($attempts | ForEach-Object {
            "- $($_.Model) ($($_.Route)): $($_.Class)$(if ($_.Why) { " -- $($_.Why)" })"
        })
    $attemptLines += "ran: $implementedBy ($($resolved[$implementedBy].Model), route $($resolved[$implementedBy].Route))"
    Set-PullRequestSection -Pr $prNumber -Heading 'Implementer attempts' -Body ($attemptLines -join "`n") -Worktree $worktree

    if ($finalRun -and $finalRun.OutsidePathsKnown) {
        $paths = @($finalRun.OutsidePaths)
        if ($paths.Count -eq 0) { $outsideBody = 'none' } else { $outsideBody = (@($paths | ForEach-Object { "- $_" }) -join "`n") }
    } else {
        $outsideBody = 'unknown: the session export failed'
    }
    Write-Host 'Outside paths touched:'
    Write-Host $outsideBody
    Set-PullRequestSection -Pr $prNumber -Heading 'Outside paths touched' -Body $outsideBody -Worktree $worktree
}
