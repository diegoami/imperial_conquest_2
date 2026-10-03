<#
.SYNOPSIS
    Builds the Imperial Conquest 2 Godot game as a self-contained Windows export.

.DESCRIPTION
    T27 "Packaging" (docs/tasks/T27.md). Runs the `Windows Desktop` preset from
    godot/export_presets.cfg (x86_64, .NET) and assembles a launchable, self-contained folder under
    the git-ignored rendered/:

        rendered/export/ic2/
            game/                       <- res:// (the executable, its .pck and the .NET runtime)
                IC2.MapViewer.exe
                IC2.MapViewer.pck
                data_IC2.MapViewer_windows_x86_64/...
            data/                       <- copied beside the export, not the source tree
                worlds/ rulesets/ scenarios/
            assets/packs/               <- the packs the map draws from

    Why data/ and assets/ sit next to game/ rather than inside it: Godot globalizes res:// to the
    executable's own directory in an exported build, and the game's dev-time repository-root
    convention (GameSessionFactory.RepositoryRootFromGlobalizedResPath, shared by Slice.cs,
    GameDataContext and the asset-pack loader) is res:///.. . The package therefore makes that
    parent look like a repository root -- data/ and assets/packs/ are its siblings -- so the same
    path logic works with no source tree in sight. #454 item 5 (the asset pack outside res://) is
    handled by the same copy. The package is self-contained: rendered/export/ic2 can be moved
    anywhere and still runs.

    The .NET export needs a solution next to godot/IC2.MapViewer.csproj; this repository
    deliberately ships only the .csproj (godot/IC2.MapViewer.csproj is not in IC2.sln, so
    `dotnet build IC2.sln` works without the Godot SDK). Godot's exporter looks for
    godot/IC2.MapViewer.sln specifically. This script creates that solution when it is absent,
    exports, and removes it again in a finally block, so the tree is unchanged afterwards. A
    solution that was already there (for example, one the Godot editor generated) is left alone.

    The script exits non-zero on any failure: a missing tool, a failed build, a failed export, a
    world whose terrain sidecar (*.terrain.b64, T62) is not carried, or a missing asset pack.

.PARAMETER Godot
    The Godot 4.7.2 .NET editor/console executable to run the export with. Defaults to godot.cmd on
    PATH, then godot.

.PARAMETER Verify
    After building the package, copy it away from the repository layout into rendered/verify/ and
    launch it there with a scrubbed environment (PATH reduced to the Windows system directories,
    DOTNET_ROOT and friends unset) to prove the export needs neither Godot nor the .NET SDK:
      * headless, running the real Check scene that starts a game and draws from the asset pack
        (res://Checks/AssetPackSelectionCheck.tscn) -- captured exit 0;
      * windowed, running res://Checks/ScreenshotTour.tscn -- captured exit 0 plus a screenshot of
        the loaded scenario under rendered/verify/screenshots/;
      * with clasical-mediterranean's terrain sidecar deleted, the same check must fail loudly
        (non-zero exit, MissingTerrainSidecarException in the log).

    The exported build cannot run an arbitrary scene by command-line path: the official release
    template is compiled with path overrides disabled. The verify copies instead write Godot's
    supported override.cfg next to the executable to point run/main_scene at the check scene for
    the duration of the run. The override never enters the shipped package.

.PARAMETER KeepSolution
    Leave a generated godot/IC2.MapViewer.sln in place (diagnostics only; the default removes it).

.EXAMPLE
    pwsh scripts/package.ps1
    pwsh scripts/package.ps1 -Verify
#>
[CmdletBinding()]
param(
    [string]$Godot,
    [switch]$Verify,
    [switch]$KeepSolution
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $RepoRoot -or -not (Test-Path -LiteralPath (Join-Path $RepoRoot 'IC2.sln'))) {
    throw "package: could not find the repository root from '$PSScriptRoot'."
}

$GodotDir = Join-Path $RepoRoot 'godot'
$CsprojPath = Join-Path $GodotDir 'IC2.MapViewer.csproj'
$SolutionPath = Join-Path $GodotDir 'IC2.MapViewer.sln'
$PresetName = 'Windows Desktop'

$ExportRoot = Join-Path $RepoRoot 'rendered/export/ic2'
$GameDir = Join-Path $ExportRoot 'game'
$ExeName = 'IC2.MapViewer.exe'
$ExePath = Join-Path $GameDir $ExeName
$PckPath = Join-Path $GameDir 'IC2.MapViewer.pck'

$VerifyRoot = Join-Path $RepoRoot 'rendered/verify'
$VerifyPackage = Join-Path $VerifyRoot 'ic2'
$VerifyGame = Join-Path $VerifyPackage 'game'
$VerifyExe = Join-Path $VerifyGame $ExeName
$ScreenshotDir = Join-Path $VerifyRoot 'screenshots'

function Write-Step([string]$Message) { Write-Host "package: $Message" }

function Fail([string]$Message) {
    [Console]::Error.WriteLine("package: ERROR: $Message")
    exit 1
}

# Runs a native command, throws on a non-zero exit code, returns nothing.
function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($Arguments -join ' ')' exited with code $LASTEXITCODE."
    }
}

function Resolve-Godot {
    param([string]$Requested)
    if ($Requested) {
        $command = Get-Command $Requested -ErrorAction SilentlyContinue
        if (-not $command) { Fail "the Godot executable '$Requested' was not found." }
        return $command.Source
    }
    foreach ($candidate in @('godot.cmd', 'godot')) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }
    Fail "neither godot.cmd nor godot is on PATH; pass -Godot <path>."
}

# Creates godot/IC2.MapViewer.sln when it is absent, so Godot's .NET exporter has the solution file
# it insists on. Returns $true when this script created it (and must remove it again).
function Ensure-Solution {
    if (Test-Path -LiteralPath $SolutionPath) { return $false }
    Write-Step "generating a temporary $([System.IO.Path]::GetFileName($SolutionPath)) for the .NET export"
    Invoke-Checked -FilePath 'dotnet' -Arguments @('new', 'sln', '--name', 'IC2.MapViewer', '--output', $GodotDir, '--format', 'sln')
    Invoke-Checked -FilePath 'dotnet' -Arguments @('sln', $SolutionPath, 'add', $CsprojPath)
    return $true
}

# Writes Godot's supported override.cfg next to an executable so the run boots the given scene. Used
# only inside rendered/verify/; the shipped package never carries it.
function Set-OverrideMainScene {
    param([string]$GameDirectory, [string]$ScenePath)
    $content = "[application]`nrun/main_scene=`"$ScenePath`"`n"
    Set-Content -LiteralPath (Join-Path $GameDirectory 'override.cfg') -Value $content -NoNewline
}

# Runs an executable with a scrubbed environment: PATH reduced to the Windows system directories,
# and every DOTNET_ROOT spelling unset. Extra variables (only IC2_SCREENSHOT_DIR is used) are passed
# through and restored. Returns the process exit code; all output goes to -LogPath.
function Invoke-WithScrubbedEnvironment {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogPath,
        [hashtable]$ExtraEnvironment = @{}
    )

    $names = @('DOTNET_ROOT', 'DOTNET_ROOT_X86', 'DOTNET_ROOT(x86)') + @($ExtraEnvironment.Keys)
    $saved = @{}
    foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    $savedPath = $env:PATH

    try {
        $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
        foreach ($name in $names) {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
        foreach ($key in $ExtraEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($key, [string]$ExtraEnvironment[$key], 'Process')
        }

        if ($env:PATH -match 'Godot') { Fail "the scrubbed PATH still contains Godot: $env:PATH" }
        if ($env:PATH -match 'dotnet') { Fail "the scrubbed PATH still contains dotnet: $env:PATH" }

        Write-Step "scrubbed run: PATH='$env:PATH', DOTNET_ROOT unset; launching $Executable $($Arguments -join ' ')"

        # Start-Process rather than a pipeline: the release export is a GUI-subsystem executable, and
        # PowerShell's `*>` redirection captures nothing (and leaves $LASTEXITCODE null) for it.
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
            if ($null -ne $saved[$name]) {
                [Environment]::SetEnvironmentVariable($name, [string]$saved[$name], 'Process')
            }
            else {
                [Environment]::SetEnvironmentVariable($name, $null, 'Process')
            }
        }
    }
}

function Copy-Tree {
    param([string]$Source, [string]$Destination)
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
}

# Confirms the package carries everything the game loads at run time, and fails loudly when a world
# names a terrain sidecar (*.terrain.b64, T62) that is not there. A world is two files; a missing
# sidecar makes GameDataRepository.Load throw for every scenario, so it must never ship.
function Assert-PackageContents {
    param([string]$Root)

    $dataRoot = Join-Path $Root 'data'
    foreach ($sub in @('worlds', 'rulesets', 'scenarios')) {
        $directory = Join-Path $dataRoot $sub
        if (-not (Test-Path -LiteralPath $directory)) { Fail "the package is missing data/$sub." }
        if (@(Get-ChildItem -LiteralPath $directory -Filter '*.json' -File).Count -eq 0) {
            Fail "the package carries no data/$sub/*.json."
        }
    }

    $worlds = @(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'worlds') -Filter '*.json' -File)
    foreach ($world in $worlds) {
        $document = Get-Content -LiteralPath $world.FullName -Raw | ConvertFrom-Json
        $terrain = $document.terrain
        if ($null -eq $terrain) { continue }
        if ([string]$terrain.encoding -ne 'base64') { continue }
        $dataFile = [string]$terrain.dataFile
        if ([string]::IsNullOrWhiteSpace($dataFile)) {
            Fail "world '$($world.Name)' is base64 terrain but names no dataFile sidecar."
        }

        $sidecar = Join-Path $world.DirectoryName $dataFile
        if (-not (Test-Path -LiteralPath $sidecar)) {
            Fail "world '$($world.Name)' names terrain sidecar '$dataFile', which the package does not carry."
        }
    }

    $packsRoot = Join-Path $Root 'assets/packs'
    if (-not (Test-Path -LiteralPath $packsRoot)) { Fail 'the package is missing assets/packs.' }
    $packs = @(Get-ChildItem -LiteralPath $packsRoot -Directory)
    if ($packs.Count -eq 0) { Fail 'the package carries no asset pack.' }
    foreach ($pack in $packs) {
        if (-not (Test-Path -LiteralPath (Join-Path $pack.FullName 'manifest.json'))) {
            Fail "asset pack '$($pack.Name)' carries no manifest.json."
        }
    }

    Assert-NoOriginalGameFiles -Root $Root

    Write-Step ("carried {0} world(s), {1} ruleset(s), {2} scenario(s), {3} asset pack(s)" -f `
        $worlds.Count,
        @(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'rulesets') -Filter '*.json' -File).Count,
        @(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'scenarios') -Filter '*.json' -File).Count,
        $packs.Count)
}

# The oldest standing constraint: no file originating from the user's original installation may ship
# (docs/release-plan.md item 13). The export's own files are the game executable, the .pck, the .NET
# runtime (including createdump.exe), and the copied data/assets; the sound stubs are the project's
# own generated .wav files. A .dat/.hlp/.cnt/.sav, or an executable outside the known set, is not
# ours and fails the package.
function Assert-NoOriginalGameFiles {
    param([string]$Root)

    $knownExecutables = @(
        $ExeName,
        'IC2.MapViewer.console.exe',
        'createdump.exe'
    )
    $forbiddenExtensions = @('.dat', '.hlp', '.cnt', '.sav', '.DAT', '.HLP', '.CNT', '.SAV')

    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
        if ($forbiddenExtensions -contains $file.Extension) {
            Fail "the package contains '$($file.FullName)', a file type that only comes from the original installation (release-plan.md item 13)."
        }
        if ($file.Extension -eq '.exe' -and $knownExecutables -notcontains $file.Name) {
            Fail "the package contains unexpected executable '$($file.FullName)' (release-plan.md item 13)."
        }
    }
}

# ---------------------------------------------------------------------------------------------------
# Build and export
# ---------------------------------------------------------------------------------------------------

$GodotExe = Resolve-Godot -Requested $Godot
Write-Step "using Godot at $GodotExe"
Write-Step 'building godot/IC2.MapViewer.csproj'
Invoke-Checked -FilePath 'dotnet' -Arguments @('build', $CsprojPath, '--nologo', '-v', 'q')

$createdSolution = $false
try {
    $createdSolution = Ensure-Solution

    if (Test-Path -LiteralPath $ExportRoot) { Remove-Item -LiteralPath $ExportRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $GameDir | Out-Null

    $exportLog = Join-Path $RepoRoot 'rendered/export/package-export.log'
    Write-Step "exporting preset '$PresetName' to $ExePath"
    & $GodotExe --headless --path $GodotDir --export-release $PresetName $ExePath *> $exportLog
    $exportCode = $LASTEXITCODE

    if ($exportCode -ne 0) { Fail "the Godot export exited with code $exportCode; see $exportLog." }
    if (Select-String -LiteralPath $exportLog -Pattern '^\s*ERROR:' -Quiet) {
        Fail "the Godot export reported an error; see $exportLog."
    }
    if (-not (Test-Path -LiteralPath $ExePath)) { Fail "the export did not produce $ExePath." }
    if (-not (Test-Path -LiteralPath $PckPath)) { Fail "the export did not produce $PckPath." }

    Write-Step 'copying data/ and assets/packs/ beside the export'
    Copy-Tree -Source (Join-Path $RepoRoot 'data') -Destination (Join-Path $ExportRoot 'data')
    Copy-Tree -Source (Join-Path $RepoRoot 'assets/packs') -Destination (Join-Path $ExportRoot 'assets/packs')

    Assert-PackageContents -Root $ExportRoot
    Write-Step "package OK: $ExportRoot"
}
finally {
    if ($createdSolution -and -not $KeepSolution) {
        if (Test-Path -LiteralPath $SolutionPath) { Remove-Item -LiteralPath $SolutionPath -Force }
    }
}

# ---------------------------------------------------------------------------------------------------
# Verify: launch the copied-away package with no Godot and no .NET SDK on PATH
# ---------------------------------------------------------------------------------------------------

if (-not $Verify) {
    Write-Step 'skipping the launch verification; pass -Verify to run it.'
    exit 0
}

try {
    Write-Step "copying the package away from the repository layout to $VerifyPackage"
    Copy-Tree -Source $ExportRoot -Destination $VerifyPackage

    if (-not (Test-Path -LiteralPath (Join-Path $VerifyRoot 'ic2/data/worlds/classical-mediterranean.terrain.b64'))) {
        Fail 'the verify copy is missing the classical-mediterranean terrain sidecar.'
    }

    # 1. Headless: the real chain boots a scenario, resolves the default 'authored' pack and quits 0.
    $headlessLog = Join-Path $VerifyRoot 'asset-pack-selection-check.log'
    Set-OverrideMainScene -GameDirectory $VerifyGame -ScenePath 'res://Checks/AssetPackSelectionCheck.tscn'
    Write-Step 'launching the exported build headless with a scrubbed environment'
    $code = Invoke-WithScrubbedEnvironment -Executable $VerifyExe -Arguments @('--headless') -LogPath $headlessLog
    if ($code -ne 0) { Fail "the scrubbed headless launch exited with code $code; see $headlessLog." }
    if (-not (Select-String -LiteralPath $headlessLog -Pattern 'exiting with code 0' -Quiet)) {
        Fail "the headless launch did not report success; see $headlessLog."
    }
    if (-not (Select-String -LiteralPath $headlessLog -Pattern "resolves the default 'authored' pack" -Quiet)) {
        Fail "the headless launch did not draw from the asset pack; see $headlessLog."
    }

    # 2. Windowed: the screenshot tour loads the scenario and saves a frame to prove it rendered.
    if (Test-Path -LiteralPath $ScreenshotDir) { Remove-Item -LiteralPath $ScreenshotDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ScreenshotDir | Out-Null
    $tourLog = Join-Path $VerifyRoot 'screenshot-tour.log'
    Set-OverrideMainScene -GameDirectory $VerifyGame -ScenePath 'res://Checks/ScreenshotTour.tscn'
    Write-Step 'launching the exported build windowed with a scrubbed environment'
    $code = Invoke-WithScrubbedEnvironment -Executable $VerifyExe `
        -Arguments @('--windowed', '--resolution', '1500x850') `
        -LogPath $tourLog `
        -ExtraEnvironment @{ IC2_SCREENSHOT_DIR = $ScreenshotDir }
    if ($code -ne 0) { Fail "the scrubbed windowed launch exited with code $code; see $tourLog." }
    $screenshot = Join-Path $ScreenshotDir '04-main-game-screen.png'
    if (-not (Test-Path -LiteralPath $screenshot)) { Fail "the windowed run saved no screenshot at $screenshot; see $tourLog." }
    if ((Get-Item -LiteralPath $screenshot).Length -le 0) { Fail "the screenshot at $screenshot is empty." }

    # 3. A missing terrain sidecar fails every scenario: remove the classical world's sidecar and
    #    watch the repository load throw before any scenario can start.
    Write-Step 'removing the terrain sidecar to prove the failure is loud'
    $missingSidecar = Join-Path $VerifyRoot 'ic2/data/worlds/classical-mediterranean.terrain.b64'
    Remove-Item -LiteralPath $missingSidecar -Force
    $missingLog = Join-Path $VerifyRoot 'missing-terrain-sidecar.log'
    Set-OverrideMainScene -GameDirectory $VerifyGame -ScenePath 'res://Checks/AssetPackSelectionCheck.tscn'
    $code = Invoke-WithScrubbedEnvironment -Executable $VerifyExe -Arguments @('--headless') -LogPath $missingLog
    if ($code -eq 0) { Fail "the export started with the terrain sidecar removed; see $missingLog." }
    if (-not (Select-String -LiteralPath $missingLog -Pattern 'MissingTerrainSidecarException' -Quiet)) {
        Fail "the missing sidecar failed without naming MissingTerrainSidecarException; see $missingLog."
    }

    Write-Step "verify OK: screenshot at $screenshot"
    exit 0
}
catch {
    Fail $_.Exception.Message
}
