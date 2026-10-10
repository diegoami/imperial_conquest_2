<#
.SYNOPSIS
    Dispatch aliases for quota-tracker's pick, pair and ranking (CLAUDE.md rule 17).
.DESCRIPTION
    No model policy lives here. The response's ranking is already in rank order; do not
    re-sort it. Unknown provider+model aliases cannot run in these dispatch scripts and
    are reported before taking the next row. Family comes from the service, not an alias list.
    ReviewTier and ExcludeFamily are passed through as review_tier and exclude_family.
    AfterRank resumes a ranking only after a provider outage (rule 21), never a process failure.
    RecommendFile supplies an offline response; SelfTest never dispatches OpenCode.
#>
[CmdletBinding()]
param(
    [ValidateSet('implementer', 'reviewer', 'pair')] [string] $Role,
    [ValidateSet('heavy', 'light')] [string] $Weight = 'heavy',
    [ValidateSet('heavy', 'light')] [string] $ReviewTier,
    [string] $ExcludeFamily,
    [int] $AfterRank = 0,
    [string] $ImplementerFamily,
    [switch] $Pick,
    [string] $RecommendFile,
    [string] $RecommendUrl = 'http://localhost:8765/recommend',
    [int] $RecommendRetryWaitSec = 180,
    [int] $RecommendPollSec = 5,
    [switch] $SelfTest
)
$ErrorActionPreference = 'Stop'

# Capability map only: provider AND model identify the exact dispatch alias.
# Claude commands are run by the main session, not these OpenCode dispatchers.
$ModelAliases = @{
    'openai|gpt-6.1-sol' = @{ implementer = 'sol'; reviewer = 'sol' }
    'openai|gpt-5.6-luna' = @{ implementer = 'luna'; reviewer = 'luna' }
    'minimax|MiniMax-M3' = @{ implementer = 'mm-m3'; reviewer = 'mm-m3' }
    'minimax|MiniMax-M2.7' = @{ implementer = 'mm-m2.7'; reviewer = 'mm-m2.7' }
    'zai|glm-5.3' = @{ implementer = 'glm'; reviewer = 'glm' }
    'zai|glm-5.3-flash' = @{ implementer = 'glm-flash'; reviewer = 'glm-flash' }
    'opencode_go|mimo-v2.6-pro' = @{ implementer = 'mimo-pro'; reviewer = 'mimo-pro' }
    'opencode_go|mimo-v2.6-flash' = @{ implementer = 'mimo-flash'; reviewer = 'mimo-flash' }
    'alibaba|qwen3.8-max' = @{ reviewer = 'qwen' }
    'alibaba|qwen3.8-flash' = @{ implementer = 'qwen-flash'; reviewer = 'qwen-flash' }
    'alibaba|glm-5.3' = @{ implementer = 'ali-glm'; reviewer = 'ali-glm' }
    'openrouter|nvidia/nemotron-3-ultra-550b-a55b:free' = @{ reviewer = 'nemotron' }
    'openrouter|cohere/north-mini-code:free' = @{ reviewer = 'north-mini' }
    'openrouter|thinkingmachines/inkling:free' = @{ reviewer = 'inkling' }
    'openrouter|poolside/laguna-s-2.1:free' = @{ reviewer = 'laguna' }
}

function Get-RecommendUrl([string] $Url, [string] $Tier, [string] $ReviewTier, [string] $ExcludeFamily) {
    $query = @('tier=' + [uri]::EscapeDataString($Tier))
    if ($ReviewTier) { $query += 'review_tier=' + [uri]::EscapeDataString($ReviewTier) }
    if ($ExcludeFamily) { $query += 'exclude_family=' + [uri]::EscapeDataString($ExcludeFamily) }
    $separator = if ($Url.Contains('?')) { '&' } else { '?' }
    return $Url + $separator + ($query -join '&')
}

function Get-Recommendation {
    param([string] $File, [string] $Url, [int] $WaitSec, [int] $PollSec)
    if ($File) { return (Get-Content -Raw -LiteralPath $File | ConvertFrom-Json) }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($WaitSec)
    while ($true) {
        try { $response = Invoke-RestMethod -Uri $Url -TimeoutSec 5 -ErrorAction Stop }
        catch { throw "Service did not answer. Run systemctl --user restart quota-tracker in WSL and retry; if it still does not answer, tell the owner. $($_.Exception.Message)" }
        if ($response.note -match 'loading' -and @($response.ranking).Count -eq 0) {
            if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'Statistics still loading; retry, then tell the owner. No model chosen.' }
            Start-Sleep -Seconds $PollSec
        } else { return $response }
    }
}

function Convert-ModelRow($Row, [string] $Role) {
    if (-not $Row) { return $null }
    $alias = $null
    $key = "$($Row.provider)|$($Row.model)"
    if ($ModelAliases.ContainsKey($key)) { $alias = $ModelAliases[$key][$Role] }
    [pscustomobject]@{
        Alias = $alias; Provider = $Row.provider; Model = $Row.model; Family = $Row.family
        Rank = $Row.rank; Outlook = $Row.outlook; Score = $Row.score
        CoversPct = $Row.covers_pct; ReliabilityFactor = $Row.reliability.factor
        Reasons = @($Row.reasons); Command = $Row.command; Confidence = $Row.confidence
    }
}

function Resolve-Choice {
    param($Recommend, [string] $Role, $Preferred, [int] $AfterRank = 0, [string] $SkipFamily)
    $passed = [System.Collections.Generic.List[string]]::new()
    $chosen = $null
    # The preferred pick/pair is authoritative, even when it is not the first ranking row.
    # Fallback starts AFTER that row's rank; it never restarts at rank 1.
    $cursor = $AfterRank
    if ($Preferred -and $AfterRank -eq 0) {
        $row = Convert-ModelRow $Preferred $Role
        if ($row.Alias) { $chosen = $row }
        else {
            $passed.Add("rank $($row.Rank): no $Role alias for $($row.Provider)/$($row.Model); cannot run here, use its command in the main session or the next row")
            $cursor = [int]$row.Rank
        }
    }
    if (-not $chosen) {
        foreach ($candidate in @($Recommend.ranking)) {
            if ([int]$candidate.rank -le $cursor) { continue }
            $row = Convert-ModelRow $candidate $Role
            if ($SkipFamily -and $row.Family -eq $SkipFamily) {
                $passed.Add("rank $($row.Rank): family $($row.Family) is the pair implementer's family")
                continue
            }
            if (-not $row.Alias) {
                $passed.Add("rank $($row.Rank): no $Role alias for $($row.Provider)/$($row.Model); cannot run here")
                continue
            }
            $chosen = $row
            break
        }
    }
    [pscustomobject]@{ Chosen = $chosen; Passed = @($passed); Role = $Role }
}

function Resolve-Pair($Recommend) {
    $impl = Resolve-Choice -Recommend $Recommend -Role implementer -Preferred $Recommend.pair.implementer
    if (-not $impl.Chosen) { return [pscustomobject]@{ Implementer = $impl; Reviewer = $null } }
    $review = Resolve-Choice -Recommend $Recommend -Role reviewer -Preferred $Recommend.pair.reviewer -SkipFamily $impl.Chosen.Family
    [pscustomobject]@{ Implementer = $impl; Reviewer = $review }
}

function Format-Row($Row) {
    "rank $($Row.Rank), alias $($Row.Alias), provider $($Row.Provider), model $($Row.Model), family $($Row.Family), outlook $($Row.Outlook), score $($Row.Score), covers_pct $($Row.CoversPct), reliability.factor $($Row.ReliabilityFactor), reasons: $($Row.Reasons -join '; ')"
}

function Format-Pick($Row) {
    $meta = [ordered]@{
        name = $Row.Alias; provider = $Row.Provider; model = $Row.Model; family = $Row.Family
        rank = $Row.Rank; outlook = $Row.Outlook; score = $Row.Score; covers_pct = $Row.CoversPct
        reliability_factor = $Row.ReliabilityFactor; reasons = $Row.Reasons; command = $Row.Command
        confidence = $Row.Confidence
    }
    'CHOOSER_META=' + (ConvertTo-Json -Compress -Depth 8 $meta)
    $Row.Alias
}

function Get-ChoiceExitCode($Chosen) { if ($Chosen) { return 0 }; return 3 }

function Invoke-ChooserSelfTest {
    $checks = [System.Collections.Generic.List[object]]::new()
    function Check([string] $Name, [bool] $Ok) { $checks.Add([pscustomobject]@{ Name = $Name; Ok = $Ok }) }
    function Row($Rank, $Provider, $Model, $Family, $Factor = 1) {
        [pscustomobject]@{ rank = $Rank; provider = $Provider; model = $Model; family = $Family
            outlook = 'tight'; score = 0; covers_pct = 50; reliability = @{ factor = $Factor }
            reasons = @('test reason'); command = 'test command' }
    }
    $first = Row 1 minimax MiniMax-M3 minimax
    $second = Row 2 opencode_go mimo-v2.6-pro mimo 0.5
    $third = Row 3 claude opus claude
    $rec = [pscustomobject]@{ ranking = @($first, $second, $third); pick = $first; skipped = @(); pair = @{ implementer = $first; reviewer = $second } }
    $r = Resolve-Choice $rec implementer $rec.pick
    Check 'all tight, Claude third: rank 1 chosen' ($r.Chosen.Rank -eq 1 -and $r.Chosen.Alias -eq 'mm-m3')
    $r = Resolve-Choice $rec implementer $null 1
    Check 'reliability 0.5 rank 2 stays second' ($r.Chosen.Rank -eq 2 -and $r.Chosen.ReliabilityFactor -eq 0.5)
    $empty = [pscustomobject]@{ ranking = @(); pick = $null; skipped = @(@{ provider = 'minimax'; why = 'exhausted' }) }
    $r = Resolve-Choice $empty implementer $empty.pick
    Check 'all skipped: no chosen row and exit 3' (-not $r.Chosen -and (Get-ChoiceExitCode $r.Chosen) -eq 3)
    $same = Row 2 openai gpt-5.6-luna vendor-family
    $impl = Row 1 openai gpt-6.1-sol vendor-family
    $unknown = Row 2 unknown unknown other
    $next = Row 3 minimax MiniMax-M3 alternate-family
    $pairRec = [pscustomobject]@{ ranking = @($impl, $same, $next); pair = @{ implementer = $impl; reviewer = $unknown } }
    $pair = Resolve-Pair $pairRec
    # Preferred unknown at rank 2 -> rank 3, then explicitly exercise family skipping.
    Check 'pair unknown reviewer maps to next rank' ($pair.Reviewer.Chosen.Rank -eq 3)
    $unknown.rank = 1
    $pair = Resolve-Pair $pairRec
    Check 'pair reviewer fallback skips service implementer family' ($pair.Reviewer.Chosen.Alias -eq 'mm-m3' -and ($pair.Reviewer.Passed -join ';') -match 'vendor-family')
    $url = Get-RecommendUrl 'http://localhost:8765/recommend' heavy light 'family with space'
    Check 'request passes review_tier and exclude_family' ($url -eq 'http://localhost:8765/recommend?tier=heavy&review_tier=light&exclude_family=family%20with%20space')
    $rec.pick = $second
    $r = Resolve-Choice $rec implementer $rec.pick
    Check 'pick authoritative, not recalculated from rank 1' ($r.Chosen.Rank -eq 2)
    $rec.pair.implementer = $second
    $rec.pair.reviewer = $first
    $pair = Resolve-Pair $rec
    Check 'pair authoritative, not reconstructed' ($pair.Implementer.Chosen.Rank -eq 2 -and $pair.Reviewer.Chosen.Rank -eq 1)
    $luna = Row 1 openai gpt-5.6-luna openai
    Check 'Luna has implementer alias without a tier exclusion' ((Convert-ModelRow $luna implementer).Alias -eq 'luna')
    $ali = Row 1 alibaba glm-5.3 glm
    Check 'Alibaba alias has no gate' ((Convert-ModelRow $ali implementer).Alias -eq 'ali-glm')
    $unknown = Row 1 minimax future-model minimax
    Check 'unknown model never mapped by provider alone' (-not (Convert-ModelRow $unknown implementer).Alias)
    $line = Format-Row (Convert-ModelRow $second reviewer)
    Check 'report contains every required row field' ($line -match 'rank 2, alias mimo-pro, provider opencode_go, model mimo-v2.6-pro, family mimo, outlook tight, score 0, covers_pct 50, reliability.factor 0.5, reasons: test reason')
    # Exercise actual process exit, using worktree-local scratch only.
    $scratch = Join-Path (Split-Path $PSScriptRoot -Parent) 'rendered/choose-model-selftest.json'
    try {
        $empty | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $scratch -Encoding utf8
        & pwsh -NoProfile -File $PSCommandPath -Role implementer -RecommendFile $scratch *> $null
        Check 'all skipped process exits 3' ($LASTEXITCODE -eq 3)
    } finally { Remove-Item -LiteralPath $scratch -ErrorAction SilentlyContinue }
    foreach ($c in $checks) { "$(if ($c.Ok) { 'PASS' } else { 'FAIL' }): $($c.Name)" }
    return @($checks | Where-Object { -not $_.Ok }).Count
}

if ($SelfTest) {
    $output = @(Invoke-ChooserSelfTest)
    $failures = $output[-1]
    $output | Select-Object -SkipLast 1 | Write-Output
    exit $(if ($failures -eq 0) { 0 } else { 1 })
}
if (-not $Role) { throw 'Give -Role implementer, reviewer or pair.' }
$url = Get-RecommendUrl $RecommendUrl $Weight $ReviewTier $ExcludeFamily
try { $recommend = Get-Recommendation -File $RecommendFile -Url $url -WaitSec $RecommendRetryWaitSec -PollSec $RecommendPollSec }
catch { [Console]::Error.WriteLine($_.Exception.Message); exit 3 }

# Per-row evidence is printed even in pair mode. Pick keeps the alias as its final line
# for dispatchers; all rows and skipped reasons remain in the captured run output.
"request: $url"
foreach ($row in @($recommend.ranking)) {
    if ($Role -eq 'pair') {
        'implementer row: ' + (Format-Row (Convert-ModelRow $row implementer))
        'reviewer row: ' + (Format-Row (Convert-ModelRow $row reviewer))
    } else { Format-Row (Convert-ModelRow $row $Role) }
}
foreach ($skipped in @($recommend.skipped)) {
    $why = if ($skipped.why) { $skipped.why } else { $skipped.reasons }
    "skipped: $($skipped.provider) $($skipped.model) -- $($why -join '; ')"
}
if ($Role -eq 'pair') {
    $pair = Resolve-Pair $recommend
    foreach ($part in 'Implementer', 'Reviewer') {
        $result = $pair.$part
        foreach ($why in $result.Passed) { "cannot run: $why" }
        if ($result.Chosen) { "${part}: " + (Format-Row $result.Chosen) }
        else { "${part}: none; tell the owner" }
    }
    exit $(if ($pair.Implementer.Chosen -and $pair.Reviewer.Chosen) { 0 } else { 3 })
}
$result = Resolve-Choice -Recommend $recommend -Role $Role -Preferred $recommend.pick -AfterRank $AfterRank -SkipFamily $ImplementerFamily
foreach ($why in $result.Passed) { "cannot run: $why" }
if (-not $result.Chosen) { 'chosen: none; tell the owner'; exit 3 }
if ($Pick) { Format-Pick $result.Chosen } else { 'chosen: ' + (Format-Row $result.Chosen) }
exit 0
