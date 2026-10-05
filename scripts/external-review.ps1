<#
.SYNOPSIS
    Hands one pull request to the local OpenCode install for an external review (Luna alone on the
    direct OpenAI route by default, or the one reviewer the main session names for the PR's review
    tier, build-process.md §3.4: `sol` for a complex PR, `luna` plus `glm` or `deepseek-pro` for the
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
    A post that GitHub does not take (gh exits non-zero, or no comment URL comes back; bug #645)
    is retried once; if it fails again the script prints "NOT posted:", saves the review to
    rendered\review-not-posted-pr<pr>-<reviewer>-<time>.md, applies no label and exits 5: the main
    session reads the saved file and posts it by hand. Exit codes: 0 posted (and labelled with
    -ApplyLabel), 1 refused or a defect, 3 no review (OpenCode unavailable or every model failed),
    4 posted flagged with no label, 5 not posted, saved.
    With -Reviewer auto (the default) the chain is Luna alone on `openai/gpt-5.6-luna` (issue #575:
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
    deepseek, deepseek-pro, deepseek-flash; Qwen: qwen, qwen-flash), and an explicit -Reviewer of that family is refused with exit 1.
    If every model fails, the chain stops early, the exclusion leaves no model, or OpenCode is not
    installed, nothing is posted and the script exits 3 ("OpenCode unavailable: ...");
    build-process.md §4.9 says what the main session does then. An explicit -Reviewer runs only
    that model, and exits 3 the same way when it fails.
    The model never writes to GitHub: the agent file denies push, merge, comment and label
    commands, and this script is the only writer. A cut-off review (the 2026-09-25 #370 case) is
    posted with a note and no label and the script exits 4 (issue #575); it is never acted on.

    Reviewer -> OpenCode model id. GLM Flash and DeepSeek are on the OpenCode Go list
    (`opencode-go/deepseek-v4.1-flash`; GLM is on the Z.AI Coding Plan, `zai-coding-plan/glm-5.3` and `zai-coding-plan/glm-5.3-flash`, the user's decision of 2026-10-04); luna is the direct OpenAI
    route, `openai/gpt-5.6-luna` (GPT-5.6 Luna, on its own weekly OpenAI pool; harness_imperial L51,
    the user's decision of 2026-10-05), via the machine's OpenAI login (never a Luna on OpenCode Go:
    Go's proxied `opencode-go/gpt-6-luna` returned Bad Request in long runs, #553); sol is
    `openai/gpt-6-sol` on the same login; deepseek-pro is `opencode-go/deepseek-v4-pro`. An OpenAI
    run that fails with "The usage limit has been reached" means that model's OpenAI quota is out.
    GPT-5.6 Luna draws on its own weekly window, separate from Sol's: read quota-tracker
    (docs/environment.md) rather than probing Luna (build-process.md §3.4).
    `opencode models` shows what this machine has.
    The Alibaba Token Plan (`alibaba-token-plan/…`, the user's decision of 2026-10-05) carries the
    Qwen family, qwen (`qwen3.8-max`) and qwen-flash (`qwen3.8-flash`), and a second route for
    deepseek (`deepseek-v4.1-flash`), deepseek-pro (`deepseek-v4-pro`) and glm (`glm-5.3`); -Route
    picks it. Its key is the user variable ALIBABA_TOKEN_PLAN_API_KEY, loaded into this process when
    missing and never printed; "Invalid API-key" or "Provider not found" from it stops the script
    (exit 1, not 3) with the cause, never a retry (docs/environment.md).
    OpenCode reads CLAUDE.md as its instructions file when no AGENTS.md exists; that is
    harmless here (the reviewer gets the token-economy rules) and no AGENTS.md is added.

.PARAMETER Pr
    The pull request number.
.PARAMETER Reviewer
    auto (default: Luna on openai/gpt-5.6-luna at high effort alone, then a cold Claude Opus by
    hand; issue #575 keeps one OpenCode model per role before Claude), or glm-flash, glm, luna,
    sol, deepseek, deepseek-pro for that model alone. Light models run at high effort (luna,
    glm-flash; deepseek has no variant). Heavy models run light (the user's decision of 2026-10-05:
    medium rather than high, or light when medium is not needed): glm at low (GLM-5.3 offers only
    low, high and max), deepseek-pro at high (DeepSeek V4 Pro offers only high and max), and sol at
    low by default and medium with -Effort medium, never higher (the user's decision of 2026-10-03).
    sol (GPT-6 Sol) is the complex tier's reviewer; luna with glm or deepseek-pro, in two runs, is the
    Luna pair; glm, then deepseek-pro (DeepSeek V4 Pro, `opencode-go/deepseek-v4-pro`), then luna
    are Sol's substitutes when it cannot review (build-process.md §3.4), then qwen (Qwen3.8 Max at
    low). qwen-flash (Qwen3.8 Flash at medium; it offers low, medium and xhigh) is the last re-check
    reviewer of named fixes.
.PARAMETER Route
    Which provider DeepSeek and GLM run through: auto (the default) takes the usual one (OpenCode Go
    for deepseek and deepseek-pro, Z.AI for glm) unless quota-tracker's /avoid lists it
    (opencode_go, zai), then the Alibaba Token Plan's id for the same model; when the tracker does not
    answer, the usual route. go, zai or alibaba force one; a named reviewer that route does not serve
    is refused (exit 1). Qwen is always alibaba, luna and sol always openai, glm-flash always zai. The
    route is printed, and named in the posted review's signature line. A reviewer none of whose
    routes has quota (/avoid lists them all, or a forced -Route's provider) is skipped, and with none
    left the script exits 3 with the cause.
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
    and the model:<name> label are applied. With -ExcludeModel, -WhatIf first runs the family
    check, so a reviewer of the implementer's family is refused with exit 1 (PR #642 review R6).
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
    line (deepseek-flash, glm-flash, glm, luna, qwen-flash, mimo-pro, mimo-flash; or a reviewer
    name, sol and qwen included). Every reviewer of the same family is dropped from the chain (deepseek-flash is
    DeepSeek, as are deepseek and deepseek-pro; luna and sol are both OpenAI). A deepseek-flash
    implementer never collides with the Luna or Sol reviewer; a luna implementer excludes both, so -Reviewer auto leaves no OpenCode
    reviewer, the script exits 3 and a cold Claude Opus reviews. sonnet and opus name a Claude
    implementer (the Sonnet fallback, or Opus on an architecture task): accepted so that every run
    can pass -ExcludeModel, they exclude no OpenCode reviewer, since this script has no Claude
    one (the main session never picks a Claude reviewer for that PR). Without it, a
    model:<name> label on the PR or on -Issue is used when one names an OpenCode model.
.PARAMETER Effort
    Sol's effort (its OpenCode variant): low (the default when this is not given) or medium. It
    applies to sol only; every other reviewer keeps its own variant. high and above cannot be
    passed (the user's decision of 2026-10-03: Sol never at high, at most medium, better light).
    build-process.md §3.4 says when medium earns its cost.
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
    [ValidateSet('auto', 'glm-flash', 'glm', 'luna', 'sol', 'deepseek', 'deepseek-pro', 'qwen', 'qwen-flash')] [string] $Reviewer = 'auto',
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
    [ValidateSet('deepseek-flash', 'glm-flash', 'glm', 'luna', 'sol', 'mimo-pro', 'mimo-flash', 'deepseek', 'deepseek-pro', 'qwen', 'qwen-flash', 'sonnet', 'opus')] [string] $ExcludeModel,
    [hashtable] $ModelIds,
    [ValidateSet('low', 'medium')] [string] $Effort,
    [ValidateSet('auto', 'go', 'zai', 'alibaba')] [string] $Route = 'auto'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-OpenCodeWatched.ps1')
# The Alibaba Token Plan's key comes from the user environment when this process predates it (never printed).
$null = Import-AlibabaTokenPlanKey

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
    # Sol at low effort by default, medium at most; and the exclusion is by family: an OpenAI implementer
    # (luna or sol) excludes both OpenAI reviewers, and a DeepSeek implementer excludes neither.
    $reviewerSet = @((Get-Command $PSCommandPath).Parameters['Reviewer'].Attributes |
        Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] } |
        ForEach-Object { $_.ValidValues }) | Where-Object { $_ -ne 'auto' }
    foreach ($name in $reviewerSet) {
        $ruleChecks += [pscustomobject]@{ Name = "reviewer $name has a model, a variant and a display name"; Ok = ($models.ContainsKey($name) -and $variants.ContainsKey($name) -and $displayNames.ContainsKey($name)) }
    }
    $ruleChecks += [pscustomobject]@{ Name = 'sol is openai/gpt-6-sol, shown as Sol'; Ok = ($models['sol'] -eq 'openai/gpt-6-sol' -and $displayNames['sol'] -eq 'Sol') }
    # The user's decision of 2026-10-03: Sol at low by default, medium at most, never high.
    $effortSet = @((Get-Command $PSCommandPath).Parameters['Effort'].Attributes |
        Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] } |
        ForEach-Object { $_.ValidValues })
    $ruleChecks += [pscustomobject]@{ Name = 'sol defaults to low effort'; Ok = ((Get-SolVariant '') -eq 'low') }
    $ruleChecks += [pscustomobject]@{ Name = '-Effort medium gives sol medium'; Ok = ((Get-SolVariant 'medium') -eq 'medium') }
    $ruleChecks += [pscustomobject]@{ Name = '-Effort admits only low and medium (high is refused)'; Ok = ((($effortSet | Sort-Object) -join ',') -eq 'low,medium') }
    $ruleChecks += [pscustomobject]@{ Name = '-Effort changes no other reviewer (luna and deepseek-pro at high, glm at low)'; Ok = ($variants['luna'] -eq 'high' -and $variants['glm'] -eq 'low' -and $variants['deepseek-pro'] -eq 'high') }
    $ruleChecks += [pscustomobject]@{ Name = 'luna is GPT-5.6 Luna on the direct OpenAI route (L51)'; Ok = ($models['luna'] -eq 'openai/gpt-5.6-luna') }
    $ruleChecks += [pscustomobject]@{ Name = 'a luna implementer excludes luna and sol'; Ok = ((@($reviewerOf['luna']) | Sort-Object) -join ',' -eq 'luna,sol') }
    $ruleChecks += [pscustomobject]@{ Name = 'a sol implementer excludes luna and sol'; Ok = ((@($reviewerOf['sol']) | Sort-Object) -join ',' -eq 'luna,sol') }
    $ruleChecks += [pscustomobject]@{ Name = 'a deepseek-flash implementer excludes neither luna nor sol'; Ok = (@($reviewerOf['deepseek-flash']) -notcontains 'luna' -and @($reviewerOf['deepseek-flash']) -notcontains 'sol') }
    # PR #642 review R6: the family check uses one helper, and runs before -WhatIf returns.
    $ruleChecks += [pscustomobject]@{ Name = 'Get-ExcludedReviewers: a luna implementer excludes sol'; Ok = ((Get-ExcludedReviewers @('luna')) -contains 'sol') }
    $ruleChecks += [pscustomobject]@{ Name = 'Get-ExcludedReviewers: no implementer (-WhatIf without -ExcludeModel) excludes nothing'; Ok = (@(Get-ExcludedReviewers $null).Count -eq 0) }
    $ruleChecks += [pscustomobject]@{ Name = 'Get-ExcludedReviewers: a sonnet implementer excludes nothing'; Ok = (@(Get-ExcludedReviewers @('sonnet')).Count -eq 0) }
    $probeDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'rendered'
    New-Item -ItemType Directory -Force -Path $probeDir | Out-Null
    $probeBrief = Join-Path $probeDir "selftest-brief-$([guid]::NewGuid().ToString('N').Substring(0, 8)).md"
    Set-Content -LiteralPath $probeBrief -Value "T0 review (Sol)`nself-test probe brief" -Encoding utf8
    $null = & pwsh -NoProfile -File $PSCommandPath -Pr 1 -Reviewer sol -ExcludeModel luna -BriefFile $probeBrief -WhatIf 2>&1
    $whatIfCode = $LASTEXITCODE
    Remove-Item -LiteralPath $probeBrief -Force -ErrorAction SilentlyContinue
    $ruleChecks += [pscustomobject]@{ Name = "-WhatIf -Reviewer sol -ExcludeModel luna is refused with exit 1 (got $whatIfCode)"; Ok = ($whatIfCode -eq 1) }
    # Bug #645: a failed post is never reported as posted. gh stubbed to fail twice, then to answer
    # without a comment URL, then to succeed.
    $failGh = { $global:LASTEXITCODE = 1; 'Post "https://api.github.com/graphql": unexpected EOF' }
    $noUrlGh = { $global:LASTEXITCODE = 0; '' }
    $okGh = { $global:LASTEXITCODE = 0; 'https://github.com/diegoami/imperial_conquest_2/pull/1#issuecomment-1' }
    $script:ghCalls = 0
    $countingFailGh = { $script:ghCalls++; $global:LASTEXITCODE = 1; 'unexpected EOF' }
    $ruleChecks += [pscustomobject]@{ Name = 'a gh failure is not posted'; Ok = ($null -eq (Publish-ReviewComment -Pr 1 -BodyFile 'x' -Gh $failGh 6>$null)) }
    $null = Publish-ReviewComment -Pr 1 -BodyFile 'x' -Gh $countingFailGh 6>$null
    $ruleChecks += [pscustomobject]@{ Name = "a failed post is retried once (gh called $($script:ghCalls) times)"; Ok = ($script:ghCalls -eq 2) }
    $ruleChecks += [pscustomobject]@{ Name = 'exit 0 with no comment URL is not posted'; Ok = ($null -eq (Publish-ReviewComment -Pr 1 -BodyFile 'x' -Gh $noUrlGh 6>$null)) }
    $ruleChecks += [pscustomobject]@{ Name = 'a comment URL is posted'; Ok = ((Publish-ReviewComment -Pr 1 -BodyFile 'x' -Gh $okGh 6>$null) -like '*#issuecomment-1') }
    # PR #642 review R1: a Claude implementer passes -ExcludeModel sonnet or opus like any other;
    # both are valid values and exclude no OpenCode reviewer.
    $excludeSet = @((Get-Command $PSCommandPath).Parameters['ExcludeModel'].Attributes |
        Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] } |
        ForEach-Object { $_.ValidValues })
    foreach ($claude in 'sonnet', 'opus') {
        $ruleChecks += [pscustomobject]@{ Name = "-ExcludeModel $claude is accepted and excludes no OpenCode reviewer"; Ok = ($excludeSet -contains $claude -and $reviewerOf.ContainsKey($claude) -and @($reviewerOf[$claude]).Count -eq 0) }
    }
    # Sol's substitutes (the user's policy of 2026-10-03): deepseek-pro is DeepSeek V4 Pro on Go, in
    # the DeepSeek family.
    $ruleChecks += [pscustomobject]@{ Name = 'deepseek-pro is opencode-go/deepseek-v4-pro at high, shown as DeepSeek Pro'; Ok = ($models['deepseek-pro'] -eq 'opencode-go/deepseek-v4-pro' -and $variants['deepseek-pro'] -eq 'high' -and $displayNames['deepseek-pro'] -eq 'DeepSeek Pro') }
    $ruleChecks += [pscustomobject]@{ Name = 'a deepseek-flash implementer excludes deepseek and deepseek-pro'; Ok = ((@($reviewerOf['deepseek-flash']) | Sort-Object) -join ',' -eq 'deepseek,deepseek-pro') }
    $ruleChecks += [pscustomobject]@{ Name = 'a glm implementer excludes neither deepseek-pro nor luna'; Ok = (@($reviewerOf['glm']) -notcontains 'deepseek-pro' -and @($reviewerOf['glm']) -notcontains 'luna') }
    $ruleChecks += [pscustomobject]@{ Name = 'a deepseek-pro implementer is accepted and excludes its family'; Ok = ($excludeSet -contains 'deepseek-pro' -and (@($reviewerOf['deepseek-pro']) | Sort-Object) -join ',' -eq 'deepseek,deepseek-pro') }
    # The Qwen family and the Alibaba routes (the user's decision of 2026-10-05).
    $ruleChecks += [pscustomobject]@{ Name = 'qwen is alibaba-token-plan/qwen3.8-max at low, shown as Qwen'; Ok = ($models['qwen'] -eq 'alibaba-token-plan/qwen3.8-max' -and $variants['qwen'] -eq 'low' -and $displayNames['qwen'] -eq 'Qwen') }
    $ruleChecks += [pscustomobject]@{ Name = 'qwen-flash is alibaba-token-plan/qwen3.8-flash at medium, shown as Qwen Flash'; Ok = ($models['qwen-flash'] -eq 'alibaba-token-plan/qwen3.8-flash' -and $variants['qwen-flash'] -eq 'medium' -and $displayNames['qwen-flash'] -eq 'Qwen Flash') }
    foreach ($q in 'qwen', 'qwen-flash') {
        $ruleChecks += [pscustomobject]@{ Name = "a $q implementer is accepted and excludes qwen and qwen-flash"; Ok = ($excludeSet -contains $q -and (@($reviewerOf[$q]) | Sort-Object) -join ',' -eq 'qwen,qwen-flash') }
    }
    $ruleChecks += [pscustomobject]@{ Name = 'no other family excludes a Qwen reviewer'; Ok = (@($reviewerOf.Keys | Where-Object { $_ -notlike 'qwen*' } | Where-Object { @($reviewerOf[$_]) -match '^qwen' }).Count -eq 0) }
    $ruleChecks += [pscustomobject]@{ Name = 'Alibaba ids: deepseek-v4.1-flash, deepseek-v4-pro, glm-5.3; none for glm-flash, luna, sol'; Ok = ($alibabaIds['deepseek'] -eq 'alibaba-token-plan/deepseek-v4.1-flash' -and $alibabaIds['deepseek-pro'] -eq 'alibaba-token-plan/deepseek-v4-pro' -and $alibabaIds['glm'] -eq 'alibaba-token-plan/glm-5.3' -and -not $alibabaIds['glm-flash'] -and -not $alibabaIds['luna'] -and -not $alibabaIds['sol']) }
    $rt = { param($name, $route, $answered, $avoid) Resolve-OpenCodeRoute -Usual $models[$name] -Alibaba $alibabaIds[$name] -Route $route -Answered $answered -Avoid $avoid }
    $r = & $rt 'deepseek-pro' 'auto' $true @()
    $ruleChecks += [pscustomobject]@{ Name = 'route auto, nothing avoided: deepseek-pro stays on go'; Ok = ($r.Route -eq 'go' -and $r.Model -eq 'opencode-go/deepseek-v4-pro') }
    $r = & $rt 'deepseek-pro' 'auto' $true @('opencode_go')
    $ruleChecks += [pscustomobject]@{ Name = 'route auto, opencode_go avoided: deepseek-pro moves to alibaba'; Ok = ($r.Route -eq 'alibaba' -and $r.Model -eq 'alibaba-token-plan/deepseek-v4-pro' -and -not $r.Avoided) }
    $r = & $rt 'glm' 'auto' $true @('zai')
    $ruleChecks += [pscustomobject]@{ Name = 'route auto, zai avoided: glm moves to alibaba glm-5.3'; Ok = ($r.Route -eq 'alibaba' -and $r.Model -eq 'alibaba-token-plan/glm-5.3') }
    $r = & $rt 'glm' 'auto' $true @('zai', 'alibaba')
    $ruleChecks += [pscustomobject]@{ Name = 'route auto, zai and alibaba avoided: glm is Avoided on zai'; Ok = ($r.Route -eq 'zai' -and $r.Avoided) }
    $r = & $rt 'glm' 'auto' $false @()
    $ruleChecks += [pscustomobject]@{ Name = 'route auto, tracker silent: glm keeps zai'; Ok = ($r.Route -eq 'zai' -and $r.Model -eq 'zai-coding-plan/glm-5.3' -and -not $r.Avoided) }
    $r = & $rt 'glm-flash' 'auto' $true @('zai')
    $ruleChecks += [pscustomobject]@{ Name = 'route auto, zai avoided: glm-flash has no Alibaba id and is Avoided'; Ok = ($r.Route -eq 'zai' -and $r.Avoided) }
    $r = & $rt 'deepseek' 'alibaba' $false @()
    $ruleChecks += [pscustomobject]@{ Name = '-Route alibaba: deepseek runs alibaba deepseek-v4.1-flash'; Ok = ($r.Model -eq 'alibaba-token-plan/deepseek-v4.1-flash') }
    $r = & $rt 'glm' 'go' $false @()
    $ruleChecks += [pscustomobject]@{ Name = '-Route go is refused for glm'; Ok = ($r.Refused) }
    $r = & $rt 'luna' 'alibaba' $false @()
    $ruleChecks += [pscustomobject]@{ Name = '-Route alibaba is refused for luna'; Ok = ($r.Refused) }
    $r = & $rt 'qwen' 'auto' $true @('opencode_go', 'zai')
    $ruleChecks += [pscustomobject]@{ Name = 'route auto: qwen is always alibaba'; Ok = ($r.Route -eq 'alibaba' -and $r.Model -eq 'alibaba-token-plan/qwen3.8-max' -and -not $r.Avoided) }
    $ruleChecks += [pscustomobject]@{ Name = 'Alibaba errors: Invalid API-key and Provider not found stop the run; others do not'; Ok = ((Get-AlibabaFailure 'Error: Invalid API-key provided' 'X') -like '*auth.json in X*' -and (Get-AlibabaFailure 'Error: Provider not found: alibaba-token-plan' 'X') -like '*Provider not found*' -and $null -eq (Get-AlibabaFailure 'Error: rate limited' 'X')) }
    $probeBrief = Join-Path $probeDir "selftest-brief-$([guid]::NewGuid().ToString('N').Substring(0, 8)).md"
    Set-Content -LiteralPath $probeBrief -Value "T0 review (Qwen)`nself-test probe brief" -Encoding utf8
    $null = & pwsh -NoProfile -File $PSCommandPath -Pr 1 -Reviewer qwen -ExcludeModel qwen-flash -BriefFile $probeBrief -WhatIf 2>&1
    $qwenCode = $LASTEXITCODE
    $null = & pwsh -NoProfile -File $PSCommandPath -Pr 1 -Reviewer luna -Route alibaba -BriefFile $probeBrief -WhatIf 2>&1
    $lunaRouteCode = $LASTEXITCODE
    Remove-Item -LiteralPath $probeBrief -Force -ErrorAction SilentlyContinue
    $ruleChecks += [pscustomobject]@{ Name = "-WhatIf -Reviewer qwen -ExcludeModel qwen-flash is refused with exit 1 (got $qwenCode)"; Ok = ($qwenCode -eq 1) }
    $r = & $rt 'deepseek-pro' 'alibaba' $true @('alibaba')
    $ruleChecks += [pscustomobject]@{ Name = '-Route alibaba with alibaba avoided: deepseek-pro is Avoided'; Ok = ($r.Avoided -and -not $r.Refused) }
    # Sol's review of PR 783, R2 and R4: the reviewer path acts on /avoid (IC2_QUOTA_AVOID stands in
    # for the service), and a -WhatIf without -ExcludeModel succeeds.
    $probeBrief = Join-Path $probeDir "selftest-brief-$([guid]::NewGuid().ToString('N').Substring(0, 8)).md"
    Set-Content -LiteralPath $probeBrief -Value "T0 review (Luna)`nself-test probe brief" -Encoding utf8
    $probe = { param($avoid, [string[]] $more) $saved = $env:IC2_QUOTA_AVOID; $env:IC2_QUOTA_AVOID = $avoid
        try { $o = (& pwsh -NoProfile -File $PSCommandPath -Pr 1 -BriefFile $probeBrief -WhatIf @more 2>&1 | Out-String); [pscustomobject]@{ Code = $LASTEXITCODE; Out = $o } }
        finally { if ($null -eq $saved) { Remove-Item Env:IC2_QUOTA_AVOID -ErrorAction SilentlyContinue } else { $env:IC2_QUOTA_AVOID = $saved } } }
    $p = & $probe 'none' @('-Reviewer', 'luna')
    $ruleChecks += [pscustomobject]@{ Name = "-WhatIf -Reviewer luna without -ExcludeModel exits 0 (got $($p.Code))"; Ok = ($p.Code -eq 0) }
    $p = & $probe 'alibaba' @('-Reviewer', 'qwen')
    $ruleChecks += [pscustomobject]@{ Name = "-WhatIf -Reviewer qwen with alibaba avoided exits 3 (got $($p.Code))"; Ok = ($p.Code -eq 3) }
    $p = & $probe 'zai,alibaba' @('-Reviewer', 'glm')
    $ruleChecks += [pscustomobject]@{ Name = "-WhatIf -Reviewer glm with zai and alibaba avoided exits 3 (got $($p.Code))"; Ok = ($p.Code -eq 3) }
    $p = & $probe 'zai' @('-Reviewer', 'glm')
    $ruleChecks += [pscustomobject]@{ Name = "-WhatIf -Reviewer glm with zai avoided runs alibaba-token-plan/glm-5.3 (got $($p.Code))"; Ok = ($p.Code -eq 0 -and $p.Out -like '*alibaba-token-plan/glm-5.3*') }
    Remove-Item -LiteralPath $probeBrief -Force -ErrorAction SilentlyContinue
    $ruleChecks += [pscustomobject]@{ Name = "-WhatIf -Reviewer luna -Route alibaba is refused with exit 1 (got $lunaRouteCode)"; Ok = ($lunaRouteCode -eq 1) }
    # The owner's decision of 2026-10-05: Alibaba runs have their own data folder, never holding an
    # auth.json. Exercised on a scratch root (IC2_OPENCODE_DATA_HOME) with a dummy auth source.
    $d = Get-OpenCodeDataDirs -Root 'C:\r'
    $da = Get-OpenCodeDataDirs -Root 'C:\r' -Alibaba
    $ruleChecks += [pscustomobject]@{ Name = 'Alibaba runs use <root>\data-alibaba; others <root>\data; cache and state shared'; Ok = ($d.XDG_DATA_HOME -eq 'C:\r\data' -and $da.XDG_DATA_HOME -eq 'C:\r\data-alibaba' -and $da.XDG_CACHE_HOME -eq $d.XDG_CACHE_HOME -and $da.XDG_STATE_HOME -eq $d.XDG_STATE_HOME) }
    $scratch = Join-Path $probeDir "selftest-datahome-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $fakeAuth = Join-Path $scratch 'source-auth.json'
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    Set-Content -LiteralPath $fakeAuth -Value '{}' -Encoding utf8
    $savedEnv = @{ IC2_OPENCODE_DATA_HOME = $env:IC2_OPENCODE_DATA_HOME; IC2_OPENCODE_AUTH_SOURCE = $env:IC2_OPENCODE_AUTH_SOURCE }
    $env:IC2_OPENCODE_DATA_HOME = $scratch; $env:IC2_OPENCODE_AUTH_SOURCE = $fakeAuth
    $alibabaAuth = Join-Path $scratch 'data-alibaba\opencode\auth.json'
    try {
        $st = Initialize-OpenCodeDataHome -Major 1 -Alibaba 6>$null
        $xdg = $env:XDG_DATA_HOME
        Restore-OpenCodeDataHome $st
        $ruleChecks += [pscustomobject]@{ Name = 'an Alibaba run creates data-alibaba, points XDG_DATA_HOME at it, and copies no auth.json'; Ok = ($xdg -eq (Join-Path $scratch 'data-alibaba') -and (Test-Path -LiteralPath (Join-Path $scratch 'data-alibaba\opencode')) -and -not (Test-Path -LiteralPath $alibabaAuth)) }
        $st = Initialize-OpenCodeDataHome -Major 1 6>$null
        Restore-OpenCodeDataHome $st
        $ruleChecks += [pscustomobject]@{ Name = 'another provider still copies auth.json into data, never into data-alibaba'; Ok = ((Test-Path -LiteralPath (Join-Path $scratch 'data\opencode\auth.json')) -and -not (Test-Path -LiteralPath $alibabaAuth)) }
        Set-Content -LiteralPath $alibabaAuth -Value '{}' -Encoding utf8
        $stopped = $false
        try { $st = Initialize-OpenCodeDataHome -Major 1 -Alibaba 6>$null; Restore-OpenCodeDataHome $st } catch { $stopped = -not (Test-OpenCodeInfraFailure $_) -and $_.Exception.Message -like '*holds an auth.json*' }
        $ruleChecks += [pscustomobject]@{ Name = 'an auth.json in data-alibaba stops the run (not an infrastructure failure)'; Ok = $stopped }
    } finally {
        foreach ($k in $savedEnv.Keys) { if ($null -eq $savedEnv[$k]) { [System.Environment]::SetEnvironmentVariable($k, $null, 'Process') } else { [System.Environment]::SetEnvironmentVariable($k, $savedEnv[$k], 'Process') } }
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
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
# (issue #551), except the reviewer: it is Luna on the direct OpenAI route, `openai/gpt-5.6-luna`,
# via the machine's OpenAI login (issue #575; Go's proxied `opencode-go/gpt-6-luna` upstream
# returned Bad Request in long runs, #553). Luna at high effort is the review model (one OpenCode
# model per role before Claude); glm-flash, glm and deepseek stay valid as explicit -Reviewer
# values, and no default path picks them. sol (GPT-6 Sol, `openai/gpt-6-sol`, the same OpenAI login)
# is the complex tier's reviewer and luna plus glm or deepseek-pro the Luna pair (the user's decision of
# 2026-10-03, build-process.md §3.4); the main session passes them explicitly, so auto stays Luna.
$models = @{
    'glm-flash' = 'zai-coding-plan/glm-5.3-flash'
    glm         = 'zai-coding-plan/glm-5.3'
    luna        = 'openai/gpt-5.6-luna'
    sol         = 'openai/gpt-6-sol'
    deepseek    = 'opencode-go/deepseek-v4.1-flash'
    # DeepSeek V4 Pro, Sol's second substitute (the user's policy of 2026-10-03, build-process.md
    # §3.4 "When Sol cannot review"); `opencode models opencode-go --verbose` lists its variants as
    # high and max, so it runs at high like the others.
    'deepseek-pro' = 'opencode-go/deepseek-v4-pro'
    # The Qwen family, on the Alibaba Token Plan only (the user's decision of 2026-10-05): qwen is
    # Qwen3.8 Max, a heavy reviewer; qwen-flash is Qwen3.8 Flash, the light re-check reviewer.
    qwen         = 'alibaba-token-plan/qwen3.8-max'
    'qwen-flash' = 'alibaba-token-plan/qwen3.8-flash'
}
if ($ModelIds) { foreach ($k in $ModelIds.Keys) { $models[$k] = $ModelIds[$k] } }
# Provider-specific variant. Invoke-OpenCodeWatched passes it by the CLI's major version (1.x
# `--variant v`, 2.x the model's `#v` suffix). Empty means none. Effort is `high` everywhere (issue #575: `max` is
# overkill); luna was already high.
# Sol runs light: low by default, medium with -Effort medium, never high or above (the user's
# decision of 2026-10-03; its variants are none, low, medium, high, xhigh and max). -Effort's
# ValidateSet admits only low and medium, and it changes sol's variant alone.
function Get-SolVariant([string] $Requested) { if ($Requested) { return $Requested } return 'low' }
# Qwen3.8 Max and Flash offer low, medium and xhigh (`opencode models alibaba-token-plan --verbose`,
# 2026-10-05): qwen, heavy, runs at low; qwen-flash at medium (it has no high, and xhigh is overkill).
$variants = @{ 'glm-flash' = 'high'; glm = 'low'; luna = 'high'; sol = (Get-SolVariant $Effort); deepseek = ''; 'deepseek-pro' = 'high'; qwen = 'low'; 'qwen-flash' = 'medium' }
$displayNames = @{ 'glm-flash' = 'GLM Flash'; glm = 'GLM'; luna = 'Luna'; sol = 'Sol'; deepseek = 'DeepSeek'; 'deepseek-pro' = 'DeepSeek Pro'; qwen = 'Qwen'; 'qwen-flash' = 'Qwen Flash' }
# The Alibaba Token Plan route of the DeepSeek and GLM reviewers (the user's decision of 2026-10-05):
# the same model, so the same name and family, on another provider. -Route picks it (auto: when
# quota-tracker's /avoid lists the usual provider, opencode_go or zai). The variants are the same
# (Alibaba's deepseek-v4-pro offers high and max, glm-5.3 low, high and max). No light GLM on Alibaba.
$alibabaIds = @{
    deepseek       = 'alibaba-token-plan/deepseek-v4.1-flash'
    'deepseek-pro' = 'alibaba-token-plan/deepseek-v4-pro'
    glm            = 'alibaba-token-plan/glm-5.3'
}
# The reviewer's model family is never the implementer's (build-process.md §3.4). The implementing
# model comes from -ExcludeModel, else from a model:<name> label on the PR or its issue that names an
# OpenCode model (model:opus and model:sonnet name Claude, which is not in this chain). Implementer
# name -> the reviewer names of the same family; the MiMo models have no reviewer here. The families
# (the user's decision of 2026-10-03): OpenAI is luna and sol, GLM is glm and glm-flash, DeepSeek is
# deepseek, deepseek-pro and deepseek-flash. A luna or sol implementer excludes both OpenAI reviewers, so -Reviewer
# auto leaves no OpenCode reviewer and the script exits 3 (a cold Claude Opus reviews), and -Reviewer
# sol or luna is refused. A Claude implementer (sonnet, opus) excludes no OpenCode reviewer: the
# Claude family has no reviewer here, and the main session keeps Claude off that PR's review.
$reviewerOf = @{
    'deepseek-flash' = @('deepseek', 'deepseek-pro')
    'deepseek'       = @('deepseek', 'deepseek-pro')
    'deepseek-pro'   = @('deepseek', 'deepseek-pro')
    'glm-flash'      = @('glm-flash', 'glm')
    'glm'            = @('glm-flash', 'glm')
    'luna'           = @('luna', 'sol')
    'sol'            = @('luna', 'sol')
    # Qwen is its own family (the user's decision of 2026-10-05): a Qwen implementer excludes both
    # Qwen reviewers, and Qwen reviews any other family's PR.
    'qwen'           = @('qwen', 'qwen-flash')
    'qwen-flash'     = @('qwen', 'qwen-flash')
    'sonnet'         = @()
    'opus'           = @()
}

function Get-ExcludedReviewers([string[]] $Implementers) {
    # The reviewer names the implementers' families exclude (build-process.md §3.4).
    # Null names are skipped: -WhatIf without -ExcludeModel passes no implementer, which PowerShell
    # binds as one $null, and indexing the map with it threw (every such -WhatIf exited 1).
    return @($Implementers | Where-Object { $_ } | ForEach-Object { $reviewerOf[$_] } | Where-Object { $_ } | Select-Object -Unique)
}

function Publish-ReviewComment {
    # Posts the review as one PR comment and returns the comment's URL, or $null when it was not
    # posted (bug #645: during GitHub's 2026-10-03 outage `gh pr comment` failed with
    # 'Post "https://api.github.com/graphql": unexpected EOF' and the script still said "posted").
    # It checks gh's exit code AND that a comment URL came back, and retries once. -Gh is the gh
    # invocation, replaceable so -SelfTest can stub a failing gh.
    param([int] $Pr, [string] $BodyFile, [scriptblock] $Gh = { & gh @args })
    for ($try = 1; $try -le 2; $try++) {
        $global:LASTEXITCODE = 0
        $out = (& $Gh pr comment $Pr --body-file $BodyFile 2>&1 | Out-String)
        $code = $global:LASTEXITCODE
        $url = [regex]::Match($out, 'https://github\.com/\S+#issuecomment-\d+').Value
        if ($code -eq 0 -and $url) { return $url }
        Write-Host "gh pr comment failed (try $try of 2, exit $code): $($out.Trim())"
    }
    return $null
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
# The family check runs before the OpenCode probe and before -WhatIf returns (PR #642 review R6), so
# a green -WhatIf probe also says the reviewer is permitted for -ExcludeModel's implementer. Without
# -ExcludeModel, -WhatIf reads no labels and says the family was not checked.
if (-not $ExcludeModel -and -not $WhatIf -and -not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh is not on PATH.' }
$implementers = if ($ExcludeModel) { @($ExcludeModel) } elseif ($WhatIf) { @() } else {
    $labels = @(gh pr view $Pr --json labels --jq '.labels[].name' 2>$null)
    if ($Issue) { $labels += @(gh issue view $Issue --json labels --jq '.labels[].name' 2>$null) }
    @($labels | Where-Object { $_ -match '^model:(.+)$' } | ForEach-Object { $_.Substring(6) } |
        Where-Object { $_ -in 'deepseek-flash', 'glm-flash', 'glm', 'luna', 'sol', 'mimo-pro', 'mimo-flash', 'deepseek', 'deepseek-pro', 'qwen', 'qwen-flash' } | Select-Object -Unique)
}
if ($WhatIf -and -not $ExcludeModel) { Write-Host 'family not checked: pass -ExcludeModel <implemented by> to check it.' }
$excluded = Get-ExcludedReviewers $implementers
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
# The route of each chain reviewer (the user's decision of 2026-10-05). quota-tracker's /avoid is read
# once: -Route auto moves DeepSeek or GLM to the Alibaba Token Plan when its usual provider is
# avoided, unless Alibaba is avoided too; a tracker that does not answer keeps the usual route. A
# reviewer none of whose routes has quota (Qwen with alibaba avoided; GLM or DeepSeek with both its
# provider and alibaba avoided; a forced -Route whose provider is avoided) is dropped, as Appendix C's
# QUOTA FIRST skips it, and when none is left the script exits 3 with the cause, so the main session
# takes the next reviewer. An explicit -Route that a named reviewer has no id for is refused (exit 1).
$quota = Get-QuotaAvoid
$resolved = @{}
$routeSkips = @()
foreach ($name in $chain) {
    $r = Resolve-OpenCodeRoute -Usual $models[$name] -Alibaba $alibabaIds[$name] -Route $Route -Answered $quota.Answered -Avoid $quota.Providers
    if ($r.Refused) {
        [Console]::Error.WriteLine("Refused: $($displayNames[$name]): $($r.Why). Nothing posted.")
        exit 1
    }
    if ($r.Avoided) { Write-Host "skipped: $($displayNames[$name]) ($($r.Why))"; $routeSkips += "$($displayNames[$name]): $($r.Why)"; continue }
    $resolved[$name] = $r
    Write-Host "route: $($displayNames[$name]) on $($r.Route) ($($r.Model)): $($r.Why)"
}
$chain = @($chain | Where-Object { $resolved.ContainsKey($_) })
if (-not $chain) {
    [Console]::Error.WriteLine("OpenCode unavailable: no reviewer with quota ($($routeSkips -join '; ')). Nothing posted.")
    exit 3
}
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
        $null = Invoke-OpenCodeWatched -WhatIf -Agent 'external-reviewer' -Model $resolved[$name].Model -Variant $variants[$name] -Prompt $brief `
            -WorkDir (Get-Location).Path -Title "ic2-pr$Pr-$name"
    }
    exit 0
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh is not on PATH.' }

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
    $model = $resolved[$Name].Model
    $route = $resolved[$Name].Route
    $variant = $variants[$Name]
    $header = if ($Reviewer -eq 'auto') { $briefHeader -replace '\([^()]*\)\s*$', "($($displayNames[$Name]))" } else { $briefHeader }
    $rules = Get-ReviewOutputRules -Header $header -Worktree $worktree -HeadSha $headSha
    $prompt = $header + "`n" + $briefRest + $rules
    $fail = { param($reason, $detail) [pscustomobject]@{ Ok = $false; Name = $Name; Model = $model; Route = $route; Header = $header; Reason = $reason; Detail = $detail } }

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
    return [pscustomobject]@{ Ok = $true; Name = $Name; Model = $model; Route = $route; Header = $header; Review = $rewritten; Verdict = $parsed.Verdict; Flagged = $parsed.Flagged; FlagNote = $parsed.FlagNote }
}

$result = $null
$failures = @()
$sameCause = $null
try {
    if ($FixturesDir) { $env:IC2_FIXTURES_DIR = $FixturesDir }
    $n = 0
    foreach ($name in $chain) {
        $n++
        Write-Host "attempt $n/$($chain.Count): $($displayNames[$name]) ($($resolved[$name].Model), route $($resolved[$name].Route))$(if ($variants[$name]) { " with variant $($variants[$name])" }), OpenCode $($cli.Version)"
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
        $body = $review + "`n`n— $($displayNames[$result.Name]), via scripts/external-review.ps1 ($($result.Model), route $($result.Route))"
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
            $script:postedUrl = Publish-ReviewComment -Pr $Pr -BodyFile $bodyFile
            if (-not $script:postedUrl) {
                # Bug #645: never report or label a review GitHub did not take. Keep its text in a
                # named file for the main session to post by hand, and exit 5.
                $savedDir = Join-Path $repo 'rendered'
                New-Item -ItemType Directory -Force -Path $savedDir | Out-Null
                $script:savedReview = Join-Path $savedDir "review-not-posted-pr$Pr-$($result.Name)-$(Get-Date -Format 'yyyyMMdd-HHmmss').md"
                Set-Content -LiteralPath $script:savedReview -Value $body -Encoding utf8
                Write-Host "NOT posted: $(($review -split "`r?`n")[0]) / $($result.Verdict); no label applied; the review is saved in $script:savedReview"
            } elseif ($result.Flagged) {
                Write-Host "posted with the note line ($script:postedUrl); no label applied ($($result.FlagNote)). Read it and decide."
            } else {
                Write-Host "posted: $(($review -split "`r?`n")[0]) / $($result.Verdict) ($script:postedUrl)"
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
if ($script:savedReview) {
    [Console]::Error.WriteLine("Review on PR #$Pr was NOT posted (gh pr comment failed twice); no label applied. Read $script:savedReview and post it by hand (exit 5).")
    exit 5
}
if ($result.Flagged) {
    [Console]::Error.WriteLine("Review on PR #$Pr is $($result.FlagNote); posted without a label. Read it and decide (exit 4).")
    exit 4
}
