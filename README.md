# Imperial Conquest 2 research and reimplementation

This repository contains original research code and documentation toward a modern reimplementation of Imperial Conquest 2. It does **not** contain the game's executables, data, help files, sounds, or saves. Supply files from your own installation when using the tools.

The first milestone is a C#/.NET parser for the shared world prefix in the original `.DAT` and `.sav` files. Godot will be used later for the map and interface; it is not required to build or run this parser.

## Requirements

- .NET 10 SDK to build and run the parser and command-line inspector. A future Godot C# project must target a compatible .NET version to reference the parser library.
- A copy of `Imperial Conquest 2.dat`; optionally, a `.sav` file for comparison.

## Point the inspector at your original files

Keep the original game files in a folder outside this repository. Copy `assets.example.ini` to `assets.local.ini` in the repository root, then set `directory` to the folder containing `Imperial Conquest 2.dat`. For example:

```ini
[assets]
directory = C:\path\to\imp_conq_original
```

`assets.local.ini` is Git-ignored because it is specific to your computer. The tracked example is safe to share. Saves may be placed in a `saves` subfolder of the asset directory; sound files can remain in `WAVS`. The inspector reads the DAT and optional save only. It does not need the EXE, help, or sounds.

Screenshots can be kept in a `screenshots` subfolder beside `saves`. Name them `<save-number>.<image-number>.png`: for example, `screenshots/7.1.png` through `screenshots/7.5.png` correspond to `saves/7.sav`. These screenshots are reference material for research and remain outside Git; a `screenshots/` folder copied into the repository is also ignored.

Recordings can be kept in a `recordings` subfolder beside `saves` and `screenshots`. MP4 files and the `recordings/` folder are Git-ignored. Research reports contain observations and hashes, not copies of the original media.

From the repository root:

```text
dotnet build src/IC2.Inspect/IC2.Inspect.csproj
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --save "saves/1,sav.sav"
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --compare-saves saves/1.sav saves/4.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --render-map rendered/map.svg
```

Run these commands from the repository root. The inspector validates the 320 × 140 grid and 334 city records, then reports changed cells and city records if a save is supplied. With `--compare-saves`, it compares two original saves and reports hashes, map transitions, and city-record byte changes without assigning meaning to unknown fields. `--render-map` creates an SVG from the original DAT with provisional terrain colors and city markers. The `rendered/` folder is Git-ignored because generated maps derive from the original game data. A relative save path is resolved under the configured asset directory. For automation, `--config <path>` selects a different INI; the original positional DAT/SAV paths still work. The inspector does not execute or modify the original game.

See the [roadmap](docs/roadmap.md) for the full development sequence, [research notes](docs/research.md) for evidence and uncertain fields, [map-layout notes](docs/reports/map-layout.md), [save/screenshot analysis](docs/reports/saves-and-screenshots.md), [one-turn save comparison](docs/reports/one-turn-save-comparison.md), [battle observation](docs/reports/battle-observation.md), [strategic recording and summer saves](docs/reports/strategic-recording-and-summer-saves.md), and [menu/toolbar inventory](docs/reports/menu-and-toolbar-inventory.md). The original files are intentionally excluded by `.gitignore`.
