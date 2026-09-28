<#
.SYNOPSIS
    Hands one pull request to the local OpenCode install for an external review (GLM, then Luna,
    then DeepSeek on an infrastructure failure), and posts the result as the one PR comment
    build-process.md §4.9 expects.

.DESCRIPTION
    The main session fills the reviewer brief itself (build-process.md Appendix B for a task PR,
    the plan-review prompt for a plan PR) and passes it as -BriefFile, exactly as it would to a
    Claude reviewer. This script then:
      1. creates a detached worktree at the PR head under ic2-work\<pr>-external-review;
      2. runs `opencode run` there with the .opencode/agents/external-reviewer.md agent and the
         model of the reviewer, feeding it the brief plus the output rules. The run is watched
         (scripts/Invoke-OpenCodeWatched.ps1): if OpenCode creates no session within
         -StartupTimeoutSec (the 2026-09-28 startup hang) or does not finish within
         -TotalTimeoutSec, its process tree is killed;
      3. takes the run's final message as the review, checks it is complete (header line,
         verdict line, and the verdict repeated as the last line), and posts it as one PR comment
         with `gh pr comment --body-file`;
      4. with -ApplyLabel, applies status:approved or status:rework to the task's issue from the
         verdict, as a Claude reviewer would (never for a plan PR);
      5. removes the worktree.
    With -Reviewer auto (the default) the models form a chain: GLM, then Luna, then DeepSeek, each
    tried once. The next model runs ONLY on an infrastructure failure: no session in time, no exit
    in time, a non-zero exit, the fallback-to-default-agent guard, or an incomplete review. The
    worktree is recreated for each attempt. The posted header names the model that reviewed and
    the ones that failed before it, e.g. "Plan review (Luna; GLM failed: no session in 180 s)".
    If every model fails, nothing is posted and the script exits 3 ("OpenCode unavailable: ...");
    build-process.md §4.9 says what the main session does then. An explicit -Reviewer runs only
    that model, and exits 3 the same way when it fails.
    The model never writes to GitHub: the agent file denies push, merge, comment and label
    commands, and this script is the only writer. A cut-off review (the 2026-09-25 #370 case)
    therefore cannot reach the PR: the run is reported as incomplete and nothing is posted.

    Reviewer -> OpenCode model id, from the Zen model list (`opencode/gpt-6-luna`,
    `opencode/deepseek-v4.1-flash`); `opencode models opencode` shows what this machine has.
    OpenCode reads CLAUDE.md as its instructions file when no AGENTS.md exists; that is
    harmless here (the reviewer gets the token-economy rules) and no AGENTS.md is added.

.PARAMETER Pr
    The pull request number.
.PARAMETER Reviewer
    auto (default: the chain GLM-5.3 at max effort, then Luna, then DeepSeek), or glm, luna,
    deepseek for that model alone.
.PARAMETER BriefFile
    The filled reviewer brief. Its first line must be the review header the model is to print,
    for example "Plan review (Luna)" or "T94 review (DeepSeek)". With -Reviewer auto, the text in
    its final parentheses is replaced by the name of the model each attempt runs.
.PARAMETER Issue
    The task's issue number, needed only with -ApplyLabel.
.PARAMETER ApplyLabel
    Apply status:approved / status:rework to -Issue from the verdict (task PRs only).
.PARAMETER FixturesDir
    A clone of ic2-test-fixtures; exported as IC2_FIXTURES_DIR for the run so the data tests
    run instead of skipping.
.PARAMETER DryRun
    Do everything except post and label; print the review to stdout instead.
.PARAMETER StartupTimeoutSec
    How long a run may take to create its OpenCode session before it is killed (default 180).
.PARAMETER TotalTimeoutSec
    How long a run may take in all before it is killed (default 3600).
.PARAMETER ModelIds
    Overrides of the reviewer -> model id map, e.g. @{ glm = 'opencode/glm-5.4' }, for when
    `opencode models` shows a different id (or, in a test, a bad id to exercise the chain).

.EXAMPLE
    pwsh scripts/external-review.ps1 -Pr 479 -BriefFile C:\tmp\479-brief.md
.EXAMPLE
    pwsh scripts/external-review.ps1 -Pr 466 -Reviewer deepseek -BriefFile C:\tmp\466-brief.md -Issue 24 -ApplyLabel -FixturesDir C:\Users\diego\projects\ic2-test-fixtures
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $Pr,
    [ValidateSet('auto', 'glm', 'luna', 'deepseek')] [string] $Reviewer = 'auto',
    [Parameter(Mandatory)] [string] $BriefFile,
    [int] $Issue,
    [switch] $ApplyLabel,
    [string] $FixturesDir,
    [switch] $DryRun,
    [int] $StartupTimeoutSec = 180,
    [int] $TotalTimeoutSec = 3600,
    [hashtable] $ModelIds
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-OpenCodeWatched.ps1')

# Reviewer name -> OpenCode model id. Edit here (or pass -ModelIds) if `opencode models` shows a
# different id. From the 2026-09-28 comparison of the models this OpenCode plan lists: glm-5.3 at
# max effort (index up to 45) is the review model; gpt-6-luna (29-37) is the cheap routine model and
# a second opinion; deepseek stays for a third. `opencode models` lists what this machine has.
$models = @{
    glm      = 'opencode/glm-5.3'
    luna     = 'opencode/gpt-6-luna'
    deepseek = 'opencode/deepseek-v4.1-flash'
}
if ($ModelIds) { foreach ($k in $ModelIds.Keys) { $models[$k] = $ModelIds[$k] } }
# Provider-specific variant, passed as `--variant` (the docs' flag; a `#variant` suffix on the model
# id is not documented). Empty means none.
$variants = @{ glm = 'max'; luna = 'high'; deepseek = '' }
$displayNames = @{ glm = 'GLM'; luna = 'Luna'; deepseek = 'DeepSeek' }
# The fallback chain (the user's decision of 2026-09-28): each model once, the next only on an
# infrastructure failure.
$chain = if ($Reviewer -eq 'auto') { @('glm', 'luna', 'deepseek') } else { @($Reviewer) }

function Get-RepoRoot {
    $root = git rev-parse --show-toplevel 2>$null
    if (-not $root) { throw 'Run this from inside the repository.' }
    return (Resolve-Path $root).Path
}

$repo = Get-RepoRoot
# ic2-work sits beside the MAIN checkout, even when this script runs from a worktree: resolve the
# main checkout through the common git dir rather than the current worktree's root.
$commonDir = (git -C $repo rev-parse --path-format=absolute --git-common-dir).Trim()
$mainRoot = Split-Path $commonDir -Parent
$workRoot = Join-Path (Split-Path $mainRoot -Parent) 'ic2-work'
$agentFile = Join-Path $repo '.opencode/agents/external-reviewer.md'
if (-not (Test-Path -LiteralPath $agentFile)) { throw "Agent file not found: $agentFile" }
$worktree = Join-Path $workRoot "$Pr-external-review"
if (-not (Test-Path $BriefFile)) { throw "Brief not found: $BriefFile" }
$brief = Get-Content -Raw -LiteralPath $BriefFile
$briefLines = $brief -split "`r?`n"
$briefHeader = $briefLines[0].Trim()
$briefRest = ($briefLines | Select-Object -Skip 1) -join "`n"
if ($briefHeader -notmatch 'review \(') { throw "The brief's first line must be the review header, e.g. 'Plan review (Luna)'; got: $briefHeader" }
if ($ApplyLabel -and -not $Issue) { throw '-ApplyLabel needs -Issue.' }
$null = Resolve-OpenCodeExe   # throws if opencode is not installed
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh is not on PATH.' }

$headSha = gh pr view $Pr --json headRefOid --jq .headRefOid
if (-not $headSha) { throw "Could not read PR #$Pr's head." }
git -C $repo fetch -q origin "pull/$Pr/head"

function New-ReviewWorktree {
    # 1. A detached worktree at the PR head. Never the main checkout. Recreated for every attempt,
    #    so a failed run leaves nothing behind for the next model.
    if (Test-Path $worktree) { git -C $repo worktree remove --force $worktree 2>$null }
    if (Test-Path $worktree) { Remove-Item -Recurse -Force -LiteralPath $worktree }
    git -C $repo worktree prune
    git -C $repo worktree add --detach $worktree $headSha 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git worktree add failed for $worktree at $headSha" }
    Write-Host "worktree: $worktree at $headSha"
    # The PR under review usually does not carry this agent file, and OpenCode silently falls back
    # to its default, full-permission agent when --agent names one it cannot find. Copy the
    # read-only agent into the review worktree (untracked; the worktree is removed afterwards).
    $agentDir = Join-Path $worktree '.opencode/agents'
    New-Item -ItemType Directory -Force -Path $agentDir | Out-Null
    Copy-Item -LiteralPath $agentFile -Destination (Join-Path $agentDir 'external-reviewer.md') -Force
}

$verdicts = 'approve after named fixes', 'approve', 'rework', 'user decision'

function Invoke-ReviewAttempt([string] $Name) {
    # One model, once. Returns Ok + Review + Verdict, or Ok = $false + a short Reason for an
    # infrastructure failure. A closing keyword is not an infrastructure failure: it throws.
    $model = $models[$Name]
    $variant = $variants[$Name]
    $header = if ($Reviewer -eq 'auto') { $briefHeader -replace '\([^()]*\)\s*$', "($($displayNames[$Name]))" } else { $briefHeader }
    $rules = @"

---
OUTPUT RULES (from scripts/external-review.ps1; they override anything above that conflicts):
- Do not post to GitHub, edit, commit, push, label or merge anything. The script that runs you
  posts your review and applies the label.
- Your final message is the review and nothing else. Line 1 is exactly: $header
  Line 2 is the verdict, one of: approve, approve after named fixes, rework, user decision.
  Then the findings (R1, R2, ... with file and line, blocking or not), then the verdict again
  as the very last line. A review that does not end with the verdict line is treated as cut off
  and is not posted.
- The worktree you are in is $worktree at $headSha. Pass git -C "$worktree" explicitly.
"@
    $prompt = $header + "`n" + $briefRest + $rules
    $fail = { param($reason, $detail) [pscustomobject]@{ Ok = $false; Name = $Name; Model = $model; Header = $header; Reason = $reason; Detail = $detail } }

    New-ReviewWorktree
    # 2. Run OpenCode in the worktree, watched. `opencode run [message..]` is non-interactive;
    #    `--dir` sets the directory it runs in, `--agent` and `--model provider/model` are the
    #    documented flags, and `--variant` carries the provider-specific variant. What `run` prints
    #    on stdout is not documented beyond "formatted", so step 3 looks for the header line rather
    #    than assuming the output is the final message alone. The helper reads the output back as
    #    UTF-8, so an em dash or a curly quote reaches the PR intact.
    $ocArgs = @('run', '--dir', $worktree, '--agent', 'external-reviewer', '--model', $model)
    if ($variant) { $ocArgs += @('--variant', $variant) }
    $title = "ic2-pr$Pr-$Name-$(Get-Date -Format yyyyMMddHHmmss)"
    try {
        $run = Invoke-OpenCodeWatched -Arguments $ocArgs -Prompt $prompt -WorkDir $worktree -Title $title `
            -StartupTimeoutSec $StartupTimeoutSec -TotalTimeoutSec $TotalTimeoutSec
    } catch [System.TimeoutException] {
        return (& $fail $_.Exception.Data['Reason'] $_.Exception.Message)
    } catch {
        return (& $fail "could not run ($($_.Exception.Message.Split("`n")[0]))" $_.Exception.Message)
    }
    $output = $run.Output
    if ($run.ExitCode -ne 0) { return (& $fail "exit $($run.ExitCode)" $output) }
    if ($output -match 'Falling back to default agent') { return (& $fail 'fell back to the default agent' "OpenCode did not load the external-reviewer agent (it fell back to its default, full-permission agent). Output:`n$output") }

    # 3. Completeness. The review starts at the header line (OpenCode may print tool chatter
    #    before it); it must have a verdict on line 2 and repeat it as the last non-empty line.
    $review = $run.StdOut
    $idx = $review.IndexOf($header)
    if ($idx -lt 0) { return (& $fail 'no header line in its output' "The run's output has no header line '$header'. Output:`n$output") }
    $review = $review.Substring($idx).TrimEnd()
    $lines = $review -split "`r?`n"
    $verdict = if ($lines.Count -gt 1) { $lines[1].Trim().ToLowerInvariant() } else { '' }
    if ($verdicts -notcontains $verdict) { return (& $fail 'no verdict on line 2' "Line 2 is not a verdict ('$verdict'). Output:`n$review") }
    $last = ($lines | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim().ToLowerInvariant()
    if ($last -ne $verdict) { return (& $fail 'review cut off' "The review does not end with its verdict line ('$last' vs '$verdict'): cut off. Output:`n$review") }
    if ($review -match '(?i)\b(close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved)\s+#\d+') { throw "The review contains a closing keyword before #<n>. Nothing posted." }
    return [pscustomobject]@{ Ok = $true; Name = $Name; Model = $model; Header = $header; Review = $review; Verdict = $verdict }
}

$result = $null
$failures = @()
try {
    if ($FixturesDir) { $env:IC2_FIXTURES_DIR = $FixturesDir }
    $n = 0
    foreach ($name in $chain) {
        $n++
        Write-Host "attempt $n/$($chain.Count): $($displayNames[$name]) ($($models[$name]))"
        $attempt = Invoke-ReviewAttempt $name
        if ($attempt.Ok) { $result = $attempt; break }
        Write-Host "attempt $n/$($chain.Count): $($displayNames[$name]) failed: $($attempt.Reason)"
        Write-Host ((($attempt.Detail -split "`r?`n") | Select-Object -Last 20) -join "`n")
        $failures += $attempt
    }
    if ($result) {
        $review = $result.Review
        if ($failures) {
            # Name the models that failed before this one in the posted header.
            $note = ($failures | ForEach-Object { "$($displayNames[$_.Name]) failed: $($_.Reason)" }) -join '; '
            $lines = $review -split "`r?`n"
            $lines[0] = $result.Header -replace '\)\s*$', "; $note)"
            $review = $lines -join "`n"
        }
        $body = $review + "`n`n— $($displayNames[$result.Name]), via scripts/external-review.ps1 ($($result.Model))"
        if ($DryRun) {
            Write-Output $body
        } else {
            # 4. Post, and label.
            $bodyFile = Join-Path $env:TEMP "ic2-review-$Pr.md"
            Set-Content -LiteralPath $bodyFile -Value $body -Encoding utf8
            gh pr comment $Pr --body-file $bodyFile | Out-Null
            Write-Host "posted: $(($review -split "`r?`n")[0]) / $($result.Verdict)"
            if ($ApplyLabel) {
                if ($result.Verdict -eq 'approve') {
                    gh issue edit $Issue --add-label status:approved --remove-label status:in-review | Out-Null
                } elseif ($result.Verdict -eq 'rework' -or $result.Verdict -eq 'approve after named fixes') {
                    gh issue edit $Issue --add-label status:rework --remove-label status:in-review | Out-Null
                } else {
                    Write-Host "verdict '$($result.Verdict)' applies no label; the main session decides."
                }
            }
        }
    }
}
finally {
    # 5. Clean up.
    git -C $repo worktree remove --force $worktree 2>$null
}

if (-not $result) {
    $reasons = ($failures | ForEach-Object { "$($displayNames[$_.Name]): $($_.Reason)" }) -join '; '
    [Console]::Error.WriteLine("OpenCode unavailable: $reasons. Nothing posted.")
    exit 3
}
