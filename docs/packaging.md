# Packaging

T27 builds the game into a self-contained Windows export. The script is
[`scripts/package.ps1`](../scripts/package.ps1); the export preset is
[`godot/export_presets.cfg`](../godot/export_presets.cfg) (`Windows Desktop`, x86_64, .NET).

## Prerequisites

- Windows x86_64.
- The .NET 10 SDK (`dotnet` on `PATH`) — the packaging machine needs it to build the C# project
  and to run the export.
- Godot 4.7.2.stable **.NET** ("mono") editor or console build (`godot.cmd` or `godot` on `PATH`,
  or pass `-Godot <path>`).
- The matching **export templates** for `4.7.2.stable.mono`. In the Godot editor: *Editor → Manage
  Export Templates… → Download and Install*. This installs them under
  `%APPDATA%\Godot\export_templates\4.7.2.stable.mono\`; Godot finds them by version, so a
  `godot.cmd` on `PATH` needs no further configuration. `godot/export_presets.cfg` names no custom
  template, so the built-in Windows Desktop template is used.

The machine that **runs** the export needs none of the above: it carries its own .NET runtime and
the game data.

## Build the package

```powershell
pwsh scripts/package.ps1
```

This exports the preset and assembles, under the git-ignored `rendered/export/ic2/`:

| Path | What it is |
| --- | --- |
| `game/IC2.MapViewer.exe` | the game executable |
| `game/IC2.MapViewer.pck` | the Godot resource pack |
| `game/data_IC2.MapViewer_windows_x86_64/` | the bundled .NET runtime and game assembly |
| `data/` | worlds, terrain sidecars (`*.terrain.b64`), rulesets and scenarios |
| `assets/packs/` | the placeholder and authored asset packs |

`data/` and `assets/` sit **next to `game/`**, not inside it. Godot globalizes `res://` to the
executable's own directory in an exported build, and the game's repository-root convention
(`GameSessionFactory.RepositoryRootFromGlobalizedResPath`) is `res:///..` — the same convention
`Slice.cs`, `GameDataContext` and the asset-pack loader share. The package makes that parent look
like a repository root, so the same loading code works with no source tree around, and #454 item 5's
asset pack is carried the same way. The exported folder is self-contained: it can be moved anywhere
and still runs.

Run it with:

```powershell
rendered\export\ic2\game\IC2.MapViewer.exe
```

## Verify the package

```powershell
pwsh scripts/package.ps1 -Verify
```

`-Verify` copies the package away from the repository layout (to `rendered/verify/ic2/`), launches
it there with a scrubbed environment (`PATH` reduced to the Windows system directories, and
`DOTNET_ROOT`, `DOTNET_ROOT_X86` and `DOTNET_ROOT(x86)` unset) and checks:

1. headless, `res://Checks/AssetPackSelectionCheck.tscn` starts a game, draws from the default
   `authored` pack and exits `0`;
2. windowed, `res://Checks/ScreenshotTour.tscn` loads the scenario and writes four screenshots to
   `rendered/verify/screenshots/`, the last being `04-main-game-screen.png`;
3. with `classical-mediterranean.terrain.b64` removed, the same check exits non-zero and reports
   `MissingTerrainSidecarException` (a world is two files; a missing sidecar fails every scenario).

The official release template refuses a scene path on the command line — it is built with path
overrides disabled. The verify copies therefore write Godot's supported `override.cfg` next to the
executable to point `run/main_scene` at the check scene. It is only ever written under
`rendered/verify/` and never ships.

## Release assets

Every tag's GitHub Release carries two Windows x64 files, built from that tag by
[`scripts/release-assets.ps1`](../scripts/release-assets.ps1) and attached by
[`.github/workflows/release.yml`](../.github/workflows/release.yml) (T131,
[release-plan.md §5](release-plan.md#5-release-checklist) item 20):

| File | How a player runs it |
| --- | --- |
| `ImperialConquest2-<tag>-windows-x64.zip` | Extract it anywhere and start `ic2\game\IC2.MapViewer.exe`. The `ic2` folder is the package of the section above. |
| `ImperialConquest2-<tag>-windows-x64-setup.exe` | A per-user installer (Inno Setup 6, [`installer/ic2.iss`](../installer/ic2.iss)): no administrator rights, installs under `%LOCALAPPDATA%\Programs\Imperial Conquest 2`, adds a Start-menu entry and an optional desktop shortcut, and upgrades an earlier install in place. The player's saves live in the user profile and are untouched. Neither file is code-signed, so Windows SmartScreen warns on the installer. |

The installer's `AppId` is **fixed** (`{5B7E2C1A-6F3D-4E8B-9A41-1C2D3E4F5A6B}`) and must never change:
it is how a newer Setup finds and upgrades the existing install. A different `AppId` would install a
second copy beside the first.

### Building them locally

```powershell
pwsh scripts/release-assets.ps1 -Tag v0.4.1 [-Godot <console exe>] [-Iscc <ISCC.exe>] [-SmokeOnly] [-SkipInstallerTest]
```

It needs the prerequisites above plus **Inno Setup 6** (`ISCC.exe` from `-Iscc`, then `PATH`, then
`%ProgramFiles(x86)%\Inno Setup 6`, then `%LOCALAPPDATA%\Programs\Inno Setup 6`). In order, it checks
the tag out into a scratch worktree (`rendered/release/<tag>/src`, removed when it ends), packages that
tree with `package.ps1` (`-Verify` when the tag has `godot/Checks/AssetPackSelectionCheck.tscn`, unless
`-SmokeOnly`), runs a headless smoke of the package (`--headless --quit-after 600` with the scrubbed
environment; exit 0 and no `SCRIPT ERROR`), zips it, builds the installer and tests it (silent install
into a scratch directory, the same smoke on the installed game, silent uninstall, which must leave no
file). It then prints each file's path, size and SHA-256 and leaves them in `rendered/release/<tag>/`.

The installer test installs under the same `AppId` as a player's own install, so it **refuses to run**
when that `AppId` is already registered for the current user (a silent uninstall would remove the real
install). Pass `-SkipInstallerTest` there; the workflow's runner has no such install.

**The overlay.** A tag may predate the packaging files. When the tag lacks `godot/export_presets.cfg` or
`scripts/package.ps1` (v0.4.0 lacks both), the running checkout's copy is copied into the scratch
worktree and the script prints `release-assets: <path> is not in <tag>; using <commit>'s`. Nothing else
is overlaid: a tag whose game code those files cannot export fails, and is reported, not patched.

### The workflow

`release.yml` runs on `windows-latest` and:

- **on a push of a `v*` tag**, builds the tag (the 4.7.2 .NET editor and its export templates are
  cached), then its `attach` job calls `release-assets.ps1 -Tag <tag> -Upload`, which creates a draft
  pre-release when the tag has no Release and uploads both files;
- **on a manual dispatch** (`gh workflow run release.yml -f tag=<tag>`), does the same for an older tag,
  such as a maintenance-line tag;
- **on a pull request** that touches the workflow, `installer/` or the script, builds v0.4.0 and keeps
  the two files as workflow artifacts only.

The runs are titled `release assets: <tag>`. The workflow passes `-SmokeOnly` to the script, so the
windowed screenshot tour of `package.ps1 -Verify` stays the local check of release-plan.md §5 item 11.

Assets are **attached, never replaced**. `-Upload` refuses (exit 3) when the Release already holds a
file of either name, published or draft, and the `attach` job then fails, with the build's files kept as
the run's artifacts. Replacing an asset is the user's act, by hand (`gh release delete-asset`, then a
re-run or `gh release upload`). Only the `attach` job has write permission, and only on a push or a
dispatch; the workflow never publishes a Release or creates a tag.

## Troubleshooting

- **"no solution file was found … IC2.MapViewer.sln"** — Godot's .NET exporter insists on a
  solution next to the project, and the repository deliberately commits only the `.csproj`. The
  script generates `godot/IC2.MapViewer.sln` for the export and removes it again; if a stale one is
  present, delete it and re-run.
- **"The given export path doesn't exist"** — the script creates the output directory; this means a
  manual `godot --export-release` was pointed at a directory that does not exist.
- **A world raises `MissingTerrainSidecarException`** — the world JSON names a `dataFile`
  (`*.terrain.b64`) that is not next to it. Both files must be committed together; the packaging
  script refuses to build a package that carries only one.
- **"Project export … failed", or an `ERROR:` line, in `rendered/export/package-export.log`** —
  read that log. The usual causes are export templates that do not match the editor version, or a
  C# build failure (`dotnet build godot/IC2.MapViewer.csproj` reproduces it directly).
- **Untracked `godot/**/*.cs.uid` files after a run** — the export imports the project, and Godot
  writes a `.uid` sidecar for every C# script. Only two of them are tracked and none is ignored
  ([#489](https://github.com/diegoami/imperial_conquest_2/issues/489)), so a packaging run leaves
  about 60 untracked files behind. They are safe to delete
  (`git ls-files --others --exclude-standard -- 'godot/*.cs.uid' | xargs rm -f`).
