# Project handover — 12 September 2026 (updated same day, after the decompilation pass)

This is the starting point for the next Imperial Conquest 2 session. The goal is a **modern, playable reimplementation** that uses data from a user's original installation without checking original game files into this repository. The present milestone is a read-only C# file inspector, a Godot map viewer, **and now a working static-decompilation pipeline** — still **not yet a playable game**. The original EXE has not been *run* (no game process was launched to play it); this update adds real static decompilation of that same read-only EXE file via Ghidra, which is a different thing from executing it.

Read [the roadmap](roadmap.md) for the full sequence, [the decompilation plan](decompilation-plan.md) for the static-analysis work specifically, [research notes](research.md) for the high-level evidence, and the linked reports below before interpreting unknown fields as rules. The latest commit as of this handover is `4860a6f` ("Decompile combat resolution: melee hits both sides at once"). Everything below it in this file was true as of that commit; check `git log` first.

## Where things are

- `src/IC2.Data/`: C#/.NET 10 parsers for DAT/SAV world prefix, army, nation, recruitment, **fleet** (`SaveFleetTable.cs`), and **mercenary** (`SaveMercenaryTable.cs`) tables, and turn state. Unsupported layouts fail clearly rather than guess.
- `src/IC2.Inspect/`: CLI inspection. Commands now include `--inspect-turn/-nation/-city/-army`, `--list-armies <save> <nation>`, `--list-fleets <save>`, `--list-mercenaries <save>`, `--compare-saves`, `--render-map`, and **`--to-json <save> <output.json>`** — dumps everything known about a save (turn, all 16 nations, all 334 cities with garrisons, all armies with units, all fleets, all mercenary offers) to one file. See [README](../README.md).
- `godot/`: Godot 4.7.2 .NET viewer, references `IC2.Data` via `<ProjectReference>` — editing `IC2.Data` source is automatically picked up on the viewer's next build, no manual sync. Headless verification: `"<Godot install>\Godot_..._console.exe" --headless --path godot --quit-after 2`. **Caveat:** running this regenerates `godot/project.godot`'s header comment and flips `godot/MapViewer.cs`'s line endings as a side effect — check `git diff -w` before committing after a headless run, and revert those two files if the diff is whitespace/header-only.
- `docs/reports/`: 25 reports as of this commit. The newest ones (in order): tax/Sidon capture, mobilization/city-capture-modes, fleet order at Caere, diplomatic reparations/more captures, field recruitment (mercenary) + attrition + fleet-CityIndex correction, mercenary pool record, then four **decompiled-\*** reports (fleet/tax/mercenary formulas, recruitment cost formula, combat formula structure).
- `docs/decompilation-plan.md`: the static-analysis priority queue and status — read this before doing more decompilation work.
- `AGENTS.md`: collaboration instructions (frequent progress updates, 3–4 relevant saves for routine checks).
- User's checkout: `C:\Users\diego\projects\imperial_conquest_2`. Original game files: `C:\Users\diego\Documents\imp_conq_original` (itself a separate git repo), with `saves/` (has its own `processed/` subfolder the user moves analyzed saves into), `screenshots/` (same), `recordings/`, and `notes/1_rome.txt` (the user's own action log for the current "1_rome" play session — read this whenever new saves appear). `assets.local.ini` (git-ignored) points `IC2.Inspect`/Godot at that folder; `assets.example.ini` is the template.

## Local tooling installed this session (all outside the repo, all still present)

- `gh` (GitHub CLI), authenticated as `diegoami`.
- `jq`, Python 3.14 (already present), Node — used for JSON/binary analysis of saves.
- **`%LOCALAPPDATA%\ReTools\`**: Temurin JDK 21 (`jdk-21.0.12.1+1\`), **Ghidra 12.1.3** (`ghidra_12.1.3_PUBLIC\`), a Ghidra project (`ghidra_projects\IC2\`, already imported + auto-analyzed against the full v1.01 EXE), headless scripts (`scripts\ExportFunctions.java`, `scripts\ExportAddresses.java`, `scripts\ImportDelphiSymbols.java`), and **`delphi_symbols.tsv`/`.json`** — 282 recovered real method names across 31 classes (see below), already imported into the Ghidra project as symbols. Various `*.txt` decompiled-function dumps from this session are also there (`priority_functions.txt`, `army_recruits.txt`, `combat_loss.txt`, etc.) — check these before re-decompiling something already done.

To resume decompiling: set `$env:JAVA_HOME` to the JDK path above, then use `ghidra_12.1.3_PUBLIC\support\analyzeHeadless.bat <projectDir> IC2 -process "Imperial Conquest 2.exe" -noanalysis -scriptPath <scriptsDir> -postScript ExportAddresses.java <output.txt> <hexaddr1> <hexaddr2> ...` (no `0x` prefix on addresses). `ExportAddresses.java` uses `getFunctionContaining()`, so it works whether the address is a function start or a mid-function reference (e.g. a string cross-reference address). Look up method addresses by class name in `delphi_symbols.tsv` first — most named-class work no longer needs hand-found addresses.

## Confirmed findings to preserve

### Save/DAT structure (from save-diffing, largely settled)
- DAT/SAV share a 320×140 column-major map, 334×34-byte city records (fields: name, x, y, owner `+18`, allegiance `+20`, loyalty `+22`, supplies `+24`, fortification% `+26`, population(k) `+28`, reference population(k) `+30`, tribute `+32`).
- SAV: army count → 656-byte army records (coords, owner, moves, morale, supply, money, 20×32-byte unit slots) → fleet count → **26-byte fleet records** (`ShipCount` at `+18`, `CityIndex` at `+20` — **not a fixed home port, changes as a fleet acts**, see below) → 16×1,172-byte nation records (tax rate `+0x44A`, human flag `+0x490`, 40×8-byte per-nation recruitment slots at `+0x2E4`) → **50×12-byte mercenary offer table** (`x,y,label,type,troops,quality`; `troops` becomes sentinel `0xFFFF` on hire — confirmed both by save-diffing *and* in decompiled code) → ~2,442 still-unidentified bytes → 55-byte trailer (active nation `+36`, week `+40`, year `+42`, season `+44`). The nation-table locator now handles any fleet count (was hard-coded to exactly 2; a real save broke that assumption and the fix was verified via the per-nation name self-check).
- Map markers `333`/`335` confirmed as fleet positions (not armies) by exact coordinate match to real fleet records.
- Forced city capture (5 independent examples) always shows: owner flips, allegiance/tribute untouched, population −25–29%, fortification and loyalty drop. Peaceful defection: nothing but loyalty moves. A same-turn defect-then-reconquer (Taurasia) showed siege damage (population/fortification) persists regardless of final owner, while loyalty tracks whoever ends the turn in control.
- Garrison-to-army mobilization conserves troop count exactly (85,000→7,000 garrison = 78,000 exactly split between a refilled army +35,000 and a new army created with 43,000).
- Multi-turn save pairs **do not** show clean supply-transfer conservation (only a same-day, no-turn-advance pair does) — don't re-derive that expecting it to hold across turns.

### Decompiled formulas (new this session, via Ghidra — see `docs/decompilation-plan.md` and the four `decompiled-*` reports)
- **Delphi RTTI symbol recovery**: the method-table byte format (`Word Count` + entries of `{Word EntrySize; Pointer Addr; Byte NameLen; Name}`, class name in the same format immediately after) was reverse-engineered and scanned across the whole EXE → 282 real method names, 31 classes, all imported into Ghidra.
- **Fleet order**: `cost = ships × 10` talents, `capacity = ships × 500` troops — both exact matches to observed data. One unexplained `ships × 3` value.
- **Tax income**: `income = nationTaxBase × tax% / 100` — solving Rome's own 15%/20% data both give `nationTaxBase = 2,440` exactly. The field itself isn't named yet.
- **Recruitment cost**: `cost = (troops / 200) × priceTable[unitType]` (separate initial/quarterly tables) — solved cleanly against two *unrelated* prior reports.
- **Mercenary hiring**: `Label` is confirmed as an index into a name table (not a political nation code) — explains why it never matched `NationCatalog`. Exact cost-table values not yet solved.
- **Diplomacy (reparations): dead end via `TPolitics_MakePeace`** — that function is player-proposal-only, no treasury math. The AI-to-AI reparation formula lives in unnamed code (`ComputerGeneral` or similar), not reachable by class/method name.
- **Combat**: traced `TBattleMap_ComputerGeneral` through 4 dispatch layers; found `FUN_004381a4` is unit *placement* (independently reconfirms the 20-units/army cap), not combat. Reached the real `SHOOTS AT`/`ATTACKS` resolution functions via their known string cross-references. **Melee applies losses to both sides simultaneously in one exchange** (matches the one recorded melee's structure), scaled by a unit-type effectiveness ratio, capped at 30,000 and at 40% of each side's own troops, plus a `+2`/`−3` per-side adjustment on an unidentified (candidate-morale) field. Two helper functions and the type-effectiveness table itself are still undecompiled — structure confirmed, exact numbers aren't yet.

## Build and verification

```text
dotnet build src/IC2.Data/IC2.Data.csproj
dotnet build src/IC2.Inspect/IC2.Inspect.csproj
dotnet run --no-build --project src/IC2.Inspect/IC2.Inspect.csproj -- --to-json saves/<any.sav> out.json
```
Build `IC2.Data` before `IC2.Inspect` if both changed (shared `obj` dir). All builds were clean (0 warnings/errors) as of `4860a6f`.

## Next useful work

1. **Continue decompilation** (see `docs/decompilation-plan.md` for the live-updated priority list): decompile `FUN_0043845c`/`FUN_00438420` and the `DAT_0047946c` type-effectiveness table to get concrete combat numbers and simulate the full recorded battle from `battle-observation.md`; find the diplomacy/reparation formula by tracing from unnamed AI code instead of by class name; decompile DAT/SAV I/O routines to validate the whole `IC2.Data` byte-offset model at once and explain the remaining unknowns (fleet owner code, mercenary `Label`'s literal name strings, city-unit `StateCode`, the ~2,442 unidentified post-mercenary-table bytes).
2. **New save/screenshot/note cycles**: the user is playing an ongoing "1_rome" session (`imp_conq_original/notes/1_rome.txt`) and periodically drops new saves + notes for analysis — check that folder for anything newer than the last-processed save before starting new decompilation work, since fresh controlled-action pairs are usually higher-value than more static analysis. The user's standing instruction (see auto-memory) is to commit this kind of finding without asking first, but still ask before pushing to `origin/main`.
3. Eventually: rules specification write-up (`roadmap.md` §3), then the headless C# rules model (§4).

Keep original EXE, DAT, HLP, CNT, WAV, SAV, screenshots, and recordings outside the public repository — this now also applies to the Ghidra project and decompiled-text dumps under `%LOCALAPPDATA%\ReTools`, which are not part of this repo either. Record exact file/hash/address/screenshot evidence for each new field or formula and mark inferred meanings as candidates until checked.
