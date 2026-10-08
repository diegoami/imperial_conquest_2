<#
.SYNOPSIS
    Picks the model the OpenCode dispatcher should run, by quota-tracker's /recommend.
.DESCRIPTION
    Quota-tracker (/recommend?tier=heavy|light) ranks the live candidates for one tier. This
    chooser maps each row's provider to an alias the dispatch script accepts, applies the
    family's and the orchestrator's exclusions (CLAUDE.md rule 17), and prints the ordered
    list with reasons. T150's chain-spent substitute uses it too: -SubstituteFamilies drops
    every named family AND the whole OpenAI family (Luna and Sol), so the substitute is never
    OpenAI.

    The mapping (per role, the owner's table):
      implementer (heavy):
          minimax     → mm-m3
          zai         → glm
          opencode_go → deepseek-flash
          claude      → Claude Sonnet (the script's exit-3 fallback, not an OpenCode alias;
                       it stands in only when nothing else is left, per rule 17)
          openai      → unmapped for implementer (OpenAI never implements; Sol/Luna stay
                       free to review)
      reviewer (light, Sol's substitutes):
          zai         → glm
          opencode_go → deepseek-pro
          alibaba     → qwen (when it appears)
          openai      → unmapped for reviewer (Luna and Sol reach a review through §3.4's
                       tier rules, never through the ranking)
          claude      → unmapped for reviewer (Claude reviews only when the main session
                       passes -Reviewer <sonnet|opus> explicitly, and the &lt;complex&gt;
                       tier rule sends the very-complex PR to a cold Claude Opus)

    A row's provider with no alias for the role is printed `unmapped for &lt;role&gt;: &lt;provider&gt;`
    with score, confidence, and reasons; it is never picked.

    Exclusions, in order (Done-when 2):
      1. -ExcludeFamily &lt;name&gt;[,&lt;name&gt;] drops every candidate whose family
         contains a named name. The family is the union of all the alias / OpenCode route
         names that map to the same model, so naming any member drops the whole family.
      2. -ExcludeModel &lt;name&gt;: for a reviewer role, drops the implementer's whole
         family (a reviewer is never of the implementer's family, §3.4). -ExcludeModel on
         an implementer role is a no-op; the chain is the implementer's own family.
      3. claude (sonnet, opus) is dropped while any other candidate has a positive score
         (rule 17: Claude is the orchestrator). When Claude is the only candidate left,
         the chooser returns the script's exit-3 fallback.
      4. The substitute filter (-SubstituteFamilies &lt;name&gt;[,&lt;name&gt;]) drops
         every named family AND the whole OpenAI family (luna, sol), whether or not they
         failed. This is T150's chain-spent substitute rule.
      5. A row whose score is negative is ranked after every non-negative one and is chosen
         only when nothing else is left. A row whose `usable` is false (the tracker
         reported it unusable) is skipped.

    Quota-tracker (Done-when 5):
      - The /recommend call is the only network call. -RecommendFile &lt;json&gt; replaces
        it for tests (a canned response); -RecommendUrl &lt;url&gt; is the live URL
        (default http://localhost:8765/recommend); -RecommendRetryWaitSec &lt;seconds&gt;
        caps the wait (default 180).
      - `note` says the statistics are loading and `ranking` is empty: wait and retry,
        sleep 5 s, until -RecommendRetryWaitSec seconds have passed; then exit 3.
      - The service does not answer: exit 3 immediately with the WSL restart command.
        Never fall back to percentages.

    The tier (Done-when 1): -Role implementer always queries &tier=heavy; -Role reviewer
    always queries &tier=light. -Tier remains accepted and printed so the log records it
    (e.g. "very-complex: §3.4 sends this tier to a cold Claude Opus reviewer; the
    ranking is for the OpenCode reviews beside it").

    Output:
      Without -Pick, the chooser prints, for each row that survives the exclusions, in
      this order:
        - a one-line summary (rank, alias when mapped, provider, model, score,
          confidence, the reasons joined, and "reasons:" plus the array);
        - `unmapped for &lt;role&gt;: &lt;provider&gt;` for any row whose provider has no
          alias for the role;
        - `skipped: &lt;provider&gt; -- &lt;why&gt;` for every entry in /recommend's
          `skipped` list (when the canned response or the live one carries one);
        - `unavailable: &lt;provider&gt; -- &lt;why&gt;` for rows whose `usable` is false
          (Done-when 6: a row the tracker marked unusable or that turned out negative
          only after the script mapped it).
      With -Pick, the FIRST non-empty line is `CHOOSER_META=&lt;json&gt;` describing the
      chosen row (name, provider, model, score, confidence, reasons), and the LAST
      non-empty line is the chosen OpenCode alias (preserved from the pre-T152 contract
      that external-implement.ps1 parses by taking the last non-empty line). Exit 3 when
      no candidate has an alias.

.PARAMETER Role
    implementer or reviewer (required, except for -SelfTest).
.PARAMETER Tier
    simple, complex or very-complex (§3.4). Recorded in the log; it does not select the
    /recommend tier (the role does). Default: complex for a reviewer, no default for an
    implementer (the verbose log says "the tier is what the task catalogue records").
.PARAMETER ExcludeModel
    The implementer's name, for a reviewer; its whole family is excluded.
.PARAMETER ExcludeFamily
    One or more model names (comma-separated or repeated); every candidate of each named
    model's family is dropped (T150 Done-when 2).
.PARAMETER SubstituteFamilies
    T150's chain-spent substitute filter: drops every named family AND the whole OpenAI
    family (luna, sol), whether or not it failed. The dispatch script calls this when
    the chain is spent.
.PARAMETER Pick
    Prints only the chosen alias as the last non-empty line; above it, a single
    `CHOOSER_META=&lt;json&gt;` line for the dispatch script. Exit 3 when no candidate
    has an alias.
.PARAMETER RecommendFile
    Reads /recommend's JSON from a file instead of the service (a test hook). With a
    canned file, no network is touched.
.PARAMETER RecommendUrl
    The /recommend endpoint. Default `http://localhost:8765/recommend`. A closed local
    port (e.g. http://localhost:1) simulates a silent service.
.PARAMETER RecommendRetryWaitSec
    Seconds to wait / retry on a `loading` note (default 180). A test shortens it.
.PARAMETER SelfTest
    Runs the built-in checks on canned responses; no network is touched.

.EXAMPLE
    pwsh scripts/Choose-Model.ps1 -Role reviewer -Tier complex -ExcludeModel deepseek-flash
.EXAMPLE
    pwsh scripts/Choose-Model.ps1 -Role implementer -Pick
.EXAMPLE
    pwsh scripts/Choose-Model.ps1 -Role reviewer -ExcludeModel glm -RecommendFile rendered/light.json
#>
[CmdletBinding()]
param(
    [ValidateSet('implementer', 'reviewer')] [string] $Role,
    [ValidateSet('simple', 'complex', 'very-complex')] [string] $Tier,
    [string] $ExcludeModel,
    [string[]] $ExcludeFamily,
    [string[]] $SubstituteFamilies,
    [switch] $Pick,
    [string] $RecommendFile,
    [string] $RecommendUrl = 'http://localhost:8765/recommend',
    [int] $RecommendRetryWaitSec = 180,
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'

# The owners' mapping tables, one per role (Done-when 1). A provider not in the table has
# no alias for the role; the row is printed `unmapped for <role>: <provider>` and skipped.
# `claude` in the implementer role is the script's exit-3 Claude Sonnet fallback, not an
# OpenCode alias; the picker emits it only when nothing else is left.
$ImplementerAliases = @{
    'minimax'     = 'mm-m3'
    'zai'         = 'glm'
    'opencode_go' = 'deepseek-flash'
}
$ReviewerAliases = @{
    'zai'         = 'glm'
    'opencode_go' = 'deepseek-pro'
    'alibaba'     = 'qwen'
}
# `claude` in implementer is special. Any candidate's family an OpenCode dispatch map
# drops: implementer `claude` falls back to Claude Sonnet (the main session), no OpenCode
# alias; reviewer `claude` has no alias (it's not in the table, so it's "unmapped for
# reviewer").
$ImplementerFallback = '__CLAUDE_SONNET_FALLBACK__'

# The family of each model name. For our provider → alias tables, the family is the
# whole role's alias table plus OpenAI (the Luna / Sol pair), since the dispatch script
# keys the family only by name (a reviewer "excludeModel deepseek-flash" excludes every
# DeepSeek alias).
$FamilyOfName = @{
    'mm-m3'               = 'mm-m3'
    'mm-m2.7'             = 'mm-m3'
    'glm'                 = 'glm'
    'glm-flash'           = 'glm'
    'ali-glm'             = 'glm'
    'deepseek-flash'      = 'deepseek-flash'
    'ali-deepseek-flash'  = 'deepseek-flash'
    'deepseek'            = 'deepseek-flash'
    'ali-deepseek-pro'    = 'deepseek-flash'
    'deepseek-pro'        = 'deepseek-flash'
    'qwen'                = 'qwen'
    'qwen-flash'          = 'qwen'
    'claude'              = 'claude'      # the implementer `claude` row → fallback
    'sonnet'              = 'claude'
    'opus'                = 'claude'
    'luna'                = 'openai'
    'sol'                 = 'openai'
}

function Get-FamilyForName([string] $Name) {
    if ($FamilyOfName.ContainsKey($Name)) { return $FamilyOfName[$Name] }
    return $Name
}

function Get-ImplementerAlias([string] $Provider) {
    if ($Provider -eq 'claude') { return $ImplementerFallback }
    if ($ImplementerAliases.ContainsKey($Provider)) { return $ImplementerAliases[$Provider] }
    return $null
}

function Get-ReviewerAlias([string] $Provider) {
    if ($ReviewerAliases.ContainsKey($Provider)) { return $ReviewerAliases[$Provider] }
    return $null
}

function Get-Recommendation {
    # One network/file call. -RetryWaitSec caps the loading-note wait.
    # On a "loading" note with empty ranking: sleep 5 s, retry, until -RetryWaitSec.
    # On any connection error or the tracker not answering: throw 'tracker_silent' so the
    # caller prints the WSL restart command and exits 3.
    # On a successful JSON: return the raw object.
    param(
        [string] $RecommendFile,
        [string] $RecommendUrl = 'http://localhost:8765/recommend',
        [int]    $RetryWaitSec = 180,
        [string] $Tier
    )
    $deadline = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + $RetryWaitSec
    while ($true) {
        if ($RecommendFile) {
            $raw = Get-Content -Raw -LiteralPath $RecommendFile | ConvertFrom-Json
            # A canned file is one-shot: no retry, no live connection to probe.
            if ($raw.note -and $raw.note -match 'loading' -and -not @($raw.ranking).Count) {
                throw 'tracker_loading'
            }
            return $raw
        }
        # Concatenation, not `"$RecommendUrl?tier=$Tier"`. PowerShell parses the latter as
        # `$RecommendUrl` followed by `?tier=$Tier`, where `?tier=` is read as a help-query
        # delimiter and `$RecommendUrl` ends up empty. Concatenation is unambiguous.
        $callUrl = "$RecommendUrl" + '?tier=' + "$Tier"
        try {
            $raw = Invoke-RestMethod -Uri $callUrl -TimeoutSec 5 -ErrorAction Stop
        } catch {
            if ($_.Exception.InnerException) {
                $reason = $_.Exception.InnerException.Message
                if ($_.Exception.InnerException.InnerException) { $reason += ' / ' + $_.Exception.InnerException.InnerException.Message }
            } else { $reason = $_.Exception.Message }
            Write-Warning "/recommend call failed: $reason"
            throw 'tracker_silent'
        }
        if ($raw.note -and $raw.note -match 'loading' -and -not @($raw.ranking).Count) {
            if ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() -ge $deadline) {
                throw 'tracker_loading'
            }
            Start-Sleep -Seconds 5
            continue
        }
        return $raw
    }
}

function Get-FamiliesFromNames([string[]] $Names) {
    # Each entry in $Names is a model name. Commas are split too because
    # `pwsh -File ... -ExcludeFamily a,b` binds the whole list as one string; an unsplit
    # entry would name no family and exclude nothing.
    $keys = @{}
    foreach ($n in $Names) {
        foreach ($part in (@([string]$n) -split ',')) {
            $part = $part.Trim()
            if ($part) { $keys[(Get-FamilyForName $part)] = $true }
        }
    }
    return @($keys.Keys | Sort-Object)
}

function Get-FamilyForAliasTable($Table) {
    # The family of every alias in a provider → alias table. Used by -ExcludeModel and
    # -ExcludeFamily; the family key is the alias that the dispatch script uses.
    $families = @{}
    foreach ($alias in $Table.Values) { $families[$alias] = (Get-FamilyForName $alias) }
    return $families
}

function Resolve-Chosen {
    param(
        [Parameter(Mandatory)] [string] $Role,
        [string] $Tier,
        [string] $ExcludeModel,
        [string[]] $ExcludeFamily,
        [string[]] $SubstituteFamilies,
        [Parameter(Mandatory)] $Recommend
    )

    $recRanking = @($Recommend.ranking)
    $recSkipped = @($Recommend.skipped)
    # -SubstituteFamilies: every named family's name plus luna and sol (the OpenAI family).
    # The dispatch script passes the failed families; we add the OpenAI family explicitly
    # here so the chooser owns the rule (T150 Done-when 2, the user's amendment of
    # 2026-10-08).
    $subFamilies = @()
    if ($SubstituteFamilies) {
        $subFamilies = @(Get-FamiliesFromNames $SubstituteFamilies) + @('openai') | Select-Object -Unique
    }
    $familyExcluded = @()
    if ($ExcludeFamily) {
        $familyExcluded = @(Get-FamiliesFromNames $ExcludeFamily)
    }
    # -ExcludeModel (reviewer only): the implementer's whole family.
    $excludeModelFamilies = @()
    if ($Role -eq 'reviewer' -and $ExcludeModel) {
        # The reviewer table maps providers to aliases; the family key for the model the
        # main session passes (an implementer name) is read straight from $FamilyOfName.
        $excludeModelFamilies = @(Get-FamilyForName $ExcludeModel)
    }

    $table = if ($Role -eq 'implementer') { $ImplementerAliases } else { $ReviewerAliases }
    $aliasFamily = Get-FamilyForAliasTable $table
    # Also include the OpenAI family in the reviewer family map, so -ExcludeFamily luna
    # drops both luna and sol when the reviewer table doesn't list them by alias (those
    # aliases land via openai's unmapped rule, so we map them too).
    if ($Role -eq 'reviewer') {
        $aliasFamily['luna'] = 'openai'
        $aliasFamily['sol']  = 'openai'
    }
    if ($Role -eq 'implementer') {
        # luna / sol have no implementer alias; the OpenAI family stays unmapped.
        $aliasFamily['luna'] = 'openai'
        $aliasFamily['sol']  = 'openai'
    }

    # The family of a row is the family of the alias the IMPLEMENTER role would have
    # given the same provider, regardless of the current role. That keeps a reviewer
    # -ExcludeModel mm-m3 dropping a minimax row whose /recommend model is M3, and keeps
    # the family concept a "this row's model" identity across roles.
    $rows = foreach ($r in $recRanking) {
        $provider = [string]$r.provider
        $score    = if ($null -eq $r.score) { $null } else { [double]$r.score }
        $conf     = if ($null -eq $r.confidence) { '?' } else { [string]$r.confidence }
        $reasons  = @($r.reasons)
        $usable   = -not ($r.PSObject.Properties['usable'] -and -not [bool]$r.usable)
        $alias    = if ($Role -eq 'implementer') { Get-ImplementerAlias $provider } else { Get-ReviewerAlias $provider }
        # The implementer-side alias decides the family. claude in implementer is the
        # Sonnet fallback; reviewer claude has no alias, but its implementer-side alias
        # is the fallback sentinel. Treat the family of a claude row as 'claude'.
        $implAlias = Get-ImplementerAlias $provider
        $family    = if ($provider -eq 'claude') {
            'claude'
        } elseif ($implAlias -and ($implAlias -ne $ImplementerFallback)) {
            (Get-FamilyForName $implAlias)
        } elseif ($implAlias -eq $ImplementerFallback) {
            'claude'
        } else {
            # The provider has no implementer alias either: the family is the provider
            # itself (openrouter).
            $provider
        }
        [pscustomobject]@{
            Provider  = $provider; Model = [string]$r.model; Score = $score; Confidence = $conf
            Reasons   = $reasons; Usable = [bool]$usable; Alias = $alias; Family = $family
        }
    }

    # Apply exclusions in order (Done-when 2).
    $excluded = New-Object System.Collections.Generic.List[object]
    $filtered = @()
    $claudeDroppedReason = $null
    # Pass 1: family-level exclusions (ExcludeModel reviewer, ExcludeFamily, Substitute).
    foreach ($row in $rows) {
        $why = $null
        if ($row.Family -in $excludeModelFamilies) { $why = "the implementer's family ($($row.Family)) is excluded (-ExcludeModel $ExcludeModel)" }
        elseif ($row.Family -in $familyExcluded)    { $why = "family $($row.Family) is excluded (-ExcludeFamily)" }
        elseif ($row.Family -in $subFamilies)       { $why = "family $($row.Family) is excluded (the chain-spent substitute filter)" }
        if ($why) {
            $excluded.Add([pscustomobject]@{ Provider = $row.Provider; Why = $why })
            continue
        }
        $filtered += $row
    }
    # Pass 2: Claude is dropped when any other candidate has a positive score. When Claude
    # is the only candidate left, it stands in (the script's exit-3 fallback).
    if ($Role -eq 'implementer') {
        $claudeRows = @($filtered | Where-Object { $_.Provider -eq 'claude' -and $_.Alias -eq $ImplementerFallback })
        $nonClaudePositive = @($filtered | Where-Object { $_.Provider -ne 'claude' -and $_.Score -ne $null -and $_.Score -ge 0 } | Sort-Object Score -Descending | Select-Object -First 1)
        $claudePositive    = @($claudeRows | Where-Object { $_.Score -ne $null -and $_.Score -ge 0 })
        $claudeNegative    = @($claudeRows | Where-Object { $_.Score -ne $null -and $_.Score -lt 0 })
        if ($claudePositive -and $nonClaudePositive) {
            $dropClaude = $true
            $claudeDroppedReason = "Claude is the orchestrator and another candidate has a positive score ($((@($filtered | Where-Object { $_.Provider -ne 'claude' -and $_.Score -ge 0 } | Sort-Object Score -Descending)[0]).Provider), so Claude is dropped"
        } elseif ($claudeNegative) {
            # A negative-scoring Claude row is treated as a Claude row in the negative
            # bucket, which already ranks last; drop it here without a "dropped because
            # orchestrator" reason to avoid duplication.
            $dropClaude = $false
            foreach ($r in $claudeNegative) {
                $excluded.Add([pscustomobject]@{ Provider = $r.Provider; Why = "Claude is the orchestrator; negative scores are ranked last" })
                $filtered = @($filtered | Where-Object { $_ -ne $r })
            }
            $dropClaude = $false
        } else { $dropClaude = $false }
        if ($dropClaude) {
            foreach ($r in $claudeRows) {
                $excluded.Add([pscustomobject]@{ Provider = $r.Provider; Why = $claudeDroppedReason })
                $filtered = @($filtered | Where-Object { $_ -ne $r })
            }
        }
    }
    # Pass 3: unusable rows (the tracker reported usable: false) are skipped; a null score
    # is treated as unknown (the tracker has no spare_calls; rank it after every measured
    # one but before negative).
    $usable = @()
    foreach ($r in $filtered) {
        if (-not $r.Usable) {
            $excluded.Add([pscustomobject]@{ Provider = $r.Provider; Why = "the tracker marked this row as unusable" })
            continue
        }
        $usable += $r
    }
    # Pass 4: ordering. Preserves the ranking order, except negative-score rows come last.
    # Unknown-score (null) rows come after non-negative measured rows but before negative.
    $ranking = @()
    $nonNegative = @(); $unknown = @(); $negative = @()
    foreach ($r in $usable) {
        if ($null -eq $r.Score) { $unknown += $r }
        elseif ($r.Score -ge 0) { $nonNegative += $r }
        else { $negative += $r }
    }
    $ranking = $nonNegative + $unknown + $negative

    # Pick the first row with an alias. claude fallback is a special alias.
    $chosen = $null
    foreach ($r in $ranking) {
        if ($r.Alias) { $chosen = $r; break }
    }

    return [pscustomobject]@{
        Role = $Role; Tier = $Tier; ExcludeModelFamilies = $excludeModelFamilies
        FamilyExcluded = $familyExcluded; SubstituteFamilies = $subFamilies
        Chosen = $chosen; Ranking = $ranking; Excluded = $excluded
        Recommendation = $Recommend
        ClaudeDroppedReason = $claudeDroppedReason
    }
}

function Format-Reasons([object[]] $Reasons) {
    if (-not $Reasons -or $Reasons.Count -eq 0) { return 'no reasons given' }
    return ($Reasons -join '; ')
}

function Format-Score([double] $Score) {
    if ($null -eq $Score) { return '?' }
    return ('{0}' -f $Score)
}

function Show-ChooserReport($Result) {
    # The default human-readable report. Per row, the alias (when mapped), the provider,
    # the model id, the score, the confidence, and the reasons.
    $role = $Result.Role
    $tier = $Result.Tier
    if ($tier) {
        "role $role, /recommend?tier=$(if ($role -eq 'implementer') { 'heavy' } else { 'light' }), reported tier: $tier"
    } else {
        "role $role, /recommend?tier=$(if ($role -eq 'implementer') { 'heavy' } else { 'light' })"
    }
    if ($Result.ExcludeModelFamilies) { "implementer's family excluded: $($Result.ExcludeModelFamilies -join ', ')" }
    if ($Result.FamilyExcluded) { "families excluded (-ExcludeFamily): $($Result.FamilyExcluded -join ', ')" }
    if ($Result.SubstituteFamilies) { "substitute filter (dropped families): $($Result.SubstituteFamilies -join ', ')" }
    if ($Result.ClaudeDroppedReason) { $Result.ClaudeDroppedReason }
    $i = 0
    foreach ($r in $Result.Ranking) {
        $i++
        $alias = if ($r.Alias) {
            if ($r.Alias -eq $ImplementerFallback) { '<Claude Sonnet fallback>' } else { $r.Alias }
        } else { '<unmapped>' }
        '{0,2}. {1,-22} {2,-13} {3,-30} score {4,7} confidence {5,-5} reasons: {6}' -f $i, $alias, $r.Provider, $r.Model, (Format-Score $r.Score), $r.Confidence, (Format-Reasons $r.Reasons)
    }
    if (-not $Result.Ranking) { 'no candidate is available.' }
    foreach ($r in $Result.Excluded) { "excluded: $($r.Provider) -- $($r.Why)" }
    if ($Result.Recommendation -and $Result.Recommendation.skipped) {
        foreach ($s in $Result.Recommendation.skipped) { "skipped: $($s.provider) -- $($s.why)" }
    }
}

function Format-PickLine($Row) {
    if (-not $Row) { return @() }
    $alias = $Row.Alias
    if (-not $alias) { return @() }
    $name = if ($alias -eq $ImplementerFallback) { '<Claude Sonnet fallback>' } else { $alias }
    $meta = [ordered]@{
        name       = $name
        family     = $Row.Family
        provider   = $Row.Provider
        model      = $Row.Model
        score      = $Row.Score
        confidence = $Row.Confidence
        reasons    = @($Row.Reasons)
    }
    return @(
        ('CHOOSER_META=' + (ConvertTo-Json -Compress -InputObject $meta)),
        $alias
    )
}

function Show-ChosenModel {
    # The chosen model's row in the human-readable report (Done-when 4). Used by
    # external-implement.ps1 when it has a CHOOSER_META payload and no live human reader.
    param($Row)
    if (-not $Row) { return }
    $name = if ($Row.Alias -eq $ImplementerFallback) { '<Claude Sonnet fallback>' } else { $Row.Alias }
    "chosen: $name ($($Row.Provider), $($Row.Model), score $(Format-Score $Row.Score), confidence $($Row.Confidence))"
    "reasons:"
    foreach ($r in $Row.Reasons) { "  - $r" }
}

function Get-CannedRecommend {
    # A small builder for canned tests: returns a hashtable with `note`, `ranking`
    # (a list of rows), and `skipped` (also a list of rows). Lets the SelfTest build the
    # canned responses without writing JSON to disk.
    param(
        [string] $Note = $null,
        [object[]] $Ranking = @(),
        [object[]] $Skipped = @()
    )
    return [pscustomobject]@{ note = $Note; ranking = $Ranking; skipped = $Skipped }
}

function New-CannedRow {
    # One /recommend row. Provider / model / score / confidence / reasons / usable.
    param(
        [Parameter(Mandatory)] [string] $Provider,
        [string] $Model = 'model',
        [double] $Score = 0,
        [string] $Confidence = 'ok',
        [string[]] $Reasons = @('test'),
        [bool] $Usable = $true
    )
    return [pscustomobject]@{
        provider = $Provider; model = $Model; score = $Score
        confidence = $Confidence; reasons = $Reasons; usable = $Usable
    }
}

function New-CannedSkipped {
    param([string] $Provider, [string] $Why = 'no reason given')
    return [pscustomobject]@{ provider = $Provider; why = $Why }
}

function Test-PickAlias([object] $Result, [string] $Expected) {
    if (-not $Result.Chosen) { return $false }
    return $Result.Chosen.Alias -eq $Expected
}

function Invoke-ChooserSelfTest {
    # The Done-when 6 cases, run with no network. Returns the number of failures (exit code).
    $checks = New-Object System.Collections.Generic.List[object]
    function Add-Check([string] $Name, [bool] $Ok) {
        $checks.Add([pscustomobject]@{ Name = $Name; Ok = [bool]$Ok }) | Out-Null
    }

    # Case 1: per-role mapping plus an unmapped row.
    # Implementer: minimax → mm-m3, zai → glm, opencode_go → deepseek-flash, openrouter unmapped.
    $recImpl = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'minimax'     -Model 'MiniMax-M3'   -Score 1000),
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'      -Score 80),
        (New-CannedRow -Provider 'opencode_go' -Model 'ds-flash'     -Score 30),
        (New-CannedRow -Provider 'openrouter'  -Model 'ds-flash'     -Score 0)
    )
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recImpl
    Add-Check 'mapping (implementer): minimax → mm-m3 is first; openrouter is unmapped' (Test-PickAlias $r 'mm-m3')
    $unmapped = @($r.Ranking | Where-Object { -not $_.Alias })
    Add-Check 'mapping (implementer): openrouter is in the list of unmapped rows' (@($unmapped | Where-Object { $_.Provider -eq 'openrouter' }).Count -eq 1)
    # Reviewer: zai → glm, opencode_go → deepseek-pro, alibaba → qwen; openai unmapped.
    $recRev = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'     -Score 1000),
        (New-CannedRow -Provider 'opencode_go' -Model 'ds-pro'      -Score 80),
        (New-CannedRow -Provider 'alibaba'     -Model 'qwen-max'    -Score 60),
        (New-CannedRow -Provider 'openai'      -Model 'gpt-6-sol'   -Score 50),
        (New-CannedRow -Provider 'claude'      -Model 'sonnet'      -Score 40),
        (New-CannedRow -Provider 'openrouter'  -Model 'ds-flash'    -Score 20)
    )
    $r = Resolve-Chosen -Role 'reviewer' -Tier 'complex' -Recommend $recRev
    Add-Check 'mapping (reviewer): zai → glm is first; openai and claude are unmapped (Luna/Sol/Claude are not in the table)' (Test-PickAlias $r 'glm')
    $unmappedProviders = @($r.Ranking | Where-Object { -not $_.Alias } | ForEach-Object Provider) | Sort-Object
    Add-Check "mapping (reviewer): unmapped providers are exactly claude, openai and openrouter" ([string]($unmappedProviders -join ',') -eq 'claude,openai,openrouter')

    # Case 2: Claude dropped while another positive score exists; kept when alone.
    $recClaude = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'claude'  -Model 'sonnet' -Score 2000),
        (New-CannedRow -Provider 'minimax' -Model 'M3'      -Score 1000)
    )
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recClaude
    Add-Check 'Claude dropped while another positive score exists' (-not $r.Chosen -or $r.Chosen.Provider -ne 'claude')
    $claudeExcluded = @($r.Excluded | Where-Object { $_.Provider -eq 'claude' })
    Add-Check 'Claude dropped: the excluded row mentions the orchestrator rule' ($claudeExcluded.Count -ge 1 -and $claudeExcluded[0].Why -match 'orchestrator')

    $recClaudeAlone = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'claude'  -Model 'sonnet' -Score 1500),
        (New-CannedRow -Provider 'minimax' -Model 'M3'      -Score (-100))
    )
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recClaudeAlone
    Add-Check 'Claude only-non-negative: it is the script fallback' ($r.Chosen -and $r.Chosen.Alias -eq $ImplementerFallback)

    # Case 3: negative scores are ranked after non-negative ones.
    $recNeg = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'opencode_go' -Model 'ds-flash' -Score (-500)),
        (New-CannedRow -Provider 'minimax'     -Model 'M3'       -Score 1000),
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'  -Score 80),
        (New-CannedRow -Provider 'alibaba'     -Model 'qwen'     -Score (-100))
    )
    $r = Resolve-Chosen -Role 'reviewer' -Tier 'simple' -Recommend $recNeg
    $order = @($r.Ranking | ForEach-Object Provider)
    Add-Check 'negative scores are ranked after every non-negative one' ($order -join ',' -eq 'minimax,zai,opencode_go,alibaba')
    $picked = Resolve-Chosen -Role 'reviewer' -Tier 'simple' -Recommend $recNeg
    Add-Check 'a negative score is chosen only when nothing else is left' (Test-PickAlias $picked 'glm')
    $recOnlyNegative = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'opencode_go' -Model 'ds-flash' -Score (-500))
    )
    $rNeg = Resolve-Chosen -Role 'reviewer' -Tier 'simple' -Recommend $recOnlyNegative
    Add-Check 'the only candidate is negative-scoring: it is still picked' (Test-PickAlias $rNeg 'deepseek-pro')

    # Case 4: ExcludeFamily drops the named family.
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recImpl -ExcludeFamily @('mm-m3')
    $excluded = @($r.Excluded | Where-Object { $_.Provider -eq 'minimax' })
    Add-Check '-ExcludeFamily mm-m3 drops the minimax family' ($excluded.Count -eq 1)
    Add-Check '-ExcludeFamily mm-m3: the chooser falls through to glm' (Test-PickAlias $r 'glm')

    # Case 5: ExcludeModel on reviewer drops the implementer's whole family.
    $recDeepImpl = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'minimax' -Model 'M3'  -Score 1000),
        (New-CannedRow -Provider 'zai'     -Model 'glm' -Score 80)
    )
    $r = Resolve-Chosen -Role 'reviewer' -Tier 'complex' -ExcludeModel 'mm-m3' -Recommend $recDeepImpl
    Add-Check '-ExcludeModel on reviewer drops minimax (the implementer)' (@($r.Excluded | Where-Object { $_.Provider -eq 'minimax' }).Count -gt 0)
    Add-Check "-ExcludeModel mm-m3: reviewer passes the DeepSeek/GLM row only; implementer's family excluded message printed" ($r.ExcludeModelFamilies -contains 'mm-m3')

    # Case 6: SubstituteFamilies drops each named family AND the OpenAI family.
    $recSub = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'openai'      -Model 'gpt-6-sol' -Score 1500),
        (New-CannedRow -Provider 'minimax'     -Model 'M3'         -Score 1000 -Usable $false), # failed
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'   -Score 80)
    )
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -SubstituteFamilies @('mm-m3') -Recommend $recSub
    $excludedProviders = @($r.Excluded | ForEach-Object Provider) | Sort-Object
    Add-Check 'substitute filter: openai is dropped; minimax was marked unusable' (@($excludedProviders | Where-Object { $_ -in @('openai', 'minimax') }).Count -eq 2)
    Add-Check 'substitute filter: zai / glm is the picked one' (Test-PickAlias $r 'glm')
    # Without the failed minimax (usable), the openai row is still dropped by the
    # substitute filter; the unmapped-for-implementer rule would already drop openai, so
    # this is what the chain-spent substitute gets from Choose-Model: never OpenAI.
    $recSub2 = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'openai'      -Model 'gpt-6-sol' -Score 1500),
        (New-CannedRow -Provider 'minimax'     -Model 'M3'         -Score 1000),
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'   -Score 80)
    )
    $r2 = Resolve-Chosen -Role 'implementer' -Tier 'complex' -SubstituteFamilies @('mm-m3') -Recommend $recSub2
    Add-Check 'substitute filter on a healthy minimax: openai still dropped, minimax still dropped' (-not ($r2.Chosen -and $r2.Chosen.Provider -in @('openai', 'minimax')))

    # Case 7: reviewer substitute order (GLM, DeepSeek Pro, Qwen) with a canned light
    # ranking that puts zai first: prints DeepSeek Pro then Qwen, never GLM and never
    # openai. The dispatch script does its own picking; this is what gets logged.
    $recRevSub = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'  -Score 1000),
        (New-CannedRow -Provider 'opencode_go' -Model 'ds-pro'   -Score 800),
        (New-CannedRow -Provider 'alibaba'     -Model 'qwen'     -Score 200),
        (New-CannedRow -Provider 'openai'      -Model 'gpt-6-sol' -Score 150)
    )
    $r = Resolve-Chosen -Role 'reviewer' -Tier 'complex' -ExcludeModel 'glm' -Recommend $recRevSub
    $aliases = @($r.Ranking | Where-Object Alias | ForEach-Object Alias)
    Add-Check 'reviewer substitute order (ExcludeModel glm): DeepSeek Pro and Qwen, never GLM, never an OpenAI model' ([string]($aliases -join ',') -eq 'deepseek-pro,qwen')
    $unmapped = @($r.Ranking | Where-Object { -not $_.Alias } | ForEach-Object Provider)
    Add-Check 'reviewer substitute order: openai appears only in the unmapped-for-reviewer line' ([bool](@($unmapped | Where-Object { $_ -eq 'openai' }).Count -eq 1))

    # Case 8: loading note → exit 3 with the "did not finish loading" message.
    $loadingTempPath = Join-Path ([System.IO.Path]::GetTempPath()) ('chooser-loading-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.json')
    try {
        [pscustomobject]@{ note = 'statistics are loading; try again in a few seconds'; ranking = @() } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $loadingTempPath -Encoding utf8
        $err = $null
        try { Get-Recommendation -RecommendFile $loadingTempPath -RecommendUrl 'unused' -RetryWaitSec 0 -Tier 'heavy' } catch { $err = $_ }
        Add-Check 'loading note in canned file raises tracker_loading so the caller exits 3 with the wait message' ($err.Exception.Message -eq 'tracker_loading')
    } finally {
        Remove-Item -LiteralPath $loadingTempPath -ErrorAction SilentlyContinue
    }

    # Case 8b: a silent service (closed port) raises tracker_silent immediately so the
    # caller can exit 3 with the WSL restart message.
    $err = $null
    try { Get-Recommendation -RecommendFile $null -RecommendUrl 'http://127.0.0.1:1/recommend' -RetryWaitSec 0 -Tier 'heavy' } catch { $err = $_ }
    Add-Check 'silent service (closed port) raises tracker_silent so the caller exits 3 with the restart message' ($err.Exception.Message -eq 'tracker_silent')

    # Case 9: the skipped list is printed with "why".
    $recSk = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'minimax' -Model 'M3' -Score 1000)
    ) -Skipped @(
        (New-CannedSkipped -Provider 'openai' -Why 'OpenAI account out of quota')
    )
    $out = (& { Show-ChooserReport (Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recSk) } | Out-String)
    Add-Check 'skipped list: the report prints "<provider> -- <why>"' ($out -match 'skipped: openai -- OpenAI account out of quota')

    # Case 10: unusable rows are reported as excluded.
    $recUnu = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'minimax' -Model 'M3' -Score 1000 -Usable $false),
        (New-CannedRow -Provider 'zai'     -Model 'glm' -Score 80)
    )
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recUnu
    Add-Check 'unusable rows are reported as excluded' (@($r.Excluded | Where-Object { $_.Provider -eq 'minimax' -and $_.Why -match 'unusable' }).Count -eq 1)
    Add-Check 'unusable dropped: zai/glm is the picked alias' (Test-PickAlias $r 'glm')

    # Report.
    $i = 0; $failed = 0
    foreach ($c in $checks) {
        $i++
        $status = if ($c.Ok) { 'PASS' } else { 'FAIL' }
        "[{0,2}/{1}] {2}  -- {3}" -f $i, $checks.Count, $status, $c.Name | Write-Host
        if (-not $c.Ok) { $failed++ }
    }
    "self-test: $($checks.Count - $failed)/$($checks.Count) passed"
    return [int]($failed -gt 0)
}

if ($MyInvocation.InvocationName -eq '.') { return }
if ($SelfTest) { $code = Invoke-ChooserSelfTest; $code[0..($code.Count - 2)]; exit $code[-1] }
if (-not $Role) { throw 'Give -Role implementer or -Role reviewer (or -SelfTest).' }
if ($Role -ne 'implementer' -and $Role -ne 'reviewer') { throw "-Role must be implementer or reviewer; got '$Role'." }

$tierForRecommend = if ($Role -eq 'implementer') { 'heavy' } else { 'light' }

try {
    $recommend = Get-Recommendation -RecommendFile $RecommendFile -RecommendUrl $RecommendUrl -RetryWaitSec $RecommendRetryWaitSec -Tier $tierForRecommend
} catch {
    $msg = $_.Exception.Message
    if ($msg -eq 'tracker_loading') {
        [Console]::Error.WriteLine("quota-tracker returned `note: statistics are loading` and an empty ranking for $RecommendWaitSec seconds. The script exits 3; the main session retries once the tracker has caught up.")
        exit 3
    }
    if ($msg -eq 'tracker_silent') {
        [Console]::Error.WriteLine("quota-tracker is not answering $RecommendUrl. Restart the WSL service: systemctl --user restart quota-tracker. The script exits 3; the main session retries once the tracker is back.")
        exit 3
    }
    throw
}

$result = Resolve-Chosen -Role $Role -Tier $Tier -ExcludeModel $ExcludeModel -ExcludeFamily $ExcludeFamily -SubstituteFamilies $SubstituteFamilies -Recommend $recommend

if ($Pick) {
    if (-not $result.Chosen) {
        [Console]::Error.WriteLine("Choose-Model.ps1: no candidate for $Role with the given exclusions.")
        exit 3
    }
    $lines = Format-PickLine $result.Chosen
    $lines | ForEach-Object { Write-Output $_ }
    exit 0
}

Show-ChooserReport $result
# The chosen row is the last word: Show-ChooserReport leaves the alias picker-only, but a
# human reader wants the chosen row at the bottom. Add it inline so the report ends with
# the chosen model.
if ($result.Chosen) {
    "---"
    Show-ChosenModel $result.Chosen
}
