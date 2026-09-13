<#
.SYNOPSIS
    Guards against benign Godot-headless-run churn in godot/project.godot and godot/MapViewer.cs.

.DESCRIPTION
    T01 (docs/build-orchestration-plan.md "Build scaffolding and CI") implements the caveat from
    docs/HANDOVER.md: running Godot headless against this project (for example
    `"<Godot install>\Godot_..._console.exe" --headless --path godot --quit-after 2`) regenerates
    godot/project.godot's header and flips godot/MapViewer.cs's line endings as a side effect, even
    when nothing meaningful changed. Committing that noise pollutes history and hides real changes
    to those two files in review.

    This script checks exactly those two files against HEAD:
      - godot/project.godot : reverted if the only difference from HEAD is confined to the file's
        leading header block (everything before the first "[section]" line) and/or whitespace, and
        the rest of the file (from the first section onward) is byte-identical once line endings
        are normalised.
      - godot/MapViewer.cs  : reverted if the only difference from HEAD is whitespace (this is
        where the line-ending flip shows up).

    A real, non-whitespace/non-header change in either file is left untouched - this script never
    discards genuine work, only Godot's own regenerated noise.

.OUTPUTS
    One status line per file, plus a summary. Exit code:
      0  - both files match HEAD exactly (clean tree; nothing to do).
      1  - at least one file had benign churn (now reverted) and/or a real, un-reverted change
           remains. The summary lines name every affected file explicitly.

.EXAMPLE
    pwsh scripts/check-godot-churn.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    $root = git rev-parse --show-toplevel 2>$null
    if (-not $root) {
        throw 'check-godot-churn: not inside a git repository.'
    }
    return $root.Trim()
}

function Test-HasDiffFromHead {
    param([string]$RepoRoot, [string]$RelativePath)
    git -C $RepoRoot diff --quiet HEAD -- $RelativePath
    return ($LASTEXITCODE -ne 0)
}

function Test-WhitespaceOnlyDiff {
    param([string]$RepoRoot, [string]$RelativePath)
    git -C $RepoRoot diff --quiet --ignore-all-space --ignore-blank-lines HEAD -- $RelativePath
    return ($LASTEXITCODE -eq 0)
}

function Test-HeaderOnlyDiff {
    # "Header" = everything up to (not including) the first Godot "[section]" line. Returns $true
    # only if a section marker exists in both versions and the body from there on is identical
    # once line endings are normalised.
    param([string]$RepoRoot, [string]$RelativePath, [string]$AbsolutePath)

    $headRaw = git -C $RepoRoot show "HEAD:$RelativePath" 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }
    $headLines = ($headRaw -join "`n") -split "`r?`n"

    if (-not (Test-Path -LiteralPath $AbsolutePath)) {
        return $false
    }
    $workingRaw = Get-Content -LiteralPath $AbsolutePath -Raw
    $workingLines = $workingRaw -split "`r?`n"

    $headSectionIdx = 0
    while ($headSectionIdx -lt $headLines.Count -and $headLines[$headSectionIdx] -notmatch '^\[') {
        $headSectionIdx++
    }
    $workSectionIdx = 0
    while ($workSectionIdx -lt $workingLines.Count -and $workingLines[$workSectionIdx] -notmatch '^\[') {
        $workSectionIdx++
    }

    if ($headSectionIdx -ge $headLines.Count -or $workSectionIdx -ge $workingLines.Count) {
        # No recognisable section marker in one of the two versions - don't guess, treat as real.
        return $false
    }

    $headBody = ($headLines[$headSectionIdx..($headLines.Count - 1)] -join "`n").TrimEnd()
    $workBody = ($workingLines[$workSectionIdx..($workingLines.Count - 1)] -join "`n").TrimEnd()

    return ($headBody -eq $workBody)
}

$repoRoot = Get-RepoRoot

$targets = @(
    [pscustomobject]@{ Relative = 'godot/project.godot'; Kind = 'header' }
    [pscustomobject]@{ Relative = 'godot/MapViewer.cs'; Kind = 'whitespace' }
)

$reverted = New-Object System.Collections.Generic.List[string]
$realChanges = New-Object System.Collections.Generic.List[string]
$statusLines = New-Object System.Collections.Generic.List[string]

foreach ($target in $targets) {
    $rel = $target.Relative
    $abs = Join-Path $repoRoot $rel

    if (-not (Test-Path -LiteralPath $abs)) {
        $statusLines.Add("${rel}: not present, skipping")
        continue
    }

    if (-not (Test-HasDiffFromHead -RepoRoot $repoRoot -RelativePath $rel)) {
        $statusLines.Add("${rel}: clean")
        continue
    }

    $isBenign = Test-WhitespaceOnlyDiff -RepoRoot $repoRoot -RelativePath $rel
    if (-not $isBenign -and $target.Kind -eq 'header') {
        $isBenign = Test-HeaderOnlyDiff -RepoRoot $repoRoot -RelativePath $rel -AbsolutePath $abs
    }

    if ($isBenign) {
        git -C $repoRoot checkout -- $rel
        $reverted.Add($rel)
        $statusLines.Add("${rel}: reverted (whitespace/header-only churn from a Godot headless run)")
    }
    else {
        $realChanges.Add($rel)
        $statusLines.Add("${rel}: left alone (real change, not whitespace/header-only)")
    }
}

$statusLines | ForEach-Object { Write-Output $_ }

if ($reverted.Count -gt 0) {
    Write-Output "check-godot-churn: reverted benign churn in: $($reverted -join ', ')"
}
if ($realChanges.Count -gt 0) {
    Write-Output "check-godot-churn: real change(s) remain, not reverted: $($realChanges -join ', ')"
}

if ($reverted.Count -gt 0 -or $realChanges.Count -gt 0) {
    exit 1
}

Write-Output 'check-godot-churn: clean tree, nothing to do.'
exit 0
