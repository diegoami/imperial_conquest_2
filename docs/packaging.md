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
