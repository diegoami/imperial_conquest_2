<#
.SYNOPSIS
    Hands one pull request to the local OpenCode install for an external review (Luna alone on the
    direct OpenAI route; the main session runs a cold Claude Opus on the exit-3 failure), and posts
    the result as the one PR comment build-process.md §4.9 expects.

.DESCRIPTION
    The main session fills the reviewer brief itself (build-process.md Appendix B for a task PR,
    the plan-review prompt for a plan PR) and passes it as -BriefFile, exactly as it would to a
    Claude reviewer. This script then:
      1. creates a detached worktree at the PR head under ic2-work\<pr>-external-review-<token>,
         a path of its own, so two reviews of one PR never touch each other's tree;
      2. runs `opencode run` there with the .opencode/agents/external-reviewer.md agent and the
         model of the reviewer, feeding it the brief plus the output rules. The run is watched
         (scripts/Invoke-OpenCodeWatched.ps1) with stdin closed, because `opencode run` waits for
         stdin's end-of-file before it creates a session (the cause of the 2026-09-28 hangs). If
         OpenCode creates no session within -StartupTimeoutSec, its session makes no progress
         for -IdleTimeoutSec, or it does not finish within -TotalTimeoutSec, its process tree is
         killed;
      3. takes the run's final message as the review. A review is never thrown away (issue #575):
         the only failure is no review at all (no header line anywhere, only tool chatter or
         nothing), which moves to the next attempt. A readable review -- the header may carry
         Markdown and follow a preamble, blank lines may precede the verdict, the verdict may
         carry Markdown, a "Verdict:" prefix or trailing punctuation, and the closing verdict may
         sit among the last three non-empty lines -- is normalised (canonical header, bare verdict
         on line 2, closing verdict at the end) and posted as one PR comment with
         `gh pr comment --body-file`. A review whose verdict cannot be read, or that looks cut
         off (no closing verdict), is posted with a "> Note from scripts/external-review.ps1:
         ..." first line, applies no label and exits 4, and the main session reads it and decides.
         A closing keyword is rewritten to its bare word ("fixes #551" -> "fixes 551") and the
         review is still posted. A review that arrives flattened onto one line (seen from Luna on
         2026-09-28) is accepted when it starts with the header and a verdict and ends with the
         same verdict; its runs of spaces are turned back into paragraph breaks;
      4. with -ApplyLabel, applies status:approved or status:rework to the task's issue from the
         verdict, as a Claude reviewer would (never for a plan PR; never for a flagged review);
      5. removes the worktree it created, and only that one.
    With -Reviewer auto (the default) the chain is Luna alone on `openai/gpt-6-luna` (issue #575:
    one OpenCode model per role before Claude); on its failure the script exits 3 and the main
    session runs a cold Claude Opus reviewer. The next model runs ONLY on an infrastructure
    failure: no session
    in time, an idle session, no exit in time, a run that exits without a session, a non-zero
    exit, the fallback-to-default-agent guard, or no review at all. Any other error stops the
    script with a non-zero exit that is not 3. The worktree is recreated for each attempt. The
    posted header names the model that reviewed and the ones that failed before it, e.g. "Plan
    review (Luna; DeepSeek failed: no session in 180 s)". Two consecutive attempts failing
    with the same cause (Get-OpenCodeFailureClass) stop the chain early.
    The model that implemented the PR never reviews it: -ExcludeModel (or, when that is not given,
    a model:<name> label on the PR or on -Issue naming an OpenCode model) drops it from the chain,
    and an explicit -Reviewer naming it is refused with exit 1.
    If every model fails, the chain stops early, the exclusion leaves no model, or OpenCode is not
    installed, nothing is posted and the script exits 3 ("OpenCode unavailable: ...");
    build-process.md §4.9 says what the main session does then. An explicit -Reviewer runs only
    that model, and exits 3 the same way when it fails.
    The model never writes to GitHub: the agent file denies push, merge, comment and label
    commands, and this script is the only writer. A cut-off review (the 2026-09-25 #370 case) is
    posted with a note and no label and the script exits 4 (issue #575); it is never acted on.

    Reviewer -> OpenCode model id. GLM Flash and DeepSeek are on the OpenCode Go list
    (`opencode-go/glm-5.3-flash`, `opencode-go/deepseek-v4.1-flash`); luna is the direct OpenAI
    route, `openai/gpt-6-luna`, via the machine's OpenAI login (not Go's proxied
    `opencode-go/gpt-6-luna`, whose upstream returned Bad Request in long runs, #553).
    `opencode models` shows what this machine has.
    OpenCode reads CLAUDE.md as its instructions file when no AGENTS.md exists; that is
    harmless here (the reviewer gets the token-economy rules) and no AGENTS.md is added.

.PARAMETER Pr
    The pull request number.
.PARAMETER Reviewer
    auto (default: Luna on openai/gpt-6-luna at high effort alone, then a cold Claude Opus by
    hand; issue #575 keeps one OpenCode model per role before Claude), or glm-flash, glm, luna,
    deepseek for that model alone, each at high effort.
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
    Do everything except post and label; print the review to stdout instead, and the exit code it
    would use (0, or 4 when the review would be posted flagged).
.PARAMETER SelfTest
    Run the twelve review-parser samples (fix #575 DoD 4) and exit 0 when all match; no PR, no
    brief and no OpenCode run.
.PARAMETER StartupTimeoutSec
    How long a run may take to create its OpenCode session before it is killed (default 180).
.PARAMETER TotalTimeoutSec
    How long a run may take in all before it is killed (default 3600).
.PARAMETER IdleTimeoutSec
    How long the run's session may go without its `updated` time advancing before the run is
    killed (default 600; 0 disables). `updated` advances at each step boundary, not while a tool
    runs or a reply streams, so this must exceed the longest single step of a review.
.PARAMETER ExcludeModel
    The model that implemented the PR, as external-implement.ps1 names it on its "implemented by:"
    line (deepseek-flash, glm-flash, glm, luna, mimo-pro, mimo-flash; or a reviewer name).
    The reviewer of the same model (deepseek-flash is DeepSeek) is dropped from the chain. A
    deepseek-flash implementer never collides with the Luna reviewer; a luna implementer leaves no
    OpenCode reviewer, so the script exits 3 and a cold Claude Opus reviews. Without it, a
    model:<name> label on the PR or on -Issue is used when one names an OpenCode model.
.PARAMETER ModelIds
    Overrides of the reviewer -> model id map, e.g. @{ glm = 'opencode-go/glm-5.4' }, for when
    `opencode models` shows a different id (or, in a test, a bad id to exercise the chain).

.EXAMPLE
    pwsh scripts/external-review.ps1 -Pr 479 -BriefFile C:\tmp\479-brief.md
.EXAMPLE
    pwsh scripts/external-review.ps1 -Pr 466 -Reviewer deepseek -BriefFile C:\tmp\466-brief.md -Issue 24 -ApplyLabel -FixturesDir C:\Users\diego\projects\ic2-test-fixtures
#>
[CmdletBinding()]
param(
    [int] $Pr,
    [ValidateSet('auto', 'glm-flash', 'glm', 'luna', 'deepseek')] [string] $Reviewer = 'auto',
    [string] $BriefFile,
    [int] $Issue,
    [switch] $ApplyLabel,
    [string] $FixturesDir,
    [switch] $DryRun,
    [switch] $SelfTest,
    [int] $StartupTimeoutSec = 180,
    [int] $TotalTimeoutSec = 3600,
    [int] $IdleTimeoutSec = 600,
    [ValidateSet('deepseek-flash', 'glm-flash', 'glm', 'luna', 'mimo-pro', 'mimo-flash', 'deepseek')] [string] $ExcludeModel,
    [hashtable] $ModelIds
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-OpenCodeWatched.ps1')

# --- Review completeness (issue #575) ---------------------------------------------------------
# A review is never thrown away; the script only refuses to act on what it cannot read. A review
# with no header line anywhere is the only failure (the next attempt runs, or the script exits 3).
# A readable review is normalised and acted on. A review whose verdict cannot be read, or that
# looks cut off (no closing verdict), is posted with a note, applies no label, and exits 4 so the
# main session reads it and decides.
$verdicts = 'approve after named fixes', 'approve', 'rework', 'user decision'

function Remove-ReviewDecoration {
    # A line without the Markdown around it: a leading '>' or '#', bold/italic markers and
    # backticks. The header may come back as "**<header>**", "# <header>" or "`<header>`".
    param([string] $Text)
    if ($null -eq $Text) { return '' }
    $t = $Text.Trim()
    $t = $t -replace '^[\s>#*_`]+', ''
    $t = $t -replace '[\s*_`]+$', ''
    return $t.Trim()
}

function Get-ReviewVerdict {
    # The line's verdict in canonical form, or '' when it is not a readable verdict. Markdown
    # decoration, a "Verdict:" prefix and trailing punctuation are removed first.
    param([string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    $t = $Text.Trim()
    $t = $t -replace '^[\s>#*_`]+', ''
    $t = $t -replace '[\s*_`]+$', ''
    $t = $t -replace '[*_`]', ''
    $t = $t.Trim()
    if ($t -imatch '^verdict\s*:\s*(.+)$') { $t = $Matches[1] }
    $t = $t -replace '[.!:;,]+$', ''
    $t = $t.Trim().ToLowerInvariant()
    if ($verdicts -contains $t) { return $t }
    return ''
}

function Read-ReviewOutput {
    # Reads a review out of an `opencode run`'s stdout. Returns:
    #   Ok=$false, Reason        -- no review at all: no header line anywhere. The only failure
    #                               class; the next attempt runs, or the chain exits 3.
    #   Ok=$true, Flagged=$false -- Review (canonical header, bare verdict on line 2, closing
    #                               verdict at the end) and Verdict.
    #   Ok=$true, Flagged=$true  -- FlagNote 'verdict unreadable' or 'may be cut off', and the text
    #                               as it arrived: post it with the note, no label.
    param([string] $Text, [string] $Header)
    if ($null -eq $Text) { $Text = '' }
    $lines = @($Text -split "`r?`n")
    $headerPlain = Remove-ReviewDecoration $Header
    $headerLine = -1
    $flatTail = $null
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # The header may carry Markdown around it, case-insensitively, after any preamble.
        if ((Remove-ReviewDecoration $lines[$i]) -ieq $headerPlain) { $headerLine = $i; break }
        # Flattened: the header starts the line and the review follows on it (Luna, 2026-09-28).
        $stripped = $lines[$i].TrimStart(' ', "`t", '>', '#', '*', '_', '`')
        if ($stripped.StartsWith($headerPlain, [System.StringComparison]::OrdinalIgnoreCase)) {
            $tail = $stripped.Substring($headerPlain.Length).Trim(' ', "`t", '*', '_', '`')
            if ($tail) { $headerLine = $i; $flatTail = $tail; break }
        }
    }
    if ($headerLine -lt 0) { return [pscustomobject]@{ Ok = $false; Reason = 'no header line in its output' } }
    $content = @($lines | Select-Object -Skip $headerLine)
    $content[0] = $Header
    $raw = ($content -join "`n").TrimEnd()

    if ($null -ne $flatTail) {
        # The one-line review: leading verdict, findings, closing verdict, runs of spaces between
        # the paragraphs. The verdicts may carry Markdown and trailing punctuation.
        $flat = (@($flatTail) + @($content | Select-Object -Skip 1)) -join "`n"
        $lead = $flat -replace '^[\s>#*_`]+', ''
        $verdict = ''
        foreach ($v in $verdicts) {
            if ($lead -imatch "^$([regex]::Escape($v))([\s*_`.,!:;]|$)") { $verdict = $v; break }
        }
        if (-not $verdict) {
            return [pscustomobject]@{ Ok = $true; Flagged = $true; FlagNote = 'verdict unreadable'; Review = $raw; Verdict = '' }
        }
        $middle = Remove-ReviewDecoration ($lead.Substring($verdict.Length))
        $closing = "(?i)(^|\s)$([regex]::Escape($verdict))[\s*_`.,!:;]*$"
        if ($middle -notmatch $closing) {
            return [pscustomobject]@{ Ok = $true; Flagged = $true; FlagNote = 'may be cut off'; Review = $raw; Verdict = $verdict }
        }
        $body = @(([regex]::Replace($middle, $closing, '') -split "`n") | Where-Object { $_.Trim() }) -join "`n`n"
        $body = ($body -split ' {2,}') -join "`n`n"
        Write-Host 'note: the review arrived on one line; its paragraph breaks were restored.'
        if (-not $body.Trim()) {
            return [pscustomobject]@{ Ok = $true; Flagged = $false; Review = (@($Header, $verdict) -join "`n"); Verdict = $verdict }
        }
        return [pscustomobject]@{ Ok = $true; Flagged = $false; Review = (@($Header, $verdict, '', $body, '', $verdict) -join "`n"); Verdict = $verdict }
    }

    # The first non-empty line after the header is the verdict; blank lines before it are skipped.
    $verdictIdx = -1
    for ($i = 1; $i -lt $content.Count; $i++) {
        if ($content[$i].Trim()) { $verdictIdx = $i; break }
    }
    $verdict = ''
    if ($verdictIdx -ge 0) { $verdict = Get-ReviewVerdict $content[$verdictIdx] }
    if (-not $verdict) {
        return [pscustomobject]@{ Ok = $true; Flagged = $true; FlagNote = 'verdict unreadable'; Review = $raw; Verdict = '' }
    }
    $bodyLines = @($content | Select-Object -Skip ($verdictIdx + 1))
    # The closing verdict may sit among the last three non-empty lines: a sign-off after it is fine.
    $nonEmpty = @($bodyLines | Where-Object { $_.Trim() })
    $closed = $nonEmpty.Count -eq 0   # the leading verdict is the only verdict (as before #575)
    if (-not $closed) {
        foreach ($l in @($nonEmpty | Select-Object -Last 3)) {
            if ((Get-ReviewVerdict $l) -eq $verdict) { $closed = $true; break }
        }
    }
    if (-not $closed) {
        return [pscustomobject]@{ Ok = $true; Flagged = $true; FlagNote = 'may be cut off'; Review = $raw; Verdict = $verdict }
    }
    # Normalise: header, bare verdict, the body, the closing verdict. A body line that only repeats
    # the closing verdict is dropped and replaced by the canonical one at the end.
    while ($bodyLines.Count -gt 0 -and -not $bodyLines[0].Trim()) { $bodyLines = @($bodyLines | Select-Object -Skip 1) }
    while ($bodyLines.Count -gt 0 -and -not $bodyLines[-1].Trim()) { $bodyLines = @($bodyLines | Select-Object -First ($bodyLines.Count - 1)) }
    if ($bodyLines.Count -gt 0 -and (Get-ReviewVerdict $bodyLines[-1]) -eq $verdict) {
        $bodyLines = @($bodyLines | Select-Object -First ($bodyLines.Count - 1))
    }
    while ($bodyLines.Count -gt 0 -and -not $bodyLines[-1].Trim()) { $bodyLines = @($bodyLines | Select-Object -First ($bodyLines.Count - 1)) }
    if ($bodyLines.Count -eq 0) {
        return [pscustomobject]@{ Ok = $true; Flagged = $false; Review = (@($Header, $verdict) -join "`n"); Verdict = $verdict }
    }
    return [pscustomobject]@{ Ok = $true; Flagged = $false; Review = ((@($Header, $verdict, '') + $bodyLines + @('', $verdict)) -join "`n"); Verdict = $verdict }
}

function Invoke-ReviewParserSelfTest {
    # DoD 4 of fix #575: twelve sample outputs through Read-ReviewOutput, each showing what the
    # script would do -- post and act, post flagged with no label, or fail. Exits 0 when all match.
    $h = 'T575 review (GLM Flash)'
    $samples = @(
        [pscustomobject]@{ Name = 'bold header';            Text = "**$h**`napprove`n`nR1. fine`napprove";                Expect = 'act' },
        [pscustomobject]@{ Name = 'hash header';            Text = "# $h`napprove`n`nR1. fine`napprove";                  Expect = 'act' },
        [pscustomobject]@{ Name = 'blank before verdict';   Text = "$h`n`napprove`n`nR1. fine`napprove";                 Expect = 'act' },
        [pscustomobject]@{ Name = 'bold verdict';           Text = "$h`n**approve**`n`nR1. fine`n**approve**";             Expect = 'act' },
        [pscustomobject]@{ Name = 'Verdict: prefix';        Text = "$h`nVerdict: approve`n`nR1. fine`nVerdict: approve"; Expect = 'act' },
        [pscustomobject]@{ Name = 'trailing punctuation';   Text = "$h`napprove.`n`nR1. fine`napprove.";                   Expect = 'act' },
        [pscustomobject]@{ Name = 'sign-off after verdict'; Text = "$h`napprove`n`nR1. fine`napprove`n- GLM Flash";      Expect = 'act' },
        [pscustomobject]@{ Name = 'closing keyword';        Text = "$h`napprove`n`nR1. This fixes #551.`napprove";         Expect = 'act'; NoHash = '#551' },
        [pscustomobject]@{ Name = 'one-line review';        Text = "$h approve R1. fine  approve";                         Expect = 'act' },
        [pscustomobject]@{ Name = 'no closing verdict';     Text = "$h`napprove`n`nR1. fine`nR2. another";               Expect = 'may be cut off' },
        [pscustomobject]@{ Name = 'unreadable verdict';     Text = "$h`nLooks good to me.`n`nR1. fine`napprove";         Expect = 'verdict unreadable' },
        [pscustomobject]@{ Name = 'no header, chatter';     Text = "reading files...`nrunning tests...`nno review";      Expect = 'failure' }
    )
    $failed = 0
    $n = 0
    foreach ($s in $samples) {
        $n++
        $r = Read-ReviewOutput -Text $s.Text -Header $h
        $outcome = if (-not $r.Ok) { 'failure' } elseif ($r.Flagged) { $r.FlagNote } else { 'act' }
        $ok = $outcome -eq $s.Expect
        if ($ok -and $r.Ok -and -not $r.Flagged) {
            # The posting path rewrites a closing keyword; apply it here too and require none left.
            $posted = [regex]::Replace($r.Review, '(?i)\b(close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved)\s+#(\d+)', '$1 $2')
            if ($s.NoHash -and $posted -match [regex]::Escape($s.NoHash)) { $ok = $false }
            if ($posted -match '(?i)\b(close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved)\s+#\d+') { $ok = $false }
        }
        if (-not $ok) { $failed++ }
        $label = if ($outcome -eq 'act') { 'post and act' } elseif ($outcome -eq 'failure') { 'failure' } else { "post flagged, no label ($outcome)" }
        Write-Host ("[{0,2}] {1}  {2}  -- {3}" -f $n, $(if ($ok) { 'PASS' } else { 'FAIL' }), $label, $s.Name)
        if (-not $ok) {
            Write-Host "     expected: $($s.Expect); got verdict '$($r.Verdict)'; review:`n$($r.Review)"
            if ($r.Flagged) { Write-Host "     note line: > Note from scripts/external-review.ps1: $($r.FlagNote)" }
        } elseif ($r.Ok -and $r.Flagged) {
            Write-Host "     note line: > Note from scripts/external-review.ps1: $($r.FlagNote)"
        }
    }
    Write-Host "self-test: $($samples.Count - $failed)/$($samples.Count) passed"
    if ($failed) { return 1 }
    return 0
}

if ($SelfTest) { exit (Invoke-ReviewParserSelfTest) }
if (-not $Pr) { throw '-Pr is required (or use -SelfTest).' }
if (-not $BriefFile) { throw '-BriefFile is required.' }

# Reviewer name -> OpenCode model id. Edit here (or pass -ModelIds) if `opencode models` shows a
# different id. On 2026-10-01 the user moved the OpenCode runs from OpenCode Zen to OpenCode Go
# (issue #551), except the reviewer: it is Luna on the direct OpenAI route, `openai/gpt-6-luna`,
# via the machine's OpenAI login (issue #575; Go's proxied `opencode-go/gpt-6-luna` upstream
# returned Bad Request in long runs, #553). Luna at high effort is the review model (one OpenCode
# model per role before Claude); glm-flash, glm and deepseek stay valid as explicit -Reviewer
# values, and no default path picks them.
$models = @{
    'glm-flash' = 'opencode-go/glm-5.3-flash'
    glm         = 'opencode-go/glm-5.3'
    luna        = 'openai/gpt-6-luna'
    deepseek    = 'opencode-go/deepseek-v4.1-flash'
}
if ($ModelIds) { foreach ($k in $ModelIds.Keys) { $models[$k] = $ModelIds[$k] } }
# Provider-specific variant, passed as `--variant` (the docs' flag; a `#variant` suffix on the model
# id is not documented). Empty means none. Effort is `high` everywhere (issue #575: `max` is
# overkill); luna was already high.
$variants = @{ 'glm-flash' = 'high'; glm = 'high'; luna = 'high'; deepseek = '' }
$displayNames = @{ 'glm-flash' = 'GLM Flash'; glm = 'GLM'; luna = 'Luna'; deepseek = 'DeepSeek' }
# The fallback chain (the user's decision of 2026-10-01, issue #575): Luna alone on the direct
# OpenAI route, then the main session runs a cold Claude Opus reviewer. One OpenCode model per
# role before Claude.
$chain = if ($Reviewer -eq 'auto') { @('luna') } else { @($Reviewer) }

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
# A path of this invocation's own: a second review of the same PR never removes this one's tree.
$worktree = Join-Path $workRoot "$Pr-external-review-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$script:worktreeCreated = $false
if (-not (Test-Path $BriefFile)) { throw "Brief not found: $BriefFile" }
$brief = Get-Content -Raw -LiteralPath $BriefFile
$briefLines = $brief -split "`r?`n"
$briefHeader = $briefLines[0].Trim()
$briefRest = ($briefLines | Select-Object -Skip 1) -join "`n"
if ($briefHeader -notmatch 'review \(') { throw "The brief's first line must be the review header, e.g. 'Plan review (Luna)'; got: $briefHeader" }
if ($ApplyLabel -and -not $Issue) { throw '-ApplyLabel needs -Issue.' }
# OpenCode not installed or not found is the same signal as every model failing: exit 3.
try { $null = Resolve-OpenCodeExe } catch {
    if (-not (Test-OpenCodeInfraFailure $_)) { throw }
    [Console]::Error.WriteLine("OpenCode unavailable: $($_.Exception.Message) Nothing posted.")
    exit 3
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh is not on PATH.' }

# The reviewer's model is never the implementer's (build-process.md §3.4). The implementing model
# comes from -ExcludeModel, else from a model:<name> label on the PR or its issue that names an
# OpenCode model (model:opus and model:sonnet name Claude, which is not in this chain). Implementer
# name -> the reviewer names running the same model; the MiMo models have no reviewer here. With
# Luna as the only auto reviewer (issue #575), a luna implementer excludes it and leaves no
# OpenCode reviewer, so the script exits 3 and a cold Claude Opus reviews; both GLM names exclude
# both GLM reviewers (same model family), as before.
$reviewerOf = @{
    'deepseek-flash' = @('deepseek')
    'deepseek'       = @('deepseek')
    'glm-flash'      = @('glm-flash', 'glm')
    'glm'            = @('glm-flash', 'glm')
    'luna'           = @('luna')
}
$implementers = if ($ExcludeModel) { @($ExcludeModel) } else {
    $labels = @(gh pr view $Pr --json labels --jq '.labels[].name' 2>$null)
    if ($Issue) { $labels += @(gh issue view $Issue --json labels --jq '.labels[].name' 2>$null) }
    @($labels | Where-Object { $_ -match '^model:(.+)$' } | ForEach-Object { $_.Substring(6) } |
        Where-Object { $_ -in 'deepseek-flash', 'glm-flash', 'glm', 'luna', 'mimo-pro', 'mimo-flash', 'deepseek' } | Select-Object -Unique)
}
$excluded = @($implementers | ForEach-Object { $reviewerOf[$_] } | Where-Object { $_ } | Select-Object -Unique)
if ($implementers) { Write-Host "implemented by: $($implementers -join ', '); excluded from review: $(if ($excluded) { $excluded -join ', ' } else { 'none' })" }
if ($Reviewer -ne 'auto' -and $excluded -contains $Reviewer) {
    [Console]::Error.WriteLine("Refused: -Reviewer $Reviewer is the model that implemented PR #$Pr ($($implementers -join ', ')); the reviewer's model is never the implementer's (build-process.md §3.4). Use -Reviewer auto or another model. Nothing posted.")
    exit 1
}
$chain = @($chain | Where-Object { $excluded -notcontains $_ })
if (-not $chain) {
    [Console]::Error.WriteLine("OpenCode unavailable: no reviewer model left after excluding the implementer's ($($implementers -join ', ')). Nothing posted.")
    exit 3
}

$headSha = gh pr view $Pr --json headRefOid --jq .headRefOid
if (-not $headSha) { throw "Could not read PR #$Pr's head." }
git -C $repo fetch -q origin "pull/$Pr/head"

function New-ReviewWorktree {
    # 1. A detached worktree at the PR head. Never the main checkout. Recreated for every attempt,
    #    so a failed run leaves nothing behind for the next model. Only a tree this invocation
    #    created is ever removed.
    if ($script:worktreeCreated) {
        git -C $repo worktree remove --force $worktree 2>$null
        if (Test-Path $worktree) { Remove-Item -Recurse -Force -LiteralPath $worktree }
        git -C $repo worktree prune
        $script:worktreeCreated = $false
    }
    if (Test-Path $worktree) { throw "$worktree already exists and is not this run's." }
    git -C $repo worktree add --detach $worktree $headSha 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git worktree add failed for $worktree at $headSha" }
    $script:worktreeCreated = $true
    Write-Host "worktree: $worktree at $headSha"
    # The PR under review usually does not carry this agent file, and OpenCode silently falls back
    # to its default, full-permission agent when --agent names one it cannot find. Copy the
    # read-only agent into the review worktree (untracked; the worktree is removed afterwards).
    $agentDir = Join-Path $worktree '.opencode/agents'
    New-Item -ItemType Directory -Force -Path $agentDir | Out-Null
    Copy-Item -LiteralPath $agentFile -Destination (Join-Path $agentDir 'external-reviewer.md') -Force
}

function Invoke-ReviewAttempt([string] $Name) {
    # One model, once. Returns Ok + Review + Verdict (+ Flagged/FlagNote), or Ok = $false + a short
    # Reason for an infrastructure failure. No review at all (no header line anywhere) is the only
    # failure class here (issue #575); an unreadable or cut-off review is flagged, not failed.
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
  as the very last line. A review that does not end with the verdict line is treated as cut off:
  the script still posts it, with a note, and applies no label.
- The worktree you are in is $worktree at $headSha. Pass git -C "$worktree" explicitly.
- Stay inside ${worktree}: never read, list, write or run anything by a path outside it (not TEMP,
  not your home directory, not another worktree). OpenCode rejects such a call and the rejection
  ENDS your review. Scratch files go under rendered/ inside it. A mutation runs in place, uncommitted,
  and is restored with git checkout -- and a clean rebuild.
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
    # The helper appends a random token to the title, so the session found is this run's.
    try {
        $run = Invoke-OpenCodeWatched -Arguments $ocArgs -Prompt $prompt -WorkDir $worktree -Title "ic2-pr$Pr-$Name" `
            -StartupTimeoutSec $StartupTimeoutSec -TotalTimeoutSec $TotalTimeoutSec -IdleTimeoutSec $IdleTimeoutSec
    } catch {
        # Only OpenCode's own failures (not found, no session, idle, no exit, exited without a session)
        # advance the chain. Anything else is a defect here: rethrown, exit non-zero, not 3.
        if (-not (Test-OpenCodeInfraFailure $_)) { throw }
        return (& $fail $_.Exception.Data['Reason'] $_.Exception.Message)
    }
    $output = $run.Output
    if ($run.ExitCode -ne 0) { return (& $fail "exit $($run.ExitCode)" $output) }
    # OpenCode's own evidence (its stderr warning, the session's recorded agent), never the model's
    # words: a reviewer reading these scripts quotes the warning text (PR #482, 2026-09-28).
    if ($run.AgentFallback) { return (& $fail 'fell back to the default agent' "OpenCode did not load the external-reviewer agent (it fell back to its default, full-permission agent). Output:`n$output") }
    # A rejected tool call ends the run with exit 0 (issue #501): a failure, named by its path.
    if ($run.PermissionRejected) { return (& $fail "permission rejected: $($run.PermissionRejected)" "OpenCode's permission guard auto-rejected a tool call ($($run.PermissionRejected)), which ended the run. For external_directory, the reviewer reached outside its worktree. Output:`n$output") }

    # 3. Completeness (issue #575). No header line anywhere in the output is the only failure:
    #    the next model runs, or the chain exits 3. Anything with a header is never thrown away: a
    #    readable review is normalised and acted on; one whose verdict cannot be read, or that looks
    #    cut off, is flagged, posted with a note and applied no label (exit 4). A closing keyword is
    #    rewritten rather than thrown on.
    $parsed = Read-ReviewOutput -Text $run.StdOut -Header $header
    if (-not $parsed.Ok) { return (& $fail $parsed.Reason "The run's output has no header line '$header'. Output:`n$output") }
    $review = $parsed.Review
    $rewritten = [regex]::Replace($review, '(?i)\b(close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved)\s+#(\d+)', '$1 $2')
    if ($rewritten -ne $review) { Write-Host "note: rewrote closing keyword(s) in the review (e.g. 'fixes #551' -> 'fixes 551') so it can be posted." }
    return [pscustomobject]@{ Ok = $true; Name = $Name; Model = $model; Header = $header; Review = $rewritten; Verdict = $parsed.Verdict; Flagged = $parsed.Flagged; FlagNote = $parsed.FlagNote }
}

$result = $null
$failures = @()
$sameCause = $null
try {
    if ($FixturesDir) { $env:IC2_FIXTURES_DIR = $FixturesDir }
    $n = 0
    foreach ($name in $chain) {
        $n++
        Write-Host "attempt $n/$($chain.Count): $($displayNames[$name]) ($($models[$name]))$(if ($variants[$name]) { " with --variant $($variants[$name])" })"
        $attempt = Invoke-ReviewAttempt $name
        if ($attempt.Ok) { $result = $attempt; break }
        Write-Host "attempt $n/$($chain.Count): $($displayNames[$name]) failed: $($attempt.Reason)"
        Write-Host ((($attempt.Detail -split "`r?`n") | Select-Object -Last 20) -join "`n")
        $failures += $attempt
        # Two consecutive attempts failing with one cause stop the chain (operating-guide §3).
        if ($failures.Count -ge 2 -and (Get-OpenCodeFailureClass $failures[-1].Reason) -eq (Get-OpenCodeFailureClass $failures[-2].Reason)) {
            $sameCause = Get-OpenCodeFailureClass $attempt.Reason
            break
        }
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
        if ($result.Flagged) {
            # Never act on what cannot be read: post it (so it is not thrown away) with the note
            # first, apply no label, and tell the main session to read it and decide.
            $body = "> Note from scripts/external-review.ps1: $($result.FlagNote)`n`n$body"
        }
        if ($DryRun) {
            Write-Output $body
            if ($result.Flagged) { Write-Host 'dry run: no label would be applied; the script would exit 4.' }
            else { Write-Host 'dry run: the script would exit 0.' }
        } else {
            # 4. Post, and label (only a readable review is acted on).
            $bodyFile = Join-Path $env:TEMP "ic2-review-$Pr.md"
            Set-Content -LiteralPath $bodyFile -Value $body -Encoding utf8
            gh pr comment $Pr --body-file $bodyFile | Out-Null
            if ($result.Flagged) {
                Write-Host "posted with the note line; no label applied ($($result.FlagNote)). Read it and decide."
            } else {
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
}
finally {
    # 5. Clean up: this invocation's tree only.
    if ($script:worktreeCreated) { git -C $repo worktree remove --force $worktree 2>$null }
}

if (-not $result) {
    $reasons = ($failures | ForEach-Object { "$($displayNames[$_.Name]): $($_.Reason)" }) -join '; '
    if ($sameCause) { $reasons = "same failure twice: $sameCause ($reasons)" }
    [Console]::Error.WriteLine("OpenCode unavailable: $reasons. Nothing posted.")
    exit 3
}
if ($result.Flagged) {
    [Console]::Error.WriteLine("Review on PR #$Pr is $($result.FlagNote); posted without a label. Read it and decide (exit 4).")
    exit 4
}
