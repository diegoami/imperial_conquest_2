# Project handover — 12 September 2026 (updated after a full decompilation pass)

This is the starting point for the next Imperial Conquest 2 session. The goal is a **modern, playable reimplementation** that uses data from a user's original installation without checking original game files into this repository. The present milestone is a read-only C# file inspector, a Godot map viewer, and a working static-decompilation pipeline that has now recovered most of the game's core rules — still **not yet a playable game**. The original EXE has not been *run* (no game process launched to actually play it); everything below comes from static decompilation of that same read-only EXE file via Ghidra, a different thing from executing it.

Read [the roadmap](roadmap.md) for the full sequence, [the decompilation plan](decompilation-plan.md) for the static-analysis work specifically (its priority queue is now fully worked through — read it for what's left), [research notes](research.md) for the high-level evidence, and the linked reports below before interpreting unknown fields as rules. The latest commit as of this handover is `71975b1` ("Decompile the quarterly billing cycle"). Everything below was true as of that commit; check `git log` first.

## Where things are

- `src/IC2.Data/`: C#/.NET 10 parsers for DAT/SAV world prefix, army, nation, recruitment, fleet (`SaveFleetTable.cs`), and mercenary (`SaveMercenaryTable.cs`) tables, and turn state.
- `src/IC2.Inspect/`: CLI inspection — `--inspect-turn/-nation/-city/-army`, `--list-armies <save> <nation>`, `--list-fleets <save>`, `--list-mercenaries <save>`, `--compare-saves`, `--render-map`, `--to-json <save> <output.json>` (dumps everything known about a save to one file). See [README](../README.md).
- `godot/`: Godot 4.7.2 .NET viewer, references `IC2.Data` via `<ProjectReference>` — auto-picks-up `IC2.Data` source edits on next build. Headless check: `"<Godot install>\Godot_..._console.exe" --headless --path godot --quit-after 2`. **Caveat:** this regenerates `godot/project.godot`'s header and flips `godot/MapViewer.cs`'s line endings as a side effect — `git diff -w` before committing after a headless run and revert those two files if the diff is whitespace-only.
- `docs/reports/`: 33 reports. 12 are `decompiled-*` (see below); the rest are save-diffing reports from earlier in the project.
- `docs/decompilation-plan.md`: **read this first** for static-analysis work — the original 7-item priority queue is fully done, live-updated with what's still open.
- `AGENTS.md`: collaboration instructions (frequent progress updates, 3–4 relevant saves for routine checks).
- User's checkout: `C:\Users\diego\projects\imperial_conquest_2`. Original game files: `C:\Users\diego\Documents\imp_conq_original` (its own git repo), with `saves/` (has a `processed/` subfolder the user moves analyzed saves into), `screenshots/`, `recordings/`, and `notes/1_rome.txt` (the user's action log for the ongoing "1_rome" play session — check for anything newer than last-processed before starting new decompilation work).

## Standing user preferences (from auto-memory — check it's still current)

- **Commit AND push automatically** after finishing a unit of RE work, without asking first. Don't wait for confirmation on either step. Still avoid destructive git operations (force-push, amend, reset --hard) without explicit request.
- When correcting a claim after user feedback (e.g. "battles are never saved," "5,000 is ship capacity not troops," a note typo), fix the actual report text, not just acknowledge in chat.

## Local tooling (all outside the repo, all still present)

- `gh` (GitHub CLI, authenticated as `diegoami`), `jq`, Python 3.14, Node.
- **`ffmpeg`** at `%LOCALAPPDATA%\ReTools\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe` — for extracting frames from the recordings in `imp_conq_original\recordings\`. Used as `ffmpeg -ss <startSeconds> -i "<recording.mp4>" -vf fps=1 -frames:v <N> <outdir>/f_%03d.png`, then read the PNGs with the Read tool. The tactical battle UI's combat-resolution panel (`ATTACKS`/`SHOOTS AT` + `UNIT LOSSES`/`TROOP LOSSES`) gives exact before/after troop counts per exchange — this is a much richer source of controlled combat data than manual video scrubbing, and the user is willing to record more battles on request.
- **`%LOCALAPPDATA%\ReTools\`**: Temurin JDK 21 (`jdk-21.0.12.1+1\`), Ghidra 12.1.3 (`ghidra_12.1.3_PUBLIC\`), an imported+analyzed Ghidra project (`ghidra_projects\IC2\`), and `scripts\`:
  - `ExportFunctions.java` — dump all named functions to text.
  - `ExportAddresses.java` — decompile specific addresses; uses `getFunctionContaining()` so it works on mid-function addresses (e.g. string xrefs), not just function starts.
  - `ExportAllInRange.java` — decompile every function (named or not) in an address range to one file. Used to produce `all_app_functions.txt` (0x401000–0x460000, 1,881 functions) — **check this file before re-decompiling anything**, most of this session's later findings came from grepping it rather than fresh Ghidra runs.
  - `FindXrefs.java` / `FindCallers.java` — list references to a data address / callers of a function, via Ghidra's `ReferenceManager`.
  - `FindString.java` — search all of program memory for an ASCII string using Ghidra's own address mapping (more reliable than computing file-offset-to-VA by hand; section alignment padding caused a real off-by-`0x1000` bug once).
  - `DumpMemory.java` — dump raw bytes at an address as little-endian words. **Important finding:** several data tables (unit-type stats, the combat matrix, mercenary names) are uninitialized in the EXE (`MemoryAccessException`) — they're loaded from the DAT file at runtime, so when Ghidra can't read a table, search the DAT file directly by the data's known name strings instead (this worked twice, both times cleanly).
  - `ImportDelphiSymbols.java` — import `delphi_symbols.tsv` into Ghidra as real function symbols.
  - `delphi_symbols.tsv`/`.json` — 282 recovered real method names across 31 classes. **Look up a method's address here by class name before hand-deriving one** — most classes relevant to game rules are already named.

**A real Ghidra limitation found and worked around:** `ReferenceManager.getReferencesTo()` finds *zero* references to several known-real strings in this binary (tested: the DAT filename, `"falls to"`) — a genuine gap in Ghidra's default analysis of this Delphi build's string-loading pattern, not a dead end in the data. **Workaround that works every time:** decompile the whole application range to one file (`ExportAllInRange.java`) and grep the *decompiled text* — Ghidra's decompiler inlines string-literal operands into pseudocode even when no formal Reference was ever recorded. Prefer this over `FindXrefs.java` for any string not already tied to a known address. Function-to-function call xrefs (`FindCallers.java`) work fine, except across **virtual method calls** (e.g. `TStream.Read`), which are a real dead end for backward call-graph tracing — go find the caller by name/string instead.

To resume: `$env:JAVA_HOME = "...\jdk-21.0.12.1+1"`, then `ghidra_12.1.3_PUBLIC\support\analyzeHeadless.bat <projectDir> IC2 -process "Imperial Conquest 2.exe" -noanalysis -scriptPath <scriptsDir> -postScript <Script>.java <args...>`.

## Confirmed findings — the big picture

### Save/DAT structure (essentially complete now)
The **entire SAV file layout is confirmed directly from the actual read/write code** (`decompiled-sav-file-layout.md`), not just inferred: map (89,600B) → city table (11,356B = 334×34) → army count + records (656B each) → fleet count + records (26B each) → nation table (16×1,172B) → **mercenary table, exactly 50×12B, hard-coded in code** → **news log, a 40-slot ring buffer of 61-byte messages** (`decompiled-news-log-identified.md`) → a handful of small fixed fields including the **16-entry nation turn-order table** and a **pending-diplomacy-offer indicator** (`decompiled-turn-and-calendar-sequencing.md`) → the known 55-byte trailer. A battle-in-progress branch exists in the code but **the user confirmed saving mid-battle isn't possible in the game** — every real save's length is fully accounted for without it.

Map markers `333`/`335` = fleet positions (exact coordinate match). Fleet `CityIndex` is **not a fixed home port** — it changes as a fleet acts, candidate meaning "last city interacted with." Fleet **`OwnerCode` (+8) is now confirmed** — two independently-identified fleets (Rome via a controlled order, Carthage via a news-log-correlated loss), both matching exactly, plus all 4 in-port fleets in the same save matching their port's owner. See `fleet-owner-field-confirmed.md`.

### City capture, defection, and combat (`decompiled-city-capture-resolution.md`, `decompiled-defection-and-siege-attrition.md`, `decompiled-combat-formula-structure.md`, `combat-type-effectiveness-matrix.md`)
- Siege outcome = attacker strength (archers tripled) vs. defender strength (fortification/loyalty-based, −20% if owner≠allegiance).
- Capture transfers unity (+9/−15), wealth, city count, and the exact `nationTaxBase` field (solved to 2,440 for Rome from the tax formula) between nations.
- **A cascading defection mechanic**: after a capture, nearby weakly-defended low-loyalty cities of the same defeated nation can auto-defect — very plausibly the mechanism behind every "X defects to Y" news event this project has ever seen.
- Defection (unlike forced capture) never touches population/fortification — exact match to the empirical Modena finding.
- Population/fortification loss traces to two functions run on *every* siege attempt (win or lose): random army casualties, and a smoothing/decay adjustment to city-stat fields — meaning a capture's visible loss may be accumulated across multiple attempts, not a one-time penalty.
- Combat: melee hits both sides simultaneously per exchange, scaled by a unit-type effectiveness ratio (the 5×5 matrix is located in the DAT file, right after the unit-type table), capped at 30,000 and 40% of own troops. Shooting has a real range mechanic (in-range shots double). A "focus-fire" counter scales losses when multiple attackers target one defender.
- **The 40% melee-loss cap is now confirmed exactly** (`floor(0.4 × defenderTroops) + 1`, matching 4 of 5 fresh recorded exchanges precisely) — see `battle-recording-melee-cap-confirmed.md`. A closer, more evenly-matched recorded fight would exercise the un-capped formula branch and could resolve the effectiveness matrix's attacker/defender axis ambiguity, which this lopsided dataset couldn't.

### Economy (`decompiled-fleet-tax-and-mercenary-formulas.md`, `decompiled-recruitment-cost-formula.md`, `unit-type-stat-table-in-dat.md`, `decompiled-quarterly-billing-and-economy.md`)
- Fleet order: `cost = ships×10`, `capacity = ships×500`, `upkeep = ships×3/quarter` — all three now confirmed exactly.
- Recruitment/upkeep: `cost = (troops/200) × priceTable[unitType]`, same table for initial recruitment and ongoing quarterly upkeep (army and garrison alike). Unpaid armies lose troops for real, not just debt.
- Tax income: `income = nationTaxBase × tax% / 100`, `nationTaxBase = 2,440` solved exactly for Rome, and confirmed as the literal quarterly treasury credit (not just a dialog preview).
- **"Quarterly" = once per season** (4×/year) — found by locating the function called only at the week-11→1 wrap.
- The full unit-type stat table (moves, battalion size, shots, range, recruit cost) is in the DAT file, every field cross-checked against real observations.
- Tribute grows toward a population target moderated by tax rate; loyalty responds to tax rate with a rebellion check below a threshold; unity decays 3/quarter; diplomatic relations drift toward peace over time.

### Turn/calendar (`decompiled-turn-and-calendar-sequencing.md`, `decompiled-weather-events.md`)
Week `+2 mod 12`, season advances at 11→1, year decrements at Winter→Spring — this closes the project's original oldest open item. City-unit `StateCode` increments 2/week capped at 24 (24 is a cap, not a starting value). Fleet construction countdown and completion confirmed. Population growth is seasonal/loyalty-modulated with a Winter decline chance. Army supply consumption is seasonal. A previously-unknown **weather-event system** exists (~8× more frequent in Winter), very plausibly the source of a fleet storm/loss-at-sea mechanic.

## What's still open

- Diplomacy reparation formula — dead end via `TPolitics_MakePeace` (player-only); lives in unnamed AI code, not reachable by name.
- Mercenary hiring's exact cost-table values; two unidentified unit-type-table fields (`+0x20`, `+0x26`).
- The rebellion check (`FUN_0044c204`) and weather-event effect (`FUN_004511bc`) internals — not decompiled.
- The un-capped melee formula's exact random-roll term (the cap itself is now confirmed exactly, see above); the effectiveness matrix's attacker/defender axis; a full simulation of the whole multi-round recorded battle in `battle-observation.md`.
- A ~6-byte reconciliation gap in the SAV layout's news-log region sizing.
- `ComputerGeneral`'s actual AI decision-making (only its dispatch chain was traced; the roadmap's own scope decision favors faithful *rules* over a byte-exact AI port anyway).

## The battle-freeze investigation (not a decompilation item, but real project history)

The game reliably freezes/becomes unresponsive during manual tactical battles (see `battle-freeze-diagnosed-procmon.md`). What's been tried:

- **Procmon diagnosis**: found ~3.02-second dead-silence stalls (zero syscalls) recurring throughout a battle, each right after a short sound-effect playback thread exits. Consistent with either a hardcoded pacing delay or a blocking wait — Procmon can't distinguish a benign `Sleep` from a CPU-spinning busy-wait, since both look identical (silence) from a syscall trace.
- **WAV format conversion tried, did not fix it**: the game's `WAVS` files were legacy 8-bit unsigned PCM at 5512/11025/22050 Hz; converted all to 16-bit/44.1kHz PCM (originals preserved in `WAVS - Copy`) in case the odd format was forcing an expensive converter-chain rebuild in the modern audio engine. No improvement.
- **A Windows 98 SE VM was attempted (VirtualBox) and specifically ruled out — do not re-suggest this without the underlying condition changing.** Two Windows 98 installs both hit `SHELL32.DLL is linked to missing export SHLWAPI.DLL` crashes, even from a completely fresh disk with VirtualBox's own hardware-virtualization setting already disabled. Root cause confirmed directly in `VBox.log`: `HM: HMR3Init: Attempting fall back to NEM: VT-x is not available` — the host's WSL2 install requires Windows' Virtual Machine Platform (Hyper-V-based), which exclusively claims VT-x, forcing VirtualBox onto Microsoft's Windows Hypervisor Platform (WHP) backend regardless of any per-VM setting. WHP's real-mode/V86 CPU emulation is known to be less accurate than VirtualBox's native engine, which plausibly explains both crashes. The real fix (`bcdedit /set hypervisorlaunchtype off` + reboot, disabling WSL2 until reverted) was declined by the user — **they explicitly do not want WSL2 disabled**, so a Win9x-native VM is off the table on this machine. The VM (`ImpConq2-Win98`) has been fully removed (unregistered + disk deleted).
- **Net effect**: the freeze must continue to be characterized from modern-Windows data only (Procmon traces, video recordings, in-game battle-resolution screens) — there's no clean native-OS reference environment available to compare against on this machine.

## Build and verification

```text
dotnet build src/IC2.Data/IC2.Data.csproj
dotnet build src/IC2.Inspect/IC2.Inspect.csproj
dotnet run --no-build --project src/IC2.Inspect/IC2.Inspect.csproj -- --to-json saves/<any.sav> out.json
```
Build `IC2.Data` before `IC2.Inspect` if both changed (shared `obj` dir). All builds clean as of `71975b1`.

## Next useful work

1. **New save/screenshot/note cycles take priority** over more static analysis — check `imp_conq_original/notes/1_rome.txt` for anything newer than the last-processed save first.
2. Pick up any "What's still open" item above, per `docs/decompilation-plan.md`'s live status.
3. Eventually: write the rules specification (`roadmap.md` §3, now has a large evidence base to draw from), then the headless C# rules model (§4).

Keep original EXE, DAT, HLP, CNT, WAV, SAV, screenshots, and recordings outside the public repository — this also applies to the Ghidra project and decompiled-text dumps under `%LOCALAPPDATA%\ReTools`. Record exact file/hash/address/screenshot evidence for each new field or formula and mark inferred meanings as candidates until checked.
