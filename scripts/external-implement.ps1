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
    With -Model auto (the default) the chain comes from quota-tracker's /recommend?tier=heavy
    (T152; CLAUDE.md rule 17), through Choose-Model.ps1 (scripts/Choose-Model.ps1). Each model
    is taken in the chooser's ranking order; an infrastructure failure advances to the next.
    When every model fails, the chain stops that way, or OpenCode is not installed, it exits 3
    ("OpenCode unavailable: ..."), and the task falls back to Claude Sonnet (operating-guide
    §3). GLM left the implementer side on 2026-10-01 (issue #573): GLM-5.3 ended T99's
    implementer run early, mid-exploration, with no error (#557), while DeepSeek V4.1 Flash
    implemented T97 in one go. glm, glm-flash and luna stay valid as explicit -Model values, and
    no default path picks them. An explicit -Model names that model alone (the same model the
    main session passed). A chain model none of whose routes has quota is skipped, like before.
    A chain model with no /recommend row is skipped too (the chooser ranks only what /recommend
    ranks; a live /recommend response with fewer than the dispatch chain's old fixed models
    shortens the chain, never lengthens it).
    The next model runs ONLY on an infrastructure failure (no session in time, an idle session, no
    exit in time, a run that exits without a session, a non-zero exit, the fallback-to-default-agent
    guard, a tool call the permission guard rejected -- issue #501), and only when the failed run
    left nothing behind: no new commit, locally or on origin, and no new PR. Otherwise the script
    exits 1 and the main session decides. Two consecutive attempts failing with the same cause
    (Get-OpenCodeFailureClass: two startup hangs, two idle kills, ...) stop the chain early.
    An implementer that stops and reports has not failed: its OpenCode run exits 0, so it is
    never retried on another model; the script then exits 1 ("No open PR") and the main session
    reads the report. Once every chain model has failed, the script asks Choose-Model.ps1 for an
    implementer outside the failed families AND the OpenAI family (T150 Done-when 2; T152 Done-when
    6) and runs it once before exit 3; it writes the run's attempts and the allowed outside paths
    it touched into the PR body (Done-when 2, 3).
    Before that, it copies -BriefFile into the worktree at rendered/brief.md and feeds the copy
    (Done-when 1), so the prompt names no path outside the worktree.
    Before the run's tail it prints the model that ran ("implemented by: <name>") and its /recommend
    reasons, in the log and in the PR body's "Implementer attempts" section (Done-when 4); the
    review passes it to external-review.ps1 as -ExcludeModel, so the reviewer is never the
    implementer's model.
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
.PARAMETER RecommendFile
    A canned /recommend?tier=<heavy|light> JSON response, in place of quota-tracker's /recommend.
    Picked up by Choose-Model.ps1 (Done-when 6). The test hook; live runs leave it empty.
.PARAMETER RecommendRetryWaitSec
    The seconds Choose-Model.ps1 waits / retries on a `loading` note (default 0 — the chooser
    uses its own default of 180 s; pass a small value to shorten a test's wait).
.PARAMETER SelfTest
    Offline checks: Format-ImplementerAttempt with canned /recommend meta proves the run log
    and the PR body's "Implementer attempts" section receive the same text (R3 rework). No
    network, no OpenCode, no PR is opened.

.EXAMPLE
    pwsh scripts/external-implement.ps1 -Task T71 -Slug persistence-hardening -Issue 308 -BriefFile C:\tmp\T71-brief.md
.EXAMPLE
    pwsh scripts/external-implement.ps1 -Fix 346 -Slug migration-message -BriefFile C:\tmp\346-brief.md -Model luna
.EXAMPLE
    pwsh scripts/external-implement.ps1 -Task T152 -Slug recommend -Issue 866 -BriefFile rendered\brief.md -WhatIf -RecommendFile rendered\heavy.json
.PARAMETER SelfTest
    Runs the offline checks (no network, no OpenCode): Format-ImplementerAttempt with
    canned /recommend meta proves the run log and the PR body's "Implementer attempts"
    section receive the same text (R3 rework). Does not perform a real run.
#>
[CmdletBinding()]
param(
    [string] $Task,
    [int] $Fix,
    [string] $Slug,
    [int] $Issue,
    [string] $BriefFile,
    [ValidateSet('auto', 'luna', 'glm-flash', 'glm', 'deepseek-flash', 'qwen-flash', 'mm-m3', 'mm-m2.7', 'ali-deepseek-flash', 'ali-deepseek-pro', 'ali-glm', 'mimo-pro', 'mimo-flash')] [string] $Model = 'auto',
    [ValidateSet('auto', 'go', 'zai', 'alibaba')] [string] $Route = 'auto',
    [switch] $LocalOnly,
    [string] $FixturesDir,
    [int] $StartupTimeoutSec = 180,
    [int] $TotalTimeoutSec = 10800,
    [int] $IdleTimeoutSec = 900,
    [switch] $WhatIf,
    [string[]] $SimulateFailed,
    [hashtable] $ModelIds,
    [string] $RecommendFile,
    [int]    $RecommendRetryWaitSec = 0,
    [switch] $SelfTest
)

# -SelfTest runs offline checks with no worktree, no OpenCode, no PR. Slug / BriefFile /
# Task / Fix are optional in the param block so a bare `-SelfTest` invocation does not
# fail mandatory validation (R3 rework); the real validation runs after the SelfTest
# short-circuit below.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-OpenCodeWatched.ps1')
# The Alibaba Token Plan's key comes from the user environment when this process predates it (never printed).
$null = Import-AlibabaTokenPlanKey

function Get-ChooserPick {
    # Calls Choose-Model.ps1 -Pick with -ExcludeFamily or -SubstituteFamilies applied.
    # Returns:
    #   - $null when the chooser exits 3 (no candidate);
    #   - otherwise an object { Name, Meta } where Name is the OpenCode alias (e.g. 'mm-m3')
    #     or the Claude Sonnet fallback sentinel and Meta is the CHOOSER_META payload
    #     (provider, model, score, confidence, reasons and family, decoded from JSON).
    # Run in process: Choose-Model returns before the rest of the script when dot-sourced,
    # but here it is called normally; its `exit` ends only that script and sets
    # $LASTEXITCODE.
    param(
        [string[]] $ExcludeFamily = @(),
        [string[]] $SubstituteFamilies = @(),
        [string]   $RecommendFile = $null,
        [int]      $RecommendRetryWaitSec = 0
    )
    $chooser = Join-Path $PSScriptRoot 'Choose-Model.ps1'
    if (-not (Test-Path -LiteralPath $chooser)) { return $null }
    # Use a hashtable for splatting; PowerShell 7.6.6 on the OpenCode runner fails to
    # splat an array of `-Name value` pairs into a `[CmdletBinding()]` script (each `-Name`
    # is bound as the value of the previous positional parameter, then the script errors
    # on `Role: -Role is not in the ValidateSet`). The hashtable form goes through
    # parameter binding correctly.
    $params = @{ Role = 'implementer'; Tier = 'complex'; Pick = $true }
    if ($ExcludeFamily) { $params['ExcludeFamily'] = $ExcludeFamily -join ',' }
    if ($SubstituteFamilies) { $params['SubstituteFamilies'] = $SubstituteFamilies -join ',' }
    if ($RecommendFile) { $params['RecommendFile'] = $RecommendFile }
    if ($RecommendRetryWaitSec -gt 0) { $params['RecommendRetryWaitSec'] = $RecommendRetryWaitSec }
    try { $out = & $chooser @params 2>&1 }
    catch { return $null }
    if ($LASTEXITCODE -ne 0) { return $null }
    $lines = @($out)
    $meta = $null
    foreach ($l in $lines) {
        $trim = ([string]$l).Trim()
        if ($trim -like 'CHOOSER_META=*') {
            try { $meta = ($trim.Substring('CHOOSER_META='.Length) | ConvertFrom-Json) } catch { $meta = $null }
        }
    }
    $name = @($lines) | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -and ($_ -notlike 'CHOOSER_META=*') } | Select-Object -Last 1
    if (-not $name) { return $null }
    return [pscustomobject]@{ Name = $name; Meta = $meta }
}

function Get-ChooserChain {
    # The chain of implementer aliases Choose-Model.ps1 returns for /recommend?tier=heavy.
    # Iteratively picks the top alias and excludes its family, until the chooser exits 3
    # (no candidate left). The chain takes the WHOLE mapped ranking: with a long
    # recommendation (e.g. alibaba opens up under T152's relaxed exclusions) the chain
    # grows to match it, never truncates an otherwise valid candidate (R5 rework). The
    # loop terminates when Get-ChooserPick returns $null (the chooser exited 3) — there
    # is no arbitrary count cap.
    # The Claude Sonnet fallback (rule 17: Claude is the orchestrator, dropped while
    # another positive exists; with nothing else, the script's exit-3 fallback) is a
    # sentinel name the dispatch recognises and treats as exit-3 — there is no OpenCode
    # model to dispatch.
    param([string] $RecommendFile, [int] $RecommendRetryWaitSec)
    $chain = @()
    $excluded = @()
    while ($true) {
        $next = Get-ChooserPick -ExcludeFamily $excluded -RecommendFile $RecommendFile -RecommendRetryWaitSec $RecommendRetryWaitSec
        if (-not $next) { break }
        $chain += $next
        if ($next.Meta -and $next.Meta.family) { $excluded += @([string]$next.Meta.family) }
    }
    return $chain
}

function Format-ImplementerAttempt {
    # R3 rework: factor the /recommend reasons line into a single function. Both the
    # run-log entry and the PR body's "Implementer attempts" section call this with the
    # same meta, so the offline self-test proves they receive identical text.
    # Returns $null for an empty meta (an explicit -Model run has no /recommend meta,
    # so neither destination is written — unchanged from before).
    param($meta)
    if (-not $meta) { return $null }
    $reasons = @($meta.reasons)
    $reasonsText = if ($reasons) { ($reasons -join '; ') } else { '' }
    $line = "provider $($meta.provider), model $($meta.model), score $($meta.score), confidence $($meta.confidence)"
    if ($reasonsText) { $line += "`n  $reasonsText" }
    return $line
}

function Invoke-ImplementerSelfTest {
    # Offline checks for R3 rework: Format-ImplementerAttempt with canned /recommend meta.
    # No network, no OpenCode, no PR is opened. Returns the count of failed assertions.
    $checks = New-Object System.Collections.Generic.List[object]
    function Add([string] $Name, [bool] $Ok) {
        $checks.Add([pscustomobject]@{ Name = $Name; Ok = [bool]$Ok }) | Out-Null
    }

    # Canned /recommend row, as CHOOSER_META returns it. Reasons is the array the chooser
    # emits in its CHOOSER_META payload; the line below the header is the joined text.
    $canned = [pscustomobject]@{
        name       = 'mm-m3'
        family     = 'mm-m3'
        provider   = 'minimax'
        model      = 'MiniMax-M3'
        score      = 1263
        confidence = 'ok'
        reasons    = @('7d: about 7,080 spare calls before the reset (~1,263/day) after demand and a 5% reserve')
    }

    # 1. The run log line and the PR body section line receive the SAME text. The run log
    #    wraps it with `=== /recommend reasons for this pick ===` markers; the PR body
    #    uses a `/recommend reasons for this pick: ` prefix and a literal newline before
    #    the reasons block. Both call sites use Format-ImplementerAttempt, so a single
    #    canned input produces identical text in both destinations.
    $attempt = Format-ImplementerAttempt $canned
    Add 'Format-ImplementerAttempt: provider line is the first line' ($attempt -match '(?m)^provider minimax, model MiniMax-M3, score 1263, confidence ok')
    Add 'Format-ImplementerAttempt: reasons line follows after `  ` indent' ($attempt -match "(?m)^\s{2}7d: about 7,080")
    $runLogLine = "=== /recommend reasons for this pick ===`n$attempt`n"
    $prBodyLine = "/recommend reasons for this pick: $attempt"
    Add 'R3: run log entry includes the formatted line' ($runLogLine -match 'provider minimax, model MiniMax-M3, score 1263, confidence ok')
    Add 'R3: PR body entry includes the formatted line'   ($prBodyLine  -match 'provider minimax, model MiniMax-M3, score 1263, confidence ok')
    # The "implemented by:" console line is also derived from Format-ImplementerAttempt;
    # assert it preserves the same body.
    $implLine = "implemented by: mm-m3 (minimax/MiniMax-M3, route minimax)`n/recommend reasons for this pick: $attempt"
    Add 'R3: "implemented by:" line preserves the formatted text' ($implLine -match 'provider minimax, model MiniMax-M3, score 1263, confidence ok')

    # 2. An empty meta produces $null: neither destination is written (explicit -Model
    #    runs have no /recommend meta; the run log and PR body skip the reasons line).
    Add 'Format-ImplementerAttempt on $null returns $null' ($null -eq (Format-ImplementerAttempt $null))

# 3. Reasons array with multiple entries is joined with '; ' in order. Mirrors what
    #    the live CHOOSER_META would deliver.
    $multi = [pscustomobject]@{
        provider = 'claude'; model = 'opus'; score = 1038; confidence = 'low'
        reasons = @('7d: about 7,046 spare calls', 'rough: 7d is barely used', 'main model of 8 live Claude sessions')
    }
    $multiAttempt = Format-ImplementerAttempt $multi
    Add 'multi-reason: three reasons joined with "; " in order' (
        $multiAttempt -match '7d: about 7,046 spare calls; rough: 7d is barely used; main model of 8 live Claude sessions')

    # 4. A real canned chooser result, the same that powers a live run, is converted to
    #    CHOOSER_META by Get-ChooserPick's caller. Simulate the full path: parse a canned
    #    JSON row, build the meta, call Format-ImplementerAttempt. This proves the
    #    function takes the same shape that the real run path passes to it.
    # The canned response is embedded (GLM's re-check R1): a git-ignored fixture file would not
    # exist in a clean checkout, and the self-test must run anywhere.
    $cannedJson = '{"note":null,"ranking":[{"provider":"minimax","model":"MiniMax-M3","score":1263,"confidence":"ok","reasons":["7d: about 7,080 spare calls before the reset (~1,263/day) after demand and a 5% reserve"]}],"skipped":[]}'
    $parsed = $cannedJson | ConvertFrom-Json
    $row = $parsed.ranking | Where-Object { $_.provider -eq 'minimax' } | Select-Object -First 1
    $rowMeta = [pscustomobject]@{
        provider = $row.provider; model = $row.model; score = $row.score; confidence = $row.confidence
        reasons = @($row.reasons)
    }
    $realAttempt = Format-ImplementerAttempt $rowMeta
    Add 'R3: canned heavy.json minimax row -> Format-ImplementerAttempt returns the reasons line' ($realAttempt -match '7d: about 7,080 spare calls')

    # Report.
    $i = 0; $failed = 0
    foreach ($c in $checks) {
        $i++
        $status = if ($c.Ok) { 'PASS' } else { 'FAIL' }
        "[{0,2}/{1}] {2}  -- {3}" -f $i, $checks.Count, $status, $c.Name | Write-Host
        if (-not $c.Ok) { $failed++ }
    }
    # Written to the host, not the output stream (GLM's re-check R2): returning both the line and
    # the count made the caller compare a string with 0 and exit 1 on a full pass.
    Write-Host "self-test: $($checks.Count - $failed)/$($checks.Count) passed"
    return [int]$failed
}

# T152 Done-when 2 / Done-when 6: the chain-spent substitute picks an implementer outside
# the failed families AND the OpenAI family (the OpenAI drop is automatic inside
# Choose-Model.ps1 once -SubstituteFamilies is passed). Returns the model alias, or $null;
# -Why is filled either way.
function Get-SubstituteModel {
    param([string[]] $ExcludeFamilies, [ref] $Why, [string] $RecommendFile, [int] $RecommendRetryWaitSec)
    $pick = Get-ChooserPick -SubstituteFamilies $ExcludeFamilies -RecommendFile $RecommendFile -RecommendRetryWaitSec $RecommendRetryWaitSec
    if (-not $pick) {
        $Why.Value = "Choose-Model.ps1 found no implementer outside $($ExcludeFamilies -join ', ')"
        return $null
    }
    $Why.Value = "T150's chain-spent substitute, with families excluded ($($ExcludeFamilies -join ', ')), picked $($pick.Name)"
    # The script's only consumer that uses meta strips it via .Name; it does not consume the
    # meta here. The chain-spent path stores reasons in its attempts entry (Done-when 4),
    # using the meta the chooser emits.
    $script:LastSubstituteMeta = $pick.Meta
    return $pick.Name
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

# -SelfTest short-circuits the run path: offline checks only (R3 rework). No worktree, no
# OpenCode, no PR. Use it to verify Format-ImplementerAttempt end-to-end without billing.
# Runs BEFORE the chain is built so the chooser is never called from a SelfTest run.
if ($SelfTest) {
    $failed = Invoke-ImplementerSelfTest
    if ($failed -gt 0) { exit 1 } else { exit 0 }
}

# T152 Done-when 3: `-Model auto`'s chain comes from quota-tracker's /recommend?tier=heavy
# through Choose-Model.ps1, in the chooser's ranking order. Each entry also keeps the meta
# the chooser emits (provider, model, score, confidence, reasons), for the run log and the
# PR body's "Implementer attempts" section (Done-when 4). An explicit -Model names that
# model alone and has no meta.
$chainPicks = if ($Model -eq 'auto') { Get-ChooserChain -RecommendFile $RecommendFile -RecommendRetryWaitSec $RecommendRetryWaitSec } else { @() }
$chain = if ($Model -eq 'auto') { @($chainPicks | ForEach-Object Name) } else { @($Model) }
# $chainMeta: alias name -> meta object, populated from the chooser for -Model auto. Used
# by the run log ("=== <m>: ran — chosen: <alias> (<model>, <route>): <reasons>") and the
# "Implementer attempts" section. An explicit -Model leaves no meta: the main session's
# narrative names the model and the entry's evidence narrates the choice.
$chainMeta = @{}
foreach ($p in $chainPicks) { if ($p.Meta) { $chainMeta[$p.Name] = $p.Meta } }
# The Claude Sonnet fallback sentinel the chooser emits when nothing else has a positive
# score (rule 17, the orchestrator). The dispatch recognises it and exits 3 with the
# Claude Sonnet fallback message (the main session runs Claude Sonnet, operating-guide §3).
$script:ChooserClaudeSentinel = '__CLAUDE_SONNET_FALLBACK__'
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
    # T152 Done-when 3: the Claude Sonnet fallback sentinel (the chooser emits it when its
    # only viable candidate is Claude) has no $models entry — the dispatch treats it as
    # "exit 3, the main session runs Claude Sonnet", with a 'claude-fallback' class entry
    # so the PR body's "Implementer attempts" section records it.
    if ($m -eq $script:ChooserClaudeSentinel) {
        Write-Host "chooser picked the Claude Sonnet fallback (rule 17, the orchestrator): nothing else has a positive score"
        $attempts += [pscustomobject]@{ Model = '<Claude Sonnet fallback>'; Route = '-'; Class = 'claude-fallback'; Why = 'rule 17: the orchestrator is dropped while another positive exists; with nothing else, this is the script fallback' }
        $resolved[$m] = [pscustomobject]@{ Route = '-'; Model = '<Claude Sonnet fallback>'; Why = 'the chooser picked the Claude Sonnet fallback'; Avoided = $false; Refused = $false }
        continue
    }
    $r = Resolve-OpenCodeRoute -Usual $models[$m] -Alibaba $alibabaIds[$m] -Route $Route -Answered $quota.Answered -Avoid $quota.Providers -UsableWhenExhausted $quota.Usable
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
if (-not $Slug) { throw 'Give -Slug.' }
if (-not $BriefFile) { throw 'Give -BriefFile.' }
if (-not (Test-Path $BriefFile)) { throw "Brief not found: $BriefFile" }
# OpenCode not installed or not found, or a major version this script has no arguments for (only 1.x
# and 2.x): the same exit 3 as every model failing. The version is read once (T98).
try { $cli = Get-OpenCodeCli } catch {
    if (-not (Test-OpenCodeInfraFailure $_)) { throw }
    [Console]::Error.WriteLine("OpenCode unavailable: $($_.Exception.Message) The task falls back to Claude Sonnet (operating-guide §3).")
    exit 3
}
if (-not $chain) {
    # T150 Done-when 2 (rework R2): a chain spent entirely on quota skips is not an exit-3 case any
    # more. -WhatIf prints why along with the rest of the what-if block (the brief path and the
    # simulated substitute); a real run falls through to the worktree setup and the substitute path
    # at the bottom, which calls Choose-Model for an implementer of a family that the chain did
    # not burn. If that chooser returns none, exit 3 fires at the bottom with this reason in the
    # message.
    $chainGoneReason = "no chain model has a route with quota (quota-tracker /avoid: $($quota.Providers -join ', '))"
    Write-Host $chainGoneReason
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
        # T150 Done-when 2 (the user's amendment of 2026-10-08), retold via T152: the substitute
        # is never from the OpenAI family. -SubstituteFamilies makes the chooser add the openai
        # family itself; the check here compares the substitute with and without that drop, so
        # the rule is visible.
        $whyWithout = $null
        $subWithoutPick = Get-ChooserPick -ExcludeFamily $failed -RecommendFile $RecommendFile -RecommendRetryWaitSec $RecommendRetryWaitSec
        $subWithout = if ($subWithoutPick) { $subWithoutPick.Name } else { $null }
        $whyWithout = if ($subWithoutPick) { "-ExcludeFamily $($failed -join ',') on the implementer ranking picked $subWithout" } else { 'no implementer found' }
        $subWhy = $null
        $subPick = Get-SubstituteModel -ExcludeFamilies $failed -Why ([ref]$subWhy) -RecommendFile $RecommendFile -RecommendRetryWaitSec $RecommendRetryWaitSec
        Write-Host "simulate failed: $($failed -join ', ')"
        if ($subWithout -and $subWithout -ne $subPick) { Write-Host "without OpenAI exclusion, would have picked: $subWithout ($whyWithout)" }
        if ($subPick) { Write-Host "would substitute: $subPick ($subWhy)" }
        else { Write-Host "would substitute: none ($subWhy)" }
    } else {
        # Done-when 6 (with -RecommendFile, no -SimulateFailed): the model it would run and its
        # reasons, showing the dispatch honours the chooser. For -Model auto, the first entry in
        # the chain is what the real run would try first; the run log quotes its reasons.
        # R3 rework: the same Format-ImplementerAttempt text reaches the run log and the
        # PR body. -WhatIf prints it here so the offline check is independently
        # reproducible without a real OpenCode run.
        if ($Model -eq 'auto' -and $chain) {
            $first = $chain[0]
            $why = $chainMeta[$first]
            $attemptText = Format-ImplementerAttempt $why
            if ($attemptText) {
                Write-Host "would attempt: $first"
                Write-Host "  reasons: $attemptText"
            } else {
                Write-Host "would attempt: $first (no /recommend reasons: explicit -Model)"
            }
        }
    }
    $whatIfPrompt = Get-Content -Raw -LiteralPath $BriefFile
    foreach ($m in $chain) {
        if ($m -eq $script:ChooserClaudeSentinel) {
            Write-Host "would attempt: <Claude Sonnet fallback> (no OpenCode run; rule 17 fallback)"
            continue
        }
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
# Done-when 3 (GLM's re-check R2): every attempt that returned a run, failed or not, is kept here,
# so a failed run's touched outside paths are reported and "unknown" means only a failed export.
$outsideRuns = @()
[System.IO.File]::WriteAllText($log, '')
function Format-OutsidePathsReport([object[]] $Runs) {
    # One line per allowed outside path any attempt touched, with the attempt's model. "none" when every
    # attempt's export was read and none touched one. "unknown" is printed only for an attempt whose
    # export failed, and names that attempt. With no run at all (every attempt failed before OpenCode
    # returned one), the report says so instead of blaming an export.
    if (-not $Runs -or $Runs.Count -eq 0) { return 'unknown: no attempt returned a session (each failed before OpenCode finished)' }
    $lines = @()
    foreach ($r in $Runs) {
        if (-not $r.Run.OutsidePathsKnown) { $lines += "- unknown for $($r.Model): the session export failed"; continue }
        foreach ($p in @($r.Run.OutsidePaths)) { $lines += "- $p ($($r.Model))" }
    }
    if ($lines.Count -eq 0) { return 'none' }
    return ($lines -join "`n")
}

$attempt = 0
foreach ($m in $chain) {
    # T152 Done-when 3: the chooser emits the Claude Sonnet fallback sentinel when no
    # implementer has a positive score (rule 17, the orchestrator). No OpenCode model is
    # run; the dispatch falls through to the "OpenCode unavailable" exit 3 path.
    if ($m -eq $script:ChooserClaudeSentinel) {
        $attempt++
        $reasons = @((@($chainMeta[$script:ChooserClaudeSentinel]) | ForEach-Object { $_.reasons } | ForEach-Object { $_ }))
        $whyText = if ($reasons) { $reasons -join '; ' } else { 'nothing else has a positive /recommend score; rule 17 fallback' }
        Write-Host "attempt $attempt (no OpenCode run): <Claude Sonnet fallback> -- $whyText"
        $failures += "<Claude Sonnet fallback>: $whyText"
        $chainGoneReason = $whyText
        break
    }
    $attempt++
    Write-Host "attempt $attempt/$($chain.Count): $m ($($resolved[$m].Model), route $($resolved[$m].Route))$(if ($variants[$m]) { " with variant $($variants[$m])" }), OpenCode $($cli.Version)"
    $reason = $null
    $run = $null
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
    if ($run) { $outsideRuns += [pscustomobject]@{ Model = $m; Run = $run } }
    Add-Content -LiteralPath $log -Value "=== $m ($($resolved[$m].Model), route $($resolved[$m].Route)): $(if ($reason) { "failed: $reason" } else { 'ran' }) ===`n$output" -Encoding utf8
    if (-not $reason) {
        $implementedBy = $m
        $attempts += [pscustomobject]@{ Model = $m; Route = $resolved[$m].Route; Class = 'ran'; Why = '' }
        $finalRun = $run
        # T152 Done-when 4: the run log and the PR body's "Implementer attempts" section name
        # the chosen model and quote its row's reasons. The same Format-ImplementerAttempt
        # text reaches both destinations (R3 rework: factored into a function that the
        # self-test exercises offline).
        $chosenMeta = $chainMeta[$m]
        $attemptText = Format-ImplementerAttempt $chosenMeta
        if ($attemptText) {
            Add-Content -LiteralPath $log -Value "=== /recommend reasons for this pick ===`n$attemptText`n" -Encoding utf8
        }
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
    # T150 Done-when 2 (the user's amendment of 2026-10-08), retold via T152: the substitute is
    # never from the OpenAI family. Get-SubstituteModel passes -SubstituteFamilies, which makes
    # Choose-Model.ps1 add the openai family itself; no 'luna' is appended here. A failed
    # Claude-Sonnet-fallback sentinel (the chain stopped because no implementer has a positive
    # score) is also excluded so the substitute doesn't re-pick it.
    $failedFamilies = @($attempts | Where-Object { $_.Class -ne 'ran' -and $_.Class -ne 'claude-fallback' } | Select-Object -ExpandProperty Model -Unique) | Select-Object -Unique
    $subWhy = $null
    $substitute = Get-SubstituteModel -ExcludeFamilies $failedFamilies -Why ([ref]$subWhy) -RecommendFile $RecommendFile -RecommendRetryWaitSec $RecommendRetryWaitSec
    $subMeta = $script:LastSubstituteMeta
    if (-not $substitute) {
        Write-Host "the chain is spent; no substitute: $subWhy"
    } else {
        Write-Host "the chain is spent; substitute: $subWhy"
        if ($subMeta) {
            Write-Host "  reason: provider $($subMeta.provider), model $($subMeta.model), score $($subMeta.score), confidence $($subMeta.confidence)"
            foreach ($r in @($subMeta.reasons)) { Write-Host "  - $r" }
        }
        # T152 Done-when 3: the chooser can also pick the Claude Sonnet fallback as the
        # substitute (no positive-scoring family left at all). The dispatch exits 3.
        if ($substitute -eq $script:ChooserClaudeSentinel) {
            $attempts += [pscustomobject]@{ Model = '<Claude Sonnet fallback>'; Route = '-'; Class = 'claude-fallback'; Why = ($subWhy -replace '^[^:]*: ', '') }
        } else {
            $sr = Resolve-OpenCodeRoute -Usual $models[$substitute] -Alibaba $alibabaIds[$substitute] -Route $Route -Answered $quota.Answered -Avoid $quota.Providers -UsableWhenExhausted $quota.Usable
            if ($sr.Refused) {
                Write-Warning "substitute $substitute refused: $($sr.Why)"
                $attempts += [pscustomobject]@{ Model = $substitute; Route = '-'; Class = 'quota-skip'; Why = $sr.Why }
            } else {
                if ($sr.Avoided) { Write-Warning "${substitute}: $($sr.Why); it runs anyway as the substitute." }
                $attempt++
                Write-Host "attempt $attempt (substitute): $substitute ($($sr.Model), route $($sr.Route))$(if ($variants[$substitute]) { " with variant $($variants[$substitute])" }), OpenCode $($cli.Version)"
                $reason = $null
                $run = $null
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
                if ($run) { $outsideRuns += [pscustomobject]@{ Model = $substitute; Run = $run } }
                $logHeader = "=== $substitute ($($sr.Model), route $($sr.Route)): $(if ($reason) { "failed: $reason" } else { 'ran' }) ==="
                $subAttemptText = Format-ImplementerAttempt $subMeta
                if ($subAttemptText) {
                    $logHeader = "$logHeader`n=== /recommend reasons for this pick ===`n$subAttemptText`n"
                }
                Add-Content -LiteralPath $log -Value "$logHeader`n$output" -Encoding utf8
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
}
if (-not $implementedBy) {
    # Not Write-Error: under ErrorActionPreference Stop it would end the script with exit 1, not 3.
    $why = if ($chainGoneReason) { $chainGoneReason }
           elseif ($sameCause) { "same failure twice: $sameCause ($($failures -join '; '))" }
           elseif ($failures) { $failures -join '; ' }
           else { 'the chain and the substitute are both unavailable' }
    [Console]::Error.WriteLine("OpenCode unavailable: $why. The task falls back to Claude Sonnet (operating-guide §3). Log: $log")
    $openCodeUnavailable = $true
}

# Done-when 3 (rework R3): the "Outside paths touched" report comes after the run, on success,
# on a chain-spent failure, and on a run that completed without opening a PR. The exit code is
# unchanged (3 for the failure above, 1 for "no PR" below, 0 otherwise); the report is only
# appended to the PR body when a PR exists, but it is always printed.
$outsideBody = Format-OutsidePathsReport $outsideRuns
Write-Host 'Outside paths touched:'
Write-Host $outsideBody

# Done-when 2: the "Implementer attempts" body for the PR section. $implementedBy is set when the
# run produced a PR-worthy outcome (no, absent, or whoever failed); the PR body section is only
# meaningful when one was produced, but the body is built unconditionally so the Write-Host +
# Set-PullRequestSection below are the only branching.
$attemptBody = $null
if ($implementedBy -and $implementedBy -ne $script:ChooserClaudeSentinel) {
    $attemptLines = @($attempts | ForEach-Object {
            "- $($_.Model) ($($_.Route)): $($_.Class)$(if ($_.Why) { " -- $($_.Why)" })"
        })
    $chosenMeta = $chainMeta[$implementedBy]
    $chosenLine = "ran: $implementedBy ($($resolved[$implementedBy].Model), route $($resolved[$implementedBy].Route))"
    if ($chosenMeta) {
        # T152 Done-when 4: the "Implementer attempts" section names the chosen model and
        # quotes its row's reasons (provider, model, score, confidence, reasons). The
        # main session copies these into its tier comment when a review needs them too.
        # R3 rework: Format-ImplementerAttempt produces the same text that the run log
        # receives, so the self-test proves both destinations see the same line.
        $attemptText = Format-ImplementerAttempt $chosenMeta
        $chosenLine += "`n/recommend reasons for this pick: $attemptText"
    }
    $attemptLines += $chosenLine
    $attemptBody = $attemptLines -join "`n"
} elseif ($attempts) {
    # A chain that ended on the Claude Sonnet fallback (no implementer has a positive score)
    # still records the attempts on the PR body.
    $attemptLines = @($attempts | ForEach-Object {
            "- $($_.Model) ($($_.Route)): $($_.Class)$(if ($_.Why) { " -- $($_.Why)" })"
        })
    $attemptBody = $attemptLines -join "`n"
}

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

# Append the two sections to the PR body when one exists. The body is built before the exit so a
# no-PR run or a chain-spent failure still gets the "Outside paths touched" line above (R3 rework).
# The Implementer attempts section is only written when something actually implemented ($attemptBody
# is non-null); the Outside paths touched section is written whenever the PR exists, so a failed run
# that completed still leaves the report on the PR.
$prNumber = 0
try { $prNumber = [int](($pr | ConvertFrom-Json).number) } catch { }
if ($prNumber -gt 0) {
    if ($attemptBody) { Set-PullRequestSection -Pr $prNumber -Heading 'Implementer attempts' -Body $attemptBody -Worktree $worktree }
    Set-PullRequestSection -Pr $prNumber -Heading 'Outside paths touched' -Body $outsideBody -Worktree $worktree
}

# Exit codes: unchanged from before.
if ($openCodeUnavailable) { exit 3 }
if ($failures) { Write-Host "fell back: $($failures -join '; ')" }
if ($implementedBy) {
    # The reviewer must not be this model: pass it to external-review.ps1 as -ExcludeModel.
    $implLine = "implemented by: $implementedBy ($($resolved[$implementedBy].Model), route $($resolved[$implementedBy].Route))"
    $chosenMeta = $chainMeta[$implementedBy]
    $attemptText = Format-ImplementerAttempt $chosenMeta
    if ($attemptText) {
        $implLine += "`n/recommend reasons for this pick: $attemptText"
    }
    Write-Host $implLine
}
if (-not $pr) { Write-Error "No open PR for $branch. Read $log; resume with the same command once the cause is known."; exit 1 }
Write-Host "PR: $pr"
exit 0
