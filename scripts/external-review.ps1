<#
.SYNOPSIS
    Hands one pull request to the local OpenCode install for an external review (Luna alone on the
    direct OpenAI route by default, or the one reviewer the main session names for the PR's review
    tier, build-process.md §3.4: `sol` for a complex PR, `luna` plus `glm` or `deepseek` for the
    Luna pair; the main session runs a cold Claude Opus on the exit-3 failure), and posts the
    result as the one PR comment build-process.md §4.9 expects.

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
         Markdown (a trailing ':' or '.' is tolerated) and follow a preamble, blank lines may
         precede the verdict, the verdict is the first verdict-shaped line among the first five
         non-empty lines after the header (a short where-I-worked block may come first), it may
         carry Markdown, a "Verdict:" prefix or trailing punctuation, and the closing verdict may
         sit among the last three non-empty lines -- is normalised (canonical header, bare verdict
         on line 2, closing verdict at the end; the matched closing verdict line is removed and
         every line after it, a sign-off included, is kept) and posted as one PR comment with
         `gh pr comment --body-file`. A review whose verdict cannot be read, that looks cut off
         (no closing verdict), or that carries finding-shaped lines after its closing verdict,
         is posted as it arrived with a "> Note from scripts/external-review.ps1: ..." first
         line, applies no label and exits 4, and the main session reads it and decides. A closing
         keyword is rewritten to its bare word plus the number without the hash ("fixes #551" ->
         "fixes 551"; "Fixes: #551", "fixes#551" and "owner/repo#551" too) and the review is
         still posted. A review that arrives flattened onto one line (seen from Luna on
         2026-09-28) is accepted when it starts with the header and a verdict and ends with the
         same verdict; its runs of spaces are turned back into paragraph breaks; a flattened
         review that does not parse is posted whole, not cut down to its header;
      4. with -ApplyLabel, applies status:approved or status:rework to the task's issue from the
         verdict, as a Claude reviewer would (never for a plan PR; never for a flagged review);
      5. removes the worktree it created, and only that one.
    With -Reviewer auto (the default) the chain is Luna alone on `openai/gpt-6-luna` (issue #575:
    one OpenCode model per role before Claude), the simple tier's reviewer; on its failure the
    script exits 3 and the main session runs a cold Claude Opus reviewer. The main session picks
    the tier (build-process.md §3.4) and passes every other tier's reviewer explicitly: -Reviewer
    sol (GPT-6 Sol, `openai/gpt-6-sol`) for a complex PR, and one run per reviewer for the Luna
    pair; auto never picks sol. The next model runs ONLY on an infrastructure
    failure: no session
    in time, an idle session, no exit in time, a run that exits without a session, a non-zero
    exit, the fallback-to-default-agent guard, or no review at all. Any other error stops the
    script with a non-zero exit that is not 3. The worktree is recreated for each attempt. The
    posted header names the model that reviewed and the ones that failed before it, e.g. "Plan
    review (Luna; DeepSeek failed: no session in 180 s)". Two consecutive attempts failing
    with the same cause (Get-OpenCodeFailureClass) stop the chain early.
    The model family that implemented the PR never reviews it: -ExcludeModel (or, when that is not
    given, a model:<name> label on the PR or on -Issue naming an OpenCode model) drops every
    reviewer of that family from the chain (OpenAI: luna, sol; GLM: glm, glm-flash; DeepSeek:
    deepseek, deepseek-flash), and an explicit -Reviewer of that family is refused with exit 1.
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
    `opencode-go/gpt-6-luna`, whose upstream returned Bad Request in long runs, #553); sol is
    `openai/gpt-6-sol` on the same login.
    `opencode models` shows what this machine has.
    OpenCode reads CLAUDE.md as its instructions file when no AGENTS.md exists; that is
    harmless here (the reviewer gets the token-economy rules) and no AGENTS.md is added.

.PARAMETER Pr
    The pull request number.
.PARAMETER Reviewer
    auto (default: Luna on openai/gpt-6-luna at high effort alone, then a cold Claude Opus by
    hand; issue #575 keeps one OpenCode model per role before Claude), or glm-flash, glm, luna,
    sol, deepseek for that model alone, each at high effort. sol (GPT-6 Sol) is the complex tier's
    reviewer; luna with glm or deepseek, in two runs, is the Luna pair (build-process.md §3.4).
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
.PARAMETER WhatIf
    Print each chain reviewer's OpenCode argument line (the CLI's version decides its syntax) and exit 0,
    without fetching the PR, creating a worktree, starting a run or billing a model. Unlike -DryRun,
    which runs the reviewer (and bills it), this starts only `opencode --version`, in its own scratch directories. The chain shown is before -ExcludeModel
    and the model:<name> label are applied.
.PARAMETER SelfTest
    Run the review-parser samples (fix #575 DoD 4) and the issue #590 prompt/agent checks, and exit 0
    when all match; no PR, no brief and no OpenCode run.
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
    line (deepseek-flash, glm-flash, glm, luna, mimo-pro, mimo-flash; or a reviewer name, sol
    included). Every reviewer of the same family is dropped from the chain (deepseek-flash is
    DeepSeek; luna and sol are both OpenAI). A deepseek-flash implementer never collides with the
    Luna or Sol reviewer; a luna implementer excludes both, so -Reviewer auto leaves no OpenCode
    reviewer, the script exits 3 and a cold Claude Opus reviews. sonnet and opus name a Claude
    implementer (the Sonnet fallback, or Opus on an architecture task): accepted so that every run
    can pass -ExcludeModel, they exclude no OpenCode reviewer, since this script has no Claude
    one (the main session never picks a Claude reviewer for that PR). Without it, a
    model:<name> label on the PR or on -Issue is used when one names an OpenCode model.
.PARAMETER ModelIds
    Overrides of the reviewer -> model id map, e.g. @{ glm = 'opencode-go/glm-5.4' }, for when
    `opencode models` shows a different id (or, in a test, a bad id to exercise the chain).

.EXAMPLE
    pwsh scripts/external-review.ps1 -Pr 479 -BriefFile C:\tmp\479-brief.md
.EXAMPLE
    pwsh scripts/external-review.ps1 -Pr 640 -Reviewer sol -ExcludeModel deepseek-flash -BriefFile rendered\640-brief.md -Issue 600 -ApplyLabel
.EXAMPLE
    pwsh scripts/external-review.ps1 -Pr 466 -Reviewer deepseek -BriefFile C:\tmp\466-brief.md -Issue 24 -ApplyLabel -FixturesDir C:\Users\diego\projects\ic2-test-fixtures
#>
[CmdletBinding()]
param(
    [int] $Pr,
    [ValidateSet('auto', 'glm-flash', 'glm', 'luna', 'sol', 'deepseek')] [string] $Reviewer = 'auto',
    [string] $BriefFile,
    [int] $Issue,
    [switch] $ApplyLabel,
    [string] $FixturesDir,
    [switch] $DryRun,
    [switch] $SelfTest,
    [switch] $WhatIf,
    [int] $StartupTimeoutSec = 180,
    [int] $TotalTimeoutSec = 3600,
    [int] $IdleTimeoutSec = 600,
    [ValidateSet('deepseek-flash', 'glm-flash', 'glm', 'luna', 'sol', 'mimo-pro', 'mimo-flash', 'deepseek', 'sonnet', 'opus')] [string] $ExcludeModel,
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
$closingKeywords = 'close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved'

function Remove-ClosingKeywordHash {
    # Rewrites a closing keyword's link -- "fixes #551", "Fixes: #551", "fixes#551" and
    # "fixes owner/repo#551" -- to the bare word and the number without the hash (issue #575
    # rework R4), so a review is never discarded for one. The run's log notes the rewrite.
    param([string] $Text)
    if ($null -eq $Text) { return '' }
    $pattern = "(?i)\b($closingKeywords)(\s*:?\s*)((?:[\w.-]+/[\w.-]+)\s*)?#(\d+)"
    return [regex]::Replace($Text, $pattern, {
        param($m)
        $mid = $m.Groups[2].Value
        $repo = $m.Groups[3].Value
        # "Fixes: #12" keeps its colon; "fixes#12" and "owner/repo#12" gain the space the hash
        # took, so the word (or the repo) and the number never run together.
        $before = if ($repo) { $repo } else { $mid }
        $space = if ($before -match '\s$') { '' } else { ' ' }
        return "$($m.Groups[1].Value)$mid$repo$space$($m.Groups[4].Value)"
    })
}

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
    #   Ok=$true, Flagged=$true  -- FlagNote 'verdict unreadable', 'may be cut off' or 'text after
    #                               the closing verdict', and the text as it arrived: post it with
    #                               the note, no label.
    param([string] $Text, [string] $Header)
    if ($null -eq $Text) { $Text = '' }
    $lines = @($Text -split "`r?`n")
    $headerPlain = Remove-ReviewDecoration $Header
    $headerLine = -1
    $flatTail = $null
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # The header may carry Markdown around it, case-insensitively, after any preamble; a
        # trailing ':' or '.' (e.g. "Plan review (Luna):") is tolerated (rework R2).
        if ((Remove-ReviewDecoration $lines[$i]).TrimEnd(':', '.', ' ', "`t") -ieq $headerPlain) { $headerLine = $i; break }
        # Flattened: the header starts the line and the review follows on it (Luna, 2026-09-28).
        $stripped = $lines[$i].TrimStart(' ', "`t", '>', '#', '*', '_', '`')
        if ($stripped.StartsWith($headerPlain, [System.StringComparison]::OrdinalIgnoreCase)) {
            $tail = $stripped.Substring($headerPlain.Length).Trim(' ', "`t", '*', '_', '`')
            # A tail of punctuation alone (a bare "header:") is not a flattened review; keep
            # looking for the header (rework R2).
            if ($tail -match '\w') { $headerLine = $i; $flatTail = $tail.TrimStart(':', ' ', "`t"); break }
        }
    }
    if ($headerLine -lt 0) { return [pscustomobject]@{ Ok = $false; Reason = 'no header line in its output' } }
    $content = @($lines | Select-Object -Skip $headerLine)
    # A flagged flattened review is posted as it arrived, so keep the line before the canonical
    # header replaces it (rework R1: the review must not shrink to its header).
    $rawAsArrived = ($content -join "`n").TrimEnd()
    $content[0] = $Header
    $raw = ($content -join "`n").TrimEnd()

    if ($null -ne $flatTail) {
        # The one-line review: leading verdict, findings, closing verdict, runs of spaces between
        # the paragraphs. The verdicts may carry Markdown and trailing punctuation.
        $flat = (@($flatTail) + @($content | Select-Object -Skip 1)) -join "`n"
        $lead = $flat -replace '^[\s>#*_`:]+', ''
        $verdict = ''
        foreach ($v in $verdicts) {
            if ($lead -imatch "^$([regex]::Escape($v))([\s*_`.,!:;]|$)") { $verdict = $v; break }
        }
        if (-not $verdict) {
            return [pscustomobject]@{ Ok = $true; Flagged = $true; FlagNote = 'verdict unreadable'; Review = $rawAsArrived; Verdict = '' }
        }
        $middle = Remove-ReviewDecoration ($lead.Substring($verdict.Length))
        $closing = "(?i)(^|\s)$([regex]::Escape($verdict))[\s*_`.,!:;]*$"
        if ($middle -notmatch $closing) {
            return [pscustomobject]@{ Ok = $true; Flagged = $true; FlagNote = 'may be cut off'; Review = $rawAsArrived; Verdict = $verdict }
        }
        $body = @(([regex]::Replace($middle, $closing, '') -split "`n") | Where-Object { $_.Trim() }) -join "`n`n"
        $body = ($body -split ' {2,}') -join "`n`n"
        Write-Host 'note: the review arrived on one line; its paragraph breaks were restored.'
        if (-not $body.Trim()) {
            return [pscustomobject]@{ Ok = $true; Flagged = $false; Review = (@($Header, $verdict) -join "`n"); Verdict = $verdict }
        }
        return [pscustomobject]@{ Ok = $true; Flagged = $false; Review = (@($Header, $verdict, '', $body, '', $verdict) -join "`n"); Verdict = $verdict }
    }

    # The verdict is the first verdict-shaped line among the first five non-empty lines after the
    # header; blank lines are skipped and a short where-I-worked block may come before it
    # (rework R3).
    $verdict = ''
    $verdictIdx = -1
    $seen = 0
    for ($i = 1; $i -lt $content.Count; $i++) {
        if (-not $content[$i].Trim()) { continue }
        $seen++
        if ($seen -gt 5) { break }
        $v = Get-ReviewVerdict $content[$i]
        if ($v) { $verdict = $v; $verdictIdx = $i; break }
    }
    if (-not $verdict) {
        return [pscustomobject]@{ Ok = $true; Flagged = $true; FlagNote = 'verdict unreadable'; Review = $raw; Verdict = '' }
    }
    # The body is everything after the header except the leading verdict line; lines before it (a
    # where-I-worked block) are kept.
    $bodyLines = @()
    if ($verdictIdx -gt 1) { $bodyLines += @($content | Select-Object -Skip 1 -First ($verdictIdx - 1)) }
    $bodyLines += @($content | Select-Object -Skip ($verdictIdx + 1))
    # The closing verdict may sit among the last three non-empty lines: a sign-off after it is fine.
    $nonEmptyIdx = @()
    for ($i = 0; $i -lt $bodyLines.Count; $i++) { if ($bodyLines[$i].Trim()) { $nonEmptyIdx += $i } }
    $closeIdx = -1
    $closed = $nonEmptyIdx.Count -eq 0   # the leading verdict is the only verdict (as before #575)
    if (-not $closed) {
        foreach ($idx in @($nonEmptyIdx | Select-Object -Last 3)) {
            if ((Get-ReviewVerdict $bodyLines[$idx]) -eq $verdict) { $closed = $true; $closeIdx = $idx; break }
        }
    }
    if (-not $closed) {
        return [pscustomobject]@{ Ok = $true; Flagged = $true; FlagNote = 'may be cut off'; Review = $raw; Verdict = $verdict }
    }
    # Normalise: header, bare verdict, the body, the closing verdict. Only the matched closing
    # verdict line is removed; every line after it (a sign-off) is kept, so no text a review put
    # after its closing verdict is ever dropped (rework round 2, N1). A finding-shaped line after
    # the closing verdict (R2/N3/...) means the review is not trustworthy to act on: flag it and
    # post it whole, no label, exit 4.
    if ($closeIdx -ge 0) {
        $after = @($bodyLines | Select-Object -Skip ($closeIdx + 1))
        if (@($after | Where-Object { $_ -match '^\s*[RN]\d+' }).Count -gt 0) {
            return [pscustomobject]@{ Ok = $true; Flagged = $true; FlagNote = 'text after the closing verdict'; Review = $raw; Verdict = $verdict }
        }
        $bodyLines = @(@($bodyLines | Select-Object -First $closeIdx) + $after)
    }
    while ($bodyLines.Count -gt 0 -and -not $bodyLines[0].Trim()) { $bodyLines = @($bodyLines | Select-Object -Skip 1) }
    while ($bodyLines.Count -gt 0 -and -not $bodyLines[-1].Trim()) { $bodyLines = @($bodyLines | Select-Object -First ($bodyLines.Count - 1)) }
    if ($bodyLines.Count -eq 0) {
        return [pscustomobject]@{ Ok = $true; Flagged = $false; Review = (@($Header, $verdict) -join "`n"); Verdict = $verdict }
    }
    return [pscustomobject]@{ Ok = $true; Flagged = $false; Review = ((@($Header, $verdict, '') + $bodyLines + @('', $verdict)) -join "`n"); Verdict = $verdict }
}

function Get-ReviewOutputRules {
    # The OUTPUT RULES the script appends to the reviewer's brief (issue #590). Factored out of the
    # live path so -SelfTest can build and check the prompt text without an OpenCode run. The
    # reviewer runs inside the worktree: it runs git there as it is, never passes -C and never
    # types the worktree path (a mistyped 60-char path is an external_directory rejection that
    # ends the run). It proves its tree before reviewing.
    param([string] $Header, [string] $Worktree, [string] $HeadSha)
    return @"

---
OUTPUT RULES (from scripts/external-review.ps1; they override anything above that conflicts):
- Do not post to GitHub, edit, commit, push, label or merge anything. The script that runs you
  posts your review and applies the label.
- Your final message is the review and nothing else. Line 1 is exactly: $Header
  Line 2 is the verdict, one of: approve, approve after named fixes, rework, user decision.
  Then the findings (R1, R2, ... with file and line, blocking or not), then the verdict again
  as the very last line. A review that does not end with the verdict line is treated as cut off:
  the script still posts it, with a note, and applies no label.
- Your working directory is the worktree the script started you in: $Worktree at $HeadSha. Run
  git there as it is, without -C, and never type the worktree's path. Your first tool call prints
  git rev-parse --show-toplevel, git rev-parse HEAD and git diff --name-only origin/main...HEAD.
  The top level must be $($Worktree -replace '\\','/') -- git prints forward slashes, and slash
  direction and letter case do not count -- HEAD must be $HeadSha, and the diff must not be empty.
  Otherwise line 1 of your final message is still the header $Header, then it says you are in the
  wrong tree and stops, with no verdict, so the script flags it instead of reporting no review.
- Stay inside ${Worktree}: never read, list, write or run anything by a path outside it (not
  TEMP, not your home directory, not another worktree). OpenCode rejects such a call and the
  rejection ENDS your review. Scratch files go under rendered/ inside it. A mutation runs in
  place, uncommitted, and is restored with git checkout -- <file>, run in the worktree, and a
  clean rebuild.
"@
}

function Invoke-ReviewParserSelfTest {
    # DoD 4 of fix #575 (rework round 2): the sample outputs through Read-ReviewOutput, each showing
    # what the script would do -- post and act, post flagged with no label, or fail. MustContain
    # asserts the posted review kept the text the model wrote (flagged or acted on); NoHash asserts
    # the closing-keyword rewrite; VerdictLines asserts the normalised review does not repeat the
    # verdict. Exits 0 when all match.
    $h = 'T575 review (Luna)'
    $whereIWorked = "Where I worked: C:/Users/diego/projects/ic2-work/fix-575`nHEAD cce70f3 | branch fix/575-one-model-tolerant-review`nchanged: scripts/external-review.ps1`ntests: parse check and self-test"
    $samples = @(
        [pscustomobject]@{ Name = 'bold header';            Text = "**$h**`napprove`n`nR1. fine`napprove";                Expect = 'act' },
        [pscustomobject]@{ Name = 'hash header';            Text = "# $h`napprove`n`nR1. fine`napprove";                  Expect = 'act' },
        [pscustomobject]@{ Name = 'blank before verdict';   Text = "$h`n`napprove`n`nR1. fine`napprove";                 Expect = 'act' },
        [pscustomobject]@{ Name = 'bold verdict';           Text = "$h`n**approve**`n`nR1. fine`n**approve**";             Expect = 'act' },
        [pscustomobject]@{ Name = 'Verdict: prefix';        Text = "$h`nVerdict: approve`n`nR1. fine`nVerdict: approve"; Expect = 'act' },
        [pscustomobject]@{ Name = 'trailing punctuation';   Text = "$h`napprove.`n`nR1. fine`napprove.";                   Expect = 'act' },
        [pscustomobject]@{ Name = 'sign-off after verdict'; Text = "$h`napprove`n`nR1. fine`napprove`n- Luna";           Expect = 'act'; VerdictLines = 2; MustContain = @('- Luna') },
        # N1 (rework round 2): findings after the closing verdict are never dropped; the review is
        # posted whole, flagged, with no label and exit 4.
        [pscustomobject]@{ Name = 'findings after closing verdict'; Text = "$h`napprove`n`nR1. fine`napprove`nR2. BLOCKING: the label path is wrong`nR3. another"; Expect = 'text after the closing verdict'; MustContain = @('R2. BLOCKING: the label path is wrong', 'R3. another') },
        [pscustomobject]@{ Name = 'closing keyword';        Text = "$h`napprove`n`nR1. This fixes #551.`nR2. Fixes: #552.`nR3. fixes#553`nR4. fixes diegoami/imperial_conquest_2#554`napprove"; Expect = 'act'; NoHash = @('#551', '#552', '#553', '#554') },
        [pscustomobject]@{ Name = 'one-line review';        Text = "$h approve R1. fine  approve";                         Expect = 'act' },
        # R2: a decorated header with a trailing colon; the verdict follows on the next line.
        [pscustomobject]@{ Name = 'header trailing colon';  Text = "### $($h):`napprove`n`nR1. fine`napprove";              Expect = 'act' },
        # R3: a four-line where-I-worked block may sit between the header and the verdict.
        [pscustomobject]@{ Name = 'where-I-worked first';   Text = "$h`n$whereIWorked`napprove`n`nR1. fine`napprove";      Expect = 'act' },
        # R1: a flagged flattened review keeps its whole text, not just the header.
        [pscustomobject]@{ Name = 'flat cut off';           Text = "$h approve R1. fine  R2. the keyword rewrite is";      Expect = 'may be cut off'; MustContain = @('R1. fine', 'R2. the keyword rewrite is') },
        [pscustomobject]@{ Name = 'flat unreadable';        Text = "$h Looks good overall  R1. fine";                      Expect = 'verdict unreadable'; MustContain = @('Looks good overall', 'R1. fine') },
        [pscustomobject]@{ Name = 'no closing verdict';     Text = "$h`napprove`n`nR1. fine`nR2. another";               Expect = 'may be cut off' },
        [pscustomobject]@{ Name = 'unreadable verdict';     Text = "$h`nLooks good to me.`n`nR1. fine`nR2. another";     Expect = 'verdict unreadable' },
        [pscustomobject]@{ Name = 'no header, chatter';     Text = "reading files...`nrunning tests...`nno review";      Expect = 'failure' }
    )
    $failed = 0
    $n = 0
    foreach ($s in $samples) {
        $n++
        $r = Read-ReviewOutput -Text $s.Text -Header $h
        $outcome = if (-not $r.Ok) { 'failure' } elseif ($r.Flagged) { $r.FlagNote } else { 'act' }
        $ok = $outcome -eq $s.Expect
        # R1/N1: whatever the outcome, a postable review must keep the body the model wrote.
        foreach ($needle in @($s.MustContain)) {
            if ($needle -and $r.Review -notlike "*$needle*") { $ok = $false }
        }
        if ($ok -and $r.Ok -and -not $r.Flagged) {
            # The posting path rewrites closing keywords; use the same helper and require none left.
            $posted = Remove-ClosingKeywordHash $r.Review
            foreach ($hash in @($s.NoHash)) {
                if ($hash -and $posted -match [regex]::Escape($hash)) { $ok = $false }
            }
            if ($posted -match "(?i)\b($closingKeywords)(\s*:?\s*)((?:[\w.-]+/[\w.-]+)\s*)?#\d+") { $ok = $false }
            # R5: the normalised review carries the leading verdict and one closing verdict only.
            if ($null -ne $s.VerdictLines) {
                $count = @($posted -split "`r?`n" | Where-Object { (Get-ReviewVerdict $_) -eq $r.Verdict }).Count
                if ($count -ne $s.VerdictLines) { $ok = $false }
            }
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
    # --- issue #590: the reviewer runs git in its worktree, without -C ----------------------------
    # The live prompt (this same function) must tell the reviewer to run git where it stands --
    # no -C, no worktree path typed -- and to prove its tree with the named worktree, the named
    # head commit and a non-empty diff. The worktree is built with Join-Path (backslashes) but
    # `git rev-parse --show-toplevel` prints forward slashes, so the rules must show git's form
    # and say slash direction and case do not count. The agent body says the same; only its
    # permission block may mention git -C, and it must deny the plain push, commit, stash and
    # worktree forms a bare git would otherwise allow (issue #590).
    $sampleWorktree = 'C:\Users\diego\projects\ic2-work\590-external-review-deadbeef'
    $sampleHead = '7a8574d'
    $rules = Get-ReviewOutputRules -Header $h -Worktree $sampleWorktree -HeadSha $sampleHead
    $agentPath = Join-Path $PSScriptRoot '../.opencode/agents/external-reviewer.md'
    $agentText = Get-Content -Raw -LiteralPath $agentPath
    $fm = [regex]::Match($agentText, '(?s)^---\r?\n(.*?)\r?\n---\r?\n(.*)$')
    $agentPermissions = if ($fm.Success) { $fm.Groups[1].Value } else { '' }
    $agentBody = if ($fm.Success) { $fm.Groups[2].Value } else { $agentText }
    $ruleChecks = @(
        [pscustomobject]@{ Name = 'prompt rules do not ask for git -C'; Ok = ($rules -notmatch 'git -C') },
        [pscustomobject]@{ Name = 'agent body does not ask for git -C'; Ok = ($agentBody -notmatch 'git -C') },
        [pscustomobject]@{ Name = 'prompt rules name the worktree';     Ok = ($rules -like "*$sampleWorktree*") },
        [pscustomobject]@{ Name = 'prompt rules show git top-level form'; Ok = ($rules -like "*$($sampleWorktree -replace '\\','/')*") },
        [pscustomobject]@{ Name = 'prompt rules name the head commit';  Ok = ($rules -like "*$sampleHead*") }
    )
    foreach ($perm in @('git push *', 'git commit*', 'git stash*', 'git worktree *', 'git -C * push*', 'git -C * commit*', 'git -C * stash*', 'git -C * worktree *')) {
        $pattern = '(?m)^\s*' + [regex]::Escape('"' + $perm + '":') + '\s*deny\s*$'
        $ruleChecks += [pscustomobject]@{ Name = "agent denies `"$perm`""; Ok = ($agentPermissions -match $pattern) }
    }
    # --- the review tiers (the user's decision of 2026-10-03, build-process.md §3.4) ---------------
    # Every -Reviewer value but auto has a model id, a variant entry and a display name; sol is GPT-6
    # Sol at high effort; and the exclusion is by family: an OpenAI implementer
    # (luna or sol) excludes both OpenAI reviewers, and a DeepSeek implementer excludes neither.
    $reviewerSet = @((Get-Command $PSCommandPath).Parameters['Reviewer'].Attributes |
        Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] } |
        ForEach-Object { $_.ValidValues }) | Where-Object { $_ -ne 'auto' }
    foreach ($name in $reviewerSet) {
        $ruleChecks += [pscustomobject]@{ Name = "reviewer $name has a model, a variant and a display name"; Ok = ($models.ContainsKey($name) -and $variants.ContainsKey($name) -and $displayNames.ContainsKey($name)) }
    }
    $ruleChecks += [pscustomobject]@{ Name = 'sol is openai/gpt-6-sol at high, shown as Sol'; Ok = ($models['sol'] -eq 'openai/gpt-6-sol' -and $variants['sol'] -eq 'high' -and $displayNames['sol'] -eq 'Sol') }
    $ruleChecks += [pscustomobject]@{ Name = 'a luna implementer excludes luna and sol'; Ok = ((@($reviewerOf['luna']) | Sort-Object) -join ',' -eq 'luna,sol') }
    $ruleChecks += [pscustomobject]@{ Name = 'a sol implementer excludes luna and sol'; Ok = ((@($reviewerOf['sol']) | Sort-Object) -join ',' -eq 'luna,sol') }
    $ruleChecks += [pscustomobject]@{ Name = 'a deepseek-flash implementer excludes neither luna nor sol'; Ok = (@($reviewerOf['deepseek-flash']) -notcontains 'luna' -and @($reviewerOf['deepseek-flash']) -notcontains 'sol') }
    # PR #642 review R1: a Claude implementer passes -ExcludeModel sonnet or opus like any other;
    # both are valid values and exclude no OpenCode reviewer.
    $excludeSet = @((Get-Command $PSCommandPath).Parameters['ExcludeModel'].Attributes |
        Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] } |
        ForEach-Object { $_.ValidValues })
    foreach ($claude in 'sonnet', 'opus') {
        $ruleChecks += [pscustomobject]@{ Name = "-ExcludeModel $claude is accepted and excludes no OpenCode reviewer"; Ok = ($excludeSet -contains $claude -and $reviewerOf.ContainsKey($claude) -and @($reviewerOf[$claude]).Count -eq 0) }
    }
    foreach ($c in $ruleChecks) {
        $n++
        if (-not $c.Ok) { $failed++ }
        Write-Host ("[{0,2}] {1}  {2}  -- {3}" -f $n, $(if ($c.Ok) { 'PASS' } else { 'FAIL' }), 'check', $c.Name)
    }
    Write-Host "self-test: $($n - $failed)/$n passed"
    if ($failed) { return 1 }
    return 0
}

# Reviewer name -> OpenCode model id. Edit here (or pass -ModelIds) if `opencode models` shows a
# different id. On 2026-10-01 the user moved the OpenCode runs from OpenCode Zen to OpenCode Go
# (issue #551), except the reviewer: it is Luna on the direct OpenAI route, `openai/gpt-6-luna`,
# via the machine's OpenAI login (issue #575; Go's proxied `opencode-go/gpt-6-luna` upstream
# returned Bad Request in long runs, #553). Luna at high effort is the review model (one OpenCode
# model per role before Claude); glm-flash, glm and deepseek stay valid as explicit -Reviewer
# values, and no default path picks them. sol (GPT-6 Sol, `openai/gpt-6-sol`, the same OpenAI login)
# is the complex tier's reviewer and luna plus glm or deepseek the Luna pair (the user's decision of
# 2026-10-03, build-process.md §3.4); the main session passes them explicitly, so auto stays Luna.
$models = @{
    'glm-flash' = 'opencode-go/glm-5.3-flash'
    glm         = 'opencode-go/glm-5.3'
    luna        = 'openai/gpt-6-luna'
    sol         = 'openai/gpt-6-sol'
    deepseek    = 'opencode-go/deepseek-v4.1-flash'
}
if ($ModelIds) { foreach ($k in $ModelIds.Keys) { $models[$k] = $ModelIds[$k] } }
# Provider-specific variant. Invoke-OpenCodeWatched passes it by the CLI's major version (1.x
# `--variant v`, 2.x the model's `#v` suffix). Empty means none. Effort is `high` everywhere (issue #575: `max` is
# overkill); luna was already high.
$variants = @{ 'glm-flash' = 'high'; glm = 'high'; luna = 'high'; sol = 'high'; deepseek = '' }
$displayNames = @{ 'glm-flash' = 'GLM Flash'; glm = 'GLM'; luna = 'Luna'; sol = 'Sol'; deepseek = 'DeepSeek' }
# The reviewer's model family is never the implementer's (build-process.md §3.4). The implementing
# model comes from -ExcludeModel, else from a model:<name> label on the PR or its issue that names an
# OpenCode model (model:opus and model:sonnet name Claude, which is not in this chain). Implementer
# name -> the reviewer names of the same family; the MiMo models have no reviewer here. The families
# (the user's decision of 2026-10-03): OpenAI is luna and sol, GLM is glm and glm-flash, DeepSeek is
# deepseek and deepseek-flash. A luna or sol implementer excludes both OpenAI reviewers, so -Reviewer
# auto leaves no OpenCode reviewer and the script exits 3 (a cold Claude Opus reviews), and -Reviewer
# sol or luna is refused. A Claude implementer (sonnet, opus) excludes no OpenCode reviewer: the
# Claude family has no reviewer here, and the main session keeps Claude off that PR's review.
$reviewerOf = @{
    'deepseek-flash' = @('deepseek')
    'deepseek'       = @('deepseek')
    'glm-flash'      = @('glm-flash', 'glm')
    'glm'            = @('glm-flash', 'glm')
    'luna'           = @('luna', 'sol')
    'sol'            = @('luna', 'sol')
    'sonnet'         = @()
    'opus'           = @()
}

if ($SelfTest) { exit (Invoke-ReviewParserSelfTest) }
if (-not $Pr) { throw '-Pr is required (or use -SelfTest).' }
if (-not $BriefFile) { throw '-BriefFile is required.' }

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
# OpenCode not installed or not found, or a major version this script has no arguments for (only 1.x
# and 2.x), is the same signal as every model failing: exit 3. The version is read once (T98).
try { $cli = Get-OpenCodeCli } catch {
    if (-not (Test-OpenCodeInfraFailure $_)) { throw }
    [Console]::Error.WriteLine("OpenCode unavailable: $($_.Exception.Message) Nothing posted.")
    exit 3
}
if ($WhatIf) {
    # No PR fetch, no worktree, no OpenCode, no billing: only the argument line each chain reviewer would get.
    foreach ($name in $chain) {
        Write-Host "would attempt: $($displayNames[$name])"
        $null = Invoke-OpenCodeWatched -WhatIf -Agent 'external-reviewer' -Model $models[$name] -Variant $variants[$name] -Prompt $brief `
            -WorkDir (Get-Location).Path -Title "ic2-pr$Pr-$name"
    }
    exit 0
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh is not on PATH.' }

$implementers = if ($ExcludeModel) { @($ExcludeModel) } else {
    $labels = @(gh pr view $Pr --json labels --jq '.labels[].name' 2>$null)
    if ($Issue) { $labels += @(gh issue view $Issue --json labels --jq '.labels[].name' 2>$null) }
    @($labels | Where-Object { $_ -match '^model:(.+)$' } | ForEach-Object { $_.Substring(6) } |
        Where-Object { $_ -in 'deepseek-flash', 'glm-flash', 'glm', 'luna', 'sol', 'mimo-pro', 'mimo-flash', 'deepseek' } | Select-Object -Unique)
}
$excluded = @($implementers | ForEach-Object { $reviewerOf[$_] } | Where-Object { $_ } | Select-Object -Unique)
if ($implementers) { Write-Host "implemented by: $($implementers -join ', '); excluded from review: $(if ($excluded) { $excluded -join ', ' } else { 'none' })" }
if ($Reviewer -ne 'auto' -and $excluded -contains $Reviewer) {
    [Console]::Error.WriteLine("Refused: -Reviewer $Reviewer is of the model family that implemented PR #$Pr ($($implementers -join ', ')); the reviewer's model family is never the implementer's (build-process.md §3.4). Use -Reviewer auto or another model. Nothing posted.")
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
    $rules = Get-ReviewOutputRules -Header $header -Worktree $worktree -HeadSha $headSha
    $prompt = $header + "`n" + $briefRest + $rules
    $fail = { param($reason, $detail) [pscustomobject]@{ Ok = $false; Name = $Name; Model = $model; Header = $header; Reason = $reason; Detail = $detail } }

    New-ReviewWorktree
    # 2. Run OpenCode in the worktree, watched. `opencode run [message..]` is non-interactive;
    #    The directory it runs in is the worktree (1.x `--dir`, 2.x the process's own working
    #    directory), `--agent` and `--model provider/model` are the documented flags, and the
    #    provider-specific variant is `--variant` (1.x) or the model's `#variant` (2.x); the watcher
    #    builds the arguments by the CLI's major version. What `run` prints
    #    on stdout is not documented beyond "formatted", so step 3 looks for the header line rather
    #    than assuming the output is the final message alone. The helper reads the output back as
    #    UTF-8, so an em dash or a curly quote reaches the PR intact.
    # The helper appends a random token to the title, so the session found is this run's.
    try {
        $run = Invoke-OpenCodeWatched -Agent 'external-reviewer' -Model $model -Variant $variant -Prompt $prompt -WorkDir $worktree -Title "ic2-pr$Pr-$Name" `
            -StartupTimeoutSec $StartupTimeoutSec -TotalTimeoutSec $TotalTimeoutSec -IdleTimeoutSec $IdleTimeoutSec
    } catch {
        # Only OpenCode's own failures (not found, no session, idle, no exit, exited without a session)
        # advance the chain. Anything else is a defect here: rethrown, exit non-zero, not 3.
        if (-not (Test-OpenCodeInfraFailure $_)) { throw }
        return (& $fail $_.Exception.Data['Reason'] $_.Exception.Message)
    }
    $output = $run.Output
    # A rejected tool call ends the run (issue #501): exit 0 under 1.x, exit 1 under 2.x. It is named by its
    # path, and checked first so the 2.x exit 1 does not hide it as a plain "exit 1".
    if ($run.PermissionRejected) { return (& $fail "permission rejected: $($run.PermissionRejected)" "OpenCode's permission guard auto-rejected a tool call ($($run.PermissionRejected)), which ended the run. For external_directory, the reviewer reached outside its worktree. Output:`n$output") }
    if ($run.ExitCode -ne 0) { return (& $fail "exit $($run.ExitCode)" $output) }
    # OpenCode's own evidence (its stderr warning, the session's recorded agent), never the model's
    # words: a reviewer reading these scripts quotes the warning text (PR #482, 2026-09-28).
    if ($run.AgentFallback) { return (& $fail 'fell back to the default agent' "OpenCode did not load the external-reviewer agent (it fell back to its default, full-permission agent). Output:`n$output") }

    # 3. Completeness (issue #575). No header line anywhere in the output is the only failure:
    #    the next model runs, or the chain exits 3. Anything with a header is never thrown away: a
    #    readable review is normalised and acted on; one whose verdict cannot be read, or that looks
    #    cut off, is flagged, posted with a note and applied no label (exit 4). A closing keyword is
    #    rewritten rather than thrown on.
    $parsed = Read-ReviewOutput -Text $run.StdOut -Header $header
    if (-not $parsed.Ok) { return (& $fail $parsed.Reason "The run's output has no header line '$header'. Output:`n$output") }
    $review = $parsed.Review
    # Never discard a review for a closing keyword: rewrite every form and note it (issue #575
    # rework R4). The posting path and the self-test share Remove-ClosingKeywordHash.
    $rewritten = Remove-ClosingKeywordHash $review
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
        Write-Host "attempt $n/$($chain.Count): $($displayNames[$name]) ($($models[$name]))$(if ($variants[$name]) { " with variant $($variants[$name])" }), OpenCode $($cli.Version)"
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
            if ($result.Flagged) {
                # A flagged review is posted as it arrived: its first line may be the whole
                # flattened review, so never replace it. Prepend the failed attempts as their own
                # note line instead (rework round 2, N2).
                $review = "> Note from scripts/external-review.ps1: $note`n`n$review"
            } else {
                $lines = $review -split "`r?`n"
                $lines[0] = $result.Header -replace '\)\s*$', "; $note)"
                $review = $lines -join "`n"
            }
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
