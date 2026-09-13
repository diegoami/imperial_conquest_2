# Imperial Conquest 2 research and reimplementation

This repository contains original research code and documentation toward a modern reimplementation of Imperial Conquest 2. It does **not** contain the game's executables, data, help files, sounds, or saves. Supply files from your own installation when using the tools.

For a concise current-state summary and the next steps, start with the [project handover](docs/HANDOVER.md).

The first milestone is a C#/.NET parser for the shared world prefix in the original `.DAT` and `.sav` files and the army, nation, recruitment, and current-turn structures in known saves. A Godot map viewer uses that parser to show terrain, cities, army rosters, nation details, and the save's calendar. Godot is not required to build or run the command-line inspector.

## Godot map viewer

After setting `assets.local.ini`, open `godot/project.godot` with the Godot .NET edition (currently 4.7.2) and run the project. The viewer reads the original DAT from the configured external directory and draws its 320 × 140 map and 334 cities. It opens in a maximized window near Rome; **Show whole map** returns to the overview. Use the source selector to view the initial world or any `.sav` files in the configured `saves` folder. The mouse wheel zooms toward the cursor, and dragging with the left mouse button moves around the map. In a save, click an army flag to see its troop roster, moves, supply, and money; click a city to see its controller, allegiance, population, fortification, tribute, supplies, and **total troops assigned to the city** beside the fortification percentage, followed by its unit list. The **Nations** selector shows a nation’s leader, capital, city count, tax rate, mobilization, treasury, and human-player status. The header shows the save’s week, season, year, and active nation. For example, select `12_ptol_b.sav` and click Masada to see **49,800** city-unit troops, or select `12_rom_a.sav` and click Rome to see **70,000** after the 15,000-soldier transfer. Fleet clicks show the information currently known for them. Rivers follow their six screenshot-backed blue shapes. The 16 nation names and icon colors follow the user's ordered screenshots; some marker details remain provisional. The viewer does not yet implement turns or combat and does not copy original assets into the repository.

## Requirements

- .NET 10 SDK to build and run the parser, inspector, and Godot C# viewer. The viewer uses Godot .NET 4.7.2.
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
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --compare-saves saves/11.sav saves/11_supply.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-city saves/11_supply.sav Rome
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-army saves/11_supply.sav 100 42
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-nation saves/11_ptol.sav Ptolemaic
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-turn saves/12_ptol.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --list-armies saves/11_supply.sav Rome
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --list-fleets saves/11_supply.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --list-mercenaries saves/11_supply.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --to-json saves/11_supply.sav rendered/11_supply.json
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --render-map rendered/map.svg
```

Run these commands from the repository root. The inspector validates the 320 × 140 grid and 334 city records, then reports changed cells and city records if a save is supplied. With `--compare-saves`, it compares two original saves and reports hashes, map transitions, city supplies, and city-record byte changes. When the files have equal lengths, it also summarizes byte changes after the city table. `--inspect-city` reads a known save's city and units-at-city fields, including their total troop count; `--inspect-army` prints the army at the requested coordinates; `--list-armies` lists every army owned by a nation without needing coordinates. `--inspect-nation` shows the decoded nation panel fields; `--inspect-turn` prints the save's current calendar and active nation. `--list-fleets` lists every fleet's coordinates, ship count, and home city. `--list-mercenaries` lists every available (non-hired) mercenary offer from the fixed 50-slot table. `--to-json` writes every known field (turn, all 16 nations, all 334 cities with garrisons, all armies with units, all fleets, all mercenary offers) to one indented JSON file, for a full readable dump rather than one field at a time — it does not include raw map cells; use `--render-map` for the map itself. `--render-map` creates an SVG from the original DAT with provisional terrain colors and city markers. The `rendered/` folder is Git-ignored because generated maps derive from the original game data. A relative save path is resolved under the configured asset directory. For automation, `--config <path>` selects a different INI; the original positional DAT/SAV paths still work. The inspector does not execute or modify the original game.

See the [roadmap](docs/roadmap.md) for the full development sequence, the [game design](docs/game-design.md) and its [design audit](docs/design-audit.md) for what the reimplementation will be, the [build orchestration plan](docs/build-orchestration-plan.md) for how the build is split into parallel agent-run tasks, the [release plan](docs/release-plan.md) for how those tasks turn into version tags and releases, [research notes](docs/research.md) for evidence and uncertain fields, [city units, army transfer, and mercenaries](docs/reports/city-units-army-transfer-and-mercenaries.md), [Ptolemaic player and Week 9 analysis](docs/reports/ptolemaic-player-and-week9.md), [map-layout notes](docs/reports/map-layout.md), [rivers and map markers](docs/reports/rivers-and-map-markers.md), [army-table analysis](docs/reports/army-records-and-roman-roster.md), [Rome city, recruitment, and nations](docs/reports/rome-city-recruitment-and-nations.md), [save/screenshot analysis](docs/reports/saves-and-screenshots.md), [one-turn save comparison](docs/reports/one-turn-save-comparison.md), [battle observation](docs/reports/battle-observation.md), [battle-code entry points](docs/reports/battle-code-entry-points.md), [strategic recording and summer saves](docs/reports/strategic-recording-and-summer-saves.md), [menu/toolbar inventory](docs/reports/menu-and-toolbar-inventory.md), and [controlled army-supply transfer](docs/reports/controlled-army-supply-transfer.md). The original files are intentionally excluded by `.gitignore`.
