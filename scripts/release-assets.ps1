<#
.SYNOPSIS
    Builds, or attaches, one tag's Windows release assets: the zip and the installer.

.DESCRIPTION
    T131 (docs/tasks/T131.md). Two modes.

    BUILD (default): checks the tag out into a scratch worktree, packages it with scripts/package.ps1,
    smoke-tests the package headless, zips it and builds the Inno Setup installer, then tests the
    installer (silent install, smoke, silent uninstall). Writes, under the git-ignored
    rendered/release/<tag>/:
        ImperialConquest2-<tag>-windows-x64.zip           (the ic2 folder at the zip's top)
        ImperialConquest2-<tag>-windows-x64-setup.exe
    and prints each file's path, size and SHA-256. It never touches a Release.

    A tag may predate the packaging files. When the tag lacks godot/export_presets.cfg or
    scripts/package.ps1 (v0.4.0 lacks both), the running checkout's copy is copied into the scratch
    worktree and a line says so. Nothing else is overlaid: a tag whose game code those files cannot
    export fails, and is reported, not patched.

    UPLOAD (-Upload): builds nothing. Attaches the two files in -AssetsDir to the tag's Release and
    never replaces an asset: a Release that already holds either name is refused (exit 3). A Release
    that does not exist is created as a draft pre-release. Replacing a published asset is the user's
    act, by hand.

.PARAMETER Tag
    The git tag to build or attach.

.PARAMETER Godot
    The Godot 4.7.2 .NET console executable, passed to package.ps1 -Godot. Defaults to package.ps1's
    own lookup (godot.cmd, then godot, on PATH).

.PARAMETER Iscc
    Path to ISCC.exe (Inno Setup 6). Defaults to PATH, then the Program Files (x86) install, then the
    per-user install under %LOCALAPPDATA%\Programs.

.PARAMETER SmokeOnly
    Do not pass -Verify to package.ps1 (its windowed screenshot tour). The headless smoke always runs.

.PARAMETER SkipInstallerTest
    Skip step 7, the silent install / smoke / silent uninstall.

.PARAMETER Upload
    Attach mode (see above).

.PARAMETER AssetsDir
    With -Upload: the directory holding the two files. Default rendered/release/<tag>/.

.PARAMETER DryRun
    With -Upload: print the decision (would create, would upload, refused) and change nothing.

.EXAMPLE
    pwsh scripts/release-assets.ps1 -Tag v0.4.0
    pwsh scripts/release-assets.ps1 -Tag v0.4.1 -Upload -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Godot,
    [string]$Iscc,
    [switch]$SmokeOnly,
    [switch]$SkipInstallerTest,
    [switch]$Upload,
    [string]$AssetsDir,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $RepoRoot -or -not (Test-Path -LiteralPath (Join-Path $RepoRoot 'IC2.sln'))) {
    throw "release-assets: could not find the repository root from '$PSScriptRoot'."
}
if ($Tag -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
    throw "release-assets: '$Tag' is not a usable tag name."
}

# The AppId is fixed (installer/ic2.iss); the installer test checks it is not registered.
$AppId = '{5B7E2C1A-6F3D-4E8B-9A41-1C2D3E4F5A6B}'
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${AppId}_is1"

$ZipName = "ImperialConquest2-$Tag-windows-x64.zip"
$SetupName = "ImperialConquest2-$Tag-windows-x64-setup.exe"
$Version = $Tag -replace '^v', ''
$OutDir = Join-Path $RepoRoot "rendered/release/$Tag"

function Write-Step([string]$Message) { Write-Host "release-assets: $Message" }

function Invoke-Git {
    param([string[]]$Arguments)
    $output = & git @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') exited with code $LASTEXITCODE`: $output" }
    return $output
}

# ---------------------------------------------------------------------------------------------------
# Upload mode
# ---------------------------------------------------------------------------------------------------

function Invoke-UploadMode {
    $names = @($ZipName, $SetupName)

    $json = & gh release view $Tag --json isDraft,assets 2>&1
    $viewCode = $LASTEXITCODE
    $exists = $true
    $held = @()
    if ($viewCode -ne 0) {
        if (($json -join "`n") -match 'release not found') {
            $exists = $false
        }
        else {
            throw "gh release view $Tag failed: $json"
        }
    }
    else {
        $release = ($json -join "`n") | ConvertFrom-Json
        $held = @($release.assets | ForEach-Object { $_.name })
    }

    $clashes = @($names | Where-Object { $held -contains $_ })
    if ($clashes.Count -gt 0) {
        foreach ($name in $clashes) {
            Write-Host "release-assets: refused: $Tag's release already has $name; assets are never replaced by the script; re-upload by hand if they must change"
        }
        return 3
    }

    $dir = if ($AssetsDir) { $AssetsDir } else { $OutDir }
    $files = @($names | ForEach-Object { Join-Path $dir $_ })

    if ($DryRun) {
        if ($exists) { Write-Host "release-assets: would upload $($names -join ' and ') to the existing release of $Tag" }
        else { Write-Host "release-assets: would create a draft pre-release for $Tag, then upload $($names -join ' and ')" }
        return 0
    }

    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath $file)) { throw "release-assets: $file does not exist; nothing to attach." }
    }

    if (-not $exists) {
        Write-Step "no release for $Tag; creating a draft pre-release"
        & gh release create $Tag --draft --prerelease --verify-tag --title $Tag `
            --notes 'Draft created by the release workflow; the release note is written per release-plan.md §4.'
        if ($LASTEXITCODE -ne 0) { throw "gh release create $Tag failed with code $LASTEXITCODE." }
    }

    Write-Step "uploading $($names -join ' and ') to $Tag"
    & gh release upload $Tag @files
    if ($LASTEXITCODE -ne 0) { throw "gh release upload $Tag failed with code $LASTEXITCODE." }
    Write-Step "attached to $Tag"
    return 0
}

if ($Upload) {
    try { $code = Invoke-UploadMode }
    catch {
        [Console]::Error.WriteLine("release-assets: ERROR: $($_.Exception.Message)")
        exit 1
    }
    exit $code
}

# ---------------------------------------------------------------------------------------------------
# Build mode
# ---------------------------------------------------------------------------------------------------

# Runs an executable with a scrubbed environment (the same scrub as package.ps1's
# Invoke-WithScrubbedEnvironment, which is not dot-sourceable): PATH reduced to the Windows system
# directories, every DOTNET_ROOT spelling unset. Returns the exit code; output goes to -LogPath.
function Invoke-WithScrubbedEnvironment {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogPath
    )

    $names = @('DOTNET_ROOT', 'DOTNET_ROOT_X86', 'DOTNET_ROOT(x86)')
    $saved = @{}
    foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    $savedPath = $env:PATH
    try {
        $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
        foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
        if ($env:PATH -match 'Godot' -or $env:PATH -match 'dotnet') { throw "the scrubbed PATH still names Godot or dotnet: $env:PATH" }

        Write-Step "scrubbed run: PATH='$env:PATH', DOTNET_ROOT unset; launching $Executable $($Arguments -join ' ')"
        # Start-Process: the export is a GUI-subsystem executable, which `&` does not wait for.
        $stdout = "$LogPath.stdout"
        $stderr = "$LogPath.stderr"
        $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -Wait -PassThru `
            -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        Get-Content -LiteralPath $stdout -ErrorAction SilentlyContinue | Set-Content -LiteralPath $LogPath -Encoding utf8
        Get-Content -LiteralPath $stderr -ErrorAction SilentlyContinue | Add-Content -LiteralPath $LogPath -Encoding utf8
        Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
        return $process.ExitCode
    }
    finally {
        $env:PATH = $savedPath
        foreach ($name in $names) {
            [Environment]::SetEnvironmentVariable($name, $(if ($null -ne $saved[$name]) { [string]$saved[$name] } else { $null }), 'Process')
        }
    }
}

# The headless smoke: the game starts, runs 600 frames and quits. Passes on exit 0 and no
# SCRIPT ERROR line in the captured output.
function Invoke-Smoke {
    param([string]$GameExe, [string]$LogPath, [string]$What)
    Write-Step "headless smoke ($What)"
    $code = Invoke-WithScrubbedEnvironment -Executable $GameExe -Arguments @('--headless', '--quit-after', '600') -LogPath $LogPath
    if ($code -ne 0) { throw "the headless smoke ($What) exited with code $code; see $LogPath." }
    if (Select-String -LiteralPath $LogPath -Pattern 'SCRIPT ERROR' -Quiet) {
        throw "the headless smoke ($What) logged a SCRIPT ERROR; see $LogPath."
    }
    Write-Step "headless smoke ($What) passed"
}

function Resolve-Iscc {
    $candidates = @()
    if ($Iscc) {
        if (Test-Path -LiteralPath $Iscc) { return (Resolve-Path -LiteralPath $Iscc).Path }
        $candidates += $Iscc
    }
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $candidates += 'ISCC.exe on PATH'
    $pf86 = ${env:ProgramFiles(x86)}
    foreach ($candidate in @(
            $(if ($pf86) { Join-Path $pf86 'Inno Setup 6\ISCC.exe' }),
            $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }))) {
        if (-not $candidate) { continue }
        if (Test-Path -LiteralPath $candidate) { return $candidate }
        $candidates += $candidate
    }
    throw "ISCC.exe (Inno Setup 6) was not found. Looked at: $($candidates -join '; ')."
}

function Wait-ForUninstaller {
    # Inno's uninstaller re-launches itself from %TEMP% (is-*.tmp\unins000.tmp) and the first process
    # exits at once, so wait on the relaunched one too.
    $deadline = (Get-Date).AddSeconds(180)
    do {
        Start-Sleep -Milliseconds 500
        $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
                $_.ProcessName -like 'unins*' -or ($_.Path -and $_.Path -like '*\is-*.tmp\*')
            })
    } while ($running.Count -gt 0 -and (Get-Date) -lt $deadline)
    if ($running.Count -gt 0) { throw 'the uninstaller was still running after 180 seconds.' }
}

function Invoke-Uninstall([string]$InstallDir) {
    $uninstaller = Join-Path $InstallDir 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw "the install holds no $uninstaller." }
    $p = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "the silent uninstall exited with code $($p.ExitCode)." }
    Wait-ForUninstaller
}

function Invoke-InstallerTest {
    param([string]$SetupExe, [string]$LogDir)
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ("ic2-install-test-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $installed = $false
    $uninstalled = $false
    try {
        Write-Step "installer test: silent install into $scratch"
        $p = Start-Process -FilePath $SetupExe -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=$scratch") -Wait -PassThru
        if ($p.ExitCode -ne 0) { throw "the silent install exited with code $($p.ExitCode)." }
        $installed = $true
        $installedExe = Join-Path $scratch 'game/IC2.MapViewer.exe'
        if (-not (Test-Path -LiteralPath $installedExe)) { throw "the install holds no $installedExe." }

        Invoke-Smoke -GameExe $installedExe -LogPath (Join-Path $LogDir 'smoke-installed.log') -What 'installed copy'

        Write-Step 'installer test: silent uninstall'
        Invoke-Uninstall -InstallDir $scratch
        $uninstalled = $true
        $left = @()
        if (Test-Path -LiteralPath $scratch) { $left = @(Get-ChildItem -LiteralPath $scratch -Recurse -File -Force) }
        if ($left.Count -gt 0) {
            throw "the uninstall left $($left.Count) file(s) in ${scratch}: $(($left | Select-Object -First 5 | ForEach-Object { $_.FullName }) -join ', ')"
        }
        if (Test-Path -LiteralPath $UninstallKey) { throw 'the uninstall left the AppId registered.' }
        Write-Step 'installer test passed: install, smoke and uninstall left no file'
    }
    finally {
        # Never leave the test's registration behind, even when a step above failed.
        if ($installed -and -not $uninstalled) {
            try { Invoke-Uninstall -InstallDir $scratch } catch { Write-Warning "cleanup uninstall failed: $($_.Exception.Message)" }
        }
        if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

$worktree = Join-Path $OutDir 'src'
$worktreeAdded = $false
$smokeScratch = $null
$exitCode = 1
try {
    # The installer test touches the player's real per-user registration: refuse before building
    # anything when the fixed AppId is already registered.
    if (-not $SkipInstallerTest -and (Test-Path -LiteralPath $UninstallKey)) {
        throw ("the installer test refuses to run: the AppId $AppId is already registered for this user ($UninstallKey). " +
            'Its silent uninstall would remove the real install. Pass -SkipInstallerTest to build without it.')
    }

    & git -C $RepoRoot rev-parse -q --verify "refs/tags/$Tag" *> $null
    if ($LASTEXITCODE -ne 0) { throw "the tag '$Tag' does not exist in $RepoRoot." }

    $isccExe = Resolve-Iscc
    Write-Step "using ISCC at $isccExe"

    # 1. The tag's tree, in a scratch worktree.
    if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
    & git -C $RepoRoot worktree prune *> $null
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Write-Step "checking out $Tag into $worktree"
    Invoke-Git @('-C', $RepoRoot, 'worktree', 'add', '--detach', $worktree, "refs/tags/$Tag^{commit}") | Out-Null
    $worktreeAdded = $true

    # 2. Overlay the packaging files a tag predates.
    $runningCommit = (Invoke-Git @('-C', $RepoRoot, 'rev-parse', '--short', 'HEAD') | Select-Object -First 1)
    foreach ($relative in @('godot/export_presets.cfg', 'scripts/package.ps1')) {
        $target = Join-Path $worktree $relative
        if (Test-Path -LiteralPath $target) { continue }
        Write-Host "release-assets: $relative is not in $Tag; using $runningCommit's"
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath (Join-Path $RepoRoot $relative) -Destination $target
    }

    # 3. Package the tag's tree.
    $packageArgs = @('-NoProfile', '-File', (Join-Path $worktree 'scripts/package.ps1'))
    if ($Godot) {
        $godotCommand = Get-Command $Godot -ErrorAction SilentlyContinue
        if (-not $godotCommand) { throw "the Godot executable '$Godot' was not found." }
        $packageArgs += @('-Godot', $godotCommand.Source)
    }
    $verify = (Test-Path -LiteralPath (Join-Path $worktree 'godot/Checks/AssetPackSelectionCheck.tscn')) -and -not $SmokeOnly
    if ($verify) { $packageArgs += '-Verify' }
    $pwshExe = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
    if (-not (Test-Path -LiteralPath $pwshExe)) { $pwshExe = 'pwsh' }
    Write-Step "packaging $Tag$(if ($verify) { ' with -Verify' } else { ' (no -Verify)' })"
    & $pwshExe @packageArgs
    if ($LASTEXITCODE -ne 0) { throw "package.ps1 exited with code $LASTEXITCODE." }
    $icDir = Join-Path $worktree 'rendered/export/ic2'
    if (-not (Test-Path -LiteralPath (Join-Path $icDir 'game/IC2.MapViewer.exe'))) { throw "packaging produced no $icDir/game/IC2.MapViewer.exe." }

    # 4. The headless smoke, always, on a copy away from any repository.
    $smokeScratch = Join-Path ([IO.Path]::GetTempPath()) ("ic2-smoke-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $smokeScratch | Out-Null
    Copy-Item -LiteralPath $icDir -Destination (Join-Path $smokeScratch 'ic2') -Recurse -Force
    Invoke-Smoke -GameExe (Join-Path $smokeScratch 'ic2/game/IC2.MapViewer.exe') -LogPath (Join-Path $OutDir 'smoke-package.log') -What 'package'

    # 5. The zip, ic2\ at its top.
    $zipPath = Join-Path $OutDir $ZipName
    Write-Step "zipping to $zipPath"
    Compress-Archive -LiteralPath $icDir -DestinationPath $zipPath -CompressionLevel Optimal

    # 6. The installer, from the running checkout's script (a tag predates installer/ic2.iss).
    $iss = Join-Path $RepoRoot 'installer/ic2.iss'
    if (-not (Test-Path -LiteralPath $iss)) { throw "installer/ic2.iss is missing from $RepoRoot." }
    Write-Step 'building the installer'
    & $isccExe "/Q" "/DAppVersion=$Version" "/DSourceDir=$icDir" "/DOutputDir=$OutDir" "/FImperialConquest2-$Tag-windows-x64-setup" $iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC exited with code $LASTEXITCODE." }
    $setupPath = Join-Path $OutDir $SetupName
    if (-not (Test-Path -LiteralPath $setupPath)) { throw "ISCC produced no $setupPath." }

    # 7. The installer test.
    if ($SkipInstallerTest) { Write-Step 'skipping the installer test (-SkipInstallerTest)' }
    else { Invoke-InstallerTest -SetupExe $setupPath -LogDir $OutDir }

    # 8. Report.
    foreach ($file in @($zipPath, $setupPath)) {
        $item = Get-Item -LiteralPath $file
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $shown = $file.Substring($RepoRoot.Length).TrimStart('\', '/') -replace '\\', '/'
        Write-Host "release-assets: $shown $($item.Length) sha256 $hash"
    }
    $exitCode = 0
}
catch {
    [Console]::Error.WriteLine("release-assets: ERROR: $($_.Exception.Message)")
    $exitCode = 1
}
finally {
    if ($worktreeAdded) {
        & git -C $RepoRoot worktree remove --force $worktree *> $null
        if (Test-Path -LiteralPath $worktree) { Remove-Item -LiteralPath $worktree -Recurse -Force -ErrorAction SilentlyContinue }
        & git -C $RepoRoot worktree prune *> $null
    }
    if ($smokeScratch -and (Test-Path -LiteralPath $smokeScratch)) {
        Remove-Item -LiteralPath $smokeScratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit $exitCode
