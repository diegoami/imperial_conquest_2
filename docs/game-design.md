# Game design: a moddable reimplementation of Imperial Conquest 2

This is a **design document, not an implementation plan**. It translates everything confirmed in `docs/roadmap.md`, `docs/decompilation-plan.md`, and the 40+ reports in `docs/reports/` into a concrete design for a new game, fills the gaps the reverse-engineering left open with explicit, documented creative decisions, and lays out how the actual build phase should run once this is agreed. Four scope decisions were made explicitly with the user before writing this:

1. **Multiplayer**: single-player plus **local hotseat** (multiple humans, one machine, turns in sequence). No networked multiplayer for now, but nothing here forecloses it later (see "Determinism" below).
2. **Maps**: **arbitrary custom maps from day one**, not just the original's 320×140 grid. The original's actual map/nations/cities becomes one *shipped* world definition among possibly many, not a hardcoded assumption.
3. **Battles**: **instant abstracted auto-resolve**. No tactical grid, no placement phase, no per-action animation — a result is computed in one shot and shown as a summary, using the confirmed combat math. This also means the original's pacing-delay freeze bug cannot recur, by construction.
4. **Build process**: once this design is settled, implementation should proceed in **large autonomous chunks** (see "The build harness" at the end), not small human-reviewed increments.

Every rule below is tagged **[confirmed]** (has direct RE evidence, cited), **[derived]** (a reasonable extrapolation from confirmed data, e.g. filling in a formula's shape where only some constants were pinned down), or **[designed]** (no RE evidence exists or it was intentionally left out of scope; this is new game design). Nothing is presented as RE'd when it isn't.

## Design principles

These are the load-bearing decisions everything else follows from:

1. **Data over code, wherever the cost is reasonable.** Every gameplay *constant* (unit stats, cost tables, tax rates, caps, thresholds) lives in external data (JSON), not hardcoded in engine source. Formula *shape* (the actual arithmetic — `tax = base × rate / 100`) stays as tested engine code, not a scripting language. A full expression-scripting system was considered and rejected: it would let scenario authors change formula shape too, but it's a lot of engineering and testing surface for a one-developer(+agent) project, and "change the numbers" covers the overwhelming majority of real modding use cases. If a future scenario genuinely needs different formula shape, that's a new `Ruleset` *variant* (a small C# strategy class selected by name in the ruleset data), not a scripting engine.
2. **The original's confirmed rules are the default ruleset**, not *the* rules. A `Ruleset` is a named, versioned bundle of the constants and formula variants above. The game ships with a `"classical-faithful"` ruleset built from everything in `docs/reports/`. Anyone (including future-me, building this) can add another ruleset without touching the engine.
3. **World layout and ruleset are separate axes.** A `World` (map + starting nations/cities/units) and a `Ruleset` (the numbers/formulas governing play) are independent, swappable pieces. A `Scenario` combines one of each plus victory conditions and player/AI assignments. This is what "leave open the possibility of creating custom scenarios" means concretely — see "The scenario system" below.
4. **Determinism first.** All randomness goes through one seeded RNG service threaded through the turn coordinator. This is required for reproducible tests, for eventually adding network multiplayer without a rewrite (lockstep-style: ship commands + seed, not state), and — practically — for an autonomous build loop to write hard assertions instead of "looks right."
5. **Assets are abstract references, resolved by a pluggable pack.** Engine and rules code never touch a file path or a specific `.wav`/`.png`; they reference stable keys (`unit.archers.icon`, `sfx.city_captured`). See "Asset packs" below.
6. **Original save files are read-only, one-way input.** Confirmed in `roadmap.md` section 6 already; restated here because it interacts with rulesets — see "Original-save compatibility."

## The core data model

Four kinds of data, loaded independently:

```
World       = terrain grid (W×H, arbitrary) + city list + nation list + starting armies/fleets/units
Ruleset     = named bundle of constants, cost tables, and formula-variant selectors
Scenario    = a World + a Ruleset + victory conditions + player/AI seat assignments + (optional) turn limit
SaveGame    = a live, versioned snapshot of a Scenario in progress (includes which World/Ruleset it started from)
```

`World` is the generalization of everything `IC2.Data` already parses from the DAT/SAV files — it's the same information (map cells, cities, nations, starting armies), just no longer assumed to be exactly 320×140 with exactly 16 nations and exactly 334 cities. The original's actual data becomes **one shipped `World`** (working name: `classical-mediterranean`), produced by a one-time export tool built on top of the existing `IC2.Data` parsers — not a runtime dependency on the user owning original files. (Importing an actual original `.sav` remains a separate, ongoing feature — see below — distinct from using the *default world* to start a fresh game.)

### World format (sketch, JSON)

```json
{
  "id": "classical-mediterranean",
  "width": 320, "height": 140,
  "terrain": "base64-or-run-length-encoded tile-id grid",
  "tileTypes": ["plain", "forest", "mountain", "river", "water", "..."],
  "cities": [ { "id": 0, "name": "Rome", "x": 95, "y": 28, "owner": "rome", "population": 220, "fortification": 60, "..." } ],
  "nations": [ { "id": "rome", "name": "Rome", "color": "#c62828", "capital": 0 } ],
  "startingUnits": [ /* armies, fleets per nation */ ]
}
```

Tile types are an open, extensible list — not fixed to the 5 confirmed original terrain values **[confirmed: rivers-and-map-markers.md]** — a custom world can define new ones (a ruleset/renderer just needs matching movement-cost and art-key entries for whatever it defines).

### Ruleset format (sketch)

```json
{
  "id": "classical-faithful",
  "unitTypes": [ { "id": "light_infantry", "moves": 4, "battalionSize": 5000, "shots": 0, "range": 0, "recruitCost": 12 } ],
  "typeEffectiveness": "5x5 matrix, [confirmed: combat-type-effectiveness-matrix.md]",
  "economy": { "taxFormula": "base*rate/100", "quarterlyUpkeepPerShip": 3, "recruitCostFormula": "(troops/200)*priceTable[type]" },
  "combat": { "meleeLossCapPercent": 40, "shootingRangeFormula": "...", "qualityPromotion": { "adjacentToCasualty": true, "fromTier": "average", "toTier": "good" } },
  "diplomacy": { "...": "see Diplomacy section, all [designed]" },
  "victoryDefaults": { "...": "see Victory conditions section" }
}
```

Every numeric table here has a direct citation to a report in `docs/reports/` or is explicitly marked `[designed]` inside the file's own comments/docs (JSON doesn't support comments natively — use a sibling `.md` per ruleset explaining provenance, or a `"_provenance"` key per field).

## Subsystem-by-subsystem design

### Calendar and turns — **[confirmed]**

Week `+2 mod 12` per turn cycle, season advances at the 11→1 wrap, year decrements at the Winter→Spring wrap, quarterly billing on the season boundary. Directly reuse the confirmed model from `decompiled-turn-and-calendar-sequencing.md` and `decompiled-quarterly-billing-and-economy.md`. This subsystem needs no new design — it's ready to implement as-is, parameterized only by "weeks per season" / "seasons per year" in the ruleset in case a custom ruleset ever wants a different calendar (unlikely to be exercised, cheap to support).

**Hotseat turn model** — **[designed]**: sequential per-nation turns within one calendar tick, exactly like the original's single-active-nation model (`TPremierForm`'s active-nation field) **[confirmed structurally by the SAV trailer's active-nation field and turn-order table]**, just with human-controlled seats pausing for local input instead of dispatching to AI. Between human turns, an optional "pass the device" confirmation screen hides the previous player's info before the next human's turn starts (toggle per scenario — off by default for a fast local game between people who don't mind, on by default if a scenario explicitly marks itself "blind hotseat").

### Economy — **[confirmed, ready to implement directly]**

- Tax: `income = nationTaxBase × taxRate / 100` **[confirmed: decompiled-fleet-tax-and-mercenary-formulas.md, decompiled-quarterly-billing-and-economy.md]**.
- Quarterly upkeep: ships `×3`/quarter, armies via the recruitment price table with a real mutiny/disband consequence for non-payment **[confirmed]**.
- Tribute growth toward a population target, loyalty/rebellion tied to tax rate, gradual diplomatic thaw over time **[confirmed, structure]**; exact rebellion-check (`FUN_0044c204`) and weather-event effect (`FUN_004511bc`) internals were never decompiled **[open]** — implement the *confirmed* structure (rebellion risk rises with low loyalty and high tax) with placeholder thresholds tagged `_provenance: "designed, structure confirmed, exact thresholds not recovered"`, tunable later without an engine change.
- Weather events: a seasonal system (Winter ~8× more frequent than Summer) **[confirmed: decompiled-weather-events.md]**, effects not fully decompiled — implement as a data-driven table of possible weather effects (storm damages a fleet, drought reduces a region's supply, etc.) with the confirmed *frequency* curve and **[designed]** specific effects, easy to expand.

### Recruitment — **[confirmed]**

`cost = (troops / 200) × priceTable[unitType]` **[confirmed: decompiled-recruitment-cost-formula.md]**; 100,000-troop army cap **[confirmed]**; mercenary hire with the same cost shape plus a distinct pool **[confirmed: mercenary-pool-record.md]**. The mercenary `Label` field's exact meaning was never resolved **[open]** — treat it as a flavor name-table index only, non-gameplay-relevant, so this gap blocks nothing.

### City capture, siege, and defection — **[confirmed]**

Attacker strength (archers tripled) vs. defender strength (fortification/loyalty-based, −20% if owner≠allegiance) **[confirmed: decompiled-city-capture-resolution.md]**; forced capture changes population/fortification and pulls loyalty toward a 40 floor, defection changes neither and pulls loyalty toward a 65 floor **[confirmed: decompiled-defection-and-siege-attrition.md, galatia-elimination-and-city-resupply-confirmed.md]**; a successful capture can cascade into nearby, weakly-defended, low-loyalty cities of the same nation defecting automatically **[confirmed]**; a nation that loses its last city is eliminated (capital sentinel, unity reset) **[confirmed: galatia-elimination-and-city-resupply-confirmed.md]**. This is ready to implement close to verbatim — siege *resolution* now folds into the instant-battle-resolution engine below rather than being a separate code path, since a siege is just "attacker army vs. city garrison," resolved the same way as a field battle.

### Combat — **[derived from confirmed formulas, redesigned presentation]**

This is where the freedom to redesign matters most. The plan preserves every confirmed number while dropping the interactive tactical shell entirely:

- **What's reused as-is**: the type-effectiveness matrix **[confirmed: combat-type-effectiveness-matrix.md]**, the melee formula's shape and its exactly-confirmed 40%-of-own-troops loss cap **[confirmed: decompiled-combat-formula-structure.md, battle-recording-melee-cap-confirmed.md]**, the morale mechanic (`±2`/`−3` per exchange depending on power ratio, clamped) **[confirmed: battle-quality-promotion-and-morale-array-decompiled.md]**, and the empirically-found quality-promotion rule (an average-quality unit adjacent to a casualty gets promoted) **[confirmed, one battle's evidence]**.
- **What's redesigned**: instead of a human placing 20 units on a grid and stepping through individual shoot/melee actions with UI pacing, the engine runs the *same underlying exchange math* internally, headlessly, for as many rounds as it takes for one side to break or a round cap to hit — using unit **pairing by matching order** (largest-vs-largest by default) rather than manual placement, since there's no grid to place units on. Pairing strategy is a named, swappable ruleset field (`"pairing": "largest-vs-largest"`, `"counter-optimized"`, etc.) — **[designed]**, since the original's own placement-driven pairing doesn't translate to an instant-resolve model. This is a genuinely new mechanic layered on confirmed math, not a guess at what the original does internally during placement.
- The result is a single-shot `BattleResult` with a full per-unit-type before/after breakdown, in exactly the shape already captured from a real battle in `full-battle-resolution-rome-vs-gaul.md` — that report's numbers are a natural **regression fixture** for this engine (see "Testing" below).
- "Adjacent to a casualty" for the promotion rule needs a redefinition too, since there's no army-slot grid anymore — **[designed]**: reinterpret it as "a unit that fought in the same round as one that was destroyed," which preserves the spirit (survivors of a rough exchange get battle-hardened) without depending on a slot-index adjacency that no longer has meaning outside the original's UI.

### Diplomacy — **[designed, dead end in the original's code]**

`decompiled-city-capture-resolution.md`'s follow-up work confirmed this lives in unnamed AI-only code, never reached by name — genuinely not recoverable without much more decompilation effort, and the roadmap already treats faithful *rules* over a byte-exact AI port as the priority. Design from scratch:

- Per-nation-pair relationship state: `war | peace | alliance`, plus a numeric "opinion" score.
- Reparations on a peace treaty scale with the loser's treasury and the war's outcome — **[derived]**: an actual AI-to-AI reparation was observed exactly once (`diplomatic-reparations-and-more-captures.md`, Ptolemaic `999 → -1270`, a `-2269` payment), consistent with "loser pays a fraction of their pre-war treasury plus a war-outcome-scaled amount," but the exact formula was never isolated. Ship a **[designed]** formula with that single data point as a sanity check, tagged as inspired-by-evidence rather than confirmed.
- AI willingness to seek peace scales with relative military/economic strength and war duration — standard 4X diplomacy heuristic, **[designed]**.

### AI — **[designed, intentionally out of scope for RE by the project's own standing decision]**

A rule-based (not ML) heuristic AI, tunable via per-nation "personality" parameters in scenario data (`aggression`, `expansionDrive`, `loyaltyToAlliances`, each 0–1):

1. **Economy phase**: recruit/build up to an affordability threshold scaled by `expansionDrive`, prioritizing whichever unit types the effectiveness matrix suggests counter a currently-visible threat.
2. **Military phase**: evaluate each border city's threat level (visible enemy strength within N tiles) against its own garrison; reinforce, hold, or — if `aggression` and a favorable strength ratio both clear a threshold — attack.
3. **Diplomacy phase**: seek peace if losing and strength ratio is poor; consider alliance offers from nations with a shared enemy.
4. **Victory-awareness**: nations close to the scenario's victory condition weight their decisions toward securing it (e.g., a domination-victory AI prioritizes attacking weak neighbors over turtling).

This is intentionally simple to start — a heuristic scoring function per candidate action, pick the highest score, no search/lookahead — because it's easy to reason about, easy to tune via data, and easy to unit-test (given a world state, assert the AI picks the expected action class). More sophistication (lookahead, learning) is explicitly future work, not needed for a first playable version.

### Victory conditions — **[designed, never reverse-engineered]**

Scenario-configurable, default set to ship:

- **Domination**: control every city, or every city belonging to nations still at war with you.
- **Score at turn limit**: a weighted sum of cities held, treasury, and unity, highest wins.
- **Scenario-custom**: an explicit goal defined in the scenario file (e.g., "hold these 3 named cities on turn 50") — this is what makes custom scenarios interesting beyond "same rules, different map."

## The scenario system

A `Scenario` file is the actual moddable/shareable unit:

```json
{
  "id": "fall-of-galatia",
  "world": "classical-mediterranean",
  "ruleset": "classical-faithful",
  "seats": [ { "nation": "rome", "control": "human" }, { "nation": "seleucid", "control": "ai", "personality": { "aggression": 0.8 } } ],
  "victory": { "type": "custom", "goal": "..." },
  "turnLimit": null
}
```

Authoring is by hand-written JSON for the initial version — no in-game editor yet (explicitly future work; not needed to have a playable, moddable game). A `World` can be reused across many `Scenario`s (same map, different starting setups/rules), and a `Ruleset` can be reused across many `Scenario`s and `World`s alike (a "hard economy" ruleset variant could apply to any map).

## Asset packs — **[designed]**

An `AssetPack` is a manifest mapping stable string keys to files:

```json
{
  "unit.light_infantry.icon": "units/light_infantry.png",
  "terrain.plain.tile": "terrain/plain.png",
  "sfx.city_captured": "sfx/capture.ogg"
}
```

The repo ships a default pack built from freely-licensed/generated placeholder art and audio — good enough to play and test with, never blocking development on asset availability. A different pack (a fan-recreated "authentic style" set, or the user's own commissioned art) is a drop-in manifest + files; original copyrighted assets never enter the repo, consistent with the existing DAT/EXE/WAV policy already in place for the RE work.

## Original-save compatibility — **[confirmed format, designed policy]**

The SAV/DAT format itself is essentially fully mapped (`decompiled-sav-file-layout.md` and everything built on it). Import policy: an imported original `.sav` always maps onto the shipped `classical-mediterranean` World and the `classical-faithful` Ruleset — those are the only numbers the imported state was ever balanced against. Attempting to load an original save into a game already using a different Ruleset or World is rejected with a clear message, not silently reinterpreted. This keeps `IC2.Data`'s hard-won parsers directly useful (the import path *is* `IC2.Data`, essentially unchanged) without requiring every custom ruleset to somehow stay compatible with 1996 byte layouts.

## Testing and determinism

- One seeded RNG service, injected everywhere randomness is used (combat rolls, weather events, AI tie-breaking) — never `System.Random` called directly in gameplay code.
- **Golden fixtures from real play**, not just hand-written unit tests: the project already has exact, documented before/after numbers for real game sessions —
  - the Rome/Gaul battle's exact per-type troop losses (`full-battle-resolution-rome-vs-gaul.md`),
  - the melee 40%-cap hits from six recorded exchanges (`battle-recording-melee-cap-confirmed.md`),
  - the army-to-army transfer's exact reciprocal conservation (`army-to-army-transfer-confirmed.md`),
  - the Galatia elimination cascade's exact city-by-city transfer pattern (`galatia-elimination-and-city-resupply-confirmed.md`).
  
  Each becomes a regression test: load the equivalent starting state into the new engine, run the equivalent action, assert the same numbers come out. This is unusually strong test coverage for a reimplementation, and it's a direct payoff of this session's save-diffing work.
- Every `Ruleset`/`World` file should round-trip through (de)serialization in a test, and a minimal synthetic `World`+`Ruleset` (a 3-city, 2-nation toy scenario) should exist purely for fast unit tests that don't depend on the full classical map.

## User interface — **[designed]**

The original is pop-up-heavy: nearly every action (`TArmyToArmy`, the city resupply dialog, recruitment, diplomacy) opens a separate modal `TForm` on top of the strategic map, stacking dialogs as play goes on. The new design replaces most of that with a **persistent contextual side panel** next to the map — select something, the panel updates in place, nothing stacks or gets lost behind another window. Two moments stay as genuine full-screen/modal interruptions because the original's own design already earned them: the **battle result summary** (a real payoff moment, worth a dedicated screen) and the **hotseat handoff** (needs to be unmissable between human turns).

Screen/flow:

1. **Main menu** → New Game (pick a `Scenario`, assign human/AI per seat, tune AI personality sliders) / Load / Settings (asset-pack selection lives here) / Quit.
2. **Main game screen** — the dominant, near-always-visible view:
   - **Top bar**: calendar (week/season/year), active nation/seat (load-bearing for hotseat), End Turn.
   - **Map**: directly extends the existing `MapViewer` (Godot `Control`, zoom/pan/click-to-select already built) — rendering isn't replaced, just given commands instead of being read-only.
   - **Context panel** (right side, or below on narrow screens): swaps content by selection — a city (recruit, tax rate, garrison, the confirmed troop/money transfer-slider mechanic from `TArmyToArmy`/the resupply dialog), an army/fleet (move, mobilize, attack → triggers instant battle resolution, transfer, disband), or a nation overview (treasury, unity).
   - **Bottom toolbar**: the original's city/army/fleet-type filter icons (`menu-and-toolbar-inventory.md`), reused as map-overlay toggles.
   - **News log**: persistent and dismissible, not modal — built from the confirmed news-log content (`decompiled-news-log-identified.md`), browsed often but never blocking.
3. **Battle result** (dedicated modal): the original's own layout, exactly as captured in `full-battle-resolution-rome-vs-gaul.md` — per-type start/finish numbers, captured money/supplies — just triggered instantly instead of after a placement/tactical phase.
4. **Diplomacy screen**: the original's peace/trade/ally/war grid per nation (`menu-and-toolbar-inventory.md`'s International Relations screen) — already a clear, working UI pattern, reused as-is.
5. **Hotseat handoff**: a blocking "Pass to [Nation]" screen between human turns, with the optional blind-info-hiding mode from the multiplayer design above.

A concept mockup of the main game screen (real data, not lorem — Rome's actual 99,882-troop army composition, the actual Rome/Gaul battle numbers, real news-log text) is published as an artifact for visual reference during implementation: https://claude.ai/code/artifact/a10a8d52-fb69-4790-9394-ba6e80459aaf. It's a static/interactive mockup, not implementation — Godot scenes should follow its layout intent, not its markup.

## The build harness (once this design is agreed)

Given the "large autonomous chunks" preference, the actual build should proceed as a backlog of milestones, each with a hard, checkable **definition of done** — a CLI-runnable demo plus passing tests — so progress is self-verifiable without a human watching every step.

Proposed milestone order:

1. **Data model + loader**: `World`/`Ruleset`/`Scenario`/`SaveGame` types, JSON (de)serialization, the toy 3-city/2-nation fixture. *Done when*: round-trip tests pass.
2. **Turn/calendar engine**: week/season/year advance, active-seat rotation (including hotseat). *Done when*: a CLI can advance N turns on the toy scenario and print the calendar state.
3. **Economy**: tax, upkeep, tribute, loyalty drift. *Done when*: golden-fixture tests against the real confirmed formulas pass.
4. **Recruitment and mercenaries**. *Done when*: cost-formula tests pass against the already-solved values.
5. **Movement, supply, and fleets** (including the now-confirmed fleet owner field).
6. **City capture/siege and defection cascade**. *Done when*: a scripted scenario reproduces the Galatia-elimination pattern.
7. **Instant battle resolution**. *Done when*: the Rome/Gaul fixture reproduces the real recorded numbers within the formula's own randomness bounds, and the melee-cap fixture hits the cap where expected.
8. **Diplomacy** (new design). *Done when*: two AI nations can reach peace and a reparations payment occurs.
9. **AI** (heuristic, start simple). *Done when*: an all-AI toy scenario runs to completion (a victory condition is reached) without crashing or stalling, across many seeds.
10. **Victory conditions**.
11. **Godot UI**, incrementally layered on the by-then-solid headless engine — map rendering already exists and needs extending to commands, not replacing.
12. **Original-save import bridge** using `IC2.Data` directly.
13. **New-format save/load**.
14. **Scenario authoring docs and a couple of example custom scenarios** (proof that the moddability goal actually works, not just designed).
15. **Packaging/polish**.

Recommended operating mode: work through this list using the `/loop` autonomous mode, committing and pushing after each milestone per the existing standing rule, running that milestone's tests plus a scripted demo before moving on. Surface back to the user only when: a milestone completes, a genuinely ambiguous design gap appears that this document doesn't cover, or a decision is more product/taste than engineering (art direction, a UX call, a scope trade-off). Everything else — including every `[designed]` placeholder above — is fair game to implement and iterate on autonomously, precisely because it's already documented as a deliberate, revisitable choice rather than an unstated assumption.

## Open questions genuinely left for later

A few things were deliberately deferred rather than decided here, because they don't block starting the build:

- Exact reparations/diplomacy formula tuning — ships as a placeholder, easy to retune once players notice it feels off.
- Whether hotseat's "blind handoff" screen should be on or off by default per scenario type — a UX call, not an engineering one.
- Whether a future in-game scenario/world editor is worth building, and when.
- Whether network multiplayer is ever pursued — the determinism-first design keeps the door open without committing effort now.
