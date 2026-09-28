<#
.SYNOPSIS
    Hands one pull request to the local OpenCode install for an external review (DeepSeek or Luna),
    and posts the result as the one PR comment build-process.md §4.9 expects.

.DESCRIPTION
    The main session fills the reviewer brief itself (build-process.md Appendix B for a task PR,
    the plan-review prompt for a plan PR) and passes it as -BriefFile, exactly as it would to a
    Claude reviewer. This script then:
      1. creates a detached worktree at the PR head under ic2-work\<pr>-external-review;
      2. runs `opencode run` there with the .opencode/agents/external-reviewer.md agent and the
         model of the chosen reviewer, feeding it the brief plus the output rules;
      3. takes the run's final message as the review, checks it is complete (header line,
         verdict line, and the verdict repeated as the last line), and posts it as one PR comment
         with `gh pr comment --body-file`;
      4. with -ApplyLabel, applies status:approved or status:rework to the task's issue from the
         verdict, as a Claude reviewer would (never for a plan PR);
      5. removes the worktree.
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
    luna or deepseek.
.PARAMETER BriefFile
    The filled reviewer brief. Its first line must be the review header the model is to print,
    for example "Plan review (Luna)" or "T94 review (DeepSeek)".
.PARAMETER Issue
    The task's issue number, needed only with -ApplyLabel.
.PARAMETER ApplyLabel
    Apply status:approved / status:rework to -Issue from the verdict (task PRs only).
.PARAMETER FixturesDir
    A clone of ic2-test-fixtures; exported as IC2_FIXTURES_DIR for the run so the data tests
    run instead of skipping.
.PARAMETER DryRun
    Do everything except post and label; print the review to stdout instead.

.EXAMPLE
    pwsh scripts/external-review.ps1 -Pr 479 -Reviewer luna -BriefFile C:\tmp\479-brief.md
.EXAMPLE
    pwsh scripts/external-review.ps1 -Pr 466 -Reviewer deepseek -BriefFile C:\tmp\466-brief.md -Issue 24 -ApplyLabel -FixturesDir C:\Users\diego\projects\ic2-test-fixtures
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $Pr,
    [Parameter(Mandatory)] [ValidateSet('luna', 'deepseek')] [string] $Reviewer,
    [Parameter(Mandatory)] [string] $BriefFile,
    [int] $Issue,
    [switch] $ApplyLabel,
    [string] $FixturesDir,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

# Reviewer name -> OpenCode model id. Edit here if `opencode models` shows a different id.
$models = @{
    luna     = 'opencode/gpt-6-luna'
    deepseek = 'opencode/deepseek-v4.1-flash'
}
# Provider-specific variant, passed as `--variant` (the docs' flag; a `#variant` suffix on the model
# id is not documented). Empty means none.
$variants = @{ luna = 'high'; deepseek = '' }
$displayNames = @{ luna = 'Luna'; deepseek = 'DeepSeek' }
$model = $models[$Reviewer]
$variant = $variants[$Reviewer]

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
$header = ($brief -split "`r?`n")[0].Trim()
if ($header -notmatch 'review \(') { throw "The brief's first line must be the review header, e.g. 'Plan review (Luna)'; got: $header" }
if ($ApplyLabel -and -not $Issue) { throw '-ApplyLabel needs -Issue.' }
if (-not (Get-Command opencode -ErrorAction SilentlyContinue)) { throw 'opencode is not on PATH.' }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh is not on PATH.' }

# 1. A detached worktree at the PR head. Never the main checkout.
$headSha = gh pr view $Pr --json headRefOid --jq .headRefOid
if (-not $headSha) { throw "Could not read PR #$Pr's head." }
git -C $repo fetch -q origin "pull/$Pr/head"
if (Test-Path $worktree) { git -C $repo worktree remove --force $worktree }
git -C $repo worktree add --detach $worktree $headSha | Out-Null
Write-Host "worktree: $worktree at $headSha"
# The PR under review usually does not carry this agent file, and OpenCode silently falls back to
# its default, full-permission agent when --agent names one it cannot find. Copy the read-only
# agent into the review worktree (untracked; the worktree is removed afterwards).
$agentDir = Join-Path $worktree '.opencode/agents'
New-Item -ItemType Directory -Force -Path $agentDir | Out-Null
Copy-Item -LiteralPath $agentFile -Destination (Join-Path $agentDir 'external-reviewer.md') -Force

$verdicts = 'approve after named fixes', 'approve', 'rework', 'user decision'
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
$prompt = $brief + $rules

try {
    # 2. Run OpenCode in the worktree. `opencode run [message..]` is non-interactive; `--dir` sets
    #    the directory it runs in, `--agent` and `--model provider/model` are the documented flags,
    #    and `--variant` carries the provider-specific variant. What `run` prints on stdout is not
    #    documented beyond "formatted", so step 3 looks for the header line rather than assuming the
    #    output is the final message alone.
    if ($FixturesDir) { $env:IC2_FIXTURES_DIR = $FixturesDir }
    $started = (Get-Date).ToUniversalTime().ToString('o')
    Push-Location $worktree
    try {
        $ocArgs = @('run', '--dir', $worktree, '--agent', 'external-reviewer', '--model', $model)
        if ($variant) { $ocArgs += @('--variant', $variant) }
        $review = (& opencode @ocArgs $prompt 2>&1 | Out-String)
    } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "opencode exited with $LASTEXITCODE`n$review" }
    if ($review -match 'Falling back to default agent') { throw "OpenCode did not load the external-reviewer agent (it fell back to its default, full-permission agent). Nothing posted. Output:`n$review" }

    # 3. Completeness. The review starts at the header line (OpenCode may print tool chatter
    #    before it); it must have a verdict on line 2 and repeat it as the last non-empty line.
    $idx = $review.IndexOf($header)
    if ($idx -lt 0) { throw "The run's output has no header line '$header'. Nothing posted. Output:`n$review" }
    $review = $review.Substring($idx).TrimEnd()
    $lines = $review -split "`r?`n"
    $verdict = $lines[1].Trim().ToLowerInvariant()
    if ($verdicts -notcontains $verdict) { throw "Line 2 is not a verdict ('$verdict'). Nothing posted. Output:`n$review" }
    $last = ($lines | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim().ToLowerInvariant()
    if ($last -ne $verdict) { throw "The review does not end with its verdict line ('$last' vs '$verdict'): cut off. Nothing posted. Output:`n$review" }
    if ($review -match '(?i)\b(close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved)\s+#\d+') { throw "The review contains a closing keyword before #<n>. Nothing posted." }

    $body = $review + "`n`n— $($displayNames[$Reviewer]), via scripts/external-review.ps1 ($model)"
    if ($DryRun) { Write-Output $body; return }

    # 4. Post, and label.
    $bodyFile = Join-Path $env:TEMP "ic2-review-$Pr.md"
    Set-Content -LiteralPath $bodyFile -Value $body -Encoding utf8
    gh pr comment $Pr --body-file $bodyFile | Out-Null
    Write-Host "posted: $header / $verdict"
    if ($ApplyLabel) {
        if ($verdict -eq 'approve') {
            gh issue edit $Issue --add-label status:approved --remove-label status:in-review | Out-Null
        } elseif ($verdict -eq 'rework' -or $verdict -eq 'approve after named fixes') {
            gh issue edit $Issue --add-label status:rework --remove-label status:in-review | Out-Null
        } else {
            Write-Host "verdict '$verdict' applies no label; the main session decides."
        }
    }
}
finally {
    # 5. Clean up.
    git -C $repo worktree remove --force $worktree 2>$null
}
