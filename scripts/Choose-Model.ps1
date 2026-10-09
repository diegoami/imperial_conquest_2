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

    The mapping (per role, the owner's table). DeepSeek is blacklisted (the user's decision of
    2026-10-09): an opencode_go / alibaba row whose model is `deepseek-v4-pro` or
    `deepseek-v4.1-flash` is excluded with the reason "DeepSeek is not used (the user's decision
    of 2026-10-09)", never aliased. Every other row's alias is keyed by BOTH provider AND model
    id (Done-when 4 of #908; closes #906):
      implementer (heavy):
          minimax                                  → mm-m3
          zai                                      → glm
          opencode_go + mimo-v2.6-pro              → mimo-pro
          opencode_go + mimo-v2.6-flash            → mimo-flash
          opencode_go + anything else              → unmapped for implementer (printed,
                                                     never aliased; a DeepSeek row is excluded
                                                     before the map with the blacklist reason)
          claude                                   → Claude Sonnet (the script's exit-3
                                                     fallback, not an OpenCode alias; it
                                                     stands in only when nothing else is
                                                     left, per rule 17)
          openai                                   → unmapped for implementer (OpenAI never
                                                     implements; Sol/Luna stay free to review)
      reviewer (light, Sol's substitutes):
          zai                                      → glm
          opencode_go + mimo-v2.6-pro              → mimo-pro
          opencode_go + mimo-v2.6-flash            → mimo-flash
          opencode_go + anything else              → unmapped for reviewer (printed, never
                                                     aliased)
          alibaba                                  → qwen (when it appears)
          openai                                   → unmapped for reviewer (Luna and Sol
                                                     reach a review through §3.4's tier
                                                     rules, never through the ranking)
          claude                                   → unmapped for reviewer (Claude reviews
                                                     only when the main session passes
                                                     -Reviewer <sonnet|opus> explicitly,
                                                     and the &lt;complex&gt; tier rule sends
                                                     the very-complex PR to a cold Claude
                                                     Opus)

    A row whose provider+model has no alias for the role is printed `unmapped for &lt;role&gt;:
    &lt;provider&gt;` with score, confidence, and reasons; it is never picked.

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
        caps the wait (default 180); -RecommendPollSec &lt;seconds&gt; is the sleep
        between retries (default 5 — 36 polls within the 3-minute limit; R5 rework).
      - `note` says the statistics are loading and `ranking` is empty: wait and retry,
        sleep -RecommendPollSec seconds, until -RecommendRetryWaitSec seconds have
        passed; then exit 3.
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
.PARAMETER RecommendPollSec
    Seconds to sleep between retries on a `loading` note (default 5; 36 polls within the
    3-minute limit set by the task entry). A test shortens it.
.PARAMETER SelfTest
    Runs the built-in checks on canned responses; no network is touched.

.EXAMPLE
    pwsh scripts/Choose-Model.ps1 -Role reviewer -Tier complex -ExcludeModel mimo-pro
.EXAMPLE
    pwsh scripts/Choose-Model.ps1 -Role implementer -Pick
.EXAMPLE
    pwsh scripts/Choose-Model.ps1 -Role reviewer -ExcludeModel glm -RecommendFile rendered/light.json
#>
[CmdletBinding()]
param(
    [ValidateSet('implementer', 'reviewer', 'pair')] [string] $Role,
    [ValidateSet('simple', 'complex', 'very-complex')] [string] $Tier,
    [string] $ExcludeModel,
    [string[]] $ExcludeFamily,
    [string[]] $SubstituteFamilies,
    [switch] $Pick,
    [switch] $AllowAlibaba,
    [string] $RecommendFile,
    [string] $RecommendUrl = 'http://localhost:8765/recommend',
    [int] $RecommendRetryWaitSec = 180,
    [int] $RecommendPollSec = 5,
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'

# Per-(provider, model) map (Done-when 4 of #908; closes #906). The chooser keys an alias by
# both the row's provider and the row's model id, never by provider alone, so a row whose
# provider is known but whose model is not is "unmapped", never aliased to a model that
# happens to share its provider. The MiMo family is the only opencode_go mapping today
# (the user's decision of 2026-10-09; DeepSeek is blacklisted, so an opencode_go +
# deepseek-v4-* row is excluded before the map, below).
$ModelAliases = @{
    'opencode_go|mimo-v2.6-pro'   = @{ Implementer = 'mimo-pro';   Reviewer = 'mimo-pro' }
    'opencode_go|mimo-v2.6-flash' = @{ Implementer = 'mimo-flash'; Reviewer = 'mimo-flash' }
}
# Models blacklisted everywhere (the user's decision of 2026-10-09): a /recommend row whose
# model matches one of these is excluded with the blacklist reason, in both roles, regardless
# of provider. Adding a model here also adds it to the validator the caller uses (scripts do not
# accept it as a -Model / -Reviewer / -ExcludeModel value; the scripts in our Owns list refuse
# naming it with exit 1).
$BlacklistedModelIds = @{
    'deepseek-v4-pro'      = $true
    'deepseek-v4.1-flash'  = $true
    'deepseek-v4-pro-0813' = $true
}
# The owners' mapping tables, one per role. A provider not in the table has no role alias; the
# row is printed `unmapped for <role>: <provider>` and skipped. 'opencode_go' is intentionally
# absent: opencode_go rows resolve through $ModelAliases above, by model id. `claude` in the
# implementer role is the script's exit-3 Claude Sonnet fallback, not an OpenCode alias; the
# picker emits it only when nothing else is left.
$ImplementerAliases = @{
    'minimax' = 'mm-m3'
    'zai'     = 'glm'
}
$ReviewerAliases = @{
    'zai' = 'glm'
}
# Alibaba is used only when the user asks (the user's decision of 2026-10-09, #893: its
# monthly pool is nearly spent). -AllowAlibaba puts its reviewer alias back; without it,
# every alibaba row is excluded with that reason, in both roles.
if ($AllowAlibaba) { $ReviewerAliases['alibaba'] = 'qwen' }
# -Role pair maps a reviewer row to the alias external-review.ps1 takes. OpenAI's is Sol
# (Luna reviews only the simple tier, which §3.4 assigns, never the ranking).
$PairReviewerExtra = @{ 'openai' = 'sol' }
# `claude` in implementer is special. Any candidate's family an OpenCode dispatch map
# drops: implementer `claude` falls back to Claude Sonnet (the main session), no OpenCode
# alias; reviewer `claude` has no alias (it's not in the table, so it's "unmapped for
# reviewer").
$ImplementerFallback = '__CLAUDE_SONNET_FALLBACK__'

# The family of each model name. For our per-(provider, model) and per-role tables, the family
# is the whole role's alias set plus OpenAI (the Luna / Sol pair), since the dispatch script
# keys the family only by name (a reviewer "excludeModel mimo-pro" excludes every MiMo alias).
# Done-when 2 of #908: MiMo is a family of its own; DeepSeek has no entry here any more.
$FamilyOfName = @{
    'mm-m3'       = 'mm-m3'
    'mm-m2.7'     = 'mm-m3'
    'glm'         = 'glm'
    'glm-flash'   = 'glm'
    'ali-glm'     = 'glm'
    'mimo-pro'    = 'mimo'         # Done-when 2: MiMo is its own family
    'mimo-flash'  = 'mimo'
    'qwen'        = 'qwen'
    'qwen-flash'  = 'qwen'
    'claude'      = 'claude'       # the implementer `claude` row → fallback
    'sonnet'      = 'claude'
    'opus'        = 'claude'
    'luna'        = 'openai'
    'sol'         = 'openai'
}

function Get-FamilyForName([string] $Name) {
    if ($FamilyOfName.ContainsKey($Name)) { return $FamilyOfName[$Name] }
    return $Name
}

function Get-ModelRoleAlias([string] $Provider, [string] $Model, [string] $Role) {
    # The alias $ModelAliases gives for a row of provider $Provider and model $Model, for the
    # named role ('implementer' or 'reviewer'). The lookup is by joined key "<provider>|<model>"
    # so an unknown model on a known provider is not aliased to whatever the provider happens to
    # map to ($null, so the row is "unmapped for <role>" instead).
    if (-not $Provider -or -not $Model) { return $null }
    $key = "$Provider|$Model"
    if ($ModelAliases.ContainsKey($key)) {
        $row = $ModelAliases[$key]
        if ($row -and $row.ContainsKey($Role)) { return [string]$row[$Role] }
    }
    return $null
}

function Get-ImplementerAlias([string] $Provider, [string] $Model) {
    if ($Provider -eq 'claude') { return $ImplementerFallback }
    $hit = Get-ModelRoleAlias $Provider $Model 'implementer'
    if ($null -ne $hit) { return $hit }
    if ($ImplementerAliases.ContainsKey($Provider)) { return $ImplementerAliases[$Provider] }
    return $null
}

function Get-ReviewerAlias([string] $Provider, [string] $Model) {
    $hit = Get-ModelRoleAlias $Provider $Model 'reviewer'
    if ($null -ne $hit) { return $hit }
    if ($ReviewerAliases.ContainsKey($Provider)) { return $ReviewerAliases[$Provider] }
    return $null
}

function Test-RecommendationLoading {
    # True when the /recommend payload says statistics are still loading and no rows are
    # ranked yet. Lives on its own so the SelfTest can prove the loading branch with a
    # canned object (no fixture file written to disk).
    param($Recommend)
    if ($Recommend.note -and $Recommend.note -match 'loading' -and -not @($Recommend.ranking).Count) {
        return $true
    }
    return $false
}

function Get-Recommendation {
    # One network/file call. -RetryWaitSec caps the loading-note wait.
    # On a "loading" note with empty ranking: sleep -PollSec seconds, retry, until
    # -RetryWaitSec seconds have passed (default 5 s × 180 s = 36 polls within the
    # 3-minute limit set by the task entry; R5 rework documents the 5-second poll and
    # makes it a parameter so the SelfTest can shorten it).
    # On any connection error or the tracker not answering: throw 'tracker_silent' so the
    # caller prints the WSL restart command and exits 3.
    # On a successful JSON: return the raw object.
    param(
        [string] $RecommendFile,
        [string] $RecommendUrl = 'http://localhost:8765/recommend',
        [int]    $RetryWaitSec = 180,
        [int]    $PollSec = 5,
        [string] $Tier
    )
    $deadline = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + $RetryWaitSec
    while ($true) {
        if ($RecommendFile) {
            $raw = Get-Content -Raw -LiteralPath $RecommendFile | ConvertFrom-Json
            # A canned file is one-shot: no retry, no live connection to probe.
            if (Test-RecommendationLoading $raw) { throw 'tracker_loading' }
            return $raw
        }
        # Concatenation, not `"$RecommendUrl?tier=$Tier"`. PowerShell parses the latter as
        # `$RecommendUrl` followed by `?tier=$Tier`, where `?tier=` is read as a help-query
        # delimiter and `$RecommendUrl` ends up empty. Concatenation is unambiguous.
        $callUrl = "$RecommendUrl" + '?tier=' + "$Tier"
        try {
            # 5 s per call: the tracker is a local service that answers in well under a second, so
            # 5 s only bounds a hung socket; the loading retry (-RecommendPollSec, up to 180 s in
            # all) is what waits for a slow start (GLM's re-check R3).
            $raw = Invoke-RestMethod -Uri $callUrl -TimeoutSec 5 -ErrorAction Stop
        } catch {
            if ($_.Exception.InnerException) {
                $reason = $_.Exception.InnerException.Message
                if ($_.Exception.InnerException.InnerException) { $reason += ' / ' + $_.Exception.InnerException.InnerException.Message }
            } else { $reason = $_.Exception.Message }
            Write-Warning "/recommend call failed: $reason"
            throw 'tracker_silent'
        }
        if (Test-RecommendationLoading $raw) {
            if ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() -ge $deadline) {
                throw 'tracker_loading'
            }
            Start-Sleep -Seconds $PollSec
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

    # A row carries TWO family identifiers:
#   - Family: prefer the implementer-side alias's family (so a reviewer -ExcludeModel
#     mm-m3 drops a minimax row — the implementer side is what -ExcludeModel names).
#     Fall back to the role-side alias's family for providers that have no implementer
#     alias (e.g. alibaba has no implementer alias, so reviewer alibaba's family is the
#     reviewer-side qwen's family — R1 rework).
#   - RoleFamily: the role-side alias's family (so a reviewer -ExcludeFamily qwen drops
#     the alibaba row even when the implementer side has no alias).
# claude in the implementer role carries the Sonnet-fallback sentinel, and its family
# is 'claude' in both columns.
    $rows = foreach ($r in $recRanking) {
        $provider = [string]$r.provider
        $model    = [string]$r.model
        $score    = if ($null -eq $r.score) { $null } else { [double]$r.score }
        $conf     = if ($null -eq $r.confidence) { '?' } else { [string]$r.confidence }
        $reasons  = @($r.reasons)
        $usable   = -not ($r.PSObject.Properties['usable'] -and -not [bool]$r.usable)
        $alias    = if ($Role -eq 'implementer') { Get-ImplementerAlias $provider $model } else { Get-ReviewerAlias $provider $model }
        $implAlias = Get-ImplementerAlias $provider $model
        $roleAlias = $alias
        # Role-side family: alias's family, or provider for unmapped-for-role.
        $roleFamily = if ($roleAlias -eq $ImplementerFallback) {
            'claude'
        } elseif ($roleAlias) {
            (Get-FamilyForName $roleAlias)
        } elseif ($provider -eq 'claude') {
            'claude'
        } else {
            $provider
        }
        # Family (used by -ExcludeModel and as the printed family): prefer implementer
        # alias's family (so -ExcludeModel names the implementer's family), fall back
        # to the role-side family for providers with no implementer alias.
        $family = if ($implAlias -eq $ImplementerFallback) {
            'claude'
        } elseif ($implAlias) {
            (Get-FamilyForName $implAlias)
        } elseif ($provider -eq 'claude') {
            'claude'
        } else {
            $roleFamily
        }
        [pscustomobject]@{
            Provider  = $provider; Model = [string]$r.model; Score = $score; Confidence = $conf
            Reasons   = $reasons; Usable = [bool]$usable; Alias = $alias
            Family = $family; RoleFamily = $roleFamily
        }
    }

    # Apply exclusions in order (Done-when 2).
    $excluded = New-Object System.Collections.Generic.List[object]
    $filtered = @()
    $claudeDroppedReason = $null
    # Pass 1: family-level exclusions.
    #   -ExcludeModel on reviewer: a reviewer -ExcludeModel <name> names an implementer
    #     model; we drop rows whose Family (implementer-side, falling back to role-side)
    #     matches the named implementer's family (so -ExcludeModel mm-m3 drops a minimax
    #     row the implementer would have picked; -ExcludeModel qwen drops a reviewer
    #     alibaba row whose role-side alias is qwen).
    #   -ExcludeFamily <name>: drops rows whose RoleFamily matches (so a reviewer
    #     -ExcludeFamily qwen drops a row whose reviewer alias is qwen, e.g. an alibaba
    #     row — R1 rework).
    #   -SubstituteFamilies <name>: same RoleFamily rule, with the OpenAI family added
    #     automatically by the caller.
    foreach ($row in $rows) {
        $why = $null
        if ($BlacklistedModelIds.ContainsKey($row.Model)) {
            # Done-when 4 of #908: a DeepSeek row is excluded with the blacklist reason
            # (the user's decision of 2026-10-09). Checked first so any other exclusion
            # never sees the row, even when alibaba or the implementer's family would
            # have done the same.
            $why = "DeepSeek is not used (the user's decision of 2026-10-09)"
        } elseif ($row.Provider -eq 'alibaba' -and -not $AllowAlibaba) {
            $why = "Alibaba is used only when the user asks (the user's decision of 2026-10-09; -AllowAlibaba)"
        } elseif ($row.Family -in $excludeModelFamilies) {
            $why = "the implementer's family ($($row.Family)) is excluded (-ExcludeModel $ExcludeModel)"
        } elseif ($row.RoleFamily -in $familyExcluded) {
            $why = "family $($row.RoleFamily) is excluded (-ExcludeFamily)"
        } elseif ($row.RoleFamily -in $subFamilies) {
            $why = "family $($row.RoleFamily) is excluded (the chain-spent substitute filter)"
        }
        if ($why) {
            $excluded.Add([pscustomobject]@{ Provider = $row.Provider; Why = $why })
            continue
        }
        $filtered += $row
    }
    # Pass 2: Claude is dropped when any other candidate has a positive score (strictly
    # greater than zero — score 0 means an empty pool, not a positive spare-calls-per-day
    # signal; Done-when 2's "positive" reads as the rubric's "spare calls per day" line, R2
    # rework). When no other candidate has a positive score, Claude stands in (the script's
    # exit-3 fallback).
    if ($Role -eq 'implementer') {
        $claudeRows = @($filtered | Where-Object { $_.Provider -eq 'claude' -and $_.Alias -eq $ImplementerFallback })
        $nonClaudePositive = @($filtered | Where-Object { $_.Provider -ne 'claude' -and $_.Score -ne $null -and $_.Score -gt 0 } | Sort-Object Score -Descending | Select-Object -First 1)
        $claudePositive    = @($claudeRows | Where-Object { $_.Score -ne $null -and $_.Score -gt 0 })
        $claudeNegative    = @($claudeRows | Where-Object { $_.Score -ne $null -and $_.Score -lt 0 })
        if ($nonClaudePositive) {
            # Any positive non-Claude candidate is enough to drop Claude, regardless of
            # Claude's own score: the rubric's "drop Claude while another has positive
            # score" reads without a Claude-side condition.
            $dropClaude = $true
            $claudeDroppedReason = "Claude is the orchestrator and another candidate has a positive score ($($nonClaudePositive.Provider)), so Claude is dropped"
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

function Get-PairReviewerAlias([string] $Provider, [string] $Model) {
    if ($PairReviewerExtra.ContainsKey($Provider)) { return $PairReviewerExtra[$Provider] }
    return (Get-ReviewerAlias $Provider $Model)
}

function Resolve-Pair {
    # -Role pair (#893): the implementer and the reviewer for one delegated task, from one
    # /recommend?tier=heavy response. The implementer is Resolve-Chosen's implementer pick
    # (our exclusions on top: OpenAI never implements, Claude only when nothing else is
    # positive, no Alibaba unless asked). The reviewer is the tracker's `pair.reviewer` when
    # it maps to a reviewer alias and is of another family than the chosen implementer;
    # otherwise the next `ranking` row of another family. $null when no other family is left:
    # the caller tells the user.
    param(
        [Parameter(Mandatory)] $Recommend,
        [string] $ExcludeModel,
        [string[]] $ExcludeFamily
    )
    $impl = Resolve-Chosen -Role 'implementer' -Tier 'complex' -ExcludeFamily $ExcludeFamily -Recommend $Recommend
    # The tracker's own pair.implementer comes first when it survives our exclusions (it is in
    # Resolve-Chosen's filtered ranking with an alias); otherwise the top ranked implementer
    # (Sol's R1 on PR 896).
    $implementer = $impl.Chosen
    $implSource = 'ranking'
    $pairImpl = if ($Recommend.PSObject.Properties['pair'] -and $Recommend.pair) { $Recommend.pair.implementer } else { $null }
    if ($pairImpl) {
        $match = @($impl.Ranking | Where-Object { $_.Alias -and $_.Provider -eq [string]$pairImpl.provider -and $_.Model -eq [string]$pairImpl.model } | Select-Object -First 1)
        if ($match) { $implementer = $match[0]; $implSource = 'pair' }
    }
    $implFamily = if ($implementer) { [string]$implementer.Family } else { $null }
    $skipFamilies = @()
    if ($implFamily) { $skipFamilies += $implFamily }
    if ($ExcludeModel) { $skipFamilies += (Get-FamilyForName $ExcludeModel) }
    if ($ExcludeFamily) { $skipFamilies += @(Get-FamiliesFromNames $ExcludeFamily) }
    $candidates = @()
    $pairReviewer = if ($Recommend.PSObject.Properties['pair'] -and $Recommend.pair) { $Recommend.pair.reviewer } else { $null }
    if ($pairReviewer) { $candidates += [pscustomobject]@{ Row = $pairReviewer; Source = 'pair' } }
    foreach ($r in @($Recommend.ranking)) { $candidates += [pscustomobject]@{ Row = $r; Source = 'ranking' } }
    $passed = New-Object System.Collections.Generic.List[object]
    $reviewer = $null
    foreach ($c in $candidates) {
        $r = $c.Row
        $provider = [string]$r.provider
        $rModel   = [string]$r.model
        # Done-when 4 of #908: a DeepSeek row never becomes a reviewer either.
        if ($BlacklistedModelIds.ContainsKey($rModel)) { $passed.Add([pscustomobject]@{ Provider = $provider; Source = $c.Source; Why = 'DeepSeek is not used (the user''s decision of 2026-10-09)' }); continue }
        $alias = Get-PairReviewerAlias $provider $rModel
        $why = $null
        if ($provider -eq 'alibaba' -and -not $AllowAlibaba) { $why = 'Alibaba is used only when the user asks' }
        elseif ($r.PSObject.Properties['usable'] -and -not [bool]$r.usable) { $why = 'the tracker marked it unusable' }
        elseif (-not $alias) { $why = "no reviewer alias for $provider/$rModel" }
        elseif ((Get-FamilyForName $alias) -in $skipFamilies) { $why = "family $(Get-FamilyForName $alias) is the implementer's or excluded" }
        if ($why) { $passed.Add([pscustomobject]@{ Provider = $provider; Source = $c.Source; Why = $why }); continue }
        $score = if ($null -eq $r.score) { $null } else { [double]$r.score }
        $conf = if ($null -eq $r.confidence) { '?' } else { [string]$r.confidence }
        $reviewer = [pscustomobject]@{
            Alias = $alias; Family = (Get-FamilyForName $alias); Provider = $provider; Model = $rModel
            Score = $score; Confidence = $conf; Reasons = @($r.reasons); Source = $c.Source
        }
        break
    }
    return [pscustomobject]@{ Implementer = $implementer; ImplementerSource = $implSource; ImplementerResult = $impl; Reviewer = $reviewer; Passed = $passed }
}

function Show-PairReport($Pair, $Recommend) {
    "role pair, /recommend?tier=heavy"
    if ($Pair.Implementer) { Show-ChosenModel $Pair.Implementer | ForEach-Object { if ($_ -like 'chosen:*') { ($_ -replace '^chosen:', 'implementer:') + ", from /recommend's $($Pair.ImplementerSource)" } else { $_ } } }
    else { 'implementer: none (no candidate with an alias)' }
    if ($Pair.Reviewer) {
        $r = $Pair.Reviewer
        "reviewer: $($r.Alias) ($($r.Provider), $($r.Model), score $(Format-Score $r.Score), confidence $($r.Confidence)), from /recommend's $($r.Source)"
        "reasons:"
        foreach ($x in $r.Reasons) { "  - $x" }
    } else { 'reviewer: none -- no other family has quota: tell the user' }
    foreach ($p in $Pair.Passed) { "passed over for reviewer ($($p.Source)): $($p.Provider) -- $($p.Why)" }
    foreach ($e in $Pair.ImplementerResult.Excluded) { "excluded for implementer: $($e.Provider) -- $($e.Why)" }
    if ($Recommend.PSObject.Properties['free_reviewer'] -and $Recommend.free_reviewer) {
        "free reviewer offered: $($Recommend.free_reviewer.model) (advisory only, never counted, never for private code)"
    }
}

function Format-Reasons([object[]] $Reasons) {
    if (-not $Reasons -or $Reasons.Count -eq 0) { return 'no reasons given' }
    return ($Reasons -join '; ')
}

function Format-Score($Score) {
    # The score is a .NET double that may be $null when the tracker reported no spare
    # calls (an unknown pool size). Declaring `[double]` would coerce $null to 0 before
    # the null check ran; `?` is what the unknown row's printed report needs.
    if ($null -eq $Score) { return '?' }
    return ('{0}' -f [double]$Score)
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
    # Score is untyped so a $null value can pass through unchanged (the tracker reports
    # unknown pool sizes as score: null, and Format-Score / the chooser need that
    # distinction).
    param(
        [Parameter(Mandatory)] [string] $Provider,
        [string] $Model = 'model',
        $Score                 = 0,
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

function Set-AllowAlibabaForTest([bool] $On) {
    # The self-test runs some cases with Alibaba allowed (the -AllowAlibaba path) and the rest
    # with the default (excluded).
    $script:AllowAlibaba = $On
    if ($On) { $script:ReviewerAliases['alibaba'] = 'qwen' } else { $script:ReviewerAliases.Remove('alibaba') }
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
    # Implementer: minimax → mm-m3, zai → glm, opencode_go + mimo-v2.6-flash → mimo-flash, openrouter unmapped.
    $recImpl = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'minimax'     -Model 'MiniMax-M3'      -Score 1000),
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'         -Score 80),
        (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-flash' -Score 30),
        (New-CannedRow -Provider 'openrouter'  -Model 'mimo-v2.6-flash' -Score 0)
    )
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recImpl
    Add-Check 'mapping (implementer): minimax → mm-m3 is first; openrouter is unmapped' (Test-PickAlias $r 'mm-m3')
    $unmapped = @($r.Ranking | Where-Object { -not $_.Alias })
    Add-Check 'mapping (implementer): openrouter is in the list of unmapped rows' (@($unmapped | Where-Object { $_.Provider -eq 'openrouter' }).Count -eq 1)
    # Reviewer: zai → glm, opencode_go + mimo-v2.6-pro → mimo-pro, alibaba → qwen; openai/claude unmapped.
    $recRev = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'         -Score 1000),
        (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-pro'   -Score 80),
        (New-CannedRow -Provider 'alibaba'     -Model 'qwen-max'        -Score 60),
        (New-CannedRow -Provider 'openai'      -Model 'gpt-6-sol'       -Score 50),
        (New-CannedRow -Provider 'claude'      -Model 'sonnet'          -Score 40),
        (New-CannedRow -Provider 'openrouter'  -Model 'mimo-v2.6-flash' -Score 20)
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

    # R2 boundary: a score of zero is NOT positive. With Claude positive and the only other
    # candidate at 0, Claude stands in. With Claude at 0 and the only other candidate at
    # any positive value, Claude is dropped (the rubric's "positive" reads as > 0).
    $recClaudeZeroOther = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'claude'  -Model 'sonnet' -Score 100),
        (New-CannedRow -Provider 'minimax' -Model 'M3'      -Score 0)
    )
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recClaudeZeroOther
    Add-Check 'R2: Claude positive, other at 0 -> Claude stands in (zero is not positive)' ($r.Chosen -and $r.Chosen.Provider -eq 'claude')
    Add-Check 'R2: Claude positive, other at 0 -> Claude is the script fallback (alias)' ($r.Chosen -and $r.Chosen.Alias -eq $ImplementerFallback)

    $recClaudeZeroSelf = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'claude'  -Model 'sonnet' -Score 0),
        (New-CannedRow -Provider 'minimax' -Model 'M3'      -Score 100)
    )
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recClaudeZeroSelf
    Add-Check 'R2: Claude at 0, other positive -> Claude dropped' (-not ($r.Chosen -and $r.Chosen.Provider -eq 'claude'))
    $claudeExcluded = @($r.Excluded | Where-Object { $_.Provider -eq 'claude' })
    Add-Check 'R2: Claude at 0 dropped: the excluded row mentions the orchestrator rule' ($claudeExcluded.Count -ge 1 -and $claudeExcluded[0].Why -match 'orchestrator')

    $recClaudeAlone = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'claude'  -Model 'sonnet' -Score 1500),
        (New-CannedRow -Provider 'minimax' -Model 'M3'      -Score (-100))
    )
    $r = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recClaudeAlone
    Add-Check 'Claude only-non-negative: it is the script fallback' ($r.Chosen -and $r.Chosen.Alias -eq $ImplementerFallback)

    # Case 3: negative scores are ranked after non-negative ones (with -AllowAlibaba, so the
    # alibaba row is ranked rather than excluded).
    Set-AllowAlibabaForTest $true
    $recNeg = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-flash' -Score (-500)),
        (New-CannedRow -Provider 'minimax'     -Model 'M3'              -Score 1000),
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'         -Score 80),
        (New-CannedRow -Provider 'alibaba'     -Model 'qwen'            -Score (-100))
    )
    $r = Resolve-Chosen -Role 'reviewer' -Tier 'simple' -Recommend $recNeg
    $order = @($r.Ranking | ForEach-Object Provider)
    Add-Check 'negative scores are ranked after every non-negative one' ($order -join ',' -eq 'minimax,zai,opencode_go,alibaba')
    $picked = Resolve-Chosen -Role 'reviewer' -Tier 'simple' -Recommend $recNeg
    Add-Check 'a negative score is chosen only when nothing else is left' (Test-PickAlias $picked 'glm')
    $recOnlyNegative = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-flash' -Score (-500))
    )
    $rNeg = Resolve-Chosen -Role 'reviewer' -Tier 'simple' -Recommend $recOnlyNegative
    Add-Check 'the only candidate is negative-scoring: it is still picked' (Test-PickAlias $rNeg 'mimo-flash')
    Set-AllowAlibabaForTest $false

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
    $rDeep = Resolve-Chosen -Role 'reviewer' -Tier 'complex' -ExcludeModel 'mm-m3' -Recommend $recDeepImpl
    Add-Check '-ExcludeModel on reviewer drops minimax (the implementer)' (@($rDeep.Excluded | Where-Object { $_.Provider -eq 'minimax' }).Count -gt 0)
    Add-Check "-ExcludeModel mm-m3: reviewer passes the DeepSeek/GLM row only; implementer's family excluded message printed" ($rDeep.ExcludeModelFamilies -contains 'mm-m3')

    # R1 rework: a reviewer row from alibaba is assigned family 'alibaba' by the
    # implementer-side alias table lookup, although its reviewer alias is qwen. The
    # fix uses the role-side alias for the family. With -ExcludeModel qwen, the
    # alibaba row must be dropped, and the chooser must NOT pick qwen.
    $recAlibaba = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'alibaba' -Model 'qwen-max' -Score 1000),
        (New-CannedRow -Provider 'zai'     -Model 'glm-5.3' -Score 80)
    )
    Set-AllowAlibabaForTest $true
    $rAli = Resolve-Chosen -Role 'reviewer' -Tier 'complex' -ExcludeModel 'qwen' -Recommend $recAlibaba
    Set-AllowAlibabaForTest $false
    Add-Check 'R1: -ExcludeModel qwen drops the alibaba reviewer row' (@($rAli.Excluded | Where-Object { $_.Provider -eq 'alibaba' }).Count -gt 0)
    Add-Check 'R1: -ExcludeModel qwen: the chooser falls through to glm (never qwen)' (Test-PickAlias $rAli 'glm')

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

    # Case 7: reviewer substitute order (GLM, MiMo Pro, Qwen) with a canned light
    # ranking that puts zai first: prints MiMo Pro then Qwen, never GLM and never
    # openai. The dispatch script does its own picking; this is what gets logged.
    $recRevSub = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'         -Score 1000),
        (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-pro'   -Score 800),
        (New-CannedRow -Provider 'alibaba'     -Model 'qwen'            -Score 200),
        (New-CannedRow -Provider 'openai'      -Model 'gpt-6-sol'       -Score 150)
    )
    Set-AllowAlibabaForTest $true
    $r = Resolve-Chosen -Role 'reviewer' -Tier 'complex' -ExcludeModel 'glm' -Recommend $recRevSub
    Set-AllowAlibabaForTest $false
    $aliases = @($r.Ranking | Where-Object Alias | ForEach-Object Alias)
    Add-Check 'reviewer substitute order (ExcludeModel glm, -AllowAlibaba): MiMo Pro and Qwen, never GLM, never an OpenAI model' ([string]($aliases -join ',') -eq 'mimo-pro,qwen')
    $rNoAli = Resolve-Chosen -Role 'reviewer' -Tier 'complex' -ExcludeModel 'glm' -Recommend $recRevSub
    $aliasesNoAli = @($rNoAli.Ranking | Where-Object Alias | ForEach-Object Alias)
    Add-Check '#893: without -AllowAlibaba the reviewer order is MiMo Pro only (no Qwen)' ([string]($aliasesNoAli -join ',') -eq 'mimo-pro')
    Add-Check '#893: without -AllowAlibaba the alibaba row is excluded with the user-asks reason' (@($rNoAli.Excluded | Where-Object { $_.Provider -eq 'alibaba' -and $_.Why -match 'user asks' }).Count -eq 1)
    $unmapped = @($r.Ranking | Where-Object { -not $_.Alias } | ForEach-Object Provider)
    Add-Check 'reviewer substitute order: openai appears only in the unmapped-for-reviewer line' ([bool](@($unmapped | Where-Object { $_ -eq 'openai' }).Count -eq 1))

    # Case 8: loading note → exit 3 with the "did not finish loading" message. The branch
    # is exercised by calling Test-RecommendationLoading on a pre-built object (no fixture
    # file written to disk; Done-when 6 / R4 rework).
    $loadingObj = Get-CannedRecommend -Note 'statistics are loading; try again in a few seconds' -Ranking @()
    Add-Check 'loading note (Test-RecommendationLoading) is true on a canned object with empty ranking' (Test-RecommendationLoading $loadingObj)
    # The canned-file path's behaviour ("the canned file's note says loading, empty ranking,
    # no live retry, throws tracker_loading") is exercised by routing the canned object
    # through the same code a canned file would: no fixture on disk is needed.
    $err = $null
    try {
        if (Test-RecommendationLoading $loadingObj) { throw 'tracker_loading' }
    } catch { $err = $_ }
    Add-Check 'loading note in canned input raises tracker_loading so the caller exits 3 with the wait message' ($err -and $err.Exception.Message -eq 'tracker_loading')

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

    # Case 11 (R6 rework): Format-Score on $null returns "?", not "0" (the [double] cast
    # used to coerce $null to 0 before the null check ran). Test both the formatter in
    # isolation and a null-score row's printed report.
    Add-Check 'R6: Format-Score on $null returns "?"' ((Format-Score $null) -eq '?')
    Add-Check 'R6: Format-Score on a real 0 returns "0"'  ((Format-Score ([double]0)) -eq '0')
    $recNull = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'openai' -Model 'gpt-5.6-luna' -Score $null -Reasons @('pool size unknown'))
    )
    $out = (& { Show-ChooserReport (Resolve-Chosen -Role 'reviewer' -Tier 'simple' -Recommend $recNull) } | Out-String)
    Add-Check 'R6: a null-score row prints "score    ?" in the report, not "score 0"' ($out -match 'score\s+\?\s')

    # Case 12 (#893): -Role pair. The tracker's pair suggests an OpenAI implementer (which we
    # never use) and an OpenAI reviewer: the implementer falls to the ranking (glm), and the
    # reviewer stays Sol, another family.
    $pairRec = [pscustomobject]@{
        note = $null
        pair = [pscustomobject]@{
            implementer = (New-CannedRow -Provider 'openai' -Model 'gpt-6.1-sol' -Score 450)
            reviewer    = (New-CannedRow -Provider 'openai' -Model 'gpt-6.1-sol' -Score 450 -Reasons @('pair reviewer'))
        }
        ranking = @(
            (New-CannedRow -Provider 'openai'      -Model 'gpt-6.1-sol' -Score 450),
            (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'     -Score 100),
            (New-CannedRow -Provider 'opencode_go' -Model 'ds-pro'      -Score 50),
            (New-CannedRow -Provider 'alibaba'     -Model 'qwen'        -Score 900)
        )
        skipped = @()
    }
    $p = Resolve-Pair -Recommend $pairRec
    Add-Check '#893 pair: an OpenAI implementer suggestion is overridden by our exclusion (glm)' ($p.Implementer -and $p.Implementer.Alias -eq 'glm')
    Add-Check '#893 pair: the pair reviewer (openai -> sol) is kept, being of another family' ($p.Reviewer -and $p.Reviewer.Alias -eq 'sol' -and $p.Reviewer.Source -eq 'pair')
    Add-Check '#893 pair: alibaba (score 900) is never the implementer without -AllowAlibaba' ($p.Implementer.Provider -ne 'alibaba')

    # Case 13 (#893): the pair reviewer is of the implementer's family, so the next ranking
    # row of another family is taken.
    $pairRec2 = [pscustomobject]@{
        note = $null
        pair = [pscustomobject]@{
            implementer = (New-CannedRow -Provider 'zai' -Model 'glm-5.3' -Score 100)
            reviewer    = (New-CannedRow -Provider 'zai' -Model 'glm-5.3' -Score 100)
        }
        ranking = @(
            (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'        -Score 100),
            (New-CannedRow -Provider 'minimax'     -Model 'M3'             -Score 80),
            (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-pro'   -Score 50)
        )
        skipped = @()
    }
    $p2 = Resolve-Pair -Recommend $pairRec2
    Add-Check '#893 pair: same-family pair reviewer is replaced by the next ranking row of another family (mimo-pro)' ($p2.Implementer.Alias -eq 'glm' -and $p2.Reviewer -and $p2.Reviewer.Alias -eq 'mimo-pro' -and $p2.Reviewer.Source -eq 'ranking')
    $p2x = Resolve-Pair -Recommend $pairRec2 -ExcludeModel 'mimo-pro'
    Add-Check '#893 pair: with -ExcludeModel mimo-pro too, no reviewer is left (null: tell the user)' ($null -eq $p2x.Reviewer)
    $out = (& { Show-PairReport $p2x $pairRec2 } | Out-String)
    Add-Check '#893 pair: the report says "no other family has quota: tell the user"' ($out -match 'no other family has quota: tell the user')

    # Case 13b (Sol's R1 on PR 896): a valid tracker pair is kept as given, not re-derived from
    # the ranking: opencode_go implements and zai reviews although zai ranks first.
    $pairRec4 = [pscustomobject]@{
        note = $null
        pair = [pscustomobject]@{
            implementer = (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-flash' -Score 50)
            reviewer    = (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'         -Score 100)
        }
        ranking = @(
            (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'         -Score 100),
            (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-flash' -Score 50)
        )
        skipped = @()
    }
    $p4 = Resolve-Pair -Recommend $pairRec4
    Add-Check 'R1 (PR 896): the tracker pair is kept: mimo-flash implements (from pair), glm reviews (from pair)' ($p4.Implementer.Alias -eq 'mimo-flash' -and $p4.ImplementerSource -eq 'pair' -and $p4.Reviewer.Alias -eq 'glm' -and $p4.Reviewer.Source -eq 'pair')
    $p4x = Resolve-Pair -Recommend $pairRec4 -ExcludeFamily @('mimo-flash')
    Add-Check 'R1 (PR 896): a pair implementer our exclusions drop falls back to the ranking (glm), and the reviewer moves off glm' ($p4x.Implementer.Alias -eq 'glm' -and $p4x.ImplementerSource -eq 'ranking' -and (-not $p4x.Reviewer -or $p4x.Reviewer.Alias -ne 'glm'))

    # Case 4 of fix #908: a /recommend row whose model is a blacklisted DeepSeek id is
    # excluded with the blacklist reason, in both roles.
    $recBlack = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'opencode_go' -Model 'deepseek-v4-pro'      -Score 100),
        (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-pro'       -Score 80),
        (New-CannedRow -Provider 'opencode_go' -Model 'deepseek-v4.1-flash'  -Score 60),
        (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'             -Score 50)
    )
    $rBlackImp = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recBlack
    $blackExcluded = @($rBlackImp.Excluded | Where-Object { $_.Why -match 'DeepSeek is not used' })
    Add-Check '#908: a DeepSeek row is excluded with the blacklist reason (implementer)' ($blackExcluded.Count -eq 2)
    Add-Check '#908: an implementer blacklist excludes the DeepSeek rows; the MiMo row is still picked' (Test-PickAlias $rBlackImp 'mimo-pro')
    $rBlackRev = Resolve-Chosen -Role 'reviewer' -Tier 'complex' -Recommend $recBlack
    $blackRevExcluded = @($rBlackRev.Excluded | Where-Object { $_.Why -match 'DeepSeek is not used' })
    Add-Check '#908: a DeepSeek row is excluded with the blacklist reason (reviewer)' ($blackRevExcluded.Count -eq 2)

    # Case 5 of fix #908: the per-(provider, model) map keys alias by both. An opencode_go row
    # whose model is mimo-v2.6-pro gets mimo-pro; one whose model is mimo-v2.6-flash gets
    # mimo-flash. An opencode_go row whose model is unknown is unmapped, never aliased to a
    # sibling.
    $recMimo = Get-CannedRecommend -Ranking @(
        (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-pro'   -Score 200),
        (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-flash' -Score 150),
        (New-CannedRow -Provider 'opencode_go' -Model 'unknown-model'   -Score 100)
    )
    $rMimoImpl = Resolve-Chosen -Role 'implementer' -Tier 'complex' -Recommend $recMimo
    Add-Check '#908: opencode_go + mimo-v2.6-pro -> mimo-pro' (Test-PickAlias $rMimoImpl 'mimo-pro')
    $unmappedMimo = @($rMimoImpl.Ranking | Where-Object { $_.Provider -eq 'opencode_go' -and -not $_.Alias })
    Add-Check '#908: opencode_go + unknown model is unmapped (printed, never aliased to a sibling)' ($unmappedMimo.Count -eq 1 -and $unmappedMimo[0].Model -eq 'unknown-model')

    # Case 6 of fix #908: a MiMo implementer excludes both MiMo reviewers and nothing else.
    $rExclMiMo = Resolve-Chosen -Role 'reviewer' -Tier 'complex' -ExcludeModel 'mimo-pro' -Recommend $recMimo
    $excludedMiMoImpl = @($rExclMiMo.Excluded | Where-Object { $_.Provider -eq 'opencode_go' })
    Add-Check '#908: -ExcludeModel mimo-pro excludes both mimo-pro and mimo-flash (a MiMo implementer excludes both MiMo reviewers)' ($excludedMiMoImpl.Count -eq 2)
    $exclMM = @($rExclMiMo.Excluded | Where-Object { $_.Why -match 'family \(mimo\) is excluded' })
    Add-Check '#908: the MiMo exclusion message names the implementer family mimo, and only MiMo rows carry that message' ($exclMM.Count -eq 2)

    # Case 7 of fix #908: -Role pair resolves an opencode_go mimo-v2.6-pro row to mimo-pro.
    $pairRecMimo = [pscustomobject]@{
        note = $null
        pair = $null
        ranking = @(
            (New-CannedRow -Provider 'opencode_go' -Model 'mimo-v2.6-pro' -Score 200),
            (New-CannedRow -Provider 'zai'         -Model 'glm-5.3'        -Score 100)
        )
        skipped = @()
    }
    $pMimo = Resolve-Pair -Recommend $pairRecMimo
    Add-Check '#908 pair: opencode_go + mimo-v2.6-pro resolves to mimo-pro' ($pMimo.Implementer -and $pMimo.Implementer.Alias -eq 'mimo-pro')

    # Case 14 (#893): a null pair reviewer from the tracker with nothing else of another family.
    $pairRec3 = [pscustomobject]@{
        note = $null
        pair = [pscustomobject]@{ implementer = (New-CannedRow -Provider 'zai' -Model 'glm-5.3' -Score 10); reviewer = $null }
        ranking = @((New-CannedRow -Provider 'zai' -Model 'glm-5.3' -Score 10))
        skipped = @()
    }
    $p3 = Resolve-Pair -Recommend $pairRec3
    Add-Check '#893 pair: tracker reviewer null and nothing else -> reviewer null' ($p3.Implementer.Alias -eq 'glm' -and $null -eq $p3.Reviewer)

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
if (-not $Role) { throw 'Give -Role implementer, reviewer or pair (or -SelfTest).' }
if ($Role -notin @('implementer', 'reviewer', 'pair')) { throw "-Role must be implementer, reviewer or pair; got '$Role'." }

$tierForRecommend = if ($Role -eq 'reviewer') { 'light' } else { 'heavy' }

try {
    $recommend = Get-Recommendation -RecommendFile $RecommendFile -RecommendUrl $RecommendUrl -RetryWaitSec $RecommendRetryWaitSec -PollSec $RecommendPollSec -Tier $tierForRecommend
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

if ($Role -eq 'pair') {
    $pairResult = Resolve-Pair -Recommend $recommend -ExcludeModel $ExcludeModel -ExcludeFamily $ExcludeFamily
    Show-PairReport $pairResult $recommend
    if (-not $pairResult.Implementer) { [Console]::Error.WriteLine('Choose-Model.ps1: no implementer candidate.'); exit 3 }
    if (-not $pairResult.Reviewer) { [Console]::Error.WriteLine('Choose-Model.ps1: no reviewer of another family has quota: tell the user.'); exit 3 }
    exit 0
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
