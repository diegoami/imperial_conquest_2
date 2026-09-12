# Project handover — 12 September 2026

This is the starting point for the next Imperial Conquest 2 session. The goal is a **modern, playable reimplementation** that uses data from a user's original installation without checking original game files into this repository. The present milestone is a read-only C# file inspector and Godot map viewer, **not yet a playable game**. The original EXE has not been run in this project; reverse-engineering conclusions so far come from static files and user-supplied screenshots/recordings.

Read [the roadmap](roadmap.md) for the full sequence, [research notes](research.md) for the high-level evidence, and the linked reports below before interpreting unknown fields as rules. The latest completed research commit before this handover was `8b4a237b22e812d4fb39b2aa5e8d5331352b6150` (“Identify city-unit totals and Roman troop transfer”).

## Where things are

- `src/IC2.Data/`: C#/.NET 10 parsers for the shared DAT/SAV world prefix and known SAV army, nation, city-unit, and current-turn structures. Unsupported layouts should fail clearly rather than be guessed.
- `src/IC2.Inspect/`: command-line inspection, save comparison, and SVG map rendering. See [README](../README.md) for commands.
- `godot/`: Godot 4.7.2 .NET viewer. It shows the map, rivers, cities, army/fleet markers, city and army details, nation information, and the current week/season/active nation from a selected save. Wheel zooms; left-drag pans. It reads external assets through `assets.local.ini` and does not save over them.
- `docs/reports/`: evidence with offsets, controlled comparisons, screenshots, hashes, and explicit limits. The most recent report is [city units, army transfer, and mercenaries](reports/city-units-army-transfer-and-mercenaries.md).
- `AGENTS.md`: project collaboration instructions, including frequent progress updates and checking **three or four relevant saves** in routine analysis rather than scanning every available save.

The user's actual checkout is `C:\Users\diego\projects\imperial_conquest_2`; original files are outside Git at `C:\Users\diego\Documents\imp_conq_original`, with `saves`, `screenshots`, and `recordings` subfolders. The configured local INI is ignored by Git; `assets.example.ini` is the template. In the 12 September Codex workspace, an editable project copy was at `work\imperial_conquest_2`, while the user's checkout was outside that workspace's write permissions. Changes were published to the GitHub `main` branch through the GitHub connector. In a new environment, inspect the current checkout and remote state before editing or publishing, and do not assume these local paths or permissions still apply.

## Confirmed findings to preserve

- The full-version EXE is a 32-bit Win32 PE with strong Delphi 2 evidence. The DAT and sampled saves share a **320 × 140**, column-major, 16-bit map followed by **334 city records of 34 bytes**. The DAT's post-city layout differs from the SAV layout.
- In the sampled SAV layout, the 334-city table ends at `0x18A5C`. A 16-bit army count and **656-byte army records** follow. For an army record, `+0/+2` are coordinates, `+4` owner, `+6` moves, `+8` candidate morale value, `+10` supply, `+12` money, and `+16` starts twenty 32-byte unit slots. Troop type codes `0`–`4` are light infantry, heavy infantry, archers, light cavalry, and heavy cavalry. Army quality codes `5`–`9` are supported as poor, average, good, very good, and elite; the `5 = poor` link comes from the 15,000-soldier transfer and remains less broadly tested than the others. See [army records](reports/army-records-and-roman-roster.md).
- After the sampled saves' **two 26-byte candidate fleet records**, sixteen nation records of **1,172 bytes** begin at `0x1A434`. They provide nation/leader names, treasury, capital, city count, tax rate, mobilization, and the human-player flag at nation offset `+0x490`. Adding Ptolemaic human control changes just three save bytes, including this flag `0 → 1`. The 40 eight-byte city-unit slots at nation offset `+0x2E4` are **per nation**, correcting an earlier assumption of one global recruitment queue. The implementation currently validates this two-fleet layout. See [Ptolemaic player and Week 9](reports/ptolemaic-player-and-week9.md).
- The 55-byte SAV trailer includes the active nation at relative `+36`, displayed week at `+40`, year BC at `+42`, and season at `+44` for sampled Spring/Summer saves. `12_ptol.sav` matches **Week 9 Summer 270 BC, Ptolemaic turn**. Autumn, winter, and year rollover have not been checked.
- The parenthetical count beside a city's fortification is **the sum of its city-unit slot troop quantities**, including a unit marked “not ready”; it is not a field in the 34-byte city record. Rome's twelve slots sum to **85,000** before the transfer, and its eleven remaining slots sum to **70,000** afterward. Masada's six slots independently sum to its displayed **49,800**, despite a population of only 10,000. The viewer and inspector calculate this total. See [city units and transfer](reports/city-units-army-transfer-and-mercenaries.md).
- `12_ptol.sav` → `12_ptol_b.sav` moves two Ptolemaic armies toward Masada and Alexandria, changing their coordinates and moves remaining. `12_ptol_b.sav` → `12_rom_a.sav` removes one 15,000 light-infantry city unit from Rome and adds a 15,000 light-infantry unit to the nearby army: **48,173 → 63,173** army troops and **13 → 14** units. The latter pair also includes an unrelated Gaul army move and nation-state changes, so it is not otherwise a byte-identical single-action experiment. The post-transfer filename present on disk is `12_rom_a.sav`, not `12_rom.sav`.
- Alexandria's screenshot-listed **Egyptian light infantry, 9,056, good** corresponds to a candidate 12-byte record at `0x1EDEC` immediately after the nation table: words `190, 94, 35, 0, 9056, 7`. Coordinates, type, troop count, and quality match. The complete mercenary table's count, empty-slot rules, and the possible `35 → Egyptian` label need more evidence before general parsing.

## Build and focused verification

From the repository root, with .NET 10 SDK installed and `assets.local.ini` pointing at the user's own data:

```text
dotnet build src/IC2.Inspect/IC2.Inspect.csproj
dotnet build godot/IC2.MapViewer.csproj
dotnet run --no-build --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-city saves/12_ptol_b.sav Masada
dotnet run --no-build --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-city saves/12_rom_a.sav Rome
dotnet run --no-build --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-army saves/12_rom_a.sav 100 42
```

The latest builds passed with zero warnings/errors. The focused inspector checks returned Masada **49,800 city-unit troops**, post-transfer Rome **70,000**, and the Roman army **14 units / 63,173 troops** with a new poor 15,000 light-infantry unit. Build the two projects **sequentially**, since concurrent builds write to the shared `IC2.Data/obj` directory. The Godot source selector reads `*.sav` from the configured external `saves` folder; select a save there, then click a city or army or use the Nations selector. Opening the same Godot project after `git pull` does not require a fresh project import.

## Next useful work

1. Decode the candidate mercenary table after the nation records: establish record count/boundaries, sentinel rules, and how the nationality label is selected. Verify with another screenshot/save before exposing it generally in the viewer.
2. Decode fleet records and their map markers, then the DAT's different post-city layout. Avoid applying the SAV army/nation parser to the DAT.
3. Investigate city-unit state words, readiness, city-to-army transfers, and costs with targeted before/after saves. The first state word advances with time even in empty slots, so it is **not** a direct quality label. A separate controlled supply or money transfer would test those fields across another entity.
4. Continue static analysis of the executable's turn, economy, diplomacy, and battle code; use the [battle entry-point report](reports/battle-code-entry-points.md). Do not infer a playable rules engine from the map viewer alone. Any execution of the original binary should be a separately agreed, isolated experiment.
5. Build a deterministic headless C# rules model, then connect Godot UI to tested commands and implement save/load in a new versioned format. The broader order and definition of done are in the [roadmap](roadmap.md).

Keep original EXE, DAT, HLP, CNT, WAV, SAV, screenshots, and recordings outside the public repository. Record exact file/hash/screenshot evidence for each new field and mark inferred meanings as candidates until checked.
