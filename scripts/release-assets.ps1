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

        Write-Step "scrubbed run: PATH='$env:PATH', DOTNET_ROOT unset; launching $Executable $($Arguments -join ' '
