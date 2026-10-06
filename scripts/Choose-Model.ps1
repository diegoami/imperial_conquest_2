<#
.SYNOPSIS
    Ranks the OpenCode models for one run by live quota, pricing and strength. Chooses nothing.
.DESCRIPTION
    There is no fixed model order (the owner's decision of 2026-10-06). The main session chooses a
    model case by case; this script does the mechanical half, deterministically, and prints the
    candidates best first, each with its reasons. The main session decides and passes the name to
    external-implement.ps1 -Model or external-review.ps1 -Reviewer. Nothing is run, posted or billed.

    Where each input comes from, so nothing here can drift from the scripts that run the models:
    - names, ids, variants, the Alibaba routes and the model families: read from
      external-review.ps1 (the reviewer names, $reviewerOf) and external-implement.ps1 (the
      implementer names) by parsing them, never executing them;
    - strength (heavy, light or off): the "Model strength" table in docs/environment.md, which the
      owner edits; a name missing from it is listed as unrated and never ranked;
    - quota and pricing: quota-tracker's /quota, one call. Alibaba's night discount and Z.ai's peak
      come from each provider's `pricing` block.

    Ranking: first the fit to the tier (-Tier complex: heavy before light; simple: light before
    heavy; `off` never ranks), then quota status (ok before low), then the score, the model's
    headroom divided by its cost factor now (Alibaba's discount: 1 - pct/100; Z.ai: the
    model's peak or off-peak multiplier; otherwise 1). A provider that is exhausted, or that /quota
    reports at 95% or more, makes the model unavailable, unless the model has an Alibaba route with
    quota (it is then ranked on that route) or it is luna, which has its own weekly window
    (`gpt-5.6-luna:7d`). A provider the answering tracker does not report is unavailable too, and
    an unknown headroom is scored '?' and ranked after every measured one. When the tracker does not answer, the candidates are ranked by fit alone
    and marked "quota unknown" (CLAUDE.md rule 17: never block on it).
.PARAMETER Role
    implementer or reviewer.
.PARAMETER Tier
    simple or complex (build-process.md §3.4's review tier; for an implementer, the task's weight).
    Default: complex for a reviewer, simple for an implementer. very-complex ranks as complex and
    notes that §3.4 sends that tier to a cold Claude Opus reviewer.
.PARAMETER ExcludeModel
    The implementer's name, for a reviewer: its whole family is excluded (build-process.md §3.4).
.PARAMETER Pick
    Prints only the best name (exit 3 when no candidate is available).
.PARAMETER QuotaFile
    Reads /quota's JSON from a file instead of the service (a test hook).
.PARAMETER SelfTest
    Runs the built-in checks on fixture quotas; no service is called.
.EXAMPLE
    pwsh scripts/Choose-Model.ps1 -Role reviewer -Tier complex -ExcludeModel deepseek-flash
.EXAMPLE
    pwsh scripts/Choose-Model.ps1 -Role implementer -Pick
#>
[CmdletBinding()]
param(
    [ValidateSet('implementer', 'reviewer')] [string] $Role,
    [ValidateSet('simple', 'complex', 'very-complex')] [string] $Tier,
    [string] $ExcludeModel,
    [switch] $Pick,
    [string] $QuotaFile,
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-OpenCodeWatched.ps1')

function Get-AssignedLiteral {
    # The value of `$<Name> = <literal>` at a script's top level, evaluated on its own (hashtable and
    # array literals only; Get-SolVariant is stubbed for the reviewer's $variants).
    param([System.Management.Automation.Language.Ast] $Ast, [string] $Name)
    $node = $Ast.Find({ param($n)
            $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $n.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $n.Left.VariablePath.UserPath -eq $Name -and $n.Parent.Parent -eq $Ast }, $false)
    if (-not $node) { throw "`$$Name not found at the top level of $($Ast.Extent.File)." }
    $text = "function Get-SolVariant { param(`$r) 'low' }; `$Effort = `$null; " + $node.Right.Extent.Text
    return (& ([scriptblock]::Create($text)))
}

function Get-ValidateSetOf {
    param([System.Management.Automation.Language.ScriptBlockAst] $Ast, [string] $Parameter)
    $p = $Ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq $Parameter }
    $attr = $p.Attributes | Where-Object { $_.TypeName.Name -eq 'ValidateSet' }
    return @($attr.PositionalArguments | ForEach-Object { $_.Value })
}

function Get-ChooserCatalog {
    # Every name either script accepts, with its id, variant, Alibaba id and family.
    param([string] $ScriptDir = $PSScriptRoot)
    $parse = { param($f) [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $ScriptDir $f), [ref]$null, [ref]$null) }
    $rev = & $parse 'external-review.ps1'
    $imp = & $parse 'external-implement.ps1'
    $advisory = @(Get-AssignedLiteral $rev 'advisoryReviewers')
    $sets = @{
        reviewer    = @(Get-ValidateSetOf $rev 'Reviewer' | Where-Object { $_ -ne 'auto' -and $advisory -notcontains $_ })
        implementer = @(Get-ValidateSetOf $imp 'Model' | Where-Object { $_ -ne 'auto' })
    }
    $tables = @{
        reviewer    = @{ Ids = (Get-AssignedLiteral $rev 'models'); Variants = (Get-AssignedLiteral $rev 'variants'); Alibaba = (Get-AssignedLiteral $rev 'alibabaIds') }
        implementer = @{ Ids = (Get-AssignedLiteral $imp 'models'); Variants = (Get-AssignedLiteral $imp 'variants'); Alibaba = (Get-AssignedLiteral $imp 'alibabaIds') }
    }
    return [pscustomobject]@{ Sets = $sets; Tables = $tables; ReviewerOf = (Get-AssignedLiteral $rev 'reviewerOf'); Advisory = $advisory }
}

function Get-ModelStrength {
    # name -> heavy | light | off, from docs/environment.md's "Model strength" table.
    param([string] $DocFile = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs/environment.md'))
    $out = @{}
    $in = $false
    foreach ($line in [System.IO.File]::ReadAllLines($DocFile)) {
        if ($line -match '^#{2,4} ') { $in = $line -match 'Model strength'; continue }
        if ($in -and $line -match '^\|\s*`([^`]+)`\s*\|\s*(heavy|light|off)\s*\|') { $out[$Matches[1]] = $Matches[2] }
    }
    return $out
}

function Get-QuotaSnapshot {
    # /quota as provider -> entry, or $null when the tracker does not answer.
    param([string] $File)
    try {
        $raw = if ($File) { Get-Content -Raw -LiteralPath $File | ConvertFrom-Json } else { Invoke-RestMethod -Uri 'http://localhost:8765/quota' -TimeoutSec 5 -ErrorAction Stop }
    } catch { return $null }
    $map = @{}
    foreach ($e in @($raw)) { if ($e.provider) { $map[[string]$e.provider] = $e } }
    return $map
}

function Format-Clock([object] $Unix) {
    if (-not $Unix) { return '?' }
    return [DateTimeOffset]::FromUnixTimeSeconds([long]$Unix).ToLocalTime().ToString('ddd HH:mm')
}

function Get-RouteState {
    # One route of one model: available, headroom, status, cost factor and its reasons.
    param([string] $Name, [string] $ModelId, [hashtable] $Quota)
    $route = Get-OpenCodeRouteName $ModelId
    $provider = $script:OpenCodeRouteQuotaProvider[$route]
    if (-not $provider) { $provider = $route }
    $base = ($ModelId -split '/', 2)[1]
    $s = [ordered]@{ Route = $route; Model = $ModelId; Provider = $provider; Available = $true; Status = 'unknown'; Headroom = $null; Cost = 1.0; Notes = @() }
    if ($null -eq $Quota) { $s.Notes += 'quota unknown'; return [pscustomobject]$s }
    $q = $Quota[$provider]
    # A provider the answering tracker does not report was not checked: unavailable, never ranked
    # as if it had quota (#815 review, R1).
    if (-not $q) { $s.Available = $false; $s.Notes += "$provider not reported by quota-tracker"; return [pscustomobject]$s }
    $s.Status = [string]$q.status
    # Unknown headroom stays $null (score '?', ranked after every measured score), never a measured 0
    # (#815 review, R2).
    if ($null -ne $q.headroom_pct) { $s.Headroom = [double]$q.headroom_pct }
    if ($Name -eq 'luna') {
        # GPT-5.6 Luna draws on its own weekly window (docs/environment.md).
        $w = @($q.windows) | Where-Object { $_.name -eq 'gpt-5.6-luna:7d' } | Select-Object -First 1
        if ($w) {
            $s.Headroom = 100 - [double]$w.used_pct
            $s.Status = if ($w.used_pct -ge 95) { 'exhausted' } elseif ($w.used_pct -ge 80) { 'low' } else { 'ok' }
            $provider = 'Luna window'
        }
    }
    if ($s.Status -in 'exhausted', 'error', 'not_configured') {
        $s.Available = $false
        $s.Notes += $(if ($s.Status -eq 'exhausted') { "$provider exhausted, back $(Format-Clock $q.available_at)" } else { "$provider $($s.Status)" })
        return [pscustomobject]$s
    }
    $p = $q.pricing
    if ($p -and $p.PSObject.Properties['discount_pct']) {
        $pct = $p.discount_pct.PSObject.Properties[$base]
        if ($pct -and $p.discount_now) { $s.Cost = 1 - [double]$pct.Value / 100; $s.Notes += "discount $($pct.Value)% until $(Format-Clock $p.next_change_at)" }
        elseif ($pct) { $s.Notes += "no discount until $(Format-Clock $p.next_change_at)" }
    }
    if ($p -and $p.PSObject.Properties['multiplier']) {
        $m = $p.multiplier.PSObject.Properties[$base]
        if ($m) {
            $s.Cost = [double]$(if ($p.peak_now) { $m.Value.peak } else { $m.Value.off_peak })
            $s.Notes += $(if ($p.peak_now) { "peak $($s.Cost)x until $(Format-Clock $p.next_change_at)" } else { "off-peak $($s.Cost)x" })
        }
    }
    $s.Notes += $(if ($null -ne $s.Headroom) { "$provider $($s.Headroom)% headroom" } else { "$provider headroom unknown" })
    return [pscustomobject]$s
}

function Get-ModelRanking {
    param(
        [Parameter(Mandatory)] [string] $Role,
        [string] $Tier,
        [string] $ExcludeModel,
        [hashtable] $Quota,
        [Parameter(Mandatory)] $Catalog,
        [Parameter(Mandatory)] [hashtable] $Strength
    )
    if (-not $Tier) { $Tier = if ($Role -eq 'reviewer') { 'complex' } else { 'simple' } }
    $want = if ($Tier -eq 'simple') { 'light' } else { 'heavy' }
    $excluded = @()
    if ($Role -eq 'reviewer' -and $ExcludeModel) {
        if (-not $Catalog.ReviewerOf.ContainsKey($ExcludeModel)) { throw "Unknown -ExcludeModel '$ExcludeModel'. Known: $(@($Catalog.ReviewerOf.Keys | Sort-Object) -join ', ')." }
        $excluded = @($Catalog.ReviewerOf[$ExcludeModel])
    }
    $t = $Catalog.Tables[$Role]
    $rows = foreach ($name in $Catalog.Sets[$Role]) {
        $rating = if ($Strength.ContainsKey($name)) { $Strength[$name] } else { 'unrated' }
        $state = Get-RouteState -Name $name -ModelId $t.Ids[$name] -Quota $Quota
        $why = @()
        if (-not $state.Available -and $t.Alibaba[$name] -and $state.Route -ne 'alibaba') {
            $alt = Get-RouteState -Name $name -ModelId $t.Alibaba[$name] -Quota $Quota
            if ($alt.Available) { $why += "usual route: $($state.Notes -join '; ')"; $state = $alt }
        }
        $lowFactor = if ($state.Status -eq 'low') { 1 } else { 0 }
        $score = if ($null -ne $state.Headroom) { [math]::Round($state.Headroom / [math]::Max($state.Cost, 0.05), 1) } else { $null }
        [pscustomobject]@{
            Name = $name; Model = $state.Model; Variant = $t.Variants[$name]; Strength = $rating; Route = $state.Route
            Status = $state.Status; Headroom = $state.Headroom; Cost = $state.Cost; Score = $score
            Fit = $(if ($rating -eq $want) { 0 } elseif ($rating -in 'heavy', 'light') { 1 } else { 9 })
            Low = $lowFactor; Available = $state.Available; Excluded = ($excluded -contains $name)
            Reasons = (@($why) + @($state.Notes)) -join '; '
        }
    }
    $ranked = @($rows | Where-Object { $_.Available -and -not $_.Excluded -and $_.Fit -lt 9 } |
            Sort-Object Fit, Low, @{ Expression = { $null -eq $_.Score } }, @{ Expression = { if ($null -eq $_.Score) { 0 } else { $_.Score } }; Descending = $true }, Name)
    return [pscustomobject]@{
        Role = $Role; Tier = $Tier; Excluded = $excluded; Ranked = $ranked
        Unavailable = @($rows | Where-Object { -not $_.Available -and -not $_.Excluded })
        Unranked = @($rows | Where-Object { $_.Available -and -not $_.Excluded -and $_.Fit -ge 9 })
        Claude = $(if ($Quota -and $Quota['claude']) { $Quota['claude'] } else { $null })
    }
}

function Invoke-ChooserSelfTest {
    $catalog = Get-ChooserCatalog
    $strength = Get-ModelStrength
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $p = { param($name, $head, $status = 'ok', $extra = @{}) $o = [ordered]@{ provider = $name; status = $status; headroom_pct = $head; windows = @(); available_at = $now + 3600 }; foreach ($k in $extra.Keys) { $o[$k] = $extra[$k] }; [pscustomobject]$o }
    $alibabaPricing = { param($on) [pscustomobject]@{ discount_now = $on; next_change_at = $now + 3600; discount_pct = [pscustomobject]@{ 'qwen3.8-max' = 60; 'qwen3.8-flash' = 60; 'deepseek-v4-pro-0813' = 50; 'deepseek-v4.1-flash' = 50 } } }
    $zaiPricing = { param($peak) [pscustomobject]@{ peak_now = $peak; next_change_at = $now + 3600; multiplier = [pscustomobject]@{ 'glm-5.3' = [pscustomobject]@{ peak = 3.0; off_peak = 1.0 }; 'glm-5.3-flash' = [pscustomobject]@{ peak = 1.2; off_peak = 0.4 } } } }
    $base = {
        param($over = @{})
        $q = @{
            openai      = (& $p 'openai' 60 'ok' @{ windows = @([pscustomobject]@{ name = 'gpt-5.6-luna:7d'; used_pct = 10 }) })
            zai         = (& $p 'zai' 60 'ok' @{ pricing = (& $zaiPricing $false) })
            opencode_go = (& $p 'opencode_go' 60)
            alibaba     = (& $p 'alibaba' 60 'ok' @{ pricing = (& $alibabaPricing $false) })
            minimax     = (& $p 'minimax' 60)
            claude      = (& $p 'claude' 60)
        }
        foreach ($k in $over.Keys) { $q[$k] = $over[$k] }
        $q
    }
    $rank = { param($role, $tier, $ex, $q) Get-ModelRanking -Role $role -Tier $tier -ExcludeModel $ex -Quota $q -Catalog $catalog -Strength $strength }
    $names = { param($r) @($r.Ranked | ForEach-Object Name) }
    $checks = @()
    foreach ($role in 'reviewer', 'implementer') {
        $missing = @($catalog.Sets[$role] | Where-Object { -not $strength.ContainsKey($_) })
        $checks += [pscustomobject]@{ Name = "every $role name has a row in docs/environment.md's Model strength table (missing: $($missing -join ', '))"; Ok = ($missing.Count -eq 0) }
    }
    $checks += [pscustomobject]@{ Name = 'the catalogue reads the five 2026-10-06 names from both scripts'; Ok = (@('mm-m3', 'mm-m2.7', 'ali-deepseek-pro', 'ali-deepseek-flash', 'ali-glm' | Where-Object { $catalog.Sets.reviewer -contains $_ -and $catalog.Sets.implementer -contains $_ }).Count -eq 5) }
    $checks += [pscustomobject]@{ Name = 'advisory reviewers are never candidates'; Ok = (@($catalog.Sets.reviewer | Where-Object { $catalog.Advisory -contains $_ }).Count -eq 0) }
    $r = & $rank 'reviewer' 'complex' 'deepseek-flash' (& $base)
    $checks += [pscustomobject]@{ Name = 'a deepseek-flash implementer: no DeepSeek name is ranked'; Ok = (@(& $names $r | Where-Object { $_ -match 'deepseek' }).Count -eq 0) }
    $checks += [pscustomobject]@{ Name = 'tier complex ranks every heavy model before any light one'; Ok = (@($r.Ranked | Select-Object -First (@($r.Ranked | Where-Object Strength -eq 'heavy').Count) | Where-Object Strength -ne 'heavy').Count -eq 0) }
    $r = & $rank 'reviewer' 'simple' $null (& $base)
    $checks += [pscustomobject]@{ Name = 'tier simple ranks a light model first'; Ok = ($r.Ranked[0].Strength -eq 'light') }
    $r = & $rank 'reviewer' 'complex' $null (& $base @{ alibaba = (& $p 'alibaba' 60 'ok' @{ pricing = (& $alibabaPricing $true) }) })
    $checks += [pscustomobject]@{ Name = 'Alibaba discount on: qwen (60% off) ranks above glm at equal headroom'; Ok = ((& $names $r).IndexOf('qwen') -lt (& $names $r).IndexOf('glm')) }
    $r = & $rank 'reviewer' 'complex' $null (& $base @{ zai = (& $p 'zai' 60 'ok' @{ pricing = (& $zaiPricing $true) }) })
    $checks += [pscustomobject]@{ Name = 'Z.ai peak: glm (3x) ranks below ali-glm'; Ok = ((& $names $r).IndexOf('glm') -gt (& $names $r).IndexOf('ali-glm')) }
    $r = & $rank 'reviewer' 'complex' $null (& $base @{ zai = (& $p 'zai' 2 'exhausted') })
    $g = $r.Ranked | Where-Object Name -eq 'glm'
    $checks += [pscustomobject]@{ Name = 'zai exhausted: glm is ranked on its Alibaba route'; Ok = ($g -and $g.Route -eq 'alibaba' -and $g.Model -eq 'alibaba-token-plan/glm-5.3') }
    $checks += [pscustomobject]@{ Name = 'zai exhausted: glm-flash (no Alibaba route) is unavailable'; Ok = (@($r.Unavailable | ForEach-Object Name) -contains 'glm-flash') }
    $r = & $rank 'reviewer' 'simple' $null (& $base @{ openai = (& $p 'openai' 3 'exhausted' @{ windows = @([pscustomobject]@{ name = 'gpt-5.6-luna:7d'; used_pct = 10 }) }) })
    $checks += [pscustomobject]@{ Name = 'openai exhausted, Luna window at 10%: luna stays available, sol does not'; Ok = ((& $names $r) -contains 'luna' -and (& $names $r) -notcontains 'sol') }
    $r = & $rank 'reviewer' 'complex' $null (& $base @{ minimax = (& $p 'minimax' 1 'exhausted') })
    $checks += [pscustomobject]@{ Name = 'minimax exhausted: mm-m3 is unavailable'; Ok = (@($r.Unavailable | ForEach-Object Name) -contains 'mm-m3' -and (& $names $r) -notcontains 'mm-m3') }
    $r = & $rank 'reviewer' 'complex' $null $null
    $checks += [pscustomobject]@{ Name = 'tracker silent: every rated candidate is ranked, marked quota unknown'; Ok = ($r.Ranked.Count -gt 0 -and @($r.Ranked | Where-Object { $_.Reasons -notlike '*quota unknown*' }).Count -eq 0) }
    $r = & $rank 'implementer' 'simple' $null (& $base)
    $checks += [pscustomobject]@{ Name = "a model rated off is never ranked (mimo-pro, mimo-flash)"; Ok = ((& $names $r) -notcontains 'mimo-pro' -and (& $names $r) -notcontains 'mimo-flash') }
    $q = & $base; $q.Remove('minimax')
    $r = & $rank 'reviewer' 'complex' $null $q
    $checks += [pscustomobject]@{ Name = 'a provider missing from an answering /quota: mm-m3 is unavailable, not ranked (#815 R1)'; Ok = ((& $names $r) -notcontains 'mm-m3' -and @($r.Unavailable | ForEach-Object Name) -contains 'mm-m3') }
    $r = & $rank 'reviewer' 'complex' $null (& $base @{ minimax = [pscustomobject]@{ provider = 'minimax'; status = 'ok'; windows = @() } })
    $mm = $r.Ranked | Where-Object Name -eq 'mm-m3'
    $heavyScored = @($r.Ranked | Where-Object { $_.Strength -eq 'heavy' -and $null -ne $_.Score })
    $checks += [pscustomobject]@{ Name = 'no headroom_pct: mm-m3 keeps a null score and ranks after every measured heavy model (#815 R2)'; Ok = ($mm -and $null -eq $mm.Headroom -and $null -eq $mm.Score -and (& $names $r).IndexOf('mm-m3') -eq $heavyScored.Count) }
    $r = & $rank 'reviewer' 'complex' $null (& $base @{ alibaba = (& $p 'alibaba' 15 'low' @{ pricing = (& $alibabaPricing $false) }) })
    $checks += [pscustomobject]@{ Name = 'a low provider ranks after an ok one of the same fit'; Ok = ((& $names $r).IndexOf('qwen') -gt (& $names $r).IndexOf('mm-m3')) }
    $failed = 0
    $i = 0
    foreach ($c in $checks) { $i++; if (-not $c.Ok) { $failed++ }; '[{0}] {1}  check  -- {2}' -f $i, $(if ($c.Ok) { 'PASS' } else { 'FAIL' }), $c.Name }
    "self-test: $($checks.Count - $failed)/$($checks.Count) passed"
    return [int]($failed -gt 0)
}

if ($MyInvocation.InvocationName -eq '.') { return }
if ($SelfTest) { $code = Invoke-ChooserSelfTest; $code[0..($code.Count - 2)]; exit $code[-1] }
if (-not $Role) { throw 'Give -Role implementer or -Role reviewer (or -SelfTest).' }

$quota = Get-QuotaSnapshot -File $QuotaFile
$result = Get-ModelRanking -Role $Role -Tier $Tier -ExcludeModel $ExcludeModel -Quota $quota -Catalog (Get-ChooserCatalog) -Strength (Get-ModelStrength)
if ($Pick) {
    if (-not $result.Ranked) { [Console]::Error.WriteLine('No candidate is available.'); exit 3 }
    $result.Ranked[0].Name
    exit 0
}
"role $($result.Role), tier $($result.Tier)$(if ($ExcludeModel) { "; implemented by $ExcludeModel, excluded: $($result.Excluded -join ', ')" })"
if ($null -eq $quota) { 'quota-tracker did not answer: ranked by strength alone (CLAUDE.md rule 17).' }
if ($Tier -eq 'very-complex') { 'very complex: build-process.md §3.4 sends this tier to a cold Claude Opus reviewer; the ranking is for the OpenCode reviews beside it.' }
$i = 0
foreach ($r in $result.Ranked) {
    $i++
    '{0,2}. {1,-19} {2,-41} {3,-9} {4,-6} score {5,6}  {6}' -f $i, $r.Name, $r.Model, $(if ($r.Variant) { $r.Variant } else { '-' }), $r.Strength, $(if ($null -ne $r.Score) { $r.Score } else { '?' }), $r.Reasons
}
if (-not $result.Ranked) { 'no candidate is available.' }
foreach ($r in $result.Unavailable) { "unavailable: $($r.Name) ($($r.Reasons))" }
foreach ($r in $result.Unranked) { "not ranked: $($r.Name) (strength $($r.Strength))" }
if ($result.Claude) { "Claude (the fallback): $($result.Claude.status), $($result.Claude.headroom_pct)% headroom$(if ($result.Claude.status -eq 'exhausted') { ", back $(Format-Clock $result.Claude.available_at)" })" }
