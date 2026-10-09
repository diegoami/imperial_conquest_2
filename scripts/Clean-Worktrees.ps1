<#
.SYNOPSIS
    Lists the worktrees under ic2-work\, and -- on request -- removes the safe, old ones.

.DESCRIPTION
    T151 (Done-when 3; the user's decision of 2026-10-08: "relax the rules of deleting worktrees
    immediately after failure, throwing away diagnostics and work done. Rather plan some clean up
    regularly."). The dispatch scripts no longer delete anything: external-implement.ps1 keeps a
    failed attempt's work as a patch in its worktree, and external-review.ps1 keeps every
    attempt's review worktree (ic2-work\<pr>-review-<reviewer>-<yyyyMMdd-HHmmss>). This script is
    the scheduled cleanup that bounds the disk they grow by.

    It lists every worktree under the root (default: the ic2-work\ beside the main checkout),
    one line each: its path, branch or detached HEAD, age (last modification anywhere under it),
    PR state when the branch has one (gh pr list --head; a detached review tree's PR is read from
    its <pr>-review-... name, the pre-T151 <pr>-external-review-... names included), its size, and
    whether it is SAFE to remove.

    A tree is safe only when ALL of these hold:
      (a) `git status --porcelain --ignored` shows nothing outside bin/, obj/, .godot/ and .vs/
          -- matched at any depth as whole path components, so src/IC2.Engine/bin/ is noise too
          -- plus .opencode/, assets.local.ini and rendered/ matched only as the FIRST path
          -- component, i.e. at the tree root (src/rendered/x and docs/.opencode/y are work);
          -- the run's ignored diagnostics under a ROOT rendered/ are disposable once (c) holds
          -- (the user's decision of 2026-10-09, plan PR #894); uncommitted tracked changes and
          -- untracked files elsewhere still keep the tree;
      (b) no commit on it is absent from origin (its HEAD is contained in a remote-tracking ref;
          local knowledge only, no fetch happens here, so a stale fetch errs towards "not safe");
      (c) its branch's PR is merged or closed, or -- for a detached review tree -- the PR it
          reviewed is merged or closed. An open PR's trees are never safe, whatever their age.
    With no switch it only lists. -Apply removes the safe ones older than -OlderThanDays, with
    `git worktree remove` and then `git worktree prune`. -Force <path> removes one named tree
    whatever its state, after printing what it holds. It never touches the main checkout (a
    directory with a .git folder, not a worktree's .git file) or anything outside the root.

    It exits 0 and prints a one-line summary: "N worktrees, S safe to remove, R removed" (S is
    the count safe AND older than -OlderThanDays, the -Apply candidates; R what was actually
    removed). A refusal (-Force outside the root, or a main checkout) prints an error and exits 1.

.PARAMETER Root
    The directory holding the worktrees. Default: the ic2-work\ beside this repository's main
    checkout (resolved through the git common dir, so the script may run from a worktree).
    -SelfTest passes a temporary root in its place.
.PARAMETER Apply
    Remove the safe worktrees older than -OlderThanDays (git worktree remove, then git worktree
    prune). Without it, the script only lists.
.PARAMETER OlderThanDays
    The -Apply age threshold in days (default 7). Age is the newest modification anywhere under
    the tree, a build under bin/ included.
.PARAMETER Force
    Remove this one named worktree whatever its state, after printing what it holds. Must be
    under -Root.
.PARAMETER SelfTest
    Offline checks (no network, no real gh, no real ic2-work): temporary git repos under the TEMP
    folder build one worktree in each state the rules describe (clean and merged; dirty; ignored
    files under a root rendered/ only, both merged and with an open PR; a root .opencode/ only; a
    root assets.local.ini only; nested x/rendered/ and x/.opencode/ that still count as work; an
    unpushed commit; a detached review tree of an open PR, old; a detached review tree of a merged
    PR; a branch with a closed PR listed before an open one), and the list and -Apply are shown to
    treat each as the rules say. It also covers nested build output at src/IC2.Engine/bin/,
    godot/.godot/ and so on (R2), and that a path still present after `git worktree remove` is
    reported and left in place (R4). gh is stubbed in-process; nothing is billed.

.EXAMPLE
    pwsh scripts/Clean-Worktrees.ps1
.EXAMPLE
    pwsh scripts/Clean-Worktrees.ps1 -Apply -OlderThanDays 14
.EXAMPLE
    pwsh scripts/Clean-Worktrees.ps1 -Force C:\Users\diego\projects\ic2-work\590-review-luna-20261001-120000
#>
[CmdletBinding()]
param(
    [string] $Root = '',
    [switch] $Apply,
    [int] $OlderThanDays = 7,
    [string] $Force,
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'

# gh invoker, stubbed in-process by -SelfTest (the self-test never calls the real gh).
if (-not $script:Gh) { $script:Gh = { param([string[]] $GhArgs) & gh @GhArgs } }
function Invoke-Gh {
    param([string[]] $GhArgs)
    return (& $script:Gh $GhArgs)
}
# gh resolves its repository from the working directory: run it from the main checkout (the
# self-test's clone) so `pr list --head` and `pr view` answer for this repository.
function Invoke-GhRepo {
    param([string[]] $GhArgs)
    if ($script:RepoRoot -and (Test-Path -LiteralPath $script:RepoRoot)) {
        Push-Location -LiteralPath $script:RepoRoot
        try { return (Invoke-Gh $GhArgs) } finally { Pop-Location }
    }
    return (Invoke-Gh $GhArgs)
}

function Get-PrStateByBranch([string] $Branch) {
    # The branch's PR state (OPEN/MERGED/CLOSED), 'none' when it has no PR, $null when unknown.
    # R3: a branch can have several PRs returned by `gh pr list --state all` (an old merged or
    # closed one and a re-opened OPEN one). Read EVERY state: any open PR vetoes removal,
    # whatever order the response lists them in. Only when none is open is the tree's PR
    # MERGED/CLOSED (safe either way).
    $out = @()
    try { $out = @(Invoke-GhRepo @('pr', 'list', '--head', $Branch, '--state', 'all', '--json', 'number,state') | Where-Object { $_ }) } catch { $out = @() }
    $text = (($out | ForEach-Object { [string]$_ }) -join "`n").Trim()
    if (-not $text) { return $null }
    try { $prs = @($text | ConvertFrom-Json) } catch { return $null }
    if ($prs.Count -eq 0) { return 'none' }
    $states = @($prs | ForEach-Object { ([string]$_.state).ToUpperInvariant() })
    if ($states -contains 'OPEN') { return 'OPEN' }
    if ($states -contains 'MERGED') { return 'MERGED' }
    if ($states -contains 'CLOSED') { return 'CLOSED' }
    return ([string]$prs[0].state).ToUpperInvariant()
}

function Get-PrStateByNumber([string] $Number) {
    # A PR's state (OPEN/MERGED/CLOSED) by number, 'none' when it does not exist, $null when unknown.
    $out = @()
    try { $out = @(Invoke-GhRepo @('pr', 'view', $Number, '--json', 'state') | Where-Object { $_ }) } catch { $out = @() }
    $text = (($out | ForEach-Object { [string]$_ }) -join "`n").Trim()
    if (-not $text) { return $null }
    try { return ([string]($text | ConvertFrom-Json).state).ToUpperInvariant() } catch { return $null }
}

function Get-WorktreeStats([string] $Path) {
    # Age (the newest LastWriteTime anywhere under the tree, a build under bin/ included -- it is
    # activity) and size, in one pass. A foreach statement, not ForEach-Object: the script block's
    # scope would swallow $best and $sum.
    $best = (Get-Item -LiteralPath $Path -Force).LastWriteTime
    [long] $sum = 0
    foreach ($item in @(Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue)) {
        if ($item.LastWriteTime -gt $best) { $best = $item.LastWriteTime }
        $sum += $item.Length
    }
    return [pscustomobject]@{ Age = $best; Size = $sum }
}

function Format-Size([long] $Bytes) {
    if ($Bytes -ge 1GB) { return ('{0:N2} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N1} MB' -f ($Bytes / 1MB)) }
    return ('{0:N0} kB' -f ($Bytes / 1KB))
}

function Get-WorktreeNoise([string] $Path) {
    # Rule (a), as amended by the user's decision of 2026-10-09 (plan PR #894): the
    # `git status --porcelain --ignored` entries that are NOT allowed noise.
    #   - bin/, obj/, .godot/ and .vs/ are noise at ANY depth, as whole path components (so
    #     src/IC2.Engine/bin/ and godot/.godot/ are noise), never as substrings of a component.
    #   - .opencode/, assets.local.ini and rendered/ are noise ONLY as the FIRST path component,
    #     i.e. at the tree ROOT (so src/rendered/x and docs/.opencode/y still count as work).
    # Rule (a) does not judge by PR state -- (c) does -- so anything under a root rendered/ is
    # not work here: once the tree's PR is merged or closed, the run's ignored diagnostics there
    # are disposable (a root rendered/ whose PR is still open is kept by (c), not by (a)).
    # Uncommitted tracked changes and untracked files elsewhere still are work.
    # Returns the offending paths, empty when the tree holds nothing but allowed noise.
    $allowedAnyDepth = @('bin', 'obj', '.godot', '.vs')
    $allowedRoot = @('.opencode', 'assets.local.ini', 'rendered')
    $bad = @()
    $lines = @()
    try { $lines = @(git -C $Path status --porcelain --ignored 2>$null) } catch { $lines = @() }
    foreach ($l in $lines) {
        if (-not $l -or $l.Length -lt 4) { continue }
        $body = $l.Substring(3)
        foreach ($side in @($body -split ' -> ')) {
            $p = $side.Trim().Trim('"')
            if (-not $p) { continue }
            $parts = @($p -split '[\\/]' | Where-Object { $_ })
            $noise = $false
            foreach ($part in $parts) {
                if ($part -in $allowedAnyDepth) { $noise = $true; break }
            }
            if (-not $noise -and $parts.Count -ge 1 -and $parts[0] -in $allowedRoot) { $noise = $true }
            if (-not $noise) { $bad += $p }
        }
    }
    return @($bad | Select-Object -Unique)
}

function Test-AllCommitsOnOrigin([string] $Path) {
    # Rule (b): no commit on the tree is absent from origin -- its HEAD is contained in a
    # remote-tracking ref (ancestry covers every commit below HEAD). Local knowledge only: no
    # fetch here, so stale remote refs err towards "not safe".
    $sha = $null
    try { $sha = (git -C $Path rev-parse HEAD 2>$null) } catch { return $false }
    if (-not $sha) { return $false }
    $refs = @()
    try { $refs = @(git -C $Path branch -r --contains "$($sha.Trim())" 2>$null) } catch { return $false }
    return @($refs | Where-Object { "$_".Trim() }).Count -gt 0
}

function Get-WorktreeEntry([string] $Path) {
    # One worktree's facts, or $null when the directory is not a git worktree.
    $leaf = Split-Path $Path -Leaf
    $branch = $null
    try { $branch = (git -C $Path rev-parse --abbrev-ref HEAD 2>$null) } catch { $branch = $null }
    if (-not $branch) { return $null }
    $detached = ($branch.Trim() -eq 'HEAD')
    $stats = Get-WorktreeStats $Path
    $ageDays = ((Get-Date) - $stats.Age).TotalDays
    $prState = $null
    $branchText = ''
    $prText = ''
    if ($detached) {
        $branchText = 'detached HEAD'
        # A review tree's PR is read from its <pr>-review-... name (the pre-T151
        # <pr>-external-review-... names are recognised too, so old trees are also cleaned).
        $m = [regex]::Match($leaf, '^(\d+)-(external-)?review-')
        if ($m.Success) {
            $prState = Get-PrStateByNumber $m.Groups[1].Value
            $prText = "PR #$($m.Groups[1].Value) $(if ($prState) { $prState } else { 'state unknown' })"
        } else {
            $prText = 'detached, not a review tree'
        }
    } else {
        $branchText = $branch.Trim()
        $prState = Get-PrStateByBranch $branchText
        $prText = switch ($prState) {
            'none' { "no PR for $branchText" }
            { $null -eq $prState } { "PR state unknown for $branchText" }
            default { "PR for $branchText $prState" }
        }
    }
    # Safety (Done-when 3): all three rules must hold.
    $reasons = @()
    $noise = Get-WorktreeNoise $Path
    if ($noise) {
        $shown = @($noise | Select-Object -First 5)
        $reasons += "holds work: $($shown -join ', ')$(if ($noise.Count -gt 5) { " (+$($noise.Count - 5) more)" })"
    }
    if (-not (Test-AllCommitsOnOrigin $Path)) { $reasons += 'commits absent from origin' }
    if ($prState -notin @('MERGED', 'CLOSED')) {
        $why = switch ($prState) {
            'OPEN' { 'the PR is open' }
            'none' { "no PR ($prText)" }
            { $null -eq $prState } { 'PR state unknown' }
            default { "PR state $prState" }
        }
        $reasons += $why
    }
    return [pscustomobject]@{
        Path = $Path; Branch = $branchText; Detached = $detached
        Age = $stats.Age; AgeDays = $ageDays; SizeBytes = $stats.Size
        PrState = [string]$prState; PrText = $prText
        Safe = ($reasons.Count -eq 0); Reasons = $reasons
    }
}

function Format-WorktreeLine($Entry) {
    # One list line: path, branch or detached HEAD, age, PR state, size, safety (Done-when 3).
    $safeText = if ($Entry.Safe) { 'SAFE to remove' } else { 'NOT safe: ' + ($Entry.Reasons -join '; ') }
    return '{0} | {1} | {2:N1} days old | {3} | {4} | {5}' -f $Entry.Path, $Entry.Branch, $Entry.AgeDays, $Entry.PrText, (Format-Size $Entry.SizeBytes), $safeText
}

function Get-WorktreeEntries([string] $RootPath) {
    # Every git worktree directly under the root, in path order.
    $entries = @()
    foreach ($dir in @(Get-ChildItem -LiteralPath $RootPath -Directory -Force -ErrorAction SilentlyContinue | Sort-Object Name)) {
        $e = Get-WorktreeEntry $dir.FullName
        if ($e) { $entries += $e }
    }
    return $entries
}

function Test-WorktreeRemainder([string] $Path) {
    # R4: after `git worktree remove` and `git worktree prune`, a successful exit from git does NOT
    # establish that anything still at $Path is disposable. This reports the remainder and leaves
    # it -- it never recursively deletes it (that would be a second deletion mechanism outside the
    # safety checks). Returns $true only when the path is truly gone.
    if (Test-Path -LiteralPath $Path) {
        [Console]::Error.WriteLine("git worktree remove reported success but $Path still exists; leaving it in place (this script never recursively deletes a remainder).")
        return $false
    }
    return $true
}

function Remove-Worktree([string] $Path, [switch] $ForceRemove) {
    # Removes one worktree with `git worktree remove` (from its repository's main checkout, never
    # from the tree itself). Never the main checkout: a worktree's .git is a FILE, a main
    # checkout's is a directory. $ForceRemove adds --force (-Force, or an -Apply candidate git
    # refuses for stray files); without it a failure is reported, never forced.
    if (Test-Path -LiteralPath (Join-Path $Path '.git') -PathType Container) {
        [Console]::Error.WriteLine("refusing: $Path is a main checkout, not a worktree; this script never touches the main checkout.")
        return $false
    }
    $commonDir = $null
    try { $commonDir = (git -C $Path rev-parse --path-format=absolute --git-common-dir 2>$null) } catch { $commonDir = $null }
    if (-not $commonDir) {
        if ($ForceRemove -and (Test-Path -LiteralPath $Path)) {
            # Not a registered worktree: a leftover directory the user named with -Force.
            Remove-Item -Recurse -Force -LiteralPath $Path -ErrorAction SilentlyContinue
            return (-not (Test-Path -LiteralPath $Path))
        }
        [Console]::Error.WriteLine("not a git worktree: $Path")
        return $false
    }
    $repoRoot = (Split-Path $commonDir.Trim() -Parent)
    $gitArgs = @('worktree', 'remove') + $(if ($ForceRemove) { @('--force') })
    git -C $repoRoot @gitArgs $Path
    if ($LASTEXITCODE -ne 0) { return $false }
    # `git worktree prune` after the removal (Done-when 3). The repository must be resolved BEFORE
    # the tree goes: once the path is gone, `git -C $Path` no longer resolves, so pruning from the
    # removed path (the earlier order) did nothing.
    git -C $repoRoot worktree prune
    # R4: no recursive-delete fallback. A remainder is reported and left; Test-WorktreeRemainder
    # decides both the return value and the message.
    return (Test-WorktreeRemainder $Path)
}

function Invoke-CleanWorktreesSelfTest {
    # T151 Done-when 4: temporary worktrees in each state the rules describe, under a temp root
    # passed in place of ic2-work\, and the list and -Apply shown to treat each as Done-when 3
    # says. gh is stubbed in-process (never the real one); the only tools run are git and
    # file cmdlets, on throwaway repositories under the TEMP folder. Returns the failed count.
    $checks = New-Object System.Collections.Generic.List[object]
    function Add([string] $Name, [bool] $Ok) {
        $checks.Add([pscustomobject]@{ Name = $Name; Ok = [bool]$Ok }) | Out-Null
    }

    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('ic2-cleanwt-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    try {
        # A bare origin and a main clone: real git, throwaway, nothing pushed anywhere real.
        $origin = Join-Path $temp 'origin.git'
        $main = Join-Path $temp 'main'
        git init -q --bare -b main $origin
        git init -q -b main $main
        git -C $main config user.email selftest@local | Out-Null
        git -C $main config user.name selftest | Out-Null
        Set-Content -LiteralPath (Join-Path $main '.gitignore') -Value "rendered/`n.opencode/`nassets.local.ini`nbin/`nobj/`n.godot/`n.vs/`n" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $main 'base.txt') -Value 'base' -Encoding utf8
        git -C $main add -A
        git -C $main commit -q -m base
        git -C $main remote add origin $origin
        git -C $main push -q -u origin main
        $root = Join-Path $temp 'ic2-work'
        New-Item -ItemType Directory -Force -Path $root | Out-Null
        $script:RepoRoot = $main

        $ageTree = {
            # Make a tree "old": every LastWriteTime under it pushed back by N days.
            param([string] $Path, [int] $Days)
            Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.LastWriteTime = (Get-Date).AddDays(-$Days) }
            (Get-Item -LiteralPath $Path -Force).LastWriteTime = (Get-Date).AddDays(-$Days)
        }

        # 1. Clean and merged, recent (safe, but too young for -Apply: it stays).
        git -C $main worktree add -q (Join-Path $root 't1-merged') -b task/t1
        Set-Content -LiteralPath (Join-Path $root 't1-merged\one.txt') -Value 'one' -Encoding utf8
        # Tracked sources under src/ and godot/, so git reports the nested ignored build output
        # individually (a wholly ignored parent directory would be collapsed to `src/`).
        New-Item -ItemType Directory -Force -Path (Join-Path $root 't1-merged\src\IC2.Engine'), (Join-Path $root 't1-merged\godot') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 't1-merged\src\IC2.Engine\Program.cs') -Value 'class P { }' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root 't1-merged\godot\project.godot') -Value '; project' -Encoding utf8
        git -C (Join-Path $root 't1-merged') add -A
        git -C (Join-Path $root 't1-merged') commit -q -m t1
        git -C (Join-Path $root 't1-merged') push -q -u origin task/t1
        git -C $main merge -q --ff-only task/t1
        git -C $main push -q origin main
        $t1Sha = (git -C $main rev-parse HEAD).Trim()
        # R2: nested build output at any depth -- src/IC2.Engine/bin/, src/IC2.Engine/obj/,
        # godot/.godot/ and src/.vs/ -- is allowed noise, not work. The permitted names are
        # matched as whole path components, so only the first-component test (the old code)
        # counted these as work.
        foreach ($rel in @('src\IC2.Engine\bin\app.dll', 'src\IC2.Engine\obj\app.obj', 'godot\.godot\cache.db', 'src\.vs\project')) {
            $artifact = Join-Path (Join-Path $root 't1-merged') $rel
            New-Item -ItemType Directory -Force -Path (Split-Path $artifact -Parent) | Out-Null
            Set-Content -LiteralPath $artifact -Value 'artifact' -Encoding utf8
        }

        # 2. Dirty (a tracked file modified, uncommitted), merged branch, old.
        git -C $main worktree add -q (Join-Path $root 't2-dirty') -b task/t2
        git -C (Join-Path $root 't2-dirty') push -q -u origin task/t2
        Set-Content -LiteralPath (Join-Path $root 't2-dirty\base.txt') -Value 'dirty' -Encoding utf8
        & $ageTree (Join-Path $root 't2-dirty') 30

        # 3. Ignored files under a ROOT rendered/ only, merged branch, old. Amended 3(a) (plan PR
        # #894): once the PR is merged or closed the run's diagnostics there are disposable, so
        # this tree is SAFE now (it was work before the amendment).
        git -C $main worktree add -q (Join-Path $root 't3-rendered') -b task/t3
        git -C (Join-Path $root 't3-rendered') push -q -u origin task/t3
        New-Item -ItemType Directory -Force -Path (Join-Path $root 't3-rendered\rendered') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 't3-rendered\rendered\diagnostics.txt') -Value 'kept' -Encoding utf8
        & $ageTree (Join-Path $root 't3-rendered') 30

        # 4. An unpushed commit, old.
        git -C $main worktree add -q (Join-Path $root 't4-unpushed') -b task/t4
        Set-Content -LiteralPath (Join-Path $root 't4-unpushed\four.txt') -Value 'four' -Encoding utf8
        git -C (Join-Path $root 't4-unpushed') add -A
        git -C (Join-Path $root 't4-unpushed') commit -q -m t4
        & $ageTree (Join-Path $root 't4-unpushed') 30

        # 7. A branch whose `gh pr list --state all` response lists a CLOSED PR before an OPEN
        # one (R3): any open PR makes the tree not safe, whatever the response order. Clean and
        # pushed, so the open PR is the ONLY reason it is not safe.
        git -C $main worktree add -q (Join-Path $root 't7-closed-then-open') -b task/t7
        Set-Content -LiteralPath (Join-Path $root 't7-closed-then-open\seven.txt') -Value 'seven' -Encoding utf8
        git -C (Join-Path $root 't7-closed-then-open') add -A
        git -C (Join-Path $root 't7-closed-then-open') commit -q -m t7
        git -C (Join-Path $root 't7-closed-then-open') push -q -u origin task/t7
        & $ageTree (Join-Path $root 't7-closed-then-open') 30

        # 8 (amended 3(a), plan PR #894): a root .opencode/ (the tooling's own) is noise only at
        # the root. Clean, merged and old, so safe and old enough to remove.
        git -C $main worktree add -q (Join-Path $root 't8-opencode') -b task/t8
        New-Item -ItemType Directory -Force -Path (Join-Path $root 't8-opencode\.opencode') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 't8-opencode\.opencode\session.json') -Value '{}' -Encoding utf8
        git -C (Join-Path $root 't8-opencode') push -q -u origin task/t8
        & $ageTree (Join-Path $root 't8-opencode') 30

        # 9 (amended 3(a), plan PR #894): a root assets.local.ini is noise only at the root.
        git -C $main worktree add -q (Join-Path $root 't9-assets') -b task/t9
        Set-Content -LiteralPath (Join-Path $root 't9-assets\assets.local.ini') -Value 'token=x' -Encoding utf8
        git -C (Join-Path $root 't9-assets') push -q -u origin task/t9
        & $ageTree (Join-Path $root 't9-assets') 30

        # 10 (amended 3(a), plan PR #894): the root allowances do NOT reach into a subdirectory.
        # This tree has allowed noise at the root (.opencode/, rendered/) AND a nested x/rendered/
        # and x/.opencode/: only the nested ones are work. A tracked src/ file stops git collapsing
        # the nested ignored directories to `src/`.
        git -C $main worktree add -q (Join-Path $root 't10-nested') -b task/t10
        New-Item -ItemType Directory -Force -Path (Join-Path $root 't10-nested\src\IC2.Engine'), (Join-Path $root 't10-nested\rendered'), (Join-Path $root 't10-nested\.opencode') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 't10-nested\src\IC2.Engine\Program.cs') -Value 'class P { }' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root 't10-nested\rendered\ok.txt') -Value 'root noise' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root 't10-nested\.opencode\ok.json') -Value '{}' -Encoding utf8
        New-Item -ItemType Directory -Force -Path (Join-Path $root 't10-nested\src\rendered'), (Join-Path $root 't10-nested\src\.opencode') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 't10-nested\src\rendered\nested.txt') -Value 'work' -Encoding utf8
        Set-Content -LiteralPath (Join-Path $root 't10-nested\src\.opencode\nested.json') -Value '{}' -Encoding utf8
        git -C (Join-Path $root 't10-nested') add -A
        git -C (Join-Path $root 't10-nested') commit -q -m t10
        git -C (Join-Path $root 't10-nested') push -q -u origin task/t10
        & $ageTree (Join-Path $root 't10-nested') 30

        # 11 (amended 3(a), plan PR #894): a root rendered/ only, but its PR is OPEN. (a) is
        # satisfied (root rendered/ is not work); (c) is not, so the open PR is the ONLY reason.
        git -C $main worktree add -q (Join-Path $root 't3b-rendered-open') -b task/t3b
        New-Item -ItemType Directory -Force -Path (Join-Path $root 't3b-rendered-open\rendered') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 't3b-rendered-open\rendered\diagnostics.txt') -Value 'diag' -Encoding utf8
        git -C (Join-Path $root 't3b-rendered-open') push -q -u origin task/t3b
        & $ageTree (Join-Path $root 't3b-rendered-open') 30

        # 5. A detached review tree of an open PR, old.
        git -C $main worktree add -q --detach (Join-Path $root '777-review-luna-20260101-000000') $t1Sha
        & $ageTree (Join-Path $root '777-review-luna-20260101-000000') 30

        # 6. A detached review tree of a merged PR, old: safe, and old enough to remove.
        git -C $main worktree add -q --detach (Join-Path $root '888-review-sol-20260101-000000') $t1Sha
        & $ageTree (Join-Path $root '888-review-sol-20260101-000000') 30

        # gh is stubbed: no network, no real PRs.
        $script:BranchStates = @{
            'task/t1' = 'MERGED'
            'task/t2' = 'MERGED'
            'task/t3' = 'MERGED'
            'task/t3b' = 'OPEN'
            'task/t4' = 'MERGED'
            'task/t7' = '[{"number":700,"state":"CLOSED"},{"number":701,"state":"OPEN"}]'
            'task/t8' = 'MERGED'
            'task/t9' = 'MERGED'
            'task/t10' = 'MERGED'
        }
        $script:PrStates = @{ '777' = 'OPEN'; '888' = 'MERGED' }
        $script:Gh = {
            param([string[]] $GhArgs)
            if ($GhArgs -contains '--head') {
                $b = $GhArgs[($GhArgs.IndexOf('--head') + 1)]
                $s = $script:BranchStates[$b]
                if ($s) {
                    # A raw JSON response is passed through; a bare state is wrapped (R3).
                    if ("$s".TrimStart().StartsWith('[')) { return $s }
                    return "[{`"number`":1,`"state`":`"$s`"}]"
                }
                return '[]'
            }
            if ($GhArgs.Count -ge 2 -and $GhArgs[0] -eq 'pr' -and $GhArgs[1] -eq 'view') {
                $s = $script:PrStates[[string]$GhArgs[2]]
                if ($s) { return "{`"state`":`"$s`"}" }
            }
            return ''
        }

        # --- the list ---
        $entries = @(Get-WorktreeEntries $root)
        $lines = @($entries | ForEach-Object { Format-WorktreeLine $_ })
        $lines | ForEach-Object { Write-Host $_ }
        $listText = $lines -join "`n"
        Add 'list: eleven worktrees are listed, one line each' ($entries.Count -eq 11 -and $lines.Count -eq 11)
        Add 'list: every line names the path, the branch or detached HEAD, the age, the PR and safety' (@($lines | Where-Object { $_ -match ' \| ' }).Count -eq 11)
        $t1 = $entries | Where-Object { $_.Path -like '*t1-merged' }
        Add 'clean and merged, recent: SAFE' ($t1.Safe -and $t1.PrState -eq 'MERGED' -and $t1.AgeDays -lt 7)
        Add 'R2: nested build output (src/.../bin, src/.../obj, godot/.godot, src/.vs) is noise, not work' ((Get-WorktreeNoise (Join-Path $root 't1-merged')).Count -eq 0)
        $t2 = $entries | Where-Object { $_.Path -like '*t2-dirty' }
        Add 'dirty: NOT safe, and the work is named' (-not $t2.Safe -and (($t2.Reasons -join '; ') -match 'base\.txt'))
        $t3 = $entries | Where-Object { $_.Path -like '*t3-rendered' }
        Add 'amended 3(a): ignored files under a ROOT rendered/ only, merged PR: SAFE (disposable), no other reason' ($t3.Safe -and $t3.PrState -eq 'MERGED')
        $t3b = $entries | Where-Object { $_.Path -like '*t3b-rendered-open*' }
        Add 'amended 3(a): a ROOT rendered/ only with an OPEN PR: NOT safe, and the ONLY reason is the open PR (the rendered/ files are not work)' (-not $t3b.Safe -and $t3b.PrState -eq 'OPEN' -and (($t3b.Reasons -join '; ') -notmatch 'holds work') -and (($t3b.Reasons -join '; ') -match 'open'))
        $t8 = $entries | Where-Object { $_.Path -like '*t8-opencode' }
        Add 'amended 3(a): a ROOT .opencode/ only, merged PR: SAFE (the tooling''s own)' ($t8.Safe -and $t8.PrState -eq 'MERGED' -and (Get-WorktreeNoise (Join-Path $root 't8-opencode')).Count -eq 0)
        $t9 = $entries | Where-Object { $_.Path -like '*t9-assets' }
        Add 'amended 3(a): a ROOT assets.local.ini only, merged PR: SAFE' ($t9.Safe -and $t9.PrState -eq 'MERGED' -and (Get-WorktreeNoise (Join-Path $root 't9-assets')).Count -eq 0)
        $t10 = $entries | Where-Object { $_.Path -like '*t10-nested' }
        $t10Noise = @(Get-WorktreeNoise (Join-Path $root 't10-nested'))
        Add 'amended 3(a): a nested x/rendered/ or x/.opencode/ still counts as work; the root allowances do not reach it' (-not $t10.Safe -and $t10Noise.Count -ge 1 -and (@($t10Noise | Where-Object { $_ -notmatch '^src[\\/]' }).Count -eq 0))
        $t4 = $entries | Where-Object { $_.Path -like '*t4-unpushed' }
        Add 'an unpushed commit: NOT safe (commits absent from origin)' (-not $t4.Safe -and (($t4.Reasons -join '; ') -match 'absent from origin'))
        $t5 = $entries | Where-Object { $_.Path -like '*777-review-luna*' }
        Add 'a detached review tree of an open PR, old: NOT safe (open PR), branch shown as detached' (-not $t5.Safe -and $t5.Detached -and (($t5.Reasons -join '; ') -match 'open') -and $t5.PrState -eq 'OPEN')
        $t6 = $entries | Where-Object { $_.Path -like '*888-review-sol*' }
        Add 'a detached review tree of a merged PR, old: SAFE' ($t6.Safe -and $t6.Detached -and $t6.PrState -eq 'MERGED' -and $t6.AgeDays -gt 7)
        $t7 = $entries | Where-Object { $_.Path -like '*t7-closed-then-open*' }
        Add 'R3: a CLOSED PR listed before an OPEN one: NOT safe (any open PR vetoes), state OPEN' (-not $t7.Safe -and $t7.PrState -eq 'OPEN' -and (($t7.Reasons -join '; ') -match 'open'))

        # --- -Apply: the safe ones older than -OlderThanDays only ---
        $candidates = @($entries | Where-Object { $_.Safe -and $_.AgeDays -gt 7 })
        Add 'apply candidates: exactly the old safe ones (the merged review tree and the three amended-3(a) root-noise trees)' ($candidates.Count -eq 4 -and (@($candidates | Where-Object { $_.Path -like '*888-review-sol*' -or $_.Path -like '*t3-rendered' -or $_.Path -like '*t8-opencode' -or $_.Path -like '*t9-assets' }).Count -eq 4))
        $removedCount = 0
        foreach ($c in $candidates) {
            Write-Host "removing: $($c.Path)"
            if (Remove-Worktree $c.Path) { $removedCount++ } else { Add "apply: $($c.Path) could not be removed" $false }
        }
        Add 'apply: the merged review tree is gone' (-not (Test-Path (Join-Path $root '888-review-sol-20260101-000000')))
        Add 'apply: the amended-3(a) root-noise trees are gone (rendered/, .opencode/, assets.local.ini)' (((@('t3-rendered', 't8-opencode', 't9-assets') | Where-Object { Test-Path (Join-Path $root $_) }).Count) -eq 0)
        Add 'apply: `git worktree list` no longer names the removed tree (remove then prune)' (@((git -C $main worktree list --porcelain) -like '*888-review-sol*').Count -eq 0)
        Add 'apply: every other tree is kept' (((@('t1-merged', 't2-dirty', 't4-unpushed', '777-review-luna-20260101-000000', 't7-closed-then-open', 't10-nested', 't3b-rendered-open') | Where-Object { -not (Test-Path (Join-Path $root $_)) }).Count) -eq 0)
        $summary = '{0} worktrees, {1} safe to remove, {2} removed' -f $entries.Count, $candidates.Count, $removedCount
        Write-Host $summary
        Add 'summary: "N worktrees, S safe to remove, R removed"' ($summary -eq '11 worktrees, 4 safe to remove, 4 removed')

        # --- -Force: one named tree, whatever its state ---
        $target = Join-Path $root 't2-dirty'
        Write-Host "force: $(Format-WorktreeLine $t2)"
        $forced = Remove-Worktree $target -ForceRemove
        Add 'force: a dirty tree is removed when named' ($forced -and -not (Test-Path $target))
        # A path outside the root is refused.
        $outsideOk = $false
        try {
            $resolved = (Resolve-Path -LiteralPath (Join-Path $temp 'origin.git')).Path
            $rootFull = (Resolve-Path -LiteralPath $root).Path
            $outsideOk = -not $resolved.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)
        } catch { $outsideOk = $false }
        Add 'force: a path outside the root is recognised and would be refused' ($outsideOk)

        # --- R4: a remainder after `git worktree remove` is reported and left, never deleted ---
        $leftover = Join-Path $root 'leftover-after-remove'
        New-Item -ItemType Directory -Force -Path $leftover | Out-Null
        Set-Content -LiteralPath (Join-Path $leftover 'keep.txt') -Value 'keep' -Encoding utf8
        Add 'R4: a path still present after git worktree remove is reported and left in place, not recursively deleted' ((-not (Test-WorktreeRemainder $leftover)) -and (Test-Path -LiteralPath (Join-Path $leftover 'keep.txt')))
    } catch {
        Add 'the self-test ran without error' $false
        Write-Host "self-test error: $_"
    } finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }

    $i = 0; $failed = 0
    foreach ($c in $checks) {
        $i++
        $status = if ($c.Ok) { 'PASS' } else { 'FAIL' }
        "[{0,2}/{1}] {2}  -- {3}" -f $i, $checks.Count, $status, $c.Name | Write-Host
        if (-not $c.Ok) { $failed++ }
    }
    Write-Host "self-test: $($checks.Count - $failed)/$($checks.Count) passed"
    return [int]$failed
}

if ($SelfTest) { exit (Invoke-CleanWorktreesSelfTest) }

# --- main -------------------------------------------------------------------------------------------
# The main checkout this script's repository belongs to (the script may run from a worktree).
$script:RepoRoot = (git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $script:RepoRoot) { $script:RepoRoot = $null }
if (-not $Root) {
    if (-not $script:RepoRoot) { throw 'Give -Root, or run the script from inside the repository.' }
    $commonDir = (git -C $script:RepoRoot rev-parse --path-format=absolute --git-common-dir).Trim()
    $mainRoot = Split-Path $commonDir -Parent
    $Root = Join-Path (Split-Path $mainRoot -Parent) 'ic2-work'
}
if (-not (Test-Path -LiteralPath $Root)) {
    Write-Host "0 worktrees, 0 safe to remove, 0 removed (no such root: $Root)"
    exit 0
}

$entries = @(Get-WorktreeEntries $Root)
$candidates = @($entries | Where-Object { $_.Safe -and $_.AgeDays -gt $OlderThanDays })
$removed = 0

if ($Force) {
    $rootFull = (Resolve-Path -LiteralPath $Root).Path
    $target = $null
    try { $target = (Resolve-Path -LiteralPath $Force).Path } catch { $target = $null }
    if (-not $target) { [Console]::Error.WriteLine("-Force: no such path: $Force"); exit 1 }
    if (-not $target.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        [Console]::Error.WriteLine("refusing: $target is not under $rootFull; this script never removes anything outside its root.")
        exit 1
    }
    # Print what it holds, then remove it whatever its state.
    $entry = $entries | Where-Object { $_.Path -eq $target }
    if ($entry) {
        Write-Host (Format-WorktreeLine $entry)
    } else {
        Write-Host "$target (not a listed worktree)"
    }
    $status = @(git -C $target status --porcelain --ignored 2>$null)
    if ($status) {
        Write-Host 'what it holds:'
        @($status | Select-Object -First 20) | ForEach-Object { Write-Host "  $_" }
        if ($status.Count -gt 20) { Write-Host "  (+$($status.Count - 20) more)" }
    } else {
        Write-Host 'what it holds: nothing (clean)'
    }
    if (Remove-Worktree $target -ForceRemove) { $removed = 1 }
    else { [Console]::Error.WriteLine("could not remove $target") }
    $summary = '{0} worktrees, {1} safe to remove, {2} removed' -f $entries.Count, $candidates.Count, $removed
    Write-Host $summary
    exit 0
}

foreach ($e in $entries) { Write-Host (Format-WorktreeLine $e) }

if ($Apply) {
    foreach ($c in $candidates) {
        Write-Host "removing: $($c.Path) ($(Format-Size $c.Size), $($c.PrText))"
        if (Remove-Worktree $c.Path) { $removed++ }
        else { [Console]::Error.WriteLine("could not remove $($c.Path); left in place") }
    }
}

$summary = '{0} worktrees, {1} safe to remove, {2} removed' -f $entries.Count, $candidates.Count, $removed
Write-Host $summary
exit 0
