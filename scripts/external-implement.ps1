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
         the chosen model, feeding it the brief plus the run rules;
      4. checks the outcome: a PR exists for the branch, the worktree is clean and pushed, and
         it is detached so the branch is free for the reviewer; saves the run's output next to
         the worktree as <name>.implementer.log and prints its tail.
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
    mimo-flash-free (default), mimo-pro, mimo-flash, glm, or luna.
.PARAMETER LocalOnly
    Copy assets.local.ini from the main checkout into the worktree.
.PARAMETER FixturesDir
    A clone of ic2-test-fixtures, exported as IC2_FIXTURES_DIR for the run.

.EXAMPLE
    pwsh scripts/external-implement.ps1 -Task T71 -Slug persistence-hardening -Issue 308 -BriefFile C:\tmp\T71-brief.md
.EXAMPLE
    pwsh scripts/external-implement.ps1 -Fix 346 -Slug migration-message -BriefFile C:\tmp\346-brief.md -Model mimo-flash
#>
[CmdletBinding()]
param(
    [string] $Task,
    [int] $Fix,
    [Parameter(Mandatory)] [string] $Slug,
    [int] $Issue,
    [Parameter(Mandatory)] [string] $BriefFile,
    [ValidateSet('mimo-flash-free', 'mimo-pro', 'mimo-flash', 'glm', 'luna')] [string] $Model = 'mimo-flash-free',
    [switch] $LocalOnly,
    [string] $FixturesDir
)

$ErrorActionPreference = 'Stop'

# Chosen on 2026-09-28 from the OpenCode Go table (intelligence index / cost per benchmark task):
# MiMo-V2.6-Pro 46 / $0.13 is the default; MiMo-V2.6-Flash 38 / $0.06 for fixes and Low effort;
# GLM-5.3 45 / $2.01 is the escalation after a failed rework round; Luna is the reviewer's model
# and implements only when an entry says why. Confirm the ids with `opencode models` on first use.
# On 2026-09-28 the desktop's `opencode models` listed mimo-v2.6-flash-free, glm-5.3 and gpt-6-luna,
# but neither mimo-v2.6-pro nor mimo-v2.6-flash; those two stay in the table for the day the plan
# lists them, and mimo-flash-free is the default until then.
$models = @{
    'mimo-flash-free' = 'opencode/mimo-v2.6-flash-free'
    'mimo-pro'        = 'opencode/mimo-v2.6-pro'
    'mimo-flash'      = 'opencode/mimo-v2.6-flash'
    'glm'             = 'opencode/glm-5.3'
    'luna'            = 'opencode/gpt-6-luna'
}
$variants = @{ 'mimo-flash-free' = ''; 'mimo-pro' = ''; 'mimo-flash' = ''; 'glm' = 'max'; 'luna' = 'high' }
$modelId = $models[$Model]
$variant = $variants[$Model]

if (-not $Task -and -not $Fix) { throw 'Give -Task T<nn> or -Fix <issue>.' }
if ($Task -and $Fix) { throw '-Task and -Fix are mutually exclusive.' }
if ($Task -and $Task -notmatch '^T\d{2,3}$') { throw "-Task must look like T24; got $Task" }
if (-not (Test-Path $BriefFile)) { throw "Brief not found: $BriefFile" }
foreach ($tool in 'opencode', 'gh', 'git') { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not on PATH." } }

$repo = (Resolve-Path (git rev-parse --show-toplevel)).Path
$workRoot = Join-Path (Split-Path $repo -Parent) 'ic2-work'
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
$args = @('run', '--dir', $worktree, '--agent', 'external-implementer', '--model', $modelId)
if ($variant) { $args += @('--variant', $variant) }
Push-Location $worktree
try {
    $output = (& opencode @args $prompt 2>&1 | Out-String)
} finally { Pop-Location }
Set-Content -LiteralPath $log -Value $output -Encoding utf8
Write-Host "run output: $log"
if ($LASTEXITCODE -ne 0) { Write-Warning "opencode exited with $LASTEXITCODE" }

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
