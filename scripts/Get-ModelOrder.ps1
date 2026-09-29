<#
.SYNOPSIS
    The model order for this machine: the implementer chain, the reviewer chain and the task
    reviewer kind, from models.local.json (per machine, git-ignored) over the built-in defaults.
.DESCRIPTION
    Dot-sourced by external-implement.ps1 and external-review.ps1, which call Get-ModelOrder.
    Run directly:
      pwsh scripts/Get-ModelOrder.ps1 -Show    the effective order and where it came from
      pwsh scripts/Get-ModelOrder.ps1 -List    every model name the scripts accept: id, vendor, variant
    models.local.json (copy models.example.json to the repository root) holds "active" (a profile
    name) and "profiles" (name -> implementer[], reviewer[], taskReviewer "claude" or "opencode"),
    plus optional "ids", "variants", "vendors" and "display" maps that add or override model names.
    An id containing '<' is unfilled: that name is dropped from every chain with a warning until it
    is filled from `opencode models <provider>`. /model-order (build-process.md Appendix D) edits the
    file; nothing here writes it.
    The order decides only who runs. The rules stay: the reviewer's model is never the implementer's
    (the scripts drop the implementer's vendor from the reviewer chain), and a merge needs the
    approving review and green CI (build-process.md §3.4, §4).
#>
[CmdletBinding()]
param([switch] $Show, [switch] $List)

function Get-ModelOrder {
    param([string] $RepoRoot)
    if (-not $RepoRoot) {
        $RepoRoot = git rev-parse --show-toplevel 2>$null
        if (-not $RepoRoot) { $RepoRoot = Split-Path $PSScriptRoot -Parent }
    }

    # Built-in tables: the 2026-09-28 decisions (DeepSeek V4.1 Flash at max effort the default
    # implementer, GLM-5.3 at max the default reviewer), plus OpenAI's models through OpenCode's
    # `openai` provider as gpt-mini (cheap, an implementer) and gpt (strong, a reviewer), whose ids
    # are per machine: fill them in models.local.json from `opencode models openai`.
    $ids = [ordered]@{
        'deepseek-flash'  = 'opencode/deepseek-v4.1-flash'
        'mimo-flash-free' = 'opencode/mimo-v2.6-flash-free'
        'mimo-pro'        = 'opencode/mimo-v2.6-pro'
        'mimo-flash'      = 'opencode/mimo-v2.6-flash'
        'glm'             = 'opencode/glm-5.3'
        'luna'            = 'opencode/gpt-6-luna'
        'deepseek'        = 'opencode/deepseek-v4.1-flash'
        'gpt-mini'        = 'openai/<id from opencode models openai>'
        'gpt'             = 'openai/<id from opencode models openai>'
    }
    $variants = @{
        'deepseek-flash' = 'max'; 'mimo-flash-free' = ''; 'mimo-pro' = ''; 'mimo-flash' = ''
        'glm' = 'max'; 'luna' = 'high'; 'deepseek' = ''; 'gpt-mini' = ''; 'gpt' = 'high'
    }
    $vendors = @{
        'deepseek-flash' = 'deepseek'; 'deepseek' = 'deepseek'
        'mimo-flash-free' = 'mimo'; 'mimo-pro' = 'mimo'; 'mimo-flash' = 'mimo'
        'glm' = 'glm'; 'luna' = 'luna'; 'gpt-mini' = 'openai'; 'gpt' = 'openai'
    }
    $display = @{
        'deepseek-flash' = 'DeepSeek'; 'deepseek' = 'DeepSeek'; 'mimo-flash-free' = 'MiMo Flash'
        'mimo-pro' = 'MiMo Pro'; 'mimo-flash' = 'MiMo Flash'; 'glm' = 'GLM'; 'luna' = 'Luna'
        'gpt-mini' = 'GPT mini'; 'gpt' = 'GPT'
    }
    $profileName = 'built-in'
    $source = 'built-in (no models.local.json)'
    $implementer = @('deepseek-flash', 'mimo-flash-free', 'glm')
    $reviewer = @('glm', 'luna', 'deepseek')
    $taskReviewer = 'claude'

    $file = Join-Path $RepoRoot 'models.local.json'
    if (Test-Path -LiteralPath $file) {
        $cfg = Get-Content -LiteralPath $file -Raw -Encoding utf8 | ConvertFrom-Json
        $maps = @{ ids = $ids; variants = $variants; vendors = $vendors; display = $display }
        foreach ($mapName in $maps.Keys) {
            if ($cfg.PSObject.Properties[$mapName] -and $cfg.$mapName) {
                foreach ($prop in $cfg.$mapName.PSObject.Properties) { $maps[$mapName][$prop.Name] = [string]$prop.Value }
            }
        }
        if (-not $cfg.active) { throw "$file has no ""active"" profile name." }
        $profile = $cfg.profiles.PSObject.Properties[[string]$cfg.active]
        if (-not $profile) { throw "$file names the active profile '$($cfg.active)', which ""profiles"" does not define." }
        $profile = $profile.Value
        $profileName = [string]$cfg.active
        $source = $file
        if ($profile.implementer) { $implementer = @($profile.implementer | ForEach-Object { [string]$_ }) }
        if ($profile.reviewer) { $reviewer = @($profile.reviewer | ForEach-Object { [string]$_ }) }
        if ($profile.taskReviewer) { $taskReviewer = [string]$profile.taskReviewer }
    }

    if ($taskReviewer -notin 'claude', 'opencode') { throw "taskReviewer must be 'claude' or 'opencode', not '$taskReviewer' ($source)." }
    foreach ($name in @($implementer) + @($reviewer)) {
        if (-not $ids.Contains($name)) { throw "Unknown model name '$name' in the model order ($source). Known: $($ids.Keys -join ', ')." }
    }
    # An unfilled id drops its name from the chains rather than sending "<...>" to OpenCode.
    $filled = { param($name) -not ($ids[$name] -like '*<*') }
    foreach ($name in @($implementer) + @($reviewer) | Select-Object -Unique) {
        if (-not (& $filled $name)) { Write-Warning "model '$name' has no id yet ($($ids[$name])); dropped from the chains until models.local.json fills it (/model-order id $name <provider/model>)." }
    }
    $implementer = @($implementer | Where-Object { & $filled $_ })
    $reviewer = @($reviewer | Where-Object { & $filled $_ })
    foreach ($name in $ids.Keys) {
        if (-not $vendors.ContainsKey($name)) { $vendors[$name] = $name }
        if (-not $display.ContainsKey($name)) { $display[$name] = $name }
        if (-not $variants.ContainsKey($name)) { $variants[$name] = '' }
    }

    [pscustomobject]@{
        Profile      = $profileName
        Source       = $source
        Implementer  = $implementer
        Reviewer     = $reviewer
        TaskReviewer = $taskReviewer
        Ids          = $ids
        Variants     = $variants
        Vendors      = $vendors
        Display      = $display
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $order = Get-ModelOrder
    if ($List) {
        foreach ($name in $order.Ids.Keys) {
            $v = if ($order.Variants[$name]) { $order.Variants[$name] } else { '-' }
            '{0,-16} {1,-45} vendor {2,-9} variant {3}' -f $name, $order.Ids[$name], $order.Vendors[$name], $v
        }
    } else {
        "profile:        $($order.Profile)"
        "source:         $($order.Source)"
        "implementer:    $(if ($order.Implementer) { $order.Implementer -join ', ' } else { '(none: every name unfilled)' })"
        "reviewer:       $(if ($order.Reviewer) { $order.Reviewer -join ', ' } else { '(none: every name unfilled)' })"
        "task reviewer:  $($order.TaskReviewer)"
    }
}
