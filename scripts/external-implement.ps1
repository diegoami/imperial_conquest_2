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
    Every run takes an explicit -Model, chosen by the main session from quota-tracker's
    /recommend (CLAUDE.md rule 17; `pwsh scripts/Choose-Model.ps1 -Role implementer` or
    `-Role pair`), whose reasons it logs on the task's issue. -Model auto and its chain were
    removed by #893 (the user's request of 2026-10-09). A route that does not serve the model,
    or has no quota for it, refuses the run (exit 1). When the model fails, or OpenCode is not
    installed, the script exits 3 ("OpenCode unavailable: ...") unless the substitute below
    runs, and the main session takes the next /recommend row (operating-guide §3).
    A substitute runs ONLY on an infrastructure failure (no session in time, an idle session, no
    exit in time, a run that exits without a session, a non-zero exit, the fallback-to-default-agent
    guard, a tool call the permission guard rejected -- issue #501), and only when the failed run
    left nothing     behind: no new commit, locally or on origin, and no new PR. Otherwise the script
    exits 1 and the main session decides. A failed attempt's uncommitted work is never thrown
    away (T151 Done-when 1, #854): before the reset that readies the tree for the next model, it
    is saved as rendered/attempts/<model>-<yyyyMMdd-HHmmss>.patch in the worktree (git-ignored,
    taken as `git add -A` then `git diff --cached --binary <start commit>`; no git stash), the
    path is printed, and the patch survives the next attempt's reset. If that save fails, the
    reset and clean are SKIPPED (R1): the attempt's state is left in the tree for diagnosis, the
    script prints the cause and the log, and exits 4. Two consecutive attempts
    failing with the same cause (Get-OpenCodeFailureClass: two startup hangs, two idle kills,
    ...) mean OpenCode itself is the problem (operating-guide §3): the script stops and reports
    for diagnosis -- the failure class, both attempts' reasons, session ids and kept files, the
    run log and the outside-paths report -- and exits 3, WITHOUT running the substitute (T151
    Done-when 5, bug #868, the user's decision of 2026-10-08: "stop and diagnose the problem").
    An implementer that stops and reports has not failed: its OpenCode run exits 0, so it is
    never retried on another model; the script then exits 1 ("No open PR") and the main session
    reads the report. Once the model has failed, the script asks Choose-Model.ps1 for an
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
    qwen-flash and (with -Route alibaba) glm only; on 2026-10-09 DeepSeek went out of use
    (the user's decision: every DeepSeek model on every route), so miMo is the only
    opencode_go family now. Its key is the user variable ALIBABA_TOKEN_PLAN_API_KEY, loaded
    into this process when missing and never printed; "Invalid API-key" or "Provider not
    found" from it stops the script (exit 1) with the cause, never a retry.

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
    Required (#893: there is no default and no `auto`; the main session chooses it from
    /recommend). One model: qwen-flash (Qwen3.8 Flash at medium, `alibaba-token-plan/qwen3.8-flash`; it
    offers low, medium and xhigh), luna (GPT-5.6 Luna at
    high effort, `openai/gpt-5.6-luna`, direct OpenAI via the machine's OpenAI login), glm-flash
    (GLM-5.3 Flash at high), glm (GLM-5.3 at low, a heavy model run light; only selected explicitly;
    -Route alibaba moves it to the Alibaba Token Plan when the user asks, #893),
    miMo Pro (mimo-pro, `opencode-go/mimo-v2.6-pro`, the heavy model on OpenCode Go since
    2026-10-09; the user's decision that day), or miMo Flash (mimo-flash, `opencode-go/mimo-v2.6-flash`).
    DeepSeek is not used (the user's decision of 2026-10-09); naming a DeepSeek alias is refused with
    "DeepSeek is not used (the user's decision of 2026-10-09)".
.PARAMETER Route
    Which provider miMo and glm run through: auto (the default) takes the usual one
    (OpenCode Go, Z.AI) and warns when quota-tracker's /avoid lists it; it never moves to the
    Alibaba Token Plan, which is used only when the user asks (#893, the user's decision of
    2026-10-09: -Route alibaba). go, zai or alibaba force one: an explicit -Model that route does not serve is refused (exit
    1). The route is printed, logged and named on the
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
    the check. Each entry is a model name, or `model=class` to name the failure class
    (Get-OpenCodeFailureClass's, e.g. mimo-flash=no-session): two consecutive entries with one
    class show the same-cause stop (T151 Done-when 5: the diagnosis text and exit 3, no
    substitute); otherwise the script prints the substitute Choose-Model.ps1 would pick with those
    models' families excluded, and why, without running anything (Done-when 2's check).
.PARAMETER ModelIds
    Overrides of the model name -> model id map, e.g. @{ 'mimo-flash' = 'opencode-go/mimo-v2.6-flash' },
    for when `opencode models` shows a different id (or, in a test, a bad id to exercise the chain).
.PARAMETER RecommendFile
    A canned /recommend?tier=<heavy|light> JSON response, in place of quota-tracker's /recommend.
    Picked up by Choose-Model.ps1 (Done-when 6). The test hook; live runs leave it empty.
.PARAMETER RecommendRetryWaitSec
    The seconds Choose-Model.ps1 waits / retries on a `loading` note (default 0 — the chooser
    uses its own default of 180 s; pass a small value to shorten a test's wait).
.PARAMeter SelfTest
    Offline checks: Format-ImplementerAttempt with canned /recommend meta proves the run log
    and the PR body's "Implementer attempts" section receive the same text (R3 rework); T151
    adds the Save-AttemptState round-trip (a staged change, an unstaged change and an untracked
    file survive a reset as a patch, restored by git apply), the leftWork exit text carrying the
    "Outside paths touched" report (bug #868 R3), and the same-cause exit text naming both
    attempts' session ids and kept files and the run log (bug #868 R4). No network, no OpenCode,
    no PR is opened; the only thing it runs is git, in a throwaway repository under the TEMP
    folder.

.EXAMPLE
    pwsh scripts/external-implement.ps1 -Task T71 -Slug persistence-hardening -Issue 308 -BriefFile C:\tmp\T71-brief.md
.EXAMPLE
    pwsh scripts/external-implement.ps1 -Fix 346 -Slug migration-message -BriefFile C:\tmp\346-brief.md -Model luna
.EXAMPLE
    pwsh scripts/external-implement.ps1 -Task T152 -Slug recommend -Issue 866 -BriefFile rendered\brief.md -WhatIf -RecommendFile rendered\heavy.json
.PARAMETER SelfTest
    Runs the offline checks (no network, no OpenCode): Format-ImplementerAttempt with
    canned /recommend meta proves the run log and the PR body's "Implementer attempts"
    section receive the same text (R3 rework); the T151 checks (Save-AttemptState
    round-trip, the R1 failed-save-does-not-reset guard, the leftWork and same-cause exit
    texts) run git in a throwaway TEMP repository. Does not perform a real run.
#>
[CmdletBinding()]
param(
    [string] $Task,
    [int] $Fix,
    [string] $Slug,
    [int] $Issue,
    [string] $BriefFile,
    [ValidateSet('luna', 'glm-flash', 'glm', 'qwen-flash', 'mm-m3', 'mm-m2.7', 'ali-glm', 'mimo-pro', 'mimo-flash')] [ValidateScript({ if ($_ -match 'deepseek') { throw "DeepSeek is not used (the user's decision of 2026-10-09)" }; $true })] [string] $Model,
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

# Refuse a DeepSeek alias before any worktree, brief copy or OpenCode call (#908: the user's
# decision of 2026-10-09). A direct -Model deepseek-flash is refused at binding by the
# ValidateScript on -Model, with exit 1 and the same reason (it is not in the ValidateSet, so
# the alias is never usable); this guard covers the -ModelIds override, keys and VALUES (any id
# containing "deepseek", whatever the provider prefix), before a worktree or an OpenCode call.
$deepseekRejectMessage = 'DeepSeek is not used (the user''s decision of 2026-10-09)'
if ($ModelIds) {
    foreach ($k in @($ModelIds.Keys)) {
        if ($k -match '^(deepseek|ali-deepseek)') {
            [Console]::Error.WriteLine("Refused: -ModelId key '$k' names a DeepSeek alias; $deepseekRejectMessage.")
            exit 1
        }
        # An override VALUE is refused too (any id containing "deepseek", whatever the provider
        # prefix and the case): @{ 'mimo-pro' = 'opencode-go/deepseek-v4-pro' } would otherwise
        # replace a permitted alias's id with a blacklisted model, on every route.
        if ("$($ModelIds[$k])" -match '(?i)deepseek') {
            [Console]::Error.WriteLine("Refused: -ModelId '$k' maps to the DeepSeek model id '$($ModelIds[$k])'; $deepseekRejectMessage.")
            exit 1
        }
    }
}

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

    # --- T151 Done-when 1: Save-AttemptState keeps a failed attempt's work as a patch ----------
    # A throwaway git repository under the TEMP folder: an attempt with a staged change, an
    # unstaged change and an untracked file; the patch is saved, the tree reset and cleaned, and
    # the patch reapplied to the clean tree. No OpenCode, no network, no git stash.
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("ic2-impl-selftest-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    try {
        git init -q $tmp
        git -C $tmp config user.email selftest@local | Out-Null
        git -C $tmp config user.name selftest | Out-Null
        Set-Content -LiteralPath (Join-Path $tmp '.gitignore') -Value 'rendered/' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $tmp 'base.txt') -Value 'base' -Encoding utf8
        git -C $tmp add -A
        git -C $tmp commit -q -m base
        $startSha = (git -C $tmp rev-parse HEAD).Trim()
        # The attempt: a commit, then a staged change, an unstaged change and an untracked file.
        # (The commit can come first: the previous order committed the staged base.txt with it, so
        # there was no genuinely staged change left when Save-AttemptState ran.)
        Set-Content -LiteralPath (Join-Path $tmp 'other.txt') -Value 'other committed' -Encoding utf8
        git -C $tmp add other.txt; git -C $tmp commit -q -m other
        Set-Content -LiteralPath (Join-Path $tmp 'base.txt') -Value 'staged change' -Encoding utf8
        git -C $tmp add base.txt
        Set-Content -LiteralPath (Join-Path $tmp 'other.txt') -Value 'unstaged change' -Encoding utf8
        New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'rendered') | Out-Null
        Set-Content -LiteralPath (Join-Path $tmp 'untracked.txt') -Value 'untracked' -Encoding utf8
        Add 'DW1: the attempt really has a staged change before the save' ((git -C $tmp diff --cached --name-only).Trim() -eq 'base.txt')
        $patch = Save-AttemptState -Worktree $tmp -Model 'selftest-model' -StartSha $startSha
        Add 'DW1: a dirty attempt saves a patch under rendered/attempts/' ($patch.Status -eq 'saved' -and $patch.Patch -and (Test-Path -LiteralPath $patch.Patch) -and $patch.Patch -like '*rendered\attempts\selftest-model-*.patch')
        Add 'DW1: the patch holds the staged, the unstaged and the untracked change' ((Get-Content -Raw -LiteralPath $patch.Patch) -match 'staged change' -and (Get-Content -Raw -LiteralPath $patch.Patch) -match 'unstaged change' -and (Get-Content -Raw -LiteralPath $patch.Patch) -match 'untracked.txt')
        # The reset the next attempt would run: the patch (under git-ignored rendered/) survives.
        git -C $tmp reset -q --hard $startSha
        git -C $tmp clean -q -fd
        Add 'DW1: the reset and clean do not remove the saved patch' (Test-Path -LiteralPath $patch.Patch)
        git -C $tmp apply --whitespace=nowarn $patch.Patch
        Add 'DW1: git apply on the clean tree restores the staged change' ((Get-Content -Raw -LiteralPath (Join-Path $tmp 'base.txt')) -match 'staged change')
        Add 'DW1: git apply restores the unstaged change' ((Get-Content -Raw -LiteralPath (Join-Path $tmp 'other.txt')) -match 'unstaged change')
        Add 'DW1: git apply restores the untracked file' ((Get-Content -Raw -LiteralPath (Join-Path $tmp 'untracked.txt')) -match 'untracked')
        # A clean attempt saves nothing.
        git -C $tmp reset -q --hard $startSha; git -C $tmp clean -q -fd
        $cleanPatch = Save-AttemptState -Worktree $tmp -Model 'selftest-model' -StartSha $startSha
        Add 'DW1: a clean attempt reports Status clean and no patch' ($cleanPatch.Status -eq 'clean' -and $null -eq $cleanPatch.Patch)

        # --- R1: a failed save must NOT reset or clean the tree --------------------------------
        # Force the save to fail with a start commit git cannot resolve. Save-AttemptState must
        # report Status failed, and Reset-AttemptWorktree must refuse, leaving the attempt's
        # tracked and untracked state exactly as it is (the old code returned $null and reset).
        Set-Content -LiteralPath (Join-Path $tmp 'base.txt') -Value 'failed save keeps this' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $tmp 'untracked-failure.txt') -Value 'untracked failure' -Encoding utf8
        $failedSave = Save-AttemptState -Worktree $tmp -Model 'selftest-fail' -StartSha '0000000000000000000000000000000000000000'
        Add 'R1: a failed save reports Status failed (not a clean tree)' ($failedSave.Status -eq 'failed')
        $resetRan = Reset-AttemptWorktree -Worktree $tmp -StartSha $startSha -Save $failedSave
        Add 'R1: a failed save refuses the reset (Reset-AttemptWorktree returns false)' (-not $resetRan)
        Add 'R1: the failed attempt''s tracked change is still in the tree (no reset)' ((Get-Content -Raw -LiteralPath (Join-Path $tmp 'base.txt')) -match 'failed save keeps this')
        Add 'R1: the failed attempt''s untracked file is still in the tree (no clean)' (Test-Path -LiteralPath (Join-Path $tmp 'untracked-failure.txt'))
    } catch {
        Add 'DW1: the Save-AttemptState round-trip ran without error' $false
    } finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }

    # --- T151 Done-when 5 (bug #868): the exit texts ------------------------------------------------
    # R3: both leftWork exit-1 paths print the "Outside paths touched" report before exiting
    # (Get-LeftWorkExitText is what they print), keeping their exit code.
    $outsideRun = [pscustomobject]@{ Model = 'mimo-flash'; Run = [pscustomobject]@{ OutsidePathsKnown = $true; OutsidePaths = @('C:\Users\diego\AppData\Local\Temp\opencode') } }
    $leftWorkText = Get-LeftWorkExitText -Model 'mimo-flash' -Reason 'no session in 180 s' -Branch 'task/T99-x' -Log 'C:\work\ic2-work\T99.implementer.log' -OutsideRuns @($outsideRun)
    Add 'DW5 R3: the leftWork exit text carries the "Outside paths touched" report' ($leftWorkText -match 'Outside paths touched' -and $leftWorkText -like '*Temp\opencode (mimo-flash)*')
    # Bug #934: the report lists the outside calls the guard denied, which no longer end the run.
    $deniedRun = [pscustomobject]@{ Model = 'mm-m3'; Run = [pscustomobject]@{ OutsidePathsKnown = $true; OutsidePaths = @(); OutsideWritesDenied = @('edit {"filePath":"C:\\Users\\Users\\x.cs"}'); OutsideReadsDenied = @('Read C:/a') } }
    $deniedText = Format-OutsidePathsReport @($deniedRun)
    Add '#934: the outside-paths report lists a denied write and a denied read' ($deniedText -match 'denied write: edit' -and $deniedText -match 'denied read: Read C:/a')
    Add 'DW5 R3: the leftWork exit text keeps the original message and the log path' ($leftWorkText -like '*after committing, pushing or opening a PR*' -and $leftWorkText -like '*Log: C:\work\ic2-work\T99.implementer.log*')
    $leftWorkNone = Get-LeftWorkExitText -Model 'qwen-flash' -Reason 'exit 1' -Branch 'task/T99-x' -Log 'L' -OutsideRuns @()
    Add 'DW5 R3: no run at all still reports (unknown), never crashes' ($leftWorkNone -match 'unknown: no attempt returned a session')
    # R4: the same-cause exit names the class, both attempts' reasons, session ids and kept
    # files, and the run log, and says the substitute is not run.
    $a1 = [pscustomobject]@{ Model = 'mm-m3'; Why = 'no session in 180 s'; SessionId = 'ses_abc123'; KeptFiles = 'C:\w\rendered\attempts\mm-m3-20261009-120000.patch' }
    $a2 = [pscustomobject]@{ Model = 'qwen-flash'; Why = 'no session in 180 s'; SessionId = $null; KeptFiles = 'none (the tree was clean)' }
    $sameCauseText = Get-SameCauseExitText -Class 'no-session' -First $a1 -Second $a2 -Log 'C:\w\ic2-work\T99.implementer.log' -OutsideRuns @($outsideRun)
    Add 'DW5 R4: the same-cause text names the failure class' ($sameCauseText -match 'same failure class twice: no-session')
    Add 'DW5 R4: the same-cause text names both attempts and their reasons' ($sameCauseText -match 'mm-m3: no session in 180 s' -and $sameCauseText -match 'qwen-flash: no session in 180 s')
    Add 'DW5 R4: the same-cause text names each session id (and says unknown when there is none)' ($sameCauseText -match 'ses_abc123' -and $sameCauseText -match 'unknown \(no session came back\)')
    Add 'DW5 R4: the same-cause text names each attempt''s kept files' ($sameCauseText -match 'mm-m3-20261009-120000\.patch' -and $sameCauseText -match 'none \(the tree was clean\)')
    Add 'DW5 R4: the same-cause text names the run log and says the substitute is NOT run' ($sameCauseText -like '*run log: C:\w\ic2-work\T99.implementer.log*' -and $sameCauseText -match 'substitute attempt is NOT run')
    Add 'DW5 R4: the same-cause text carries the outside-paths report' ($sameCauseText -match 'Outside paths touched')

    # --- #908 rework (PR 930): DeepSeek is refused everywhere ---------------------------------------
    $dsReason = "DeepSeek is not used (the user's decision of 2026-10-09)"
    $selfPath = $PSCommandPath
    $probeBriefI = Join-Path ([System.IO.Path]::GetTempPath()) "ic2-implselftest-brief-$([guid]::NewGuid().ToString('N').Substring(0, 8)).md"
    Set-Content -LiteralPath $probeBriefI -Value 'self-test probe brief' -Encoding utf8
    try {
        # R2: a direct -Model deepseek-flash (and the other removed aliases) exits 1 with the reason.
        foreach ($a in 'deepseek-flash', 'deepseek-pro', 'ali-deepseek-pro', 'ali-deepseek-flash') {
            $o = (& pwsh -NoProfile -File $selfPath -Task T1 -Slug x -Issue 1 -BriefFile $probeBriefI -Model $a -WhatIf 2>&1 | Out-String)
            Add "R2: -Model $a exits 1 with '$dsReason' (got $LASTEXITCODE)" ($LASTEXITCODE -eq 1 -and $o -like "*$dsReason*")
        }
        $o = (& pwsh -NoProfile -File $selfPath -Task T1 -Slug x -Issue 1 -BriefFile $probeBriefI -Model no-such-alias -WhatIf 2>&1 | Out-String)
        Add "R2: -Model no-such-alias exits 1 and does not claim the DeepSeek reason (got $LASTEXITCODE)" ($LASTEXITCODE -eq 1 -and $o -notlike '*DeepSeek*')
        # R1: a DeepSeek id as the VALUE of an allowed key exits 1 with the reason, whatever the prefix or case.
        foreach ($v in 'opencode-go/deepseek-v4-pro', 'alibaba-token-plan/deepseek-v4.1-flash', 'zai-coding-plan/DeepSeek-V4-Pro', 'openai/deepseek-v4-pro-0813') {
            $cmd = "& '$selfPath' -Task T1 -Slug x -Issue 1 -BriefFile '$probeBriefI' -Model mimo-pro -ModelIds @{ 'mimo-pro' = '$v' } -WhatIf; exit `$LASTEXITCODE"
            $o = (& pwsh -NoProfile -Command $cmd 2>&1 | Out-String)
            Add "R1: -ModelIds @{ mimo-pro = '$v' } exits 1 with '$dsReason' (got $LASTEXITCODE)" ($LASTEXITCODE -eq 1 -and $o -like "*$dsReason*")
        }
        $cmd = "& '$selfPath' -Task T1 -Slug x -Issue 1 -BriefFile '$probeBriefI' -Model mimo-pro -ModelIds @{ 'deepseek-pro' = 'opencode-go/mimo-v2.6-pro' } -WhatIf; exit `$LASTEXITCODE"
        $o = (& pwsh -NoProfile -Command $cmd 2>&1 | Out-String)
        Add "R1: -ModelIds keyed deepseek-pro is still refused with exit 1 (got $LASTEXITCODE)" ($LASTEXITCODE -eq 1 -and $o -like "*$dsReason*")
    } finally { Remove-Item -LiteralPath $probeBriefI -Force -ErrorAction SilentlyContinue }

    # The chain-spent substitute (T150) never maps an opencode_go row to a DeepSeek alias: the
    # run that picked deepseek-flash from "provider opencode_go, model mimo-v2.6-pro" is the
    # case. The canned /recommend goes through the real Choose-Model.ps1 -Pick.
    $subDir = Join-Path ([System.IO.Path]::GetTempPath()) "ic2-implselftest-sub-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    New-Item -ItemType Directory -Force -Path $subDir | Out-Null
    try {
        function New-SubRow([string] $Provider, [string] $ModelId, $Score) { [ordered]@{ provider = $Provider; model = $ModelId; score = $Score; confidence = 'ok'; reasons = @('self-test'); usable = $true } }
        $recMimo = Join-Path $subDir 'mimo.json'
        (@{ note = $null; skipped = @(); ranking = @((New-SubRow 'minimax' 'MiniMax-M3' 900), (New-SubRow 'opencode_go' 'mimo-v2.6-pro' 500)) } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $recMimo -Encoding utf8
        $why = $null
        $sub = Get-SubstituteModel -ExcludeFamilies @('mm-m3') -Why ([ref]$why) -RecommendFile $recMimo -RecommendRetryWaitSec 0
        Add "#908 substitute: chain spent (mm-m3), opencode_go + mimo-v2.6-pro row -> mimo-pro, never a deepseek alias (got '$sub')" ($sub -eq 'mimo-pro' -and "$sub" -notmatch 'deepseek')
        $recDs = Join-Path $subDir 'ds.json'
        (@{ note = $null; skipped = @(); ranking = @((New-SubRow 'minimax' 'MiniMax-M3' 900), (New-SubRow 'opencode_go' 'deepseek-v4.1-flash' 700), (New-SubRow 'opencode_go' 'deepseek-v4-pro' 600), (New-SubRow 'alibaba' 'deepseek-v4-pro' 500)) } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $recDs -Encoding utf8
        $why2 = $null
        $sub2 = Get-SubstituteModel -ExcludeFamilies @('mm-m3') -Why ([ref]$why2) -RecommendFile $recDs -RecommendRetryWaitSec 0
        Add "#908 substitute: chain spent, only DeepSeek rows left -> none, never a deepseek alias (got '$sub2')" (-not $sub2)
        $recUnk = Join-Path $subDir 'unk.json'
        (@{ note = $null; skipped = @(); ranking = @((New-SubRow 'minimax' 'MiniMax-M3' 900), (New-SubRow 'opencode_go' 'mimo-future-9' 700)) } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $recUnk -Encoding utf8
        $sub3 = Get-SubstituteModel -ExcludeFamilies @('mm-m3') -Why ([ref]$why2) -RecommendFile $recUnk -RecommendRetryWaitSec 0
        Add "#908 substitute: an unknown opencode_go model is unmapped, so none is picked (got '$sub3')" (-not $sub3)
    } finally { Remove-Item -LiteralPath $subDir -Recurse -Force -ErrorAction SilentlyContinue }

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
        # Bug #934: the outside calls the guard denied, which no longer end the run.
        foreach ($w in @($r.Run.OutsideWritesDenied)) { if ($w) { $lines += "- denied write: $w ($($r.Model))" } }
        foreach ($d in @($r.Run.OutsideReadsDenied)) { if ($d) { $lines += "- denied read: $d ($($r.Model))" } }
    }
    if ($lines.Count -eq 0) { return 'none' }
    return ($lines -join "`n")
}

function Save-AttemptState {
    # T151 Done-when 1 (#854: T140's round-2 work was lost to the reset between attempts): a failed
    # attempt's uncommitted work is kept as a patch before the tree is reset for the next model.
    # `git add -A` stages every tracked change and untracked file (the attempt's own staged changes
    # included), then `git diff --cached --binary <start commit>` captures all of it, so
    # `git apply` on a clean tree at the start commit restores staged, unstaged and untracked work
    # alike. Never git stash (Appendix A: the stash is shared by every worktree). The patch lives
    # under rendered/attempts/ (git-ignored), so the next attempt's `git clean -fd` keeps it.
    # Returns a result object:
    #   Status 'clean'  -> there was nothing to keep; the reset is safe;
    #   Status 'saved'  -> Patch is the saved path; the reset is safe;
    #   Status 'failed' -> the save failed (R1): the caller must NOT reset or clean.
    param([string] $Worktree, [string] $Model, [string] $StartSha)
    $dirty = git -C $Worktree status --porcelain
    if (-not $dirty) { return [pscustomobject]@{ Status = 'clean'; Patch = $null; Error = $null } }
    $dir = Join-Path $Worktree 'rendered/attempts'
    try { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    catch { return [pscustomobject]@{ Status = 'failed'; Patch = $null; Error = "could not create $dir for the patch" } }
    $patch = Join-Path $dir "$Model-$(Get-Date -Format 'yyyyMMdd-HHmmss').patch"
    $n = 1
    while (Test-Path -LiteralPath $patch) { $patch = Join-Path $dir "$Model-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$n.patch"; $n++ }
    git -C $Worktree add -A
    git -C $Worktree diff --cached --binary $StartSha "--output=$patch"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $patch)) {
        $msg = "could not save $Model's uncommitted work as a patch (git diff --cached --binary failed)"
        Write-Warning "$msg; the worktree is left as it is (no reset, no clean)."
        return [pscustomobject]@{ Status = 'failed'; Patch = $null; Error = $msg }
    }
    Write-Host "kept $Model's uncommitted work as patch: $patch"
    return [pscustomobject]@{ Status = 'saved'; Patch = $patch; Error = $null }
}

function Reset-AttemptWorktree {
    # The reset and clean that ready the worktree for the next model after a failed attempt.
    # R1: when Save-AttemptState failed, this refuses -- the failed attempt's state is left exactly
    # as it is. Returns $true when the tree was reset, $false when it was deliberately kept.
    param([string] $Worktree, [string] $StartSha, [object] $Save)
    if ($Save -and $Save.Status -eq 'failed') { return $false }
    git -C $Worktree reset -q --hard $StartSha
    git -C $Worktree clean -q -fd
    return $true
}

function Get-LeftWorkExitText {
    # T151 Done-when 5 (bug #868 R3): the two leftWork exit-1 paths (a model failed after
    # committing, pushing or opening a PR) print the "Outside paths touched" report
    # (Format-OutsidePathsReport) before they exit, keeping their exit code. Factored into a
    # builder so the self-test can prove the report is part of what the exit prints.
    param([string] $Model, [string] $Reason, [string] $Branch, [string] $Log, [object[]] $OutsideRuns)
    $report = Format-OutsidePathsReport $OutsideRuns
    return "$Model failed ($Reason) after committing, pushing or opening a PR on $Branch; not retrying on another model. The main session decides. Log: $Log`nOutside paths touched:`n$report"
}

function Get-SameCauseExitText {
    # T151 Done-when 5 (bug #868 R4; the user's decision of 2026-10-08: "stop and diagnose the
    # problem"): two consecutive attempts failed with one failure class, so OpenCode itself is the
    # problem (operating-guide §3). The script stops for diagnosis -- it does NOT run T150's
    # substitute (it fell through to one until T151) -- prints the failure class, both attempts'
    # reasons, each attempt's session id and kept files, the run log and the outside-paths
    # report, and exits 3.
    param([string] $Class, [object] $First, [object] $Second, [string] $Log, [object[]] $OutsideRuns)
    $report = Format-OutsidePathsReport $OutsideRuns
    $sid = { param($a) if ($a.SessionId) { $a.SessionId } else { 'unknown (no session came back)' } }
    $text = @(
        "same failure class twice: $Class -- OpenCode itself is the problem (operating-guide §3).",
        'Stopping for diagnosis; the substitute attempt is NOT run (the user''s decision of 2026-10-08); the main session diagnoses before any retry.',
        "  $($First.Model): $($First.Why); session id: $(& $sid $First); kept files: $($First.KeptFiles)",
        "  $($Second.Model): $($Second.Why); session id: $(& $sid $Second); kept files: $($Second.KeptFiles)",
        "run log: $Log"
    ) -join "`n"
    return $text + "`nOutside paths touched:`n$report"
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
# #575). The main session chooses the implementer model from /recommend (CLAUDE.md rule 17)
# and passes it as -Model; on 2026-10-09 DeepSeek was blacklisted (the user's decision) and
# MiMo v2.6 Pro / Flash took its place (#908). GLM left the default implementer side in #573
# (GLM-5.3 ended T99's run early, mid-exploration, with no error, #557), so glm, glm-flash and
# luna stay valid only as explicit -Model values, and so does qwen-flash (-AllowAlibaba / the
# user's decision).
# MiMo Pro and MiMo Flash stay in the table (tested on implement, fix and review tasks on
# 2026-10-09). Confirm the ids with `opencode models` on first use; -ModelIds overrides any
# of them.
$models = @{
    'luna'       = 'openai/gpt-5.6-luna'
    'glm-flash'  = 'zai-coding-plan/glm-5.3-flash'
    'glm'        = 'zai-coding-plan/glm-5.3'
    'mimo-pro'   = 'opencode-go/mimo-v2.6-pro'
    'mimo-flash' = 'opencode-go/mimo-v2.6-flash'
    # Qwen3.8 Flash on the Alibaba Token Plan (the user's decision of 2026-10-05).
    'qwen-flash' = 'alibaba-token-plan/qwen3.8-flash'
    # MiniMax (minimax.io Token Plan, provider `minimax`) and the Alibaba Token Plan's GLM
    # under a name of its own (the owner's decision of 2026-10-06; DeepSeek is no longer on
    # the plan, #908). Explicit -Model values only.
    'mm-m3'   = 'minimax/MiniMax-M3'
    'mm-m2.7' = 'minimax/MiniMax-M2.7'
    'ali-glm' = 'alibaba-token-plan/glm-5.3'
}
# The Alibaba Token Plan route of the same models (-Route; the user's decision of 2026-10-05,
# 2026-10-09: DeepSeek is gone, so the AliDeepSeek* ids are gone too).
$alibabaIds = @{
    'glm' = 'alibaba-token-plan/glm-5.3'
}
if ($ModelIds) { foreach ($k in $ModelIds.Keys) { $models[$k] = $ModelIds[$k] } }
# Provider-specific variant. Invoke-OpenCodeWatched passes it by the CLI's major version (1.x
# `--variant v`, 2.x the model's `#v` suffix). Empty means none. Effort is `high` everywhere (issue #575: `max`
# is overkill); luna was already high.
# Heavy models run light (the user's decision of 2026-10-05): glm at low (GLM-5.3 has no medium).
# qwen-flash at medium: Qwen3.8 Flash offers low, medium and xhigh, no high.
$variants = @{ 'luna' = 'high'; 'glm-flash' = 'high'; 'glm' = 'low'; 'qwen-flash' = 'medium'; 'mm-m3' = 'thinking'; 'mm-m2.7' = ''; 'ali-glm' = 'low'; 'mimo-pro' = ''; 'mimo-flash' = '' }

# -SelfTest short-circuits the run path: offline checks only (R3 rework). No worktree, no
# OpenCode, no PR. Use it to verify Format-ImplementerAttempt end-to-end without billing.
# Runs BEFORE the chain is built so the chooser is never called from a SelfTest run.
if ($SelfTest) {
    $failed = Invoke-ImplementerSelfTest
    if ($failed -gt 0) { exit 1 } else { exit 0 }
}

# #893: every run names its model; the main session chose it from /recommend and logged the
# reasons on the task's issue (CLAUDE.md rule 17). There is no `auto` chain any more.
if (-not $Model) {
    throw 'Give -Model <alias>, chosen from quota-tracker''s /recommend: pwsh scripts/Choose-Model.ps1 -Role implementer (or -Role pair). -Model auto was removed by #893.'
}
$chain = @($Model)
# $chainMeta: alias name -> /recommend meta. Only the substitute (Get-SubstituteModel) brings
# meta now; the named model's reasons are on the task's issue.
$chainMeta = @{}
# The Claude Sonnet fallback sentinel the chooser emits when nothing else has a positive
# score (rule 17, the orchestrator). The dispatch recognises it and exits 3 with the
# Claude Sonnet fallback message (the main session runs Claude Sonnet, operating-guide §3).
$script:ChooserClaudeSentinel = '__CLAUDE_SONNET_FALLBACK__'
# Each chain model's route: quota-tracker's /avoid is read once (-Route auto); a silent tracker keeps
# the usual route. A -Model the route does not serve is refused.
$quota = Get-QuotaAvoid
$resolved = @{}
$kept = @()
# Done-when 2: every model this run tried, with its route and its failure class, for the PR body's
# "Implementer attempts" section.
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
    if ($r.Refused) { [Console]::Error.WriteLine("Refused: ${m}: $($r.Why)."); exit 1 }
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
        # whole list as one string. T151 Done-when 5: an entry may be `model=class` to name its
        # failure class; two consecutive entries with one class are the same-cause stop, shown
        # here (the diagnosis text and exit 3) with no substitute, exactly as the real run would.
        $failed = @($SimulateFailed | ForEach-Object { @([string]$_) -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $sim = @($failed | ForEach-Object {
                $parts = @($_ -split '=', 2)
                [pscustomobject]@{ Model = $parts[0]; Class = if ($parts.Count -gt 1) { $parts[1] } else { "simulated-$_" } }
            })
        $stopIdx = -1
        for ($i = 1; $i -lt $sim.Count; $i++) { if ($sim[$i].Class -eq $sim[$i - 1].Class) { $stopIdx = $i; break } }
        Write-Host "simulate failed: $($failed -join ', ')"
        if ($stopIdx -ge 0) {
            $simFirst = $sim[$stopIdx - 1]
            $simSecond = $sim[$stopIdx]
            $diag = Get-SameCauseExitText -Class $simSecond.Class `
                -First ([pscustomobject]@{ Model = $simFirst.Model; Why = "simulated failure ($($simFirst.Class))"; SessionId = $null; KeptFiles = "the patch Save-AttemptState would write (simulated)" }) `
                -Second ([pscustomobject]@{ Model = $simSecond.Model; Why = "simulated failure ($($simSecond.Class))"; SessionId = $null; KeptFiles = "the patch Save-AttemptState would write (simulated)" }) `
                -Log $log -OutsideRuns @()
            Write-Host $diag
            Write-Host 'WhatIf: the run would stop here and exit 3 (same failure class twice; no substitute).'
            exit 3
        }
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
        if ($subWithout -and $subWithout -ne $subPick) { Write-Host "without OpenAI exclusion, would have picked: $subWithout ($whyWithout)" }
        if ($subPick) { Write-Host "would substitute: $subPick ($subWhy)" }
        else { Write-Host "would substitute: none ($subWhy)" }
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
# behind: no new commit, locally or on origin, and no new PR. Its uncommitted edits are saved
# as a patch under rendered/attempts/ before the reset (T151 Done-when 1), never discarded.
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
# T151 Done-when 5: a same-cause pair no longer breaks the loop; it exits 3 from inside it
# (Get-SameCauseExitText), so there is no $sameCause to carry to the bottom's exit 3.
$implementedBy = $null
# The finishing run's object, for the "Outside paths touched" section (Done-when 3).
$finalRun = $null
# Done-when 3 (GLM's re-check R2): every attempt that returned a run, failed or not, is kept here,
# so a failed run's touched outside paths are reported and "unknown" means only a failed export.
$outsideRuns = @()
[System.IO.File]::WriteAllText($log, '')

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
        $attempts += [pscustomobject]@{ Model = $m; Route = $resolved[$m].Route; Class = 'ran'; Why = ''; SessionId = if ($run) { $run.SessionId } else { $null }; KeptFiles = '-' }
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
    # OpenCode itself is the problem, not the model (operating-guide §3).
    $class = Get-OpenCodeFailureClass $reason
    $sessionId = if ($run) { $run.SessionId } else { $null }
    git -C $repo fetch -q origin
    $prNow = gh pr list --head $branch --state open --json number --jq '.[0].number' 2>$null
    $leftWork = (git -C $worktree rev-parse HEAD) -ne $startSha -or
        ((git -C $repo rev-parse "origin/$branch" 2>$null) -ne $startRemote) -or
        ($prNow -and $prNow -ne $startPr)
    if ($leftWork) {
        # T151: the attempt is recorded (session id and kept files) before the exit, and the exit
        # prints the "Outside paths touched" report (bug #868 R3) while keeping its exit code.
        # Nothing is reset here: the model left work behind, so the tree keeps it.
        $attempts += [pscustomobject]@{ Model = $m; Route = $resolved[$m].Route; Class = $class; Why = $reason; SessionId = $sessionId; KeptFiles = "uncommitted work kept in the worktree ($worktree)" }
        [Console]::Error.WriteLine((Get-LeftWorkExitText -Model $m -Reason $reason -Branch $branch -Log $log -OutsideRuns $outsideRuns))
        exit 1
    }
    # T151 Done-when 1: keep the failed attempt's uncommitted work as a patch before the reset
    # (the patch lives under git-ignored rendered/attempts/, so the reset and clean below keep it).
    $save = Save-AttemptState -Worktree $worktree -Model $m -StartSha $startSha
    $keptFiles = switch ($save.Status) {
        'saved' { $save.Patch }
        'failed' { "uncommitted work kept in the worktree ($worktree); the patch could not be saved" }
        default { 'none (the tree was clean)' }
    }
    $attempts += [pscustomobject]@{ Model = $m; Route = $resolved[$m].Route; Class = $class; Why = $reason; SessionId = $sessionId; KeptFiles = $keptFiles }
    if ($save.Status -eq 'failed') {
        # R1: if the save failed, do NOT reset or clean. Stop the run with a clear message and a
        # non-zero exit, leaving the tree exactly as it is for diagnosis.
        [Console]::Error.WriteLine("$($save.Error); refusing to reset or clean $worktree -- the attempt's state is left as it is. The main session diagnoses. Log: $log")
        exit 4
    }
    if ($class -eq $lastClass) {
        # T151 Done-when 5 (bug #868 R4; the user's decision of 2026-10-08: "stop and diagnose
        # the problem"): a second consecutive attempt failed with the same class, so OpenCode
        # itself is the problem. Stop for diagnosis: no substitute attempt (the script fell
        # through to one until T151, restoring operating-guide §3's rule), no reset -- the
        # failed attempt's work stays in the tree and in the patch above -- and exit 3.
        $prev = $attempts[-2]
        $curr = $attempts[-1]
        [Console]::Error.WriteLine((Get-SameCauseExitText -Class $class -First $prev -Second $curr -Log $log -OutsideRuns $outsideRuns))
        exit 3
    }
    $null = Reset-AttemptWorktree -Worktree $worktree -StartSha $startSha -Save $save
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
                    $attempts += [pscustomobject]@{ Model = $substitute; Route = $sr.Route; Class = 'ran'; Why = ''; SessionId = if ($run) { $run.SessionId } else { $null }; KeptFiles = '-' }
                    $finalRun = $run
                } else {
                    Write-Warning "$substitute failed: $reason"
                    $failures += "${substitute}: $reason"
                    $subSessionId = if ($run) { $run.SessionId } else { $null }
                    git -C $repo fetch -q origin
                    $prNow = gh pr list --head $branch --state open --json number --jq '.[0].number' 2>$null
                    $leftWork = (git -C $worktree rev-parse HEAD) -ne $startSha -or
                        ((git -C $repo rev-parse "origin/$branch" 2>$null) -ne $startRemote) -or
                        ($prNow -and $prNow -ne $startPr)
                    if ($leftWork) {
                        # T151 (bug #868 R3): record the attempt, print the outside-paths report, keep exit 1.
                        $attempts += [pscustomobject]@{ Model = $substitute; Route = $sr.Route; Class = (Get-OpenCodeFailureClass $reason); Why = $reason; SessionId = $subSessionId; KeptFiles = "uncommitted work kept in the worktree ($worktree)" }
                        [Console]::Error.WriteLine((Get-LeftWorkExitText -Model $substitute -Reason $reason -Branch $branch -Log $log -OutsideRuns $outsideRuns))
                        exit 1
                    }
                    # T151 Done-when 1: the substitute's failed attempt keeps its uncommitted work as a
                    # patch before the reset too.
                    $subSave = Save-AttemptState -Worktree $worktree -Model $substitute -StartSha $startSha
                    $subKeptFiles = switch ($subSave.Status) {
                        'saved' { $subSave.Patch }
                        'failed' { "uncommitted work kept in the worktree ($worktree); the patch could not be saved" }
                        default { 'none (the tree was clean)' }
                    }
                    $attempts += [pscustomobject]@{ Model = $substitute; Route = $sr.Route; Class = (Get-OpenCodeFailureClass $reason); Why = $reason; SessionId = $subSessionId; KeptFiles = $subKeptFiles }
                    if ($subSave.Status -eq 'failed') {
                        # R1: do not reset or clean when the substitute's save failed.
                        [Console]::Error.WriteLine("$($subSave.Error); refusing to reset or clean $worktree -- the attempt's state is left as it is. The main session diagnoses. Log: $log")
                        exit 4
                    }
                    $null = Reset-AttemptWorktree -Worktree $worktree -StartSha $startSha -Save $subSave
                }
            }
        }
    }
}
if (-not $implementedBy) {
    # Not Write-Error: under ErrorActionPreference Stop it would end the script with exit 1, not 3.
    $why = if ($chainGoneReason) { $chainGoneReason }
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
