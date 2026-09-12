# Roadmap: modern Imperial Conquest 2 reimplementation

This is a staged plan for a compatible, modern game that reads files from a user's original Imperial Conquest 2 installation. The public repository will contain our code, tests, and research notes, but not the original EXE, DAT, HLP, CNT, WAVs, saves, or screenshots. The current reference is the full freeware v1.01 package; the v0.99 demo is useful for comparing differences.

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

- [x] Inventory and hash the five available saves. Record the reported one-turn relationship, [initial differences](reports/one-turn-save-comparison.md), and [screenshot pairings](reports/saves-and-screenshots.md).
- [ ] Keep a read-only copy of each original file. Use temporary working copies for experiments; never overwrite the originals.
- [ ] Create small synthetic files for parser tests. Keep hashes, sizes, and expected summaries for real files in the repository, while the binary fixtures stay outside Git.
- [x] Add a command that compares any two saves in the established map and city regions, not just DAT versus one save. Post-city sections still need record boundaries.

**Done when:** A second person can reproduce every stated offset and byte count with the documented commands and their own copy of the game files.

## 2. Finish the DAT and SAV format specification

- [x] Render the candidate 320 × 140 map and overlay all city coordinates; column-major storage and coordinate orientation are strongly supported by geography.
- [x] Match five common cell values to original screen terrain through registered screenshots.
- [ ] Determine the remaining tile meanings and whether the grid includes dynamic markers.
- [ ] Identify every field in the 34-byte city record by comparing saves, the help file, form labels, and executable references. Verify field width, signedness, units, and allowed range.
- [ ] Map the DAT regions after the cities: nation, army, fleet, unit, leader, and other tables; establish record boundaries and counts before assigning meanings.
- [ ] Map the remaining SAV regions, including mutable entities, calendar, turn order, diplomacy, event log, and any checks or version markers.
- [ ] Replace provisional offset-based access in `IC2.Data` with typed models only when field meanings are supported. Preserve and reject unknown data safely; validate file sizes and bounds.
- [ ] Add focused parser tests for truncated/corrupt files and known real-file summaries.

**Done when:** The parser can load the full DAT and all known saves into a documented world snapshot, and every parsed field has an evidence trail and a confidence level.

## 3. Recover the game rules and UI behavior

- [ ] Decode the full-version WinHelp topics and Delphi `TPF0` form resources. Use the help contents and UI event names to build a feature inventory.
- [ ] Analyze the full v1.01 EXE statically, starting from DAT/SAV I/O, end-turn, city changes, movement, combat, diplomacy, AI, and victory messages. Use the demo EXE to isolate demo-only behavior.
- [ ] Write a rules specification for the turn sequence, calendar, economy/taxation, city management, recruitment, armies/fleets, terrain and supply, diplomacy, tactical battle, AI, and victory/defeat.
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

- [ ] Pin compatible Godot .NET and .NET versions, then add a Godot project that references the C# data/core libraries.
- [ ] Draw the world map with pan/zoom, city/army/fleet markers, selection, tooltips, and clear ownership and terrain cues. Keep map rendering separate from game rules.
- [ ] Add the main gameplay screens: nation setup, city details, army/fleet details, orders, economy, diplomacy, news, and end-turn flow.
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

Decode enough of the post-city records to locate nations and armies, and identify the remaining terrain values with screenshots and help files. The five available saves now support city ownership codes, turn-wide city changes, local 61-byte news slots, and a displayed-week byte. These steps will turn the current prefix reader into a useful world-state viewer and give the later game model validated inputs.

## Scope decisions to revisit at the right time

- **Fidelity:** Exact rules and UI versus faithfully modeled rules with a redesigned interface. The current default is faithful rules with a modern interface.
- **Original save writing:** Optional; reading originals and writing a new, versioned format is the safer first target.
- **Web release:** Godot 4 C# desktop is the current plan. A browser target would require a separate technology decision before implementation.
