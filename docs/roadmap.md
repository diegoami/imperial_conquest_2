# Roadmap: modern Imperial Conquest 2 reimplementation

This is a staged plan for a compatible, modern game that reads files from a user's original Imperial Conquest 2 installation. The public repository will contain our code, tests, and research notes, but not the original EXE, DAT, HLP, CNT, WAVs, saves, screenshots, or recordings. The current reference is the full freeware v1.01 package; the v0.99 demo is useful for comparing differences.

The order matters: verify each file format and rule before depending on it in the new engine. Unknown fields remain explicitly unknown. We can improve the interface without assuming that a visual change also changes the rules.

## 0. Establish the baseline — complete

- [x] Inventory and hash the demo, full package, and first save.
- [x] Identify the executable as 32-bit Win32 PE with strong Delphi 2 evidence.
- [x] Create the repository, initial research reports, and C#/.NET inspector.
- [x] Keep original files outside the repository with an ignored `assets.local.ini` and a tracked template.
- [x] Read the candidate 320 × 140 map and 334 city records from DAT and SAV.
- [x] Confirm that the first supplied save changes 326 map cells and 12 city records relative to the DAT.

**Baseline limit:** These are static findings. Neither game EXE has been executed for this project. The later column-major rendering and user screenshots strongly support the map dimensions, coordinate meanings, and five terrain values, while many dynamic fields remain unverified.

## 1. Build a trustworthy sample set

- [x] Inventory and hash ten available saves. Record the reported one-turn relationship, [initial differences](reports/one-turn-save-comparison.md), [screenshot pairings](reports/saves-and-screenshots.md), [battle evidence](reports/battle-observation.md), [strategic recording](reports/strategic-recording-and-summer-saves.md), and [controlled supply transfer](reports/controlled-army-supply-transfer.md).
- [x] Analyze the [controlled 79-ton army-supply transfer](reports/controlled-army-supply-transfer.md): Rome's city stock falls by 79 and the supplied army's stock rises by 79, with no other state bytes changing.
- [ ] Keep a read-only copy of each original file. Use temporary working copies for experiments; never overwrite the originals.
- [ ] Create small synthetic files for parser tests. Keep hashes, sizes, and expected summaries for real files in the repository, while the binary fixtures stay outside Git.
- [x] Add a command that compares any two saves in the established map and city regions, not just DAT versus one save. Post-city sections still need record boundaries.

**Done when:** A second person can reproduce every stated offset and byte count with the documented commands and their own copy of the game files.

## 2. Finish the DAT and SAV format specification

- [x] Render the candidate 320 × 140 map and overlay all city coordinates; column-major storage and coordinate orientation are strongly supported by geography.
- [x] Match five common cell values to original screen terrain through registered screenshots.
- [x] Identify river values `6`–`11` and distinguish city (`20`–`199`), candidate army (`200`–`299`), and observed fleet (`333`, `335`) markers using [registered screenshots and map coordinates](reports/rivers-and-map-markers.md).
- [x] Locate the [656-byte SAV army records](reports/army-records-and-roman-roster.md), match coordinates and owner codes to map markers, and reproduce the Roman army's 13-unit roster, supplies, and money from the user's screenshots. Armies can share a cell with a fleet overlay.
- [x] Match the [Rome city panel and recruiting list](reports/rome-city-recruitment-and-nations.md) to city fields and 40 recruitment slots embedded in each SAV nation record; identify unit type `2` as archers and all 16 nation names and colors.
- [x] Locate the [16 × 1,172-byte SAV nation table](reports/ptolemaic-player-and-week9.md), match leader, capital, city count, tax rate, mobilization, and treasury to nation panels, and isolate Ptolemaic's human-player flag in a controlled save pair.
- [x] Read the active nation, week, season, and year in the save trailer, matching the Week 9 Ptolemaic screenshot. Match the Roman army information panel's moves, supplies, money, terrain, and troop-class totals.
- [x] Confirm that the number beside city fortification is the [sum of units at that city](reports/city-units-army-transfer-and-mercenaries.md): Rome `85,000 → 70,000` when 15,000 join its army, and Masada's six units sum to `49,800`. Match the transferred army unit and its “poor” quality.
- [x] Match the two Ptolemaic army movements to changed coordinates and moves remaining, and locate a candidate 12-byte mercenary record that reproduces Alexandria's 9,056 “good” Egyptian light infantry.
- [x] Confirm the mercenary record with a real hire: a fixed 50-slot table (not the whole surrounding region, corrected after an initial overcount) whose slot at Felsina's exact coordinates read troops=6,438/quality="very good" before the user's hire and the empty sentinel `0xFFFF` immediately after: [mercenary pool record](reports/mercenary-pool-record.md). `Label` still unidentified. Added `SaveMercenaryTable` and `IC2.Inspect --list-mercenaries`.
- [x] Confirm the nation record's tax-rate field with a controlled before/after action, and confirm city owner/allegiance are decoupled using a controlled capture: [Rome tax increase and Sidon's capture](reports/rome-tax-increase-and-sidon-capture.md). Also confirmed the army/fleet/nation-table offset formula generalizes past the only previously tested fleet count (2), by relaxing an overly strict guard once a real fleet-count-3 save appeared.
- [x] Confirm exact troop conservation between city-unit garrison and field armies during mobilization, fingerprint a freshly created army's starting stats, and contrast forced capture against peaceful defection: [mobilization, movement, and capture modes](reports/mobilization-movement-and-city-capture-modes.md). Found that multi-turn save pairs conflate supply transfer with normal production/consumption, so the earlier clean per-ton transfer conservation should not be assumed across turns; added `IC2.Inspect --list-armies` to find a nation's armies without knowing coordinates.
- [x] Decode fleet marker-to-record identities: a controlled 10-ship order at Caere isolated the fleet table's `ShipCount` (+18) and `CityIndex` (+20) fields, and the two pre-existing fleets' map coordinates matched cell values `333` and `335` exactly, confirming those codes as fleet positions: [fleet order at Caere](reports/fleet-order-at-caere.md).
- [x] Confirm the nation treasury field against an AI-to-AI diplomatic reparation (Ptolemaic `999 → -1270`, an exact `-2269` match), reinforce the forced-capture signature with two more captures (Brixia, Verona), and confirm the Autumn→Winter week-reset boundary: [diplomatic reparations and more captures](reports/diplomatic-reparations-and-more-captures.md).
- [x] Confirm a field-recruitment mechanic (a new "Gallic"-named unit slot, exact troop count and quality match), find a uniform ~2.7% troop loss across every unit in an army that took a city (first quantitative clue toward the battle/attrition formula), get a fifth consistent capture (Mediolanum), and use a same-turn defect-then-reconquer (Taurasia) to separate siege damage (population/fortification, persists either way) from loyalty (tracks the final owner). Also found fleet `CityIndex` is not a fixed home port — it changed on two already-deployed fleets, one without moving: [field recruitment, attrition, and fleet drift](reports/field-recruitment-uniform-attrition-and-fleet-drift.md).
- [ ] Determine the remaining tile meanings, including water value `1`, and the difference between fleet marker values `333` and `335`.
- [ ] Identify every field in the 34-byte city record by comparing saves, the help file, form labels, and executable references. Verify field width, signedness, units, and allowed range. The Rome screenshot supports city supplies, population, fortification percentage, and tribute; loyalty thresholds remain open. The fortification parenthetical is a computed city-unit total from the nation record.
- [ ] Map the DAT regions after the cities: nation, army, fleet, unit, leader, and other tables; establish record boundaries and counts before assigning meanings.
- [ ] Map the remaining SAV regions, including the rest of the fleet record (owner, and what the all-zero words hold), the ~2,442 unidentified bytes immediately after the (now located) 50-slot mercenary table, other mutable entities, full turn order, diplomacy, event log, and any checks or version markers. Trailer fields now match active nation, displayed week, season, and year in the controlled Week 7→9 pair; both observed season transitions (Summer→Autumn and Autumn→Winter) restart the week counter at 1 after week 11. Year boundaries remain unconfirmed.
- [ ] Continue replacing provisional offset-based access in `IC2.Data` with typed models only when field meanings are supported. `CityRecord.Supplies` and the known SAV army fields have direct controlled-action or screenshot evidence; preserve other unknown bytes and validate file sizes and bounds.
- [ ] Add focused parser tests for truncated/corrupt files and known real-file summaries.

**Done when:** The parser can load the full DAT and all known saves into a documented world snapshot, and every parsed field has an evidence trail and a confidence level.

## 3. Recover the game rules and UI behavior

- [ ] Decode the full-version WinHelp topics and Delphi `TPF0` form resources. Use the help contents and UI event names to build a feature inventory.
- [x] Inventory the main menu from the user walkthrough and embedded `TMainMenu` stream; [record command groups and observed dialogs](reports/menu-and-toolbar-inventory.md). Full form/help decoding and exact toolbar mappings remain open.
- [ ] Analyze the full v1.01 EXE statically, starting from DAT/SAV I/O, end-turn, city changes, movement, combat, diplomacy, AI, and victory messages. [Battle method entry points](reports/battle-code-entry-points.md) are identified, but the simulator's formulas and state layout are not yet recovered. Use the demo EXE to isolate demo-only behavior. Ghidra is now set up locally for this; see the [prioritized decompilation plan](decompilation-plan.md), which orders targets by cross-checking against the empirical formulas already gathered from save-diffing.
- [ ] Write a rules specification for the turn sequence, calendar, economy/taxation, city management, recruitment, armies/fleets, terrain and supply, diplomacy, tactical battle, AI, and victory/defeat.
  - [x] First decompiled formulas, exact matches against save-diffing data already in hand: fleet order cost (`shipCount × 10`) and capacity (`shipCount × 500`), tax income (`nationTaxBase × taxPercent / 100`, solving Rome's own numbers to `nationTaxBase = 2,440` both times), and standing recruitment cost (`(troops / 200) × priceTable[unitType]`, solved against two independent prior reports). Also confirmed in code (not just by save-diffing) the mercenary hire's `0xFFFF` sentinel and the 100,000-troop army cap, and explained the mercenary `Label` field as a name-table index. Diplomatic reparation formula not found in `TPolitics` — it's AI-only, unnamed code. See [decompiled-fleet-tax-and-mercenary-formulas.md](reports/decompiled-fleet-tax-and-mercenary-formulas.md), [decompiled-recruitment-cost-formula.md](reports/decompiled-recruitment-cost-formula.md), and [decompilation-plan.md](decompilation-plan.md).
- [ ] Record each formula or rule with its source: manual topic, executable location, save comparison, or observed behavior. Mark guesses separately.
- [ ] If static evidence is insufficient, plan controlled before/after experiments in an isolated environment. Running the original binary is a separate, explicitly approved step; this roadmap does not authorize it.

**Done when:** Each gameplay subsystem has testable inputs, outputs, edge cases, and an identified source of truth. Rules that remain uncertain are listed rather than silently invented.

## 4. Build the headless C# game model

- [ ] Define typed world state and commands independent of Godot: nations, cities, armies, fleets, units, leaders, positions, and current turn.
- [ ] Build a deterministic turn coordinator and seeded random-number interface so identical inputs produce reproducible outcomes.
- [ ] Implement state validation and command legality before effects: ownership, movement range, capacity, supply, payment, targeting, and action limits.
- [ ] Implement one subsystem at a time against the rules specification: economy, movement and supply, recruitment, diplomacy, combat, AI, then victory.
- [ ] Add tests for known boundary cases and multi-turn scenarios. Use original-save state as read-only input where its fields are understood.

**Done when:** A command-line simulation can load a world, apply legal actions, advance turns, and produce the same result from the same initial state and seed without starting Godot.

## 5. Build the Godot desktop interface

- [x] Add a Godot 4.7.2 .NET project targeting .NET 10 and referencing the C# data library. Verify it builds and loads the configured DAT without copying assets.
- [x] Draw terrain, rivers, city coordinates, candidate army/fleet markers, and locally selected save states in a click-to-inspect map viewer with mouse-wheel zoom and drag-to-pan. Start near Rome in a maximized window and retain a whole-map button.
- [x] Show known save army rosters and city fields, city-unit troop totals, and unit quantities when their map markers are clicked. Add a nation selector and current calendar/active-nation display for supported saves.
- [ ] Add fuller unit and fleet details, verified ownership colors, and terrain art. Keep map rendering separate from game rules.
- [ ] Add the main gameplay screens: nation setup, city details, army/fleet details, orders, economy, diplomacy, news, and end-turn flow.
- [ ] Route toolbar shortcuts and menu entries through the same command definitions, following the original interface's shared actions.
- [ ] Add tactical battle presentation and controls after the headless battle model is testable.
- [ ] Make keyboard and mouse interaction, scaling, and basic accessibility usable on modern desktops.

**Done when:** A player can start a game, inspect the world, issue the implemented orders, and finish a turn using the modern interface.

## 6. Save/load and compatibility

- [ ] Make original `.sav` loading read-only until the full layout is known; never modify a supplied original save in place.
- [ ] Add a versioned save format for the new engine and round-trip tests for every typed state field.
- [ ] Preserve import provenance and report unsupported fields or incompatible saves clearly.
- [ ] Consider writing original `.sav` files only after the format is fully verified and there is a specific compatibility need.

**Done when:** A new-engine save can be loaded without state loss, and supported original saves either import correctly or fail with a precise explanation.

## 7. Verify parity and playability

- [ ] Compare parser output, map locations, game limits, and known scenarios against the original help and saves.
- [ ] Test complete campaigns with seeded AI, multiple nations, and edge cases such as empty fleets, sieges, bankrupt armies, and victory conditions.
- [ ] Check performance on the complete map and long games; investigate crashes and data corruption with regression tests.
- [ ] Maintain a visible list of intentional differences from the original and unresolved fidelity gaps.

**Done when:** Core campaigns are playable from start to finish and the known rules pass automated tests. Compatibility claims state exactly what was verified.

## 8. Package and release

- [ ] Document how to point the game at a local installation and verify required files. Keep original assets out of distributed builds.
- [ ] Build and smoke-test desktop packages for the selected platforms, starting with Windows.
- [ ] Add setup, controls, troubleshooting, save migration, and contributor documentation.
- [ ] Choose a license for our own code after confirming repository ownership and contributions. Tag a first playable release, then iterate from user-reported issues.

**Done when:** A user with the original data can install, launch, play, save, and resume without development tools.

## Immediate next milestone

The SAV army and nation tables, per-nation city-unit slots, and current turn trailer are now parsed for the sampled saves. The Rome transfer and Masada screenshot establish the computed city-unit total, and a transferred army unit supports quality `5` as “poor.” A controlled tax increase and a controlled city capture ([report](reports/rome-tax-increase-and-sidon-capture.md)) confirmed the tax-rate field and the owner/allegiance distinction, and confirmed the army/fleet/nation offset formula holds for a fleet count other than 2. Next, identify the rest of the fleet record (owner code, remaining zero words) and the `333`/`335` distinction; decode city-unit state words (a garrison unit's `StateCode` was seen incrementing by 2 per elapsed turn, `8 → 10 → 12 → 14`, a candidate duration counter rather than a fixed enum); mercenary table count and empty-slot rules; and map the DAT's different post-city layout. A same-day, no-turn-advance supply transfer at a non-capital city would retest conservation, since a multi-turn resupply at Arretium showed no change in that city's own stock. A tighter single-action tax save (no other turn advance) would locate where the tax dialog's displayed "income" is stored, since the last attempt spanned three turns and mixed in AI battles and a capture. A same-day before/after Mobilize click would confirm the garrison-to-army troop conservation and new-army template (410 supply / 0 money / 8 moves / morale 2) without other confounds. Routine checks should use three or four relevant saves; larger scans need a specific reason. The larger goal remains a useful world-state viewer backed by validated inputs before implementing game rules.

## Scope decisions to revisit at the right time

- **Fidelity:** Exact rules and UI versus faithfully modeled rules with a redesigned interface. The current default is faithful rules with a modern interface.
- **Original save writing:** Optional; reading originals and writing a new, versioned format is the safer first target.
- **Web release:** Godot 4 C# desktop is the current plan. A browser target would require a separate technology decision before implementation.
