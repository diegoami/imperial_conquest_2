#Requires -Version 5.0
<#
.SYNOPSIS
Thin wrapper for scripts/generate-authored-assets.py (T51).

.DESCRIPTION
Forwards every argument to the Python generator. The generator is dry-run by
default; a real, billable run needs --key <AssetKey> (repeatable) or --all, and
reads its endpoint, model and API key from assets-generator.local.ini (git-ignored,
never committed, never printed) or the OPENROUTER_API_KEY environment variable.

Requires Python 3 with Pillow on PATH (Pillow is a generator-time dependency
only, not a build or CI dependency).

.EXAMPLE
./scripts/generate-authored-assets.ps1                    # dry run: prompts, counts, models, cost
./scripts/generate-authored-assets.ps1 --key army.tier1.icon   # real run: regenerate one key
./scripts/generate-authored-assets.ps1 --all              # real run: the full pack (22 billable images)
./scripts/generate-authored-assets.ps1 --reconform        # OFFLINE and free: rebuild the pack from rendered/authored-raw/
#>
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    $ForwardArgs
)

$ErrorActionPreference = "Stop"

$generator = Join-Path $PSScriptRoot "generate-authored-assets.py"

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) {
    $python = Get-Command py -ErrorAction SilentlyContinue
}

if ($python) {
    & $python.Source $generator @ForwardArgs
    exit $LASTEXITCODE
}

Write-Error "Python 3 was not found on PATH (with Pillow installed). The generator is scripts/generate-authored-assets.py; Pillow is a generator-time dependency only, not a build or CI dependency."
exit 1
