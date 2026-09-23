# Game design: a moddable reimplementation of Imperial Conquest 2

This is a **design document, not an implementation plan**. It translates everything confirmed in `docs/roadmap.md`, `docs/decompilation-plan.md`, and the 40+ reports in `docs/reports/` into a concrete design for a new game, fills the gaps the reverse-engineering left open with explicit, documented creative decisions, and lays out how the actual build phase should run once this is agreed. Four scope decisions were made explicitly with the user before writing this:

1. **Multiplayer**: single-player plus **local hotseat** (multiple humans, one machine, turns in sequence). No networked multiplayer for now, but nothing here forecloses it later (see "Determinism" below).
2. **Maps**: **arbitrary custom maps from day one**, not just the original's 320×140 grid. The original's actual map/nations/cities becomes one *shipped* world definition among possibly many, not a hardcoded assumption.
3. **Battles**: **instant abstracted auto-resolve**. No tactical grid, no placement phase, no per-action animation — a result is computed in one shot and shown as a summary, using the confirmed combat math. This also means the original's per-exchange pacing delay cannot recur, by construction. (That delay is **not a bug**: it is a `GetTickCount` busy-wait whose duration is a *stored per-nation player preference*, with its own `TBattleDelays` settings dialog, and it can be set to zero from inside the original game **[confirmed: battle-freeze-diagnosed-procmon.md]**. It reads as a freeze only because a busy-wait never returns to the message pump. For a reimplementation this means pacing belongs in a UI settings screen, never in the combat rules — but since the shipped resolver has no per-action step to pace, there is nothing here to port.)
4. **Build process**: once this design is settled, implementation should proceed in **large autonomous chunks** (see "The build harness" at the end), not small human-reviewed increments.

Every rule below is tagged **[confirmed]** (has direct RE evidence, cited), **[derived]** (a reasonable extrapolation from confirmed data, e.g. filling in a formula's shape where only some constants were pinned down), or **[designed]** (no RE evidence exists or it was intentionally left out of scope; this is new game design). Nothing is presented as RE'd when it isn't.

> **Read [design-audit.md](design-audit.md) alongside this document.** A systematic audit found several whole subsystems this design was silent on (naval transport and naval combat, fleet condition and repair, supply as a purchased economy with per-army money purses, city fortification orders, army/unit management) and corrected four sections that were written on assumptions rather than evidence (Movement, Recruitment's mercenary claims, Diplomacy, Victory conditions — all fixed in place below). **All ten of its open questions are answered by the user and folded in here**: the auto-resolve uses the original's own instant battle resolver (Q1); naval is in scope for the first playable version (Q2); and Q3 (diplomacy fidelity), Q5 (default victory condition), Q6 (human/AI asymmetries) and Q8 (bug-reproduction policy) are all answered the same way — **ship both `classical-faithful` and `improved` as named, user-selectable rulesets**, surfaced as a prominent choice at New Game rather than buried in scenario JSON (see "Ruleset format" and "User interface" below). The milestone list at the end has been revised for all of it. Q4 (per-army purses) is folded into the same two-preset table. Q7 (city-orders extensibility) is answered independently — ship a generic, data-driven `cityOrders` table with fortify as the only shipped entry. Q9 (supply purchase) is answered from the user's own play: resupplying at your own cities is free, buying at anyone else's costs money, in both presets. Q10 (does the rout mechanic change `combat.onDefeat`) is answered: no change.

## Design principles

These are the load-bearing decisions everything else follows from:

1. **Data over code, wherever the cost is reasonable.** Every gameplay *constant* (unit stats, cost tables, tax rates, caps, thresholds) lives in external data (JSON), not hardcoded in engine source. Formula *shape* (the actual arithmetic — `tax = base × rate / 100`) stays as tested engine code, not a scripting language. A full expression-scripting system was considered and rejected: it would let scenario authors change formula shape too, but it's a lot of engineering and testing surface for a one-developer(+agent) project, and "change the numbers" covers the overwhelming majority of real modding use cases. If a future scenario genuinely needs different formula shape, that's a new `Ruleset` *variant* (a small C# strategy class selected by name in the ruleset data), not a scripting engine.
2. **The original's confirmed rules are the default ruleset**, not *the* rules. A `Ruleset` is a named, versioned bundle of the constants and formula variants above. The game ships with a `"classical-faithful"` ruleset built from everything in `docs/reports/`. Anyone (including future-me, building this) can add another ruleset without touching the engine.
3. **World layout and ruleset are separate axes.** A `World` (map + starting nations/cities/units) and a `Ruleset` (the numbers/formulas governing play) are independent, swappable pieces. A `Scenario` combines one of each plus victory conditions and player/AI assignments. This is what "leave open the possibility of creating custom scenarios" means concretely — see "The scenario system" below.
4. **Determinism first.** All randomness goes through one seeded RNG service threaded through the turn coordinator. This is required for reproducible tests, for eventually adding network multiplayer without a rewrite (lockstep-style: ship commands + seed, not state), and — practically — for an autonomous build loop to write hard assertions instead of "looks right."
5. **Assets are abstract references, resolved by a pluggable pack.** Engine and rules code never touch a file path or a specific `.wav`/`.png`; they reference stable keys (`unit.archers.icon`, `sfx.city_captured`). See "Asset packs" below.
6. **Original save files are read-only, one-way input.** Confirmed in [`roadmap.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/roadmap.md) section 6 already; restated here because it interacts with rulesets — see "Original-save compatibility."

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

Tile types are an open, extensible list — not fixed to the original's 12 terrain codes / 6 distinct terrain names **[confirmed: terrain-move-cost-table-in-dat.md; the earlier "5 values" figure was how many had been matched to screenshots in rivers-and-map-markers.md, not how many exist]** — a custom world can define new ones (a ruleset/renderer just needs matching movement-cost and art-key entries for whatever it defines).

### Ruleset format (sketch)

```json
{
  "id": "classical-faithful",
  "unitTypes": [ { "id": "light_infantry", "moves": 4, "battalionSize": 5000, "shots": 0, "range": 0, "recruitCost": 12 } ],
  "typeEffectiveness": "5x5 matrix, [confirmed: combat-type-effectiveness-matrix.md]",
  "economy": { "taxFormula": "base*rate/100", "quarterlyUpkeepPerShip": 3, "recruitCostFormula": "(troops/200)*priceTable[type]" },
  "combat": { "onDefeat": "destroy", "meleeLossCapPercent": 40, "shootingRangeFormula": "...", "qualityPromotion": { "floorTier": "average", "furtherPromotionChance": "1 in 4", "capTier": "elite" } },
  "diplomacy": { "...": "see Diplomacy section, all [designed]" },
  "victoryDefaults": { "...": "see Victory conditions section" }
}
```

Every numeric table here has a direct citation to a report in `docs/reports/` or is explicitly marked `[designed]` inside the file's own comments/docs (JSON doesn't support comments natively — use a sibling `.md` per ruleset explaining provenance, or a `"_provenance"` key per field).

### Two shipped presets, not a pile of independent flags — **[designed, answers audit Q3/Q4/Q5/Q6/Q8]**

`Ruleset` is a general mechanism (any custom `id` a modder wants), but the game **ships exactly two named presets** and treats the choice between them as a top-level product decision, not an advanced setting: `classical-faithful` (the original's confirmed behaviour, verbatim, bugs included) and `improved` (the same subsystems, with the friendlier/designed alternative wherever the audit found a faithful-vs-improved fork). Every flag below already had a place to live — a `_provenance` pointing at its audit question, per [`build-process.md` §9](build-process.md#9-standing-governance-decisions) Q-D — this just groups them into two presets a player recognizes instead of N independent toggles nobody will tune by hand:

| Ruleset flag | `classical-faithful` | `improved` | Audit Q |
| --- | --- | --- | --- |
| `diplomacy.model` | the confirmed state machine only | the confirmed state machine plus an AI opinion-score layer on top of it (option c) | Q3 |
| `economy.purses` | per-army/per-fleet money purses (cap 1,000), supply bought from whoever owns the nearest seller | centralized to the national treasury — no local-purse micromanagement | Q4 |
| `victory.default` | total conquest, 334/334 cities, 250 BC hard end | domination-over-hostiles or score-at-limit (player's choice at New Game), shorter default turn limit | Q5 |
| `seatAsymmetry` | faithful: only AI-controlled seats lose all remaining moves on a blocked step, a split army starts with 1 move for AI / 0 for human, over-capacity embarkation is trimmed for AI only | normalized: every seat (human or AI) follows the same rule | Q6 |
| `bugPolicy.diplomaticThaw` | reproduces the original's 8-column thaw bug (a cooldown between two nations both indexed ≥ 8 never decays) | fixed silently, all 16 columns thaw | Q8 |
| `combat.onDefeat` | the loser's army/fleet is destroyed outright (see Combat) | the loser's army/fleet scatters instead of being destroyed (see Combat) | new, see below |

`classical-faithful` is the default selection at New Game, consistent with "Original-save compatibility" below, which already only accepts imports onto that preset. `improved` is presented with equal visual weight, not as a hidden alternative — see "User interface".

## Subsystem-by-subsystem design

### Calendar and turns — **[confirmed]**

Week `+2 mod 12` per turn cycle, season advances at the 11→1 wrap, year decrements at the Winter→Spring wrap, quarterly billing on the season boundary. Directly reuse the confirmed calendar model from [`decompiled-turn-and-calendar-sequencing.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-turn-and-calendar-sequencing.md) and [`decompiled-quarterly-billing-and-economy.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-quarterly-billing-and-economy.md), but read both with their later corrections. The weekly city loop the first calls "population growth" is **city supply production**, and the second's "unity decays by 3" is mobilization's ([`city-population-growth.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/city-population-growth.md); both under Economy below). This subsystem needs no new design — it's ready to implement as-is, parameterized only by "weeks per season" / "seasons per year" in the ruleset in case a custom ruleset ever wants a different calendar (unlikely to be exercised, cheap to support).

**Hotseat turn model** — **[designed]**: sequential per-nation turns within one calendar tick, exactly like the original's single-active-nation model (`TPremierForm`'s active-nation field) **[confirmed structurally by the SAV trailer's active-nation field and turn-order table]**, just with human-controlled seats pausing for local input instead of dispatching to AI. Between human turns, an optional "pass the device" confirmation screen hides the previous player's info before the next human's turn starts (toggle per scenario — off by default for a fast local game between people who don't mind, on by default if a scenario explicitly marks itself "blind hotseat").

### Economy — **[confirmed, ready to implement directly]**

- Tax: `income = nationTaxBase × taxRate / 100` **[confirmed: decompiled-fleet-tax-and-mercenary-formulas.md, decompiled-quarterly-billing-and-economy.md]**.
- Quarterly upkeep ([`upkeep-payment-and-desertion.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/upkeep-payment-and-desertion.md)). The **treasury** pays regular units and garrison slots, `(troops / 200) × price`, and launched ships, `3 × ships`. It never checks the balance, so it can go into debt. Each army's **own purse** pays its mercenaries, `((troops / 200) × price × quality) / 5` **[confirmed: the human nation's whole-quarter treasury change, 6 of 6]**. **Non-payment**: a mercenary unit reached with its army's purse empty deserts as a whole unit, taking its share of supplies. Regulars never desert, and there is no morale loss or message **[confirmed: 1 desertion, Rome in debt with every regular intact]**. **Debt** (treasury below `−wealth / 500` or −20,000, or unity under 400) risks deposition: an AI nation has a 1-in-9 chance each quarter, which resets its treasury and props up its unity **[confirmed: 2 of 15]**; a human nation in debt is deposed at the start of its turn, and its seat passes to the AI **[derived]** (T39). Income includes **trade**: each trade or alliance partner's tax base / 12 **[confirmed]** (T35).
- Quarterly city **population** growth toward each city's maximum: about a quarter of the gap, plus one, reduced in steps by the tax rate (`/120`) and by mobilization (`/300`). It is skipped while an army of a nation at war with the owner stands next to the city. There is no randomness and no seasonal term. Population changes at no other time except by siege or by becoming the capital **[confirmed: city-population-growth.md, 494 of 494 growing cities over 6 save pairs; the two divisors confirmed together by one city]**. Tribute itself doesn't grow: what grows is population, and tribute's weight in the tax base scales with population ([`nation-tax-base-and-city-economy-fields.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/nation-tax-base-and-city-economy-fields.md), correcting the older quarterly-billing report). The tax-base rebuild and the treasury credit read the grown population (T35).
- Quarterly **mobilization** decays by 3, and **unity** is recomputed as `clamp(unity + 25 − tax/2 − mobilization/5, 300, 990)`. Unity drifts up under light taxes and low mobilization, and there is no flat unity decay **[confirmed: city-population-growth.md, 71/80 and 60/80 nation-quarters, the misses being events in the round]** (T35).
- Per-turn city **supply production**: each city's stock changes by `pop × (season − 40) / 10`, which is a gain in Spring, Summer and Autumn and a loss in Winter. That change is reduced by mobilization (`/200`), stops being a gain while a hostile army is adjacent, and is capped at `pop × 10`. A city whose stock is empty in Winter loses a point of loyalty with probability 1 in 3 **[confirmed: city-population-growth.md, 10,693 of 10,980 city-turns]** (T37).
- **Buying and receiving supply** ([`supply-capacity-rounding.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/supply-capacity-rounding.md)). The supply dialog is free at the buyer's own cities and fleets, and paid at foreign ones: the buyer's purse pays `tons / 5`, which goes to the city's owner. It caps an army at `troops / 100 + 1` and a fleet at `ships × 8` **[confirmed]**, and the paid path also at `money × 5`. The same dialog moves money between the treasury and an army or fleet purse, capped at 1,000. **Automatic resupply** runs when a human army ends a move next to a non-hostile city, and every AI turn within 4 tiles. It caps at `troops / 100` and tops the purse up to 500 from the treasury at own cities **[confirmed caps; derived purse rule]** (T08, T38; the triggers are T22's and T23's).
- Also loyalty/rebellion tied to tax rate, and gradual diplomatic thaw over time **[confirmed, structure]**. The quarterly loyalty draws are now transcribed from code, though not save-checked **[derived: city-population-growth.md]**: at tax < 11 and loyalty < 80, loyalty rises by `Random(4)`; with probability 1 in 3 it falls by `Random(taxRate) / 8`; a non-capital city under 30 rebels. The rebellion's choice of new owner (`FUN_0044c204`) is only partly traced (it depends on an unidentified bitmask), and the weather-event effects (`FUN_004511bc`) were never decompiled **[open]** — implement the *confirmed* structure (rebellion risk rises with low loyalty and high tax) with placeholder thresholds tagged `_provenance: "designed, structure confirmed, exact thresholds not recovered"`, tunable later without an engine change.
- Weather events: a seasonal system (Winter ~8× more frequent than Summer) **[confirmed: decompiled-weather-events.md]**, effects not fully decompiled — implement as a data-driven table of possible weather effects (storm damages a fleet, drought reduces a region's supply, etc.) with the confirmed *frequency* curve and **[designed]** specific effects, easy to expand.

### Recruitment — **[confirmed]**

`cost = (troops / 200) × priceTable[unitType]` **[confirmed: decompiled-recruitment-cost-formula.md]**; 100,000-troop army cap **[confirmed]**; a distinct mercenary pool of 50 slots **[confirmed: mercenary-pool-record.md]**.

Mercenaries are a genuinely separate economy, not "the same cost shape" — that earlier claim carried a citation ([`mercenary-pool-record.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/mercenary-pool-record.md)) that does not contain the evidence, and the real formulas are now decompiled **[confirmed: decompiled-unit-map-orders-and-record-fields.md]**:

- Hire cost `= (troops × quarterlyPrice[unitType]) / 1000 × quality`, paid from the **hiring army's own money purse**, not the national treasury.
- Quarterly upkeep `= (troops / 200) × price[unitType] × quality / 5`, versus `(troops / 200) × price[unitType]` for regulars.
- The mercenary `Label` is an index into a name table **and** is copied into the army unit slot's `+0` word, which is the regular-versus-mercenary marker. It is gameplay-relevant, not flavour: it selects the upkeep formula and blocks the unit from being merged with regulars.

### Movement — **[confirmed structure, some numbers still open]**

This section was originally written from a generic "terrain costs movement points" assumption, without checking whether the original actually works that way — a gap the user caught. It was then *over*-corrected to "only rivers cost moves", which was also wrong. The real table has now been extracted from the DAT file **[confirmed: terrain-move-cost-table-in-dat.md]**:

- A move order is issued once (click a destination) and the engine walks a straight-line path (Bresenham) to it in one step, not tile-by-tile player input **[confirmed: decompiled-army-movement-and-river-cost.md]**.
- **Every land tile costs its terrain type's move cost** — `Plain` 1, `Desert` 1, `Forest` 2, `Mountains` 4, `River` 4 (six distinct river codes, all costing 4) **[confirmed: terrain-move-cost-table-in-dat.md]**. The movement code's `2 ≤ cell ≤ 11` guard covers all land terrain, not just the river codes 6–11, which is what the earlier "rivers only" reading got wrong.
- Water is traversable only by fleets, and costs 1 (cell code `0`, `Sea`) or 3 (cell code `1`, also named `Sea` — a deeper/slower water type) **[confirmed]**. Fleet movement uses the same table.
- A city, army or fleet marker in the path **blocks the walk entirely** rather than costing moves; interactions with those happen at the destination **[confirmed]**.
- Attempting a step you can't afford aborts the move — and **for AI-controlled nations only**, also zeroes the army's remaining moves for the whole turn **[confirmed: terrain-move-cost-table-in-dat.md]**. The earlier statement of this as a universal rule was wrong; the branch is guarded on the active nation's computer-control flag.
- What sets an army's weekly `Moves` maximum in the first place is only partly known: it's recomputed weekly and reduced by a low per-army "readiness" value **[confirmed: decompiled-turn-and-calendar-sequencing.md]**; whether troop count or unit-type composition also factors in was never confirmed (two data points in [`mobilization-movement-and-city-capture-modes.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/mobilization-movement-and-city-capture-modes.md) were consistent with "constant regardless of composition" but not conclusive) **[open]**.
- Don't confuse this with the *tactical battle* per-unit `Moves` stat (light infantry 4, heavy infantry 2, archers 4, light cavalry 6, heavy cavalry 5 — [`unit-type-stat-table-in-dat.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/unit-type-stat-table-in-dat.md)) — that governs the now-abstracted instant battle resolution's internals, not the strategic map, and the two were easy to conflate before this pass separated them explicitly.

**Ruleset default**: a `moveCost` table keyed by terrain type, shipping the original's real twelve-entry table verbatim **[confirmed]** — no placeholder is needed any more. A custom `World`'s new terrain types default to cost 1 unless a ruleset explicitly prices them, keeping the "arbitrary custom maps" goal from silently drifting away from what's actually confirmed.

### City capture, siege, and defection — **[confirmed]**

Attacker strength (archers tripled) vs. defender strength (`loyalty × 150 + finishedFortificationPercent × 250 + populationThousands × 200`, `× 5/3` for a capital with loyalty > 59, `× 4/5` if owner ≠ allegiance), with a separate `× 9/10` on the defender at the siege entry point when the attacker is the city's allegiance **[confirmed: [`investigations/siege-defender-strength.md`](investigations/siege-defender-strength.md), decompiled-city-capture-resolution.md]**; forced capture changes population/fortification and pulls loyalty toward a 40 floor, defection changes neither and pulls loyalty toward a 65 floor **[confirmed: decompiled-defection-and-siege-attrition.md, galatia-elimination-and-city-resupply-confirmed.md]**; a successful capture can cascade into nearby, weakly-defended, low-loyalty cities of the same nation defecting automatically **[confirmed]**; a nation that loses its last city is eliminated (capital sentinel, unity reset) **[confirmed: galatia-elimination-and-city-resupply-confirmed.md]**. This is ready to implement close to verbatim — siege *resolution* now folds into the instant-battle-resolution engine below rather than being a separate code path, since a siege is just "attacker army vs. city garrison," resolved the same way as a field battle.

### Combat — **[confirmed: the original's own instant resolver, ported]**

Dropping the interactive tactical shell turned out *not* to mean inventing an auto-resolve — the original has one already, and this is now a port rather than a redesign.

- **Two morales, not one — do not merge them.** The **strategic army morale** is army record `+14`, a direct multiplier in the power formulas below **[confirmed: decompiled-unit-map-orders-and-record-fields.md]**. The **per-unit tactical morale** is a separate runtime array that the `±2`/`−3`-per-exchange rule operates on **[confirmed: battle-quality-promotion-and-morale-array-decompiled.md]**. Only the first is used by the shipped resolver. They were conflated in earlier drafts because one report called `+14` "army experience". Two more facts tie them together in the original, and neither reaches the shipped resolver. First, on entry to a **tactical** battle, a side whose own nation is **computer-controlled** gets `+3` to its strategic `+14`. The write is persisted, unclamped and made once per battle, and the human side gets nothing. The instant path never writes `+14`. Second, the tactical morale that `+14` seeds is clamped to `[60, 90]` at battle start **[confirmed: battle-quality-promotion-and-morale-array-decompiled.md §"The morale formula", corrected in research `1762c84`]**.
- **Held in reserve for a future optional "detailed" resolver, not used by the shipped one**: the type-effectiveness matrix, whose attacker/defender axis orientation is now settled from the melee function's own index arithmetic — row stride 10 bytes on the **attacker's** type, column stride 2 bytes on the **defender's**, i.e. `value[attackerType][defenderType]`, the orientation already published **[confirmed: combat-type-effectiveness-matrix.md, battle-replayed-rout-mechanic-and-combat-constants.md]**; the melee formula's shape and its 40%-of-own-troops loss cap, now confirmed exactly on the **attacker** side as well as the defender's, on eleven observations across two recordings **[confirmed: decompiled-combat-formula-structure.md, battle-recording-melee-cap-confirmed.md, battle-replayed-rout-mechanic-and-combat-constants.md]**; the per-type shooting-vulnerability weight (unit-type table `+0x20`, read for the *target's* type: light inf 18 · heavy inf 2 · archers 18 · light cav 15 · heavy cav 4) **[confirmed: battle-replayed-rout-mechanic-and-combat-constants.md, which identified the field from `FUN_0043845c`'s indexing; `unit-type-stat-table-in-dat.md` published the values but left `+0x20` unidentified]**; the tactical morale rule above; and the **rout mechanic** described next.
- **The rout mechanic — reserve, not shipped, but it changes how the reserve reads [confirmed: battle-replayed-rout-mechanic-and-combat-constants.md].** `FUN_00438fb0` is called on both participants after every melee exchange and on the target after every shot: a unit is removed outright when its troops fall below its type's `standardBattalionSize / 25` (light inf 600 · heavy inf 240 · archers 140 · light cav 280 · heavy cav 100), when its tactical morale is ≤ 19, or on a `Random(m) + Random(m) ≤ 29` check in the 20…39 band — and each rout then costs every surviving friendly unit 6 morale and drags anyone below 30 out with it, while handing every live enemy +5. The cascade is **one level deep**: a unit it drags out starts no cascade of its own and earns the enemy no +5 **[confirmed: same report, consequence 3, corrected in research `eb1c886`; it first said "recursively"]**. This is **the only way a unit ever reaches zero troops in a tactical battle**: both loss caps are 40% of the side's own troops, so attrition alone can only asymptote toward 1. It supersedes the `0.6ⁿ`-compounding explanation an earlier report gave for units being wiped. Nothing in the shipped resolver needs it. The instant path annihilates the loser wholesale, and its only per-unit removal is a different rule, the small-unit deletion in the resolver block below. But any future detailed resolver that omits it would never destroy a unit at all, so it belongs in the reserve as a first-class part of the tactical rule set rather than a footnote.
- **The tactical path is strongly stochastic, which constrains how the reserve could ever be validated [confirmed: same report].** The identical starting save was fought twice and produced 63,282 vs **75,536** surviving Roman troops (a third of the total loss) and a different set of destroyed units. So a future detailed resolver's parity test against the original has to be a **distribution** test over many seeds, not an equality test against one recorded battle. This is a design constraint on that hypothetical work, not on anything shipping now — the shipped instant resolver has exactly one random term (`× (1 + random(4)/10)`), drawn through the seeded `IRng`, and stays exactly assertable under a fixed seed.
- **What's redesigned — decided (audit Q1): use the original's own instant resolver.** An earlier draft of this section proposed running the tactical exchange math headlessly with an invented `"pairing"` rule, on the stated grounds that "the original's own placement-driven pairing doesn't translate to an instant-resolve model". That premise was false: the original **already has an instant, non-tactical resolver** and uses it for every battle in which no human is involved. The reimplementation adopts it, so the auto-resolve is confirmed RE'd math rather than an invention **[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]**:

```text
armyPower(a)  = ( Σ_units powerWeight[type] × troops / 100 ) / 80 × armyMorale
fleetPower(f) = ships × condition / 10 + (carriedArmy ? armyPower/50 : 0)
                then × (1 + random(4)/10)

field battle   winner = higher armyPower, ties to the defender
               loser's army destroyed outright
               winner casualties  = applyLosses(loserPower × 40 / winnerPower)
                 ratio ≤ 40: the weaker power × 40 / the stronger, 40 only at a tie
                 per unit: troops −= troops / (Random(15) + 105) × ratio
                 then every unit left below standardBattalionSize / 10 (national)
                 or / 5 (mercenary) is deleted, before the promotion roll
               winner absorbs loser's money and supplies (supplies capped at troops/100)
               every surviving unit: quality = max(quality, average); 1-in-4 → quality + 1 (cap elite)
               unity: loser −25, winner +25 (cap 990)
               2-in-5 chance of an automatic peace treaty if loser unity > 500 and cities > 7

siege          attackerPower vs. defenderPower (fortification/loyalty-based), archers tripled
               — already the model in decompiled-city-capture-resolution.md, now consistent with the above
               the attacker takes applyLosses (with its deletion pass) on every attempt, win or lose

naval battle   winner = higher fleetPower, ties to the defender; loser's fleet (and any army
               aboard it) destroyed; winner loses ships and condition in proportion to how close
               the fight was; unity ± floor(loserShips / 2)
```

`applyLosses` is `FUN_0044AE20`. Its second pass, the small-unit deletion, is **[confirmed: decompiled-defection-and-siege-attrition.md, corrected in research `54b85d0`]**. It is a different rule from the tactical rout floor (`/ 25`). For the port, see [#289](https://github.com/diegoami/imperial_conquest_2/issues/289). The `ratio` argument at the siege and naval call sites is not in any report yet ([#290](https://github.com/diegoami/imperial_conquest_2/issues/290)).

- **Consequence to accept deliberately**: the [`full-battle-resolution-rome-vs-gaul.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/full-battle-resolution-rome-vs-gaul.md) numbers (99,882 → 63,282, per-type) came from the **tactical** path and **cannot** be reproduced by this resolver — it annihilates the loser rather than producing a per-type attrition breakdown. That fixture therefore stops being a milestone-7 acceptance test and becomes evidence for a *possible later* optional "detailed" resolver — and a **second, independent reason** to keep it out of any acceptance test has since appeared: the same save refought produced 75,536 instead of 63,282, so those numbers are one draw from a wide distribution, not a reproducible target for anything (the `Ruleset` formula-variant mechanism already covers adding one without an engine change). The same holds for the Seleucid–Ptolemaic tactical battle. The `ratio ≈ 46` at which the instant per-unit loss matches its 40.8% is **unreachable**: the ratio is at most 40, and 14–27 for those two armies over strategic morale 51–70 **[confirmed code, derived numbers: instant-resolver-cannot-reproduce-a-tactical-battle.md, corrected in research `54b85d0`]**. The resolver's ceiling is about 35.5%, reached only at a tie, so that battle measures the tactical model's excess over the instant path. It is not a calibration of `armyPower`, which has no quality term. Milestone 7's *done when* has to be restated against the instant resolver's own arithmetic instead — see the build-harness section.
- The type-effectiveness matrix, the melee 40% cap and the tactical morale rules stay recorded as confirmed research; they are inputs to that future detailed resolver, not to the shipped one. **This reserve is also the natural foundation for a possible future battle screen** (see "User interface" below): it is the one thing that could produce the per-unit-type exchange log a tactical replay would need to show, which the aggregate instant resolver never will, by design.
- The result is a single-shot `BattleResult` carrying both sides' power values, the winner, the losing side's fate (destroyed or scattered — see below), the winner's per-unit casualties and promotions, the absorbed money/supplies, the unity swing, and whether the automatic peace treaty fired. The battle-result screen presents that, not the original tactical dialog's per-type attrition table — which the shipped resolver does not produce **[designed presentation, confirmed contents]**. Keep `BattleResult` presentation-agnostic: nothing about it should have to change if a richer optional screen is bolted on later.
- The promotion rule the shipped resolver uses is the **instant path's own**, which needs no reinterpretation: every surviving unit is raised to at least "average", then 1-in-4 gains a further tier, capped at "elite" **[confirmed]**. **The tactical path's slot-adjacency promotion rule has since been withdrawn entirely** — the same battle replayed from the identical save breaks it three ways (an "average" unit adjacent to a destroyed slot was not promoted, an "average" unit with no adjacent casualty was, and a "very good" unit went to "elite", which the rule cannot express), and what fits both battles — 6 promotions across 30 survivors — is the *same* uniform 1-in-4 roll the instant path already uses **[derived, supersedes a prior derived rule: battle-replayed-rout-mechanic-and-combat-constants.md]**. Convenient rather than awkward: there is now **one** promotion rule across both code paths instead of two, so this design's choice needs no defending and a future detailed resolver inherits the same rule.

#### The defeated side's fate — `combat.onDefeat`, `classical-faithful` vs `improved` — **[designed, new]**

The original always annihilates the loser (`classical-faithful`, verbatim, as above). Dropping the tactical shell removes any sense of a fighting retreat, which reads as unusually harsh for a game whose auto-resolve now runs constantly rather than occasionally — so the `improved` ruleset changes what happens to the losing **field-battle and naval-battle** side, without touching the confirmed math for who wins or how many casualties the winner takes:

- The loser's army/fleet **survives at reduced strength** instead of being destroyed: apply the same `loserPower × 40 / winnerPower`-shaped casualty formula to the loser's own troops (mirrored, not the winner's number), rather than zeroing it out. Exact ratio is ruleset data, defaulting to the winner's own cap, tagged `_provenance: "designed, no original analogue — the original has no partial-defeat outcome to measure"`.
- The survivors **scatter**: relocated 2–4 tiles (ruleset data, `combat.improvedDefeat.scatterTiles`) from the battle site, in a direction away from the victor, onto the nearest valid tile of the right kind for that army/fleet (passable land for an army, sea for a fleet) that is not occupied by another army/fleet/city. If no valid tile exists at the target distance, shrink the distance one tile at a time down to 1; if even an adjacent tile is unavailable (fully boxed in), fall back to the `classical-faithful` outcome — destroyed — because there is nowhere to route it, and that is a corner case worth a named test rather than a silent crash.
- The scattered army/fleet's **moves are zeroed for the rest of the turn it lost on**, exactly like the existing (currently AI-only, `classical-faithful`) blocked-step rule, generalized here to whichever seat just lost. This, combined with the fact that the **victor's own move for that turn was already spent issuing the attack**, is what stops "the other army, which would not be able to catch it immediately" — nobody gets a free second action to pursue in the same turn. Catching a routed army is a *following-turn* decision, made with normal moves against a normal, visible map marker; nothing hides it.
- Money and supplies: unchanged from the confirmed formula — the winner still absorbs them (a scattered army leaves its baggage behind exactly as a destroyed one does); only the troops' fate differs.
- Unity: same ±25 swing as `classical-faithful`, since that reflects the *battle's* outcome, not the survivors' fate.
- **Siege is explicitly out of scope for this flag.** A city garrison has nowhere to scatter to — it is defending its home — so sieges keep the existing capture/defection model regardless of `combat.onDefeat`. This flag only ever touches the field-battle and naval-battle variants of the instant resolver.

### Naval — **[confirmed; in scope for the first playable version (audit Q2)]**

Decided: naval is in, not deferred. Without transport, large parts of the classical Mediterranean map are unreachable, so this is load-bearing rather than a side feature. Everything here is confirmed **[decompiled-unit-map-orders-and-record-fields.md, decompiled-diplomacy-peace-terms-and-instant-battles.md]**:

- **Building**: orders are 10–100 ships, cost `ships × 10`, and take a 24-tick construction countdown at a named coastal city; only nations with coastal cities can build. The fleet appears on the map on completion with condition 100%, 50 tons of supply, and no money.
- **Upkeep and supply**: `ships × 3` per quarter from the treasury; supply capacity `ships × 8` tons, bought at 1 talent per 5 tons out of the fleet's own purse (cap 1,000).
- **Movement**: sea tiles only, paying the terrain table's sea costs (1 for the common code, 3 for the slower one); the same one-click Bresenham walk as armies.
- **Transport**: one army per fleet, capacity `ships × 500` troops. An embarked army leaves the map, moves with the fleet, and is lost if the fleet is lost.
- **Condition and repair**: condition is a 0–100% multiplier on combat strength, damaged by battle (and by storms, via the confirmed weather system), repaired only at one of your own cities at `ships × points / 5` talents, which costs the fleet its remaining moves.
- **Combat**: the naval resolver in the Combat section above. A fleet docked at its own city cannot be attacked.
- **Fleet management**: join (≤ 100 ships combined), split (≥ 20 ships), fleet-to-fleet transfer, and scuttle (near your own city; money to the treasury, supplies to the city). None of these work while carrying an army.

### Diplomacy — **[mostly confirmed; only the AI's decision-making is designed]**

**This section's original premise was wrong.** It said diplomacy "lives in unnamed AI-only code, never reached by name — genuinely not recoverable". That conclusion came from decompiling `TPolitics_MakePeace` alone; the rest of the system is in `TPolitics`, `TBattlePols` and `FUN_00450C68`, all reachable by name. See [decompiled-diplomacy-peace-terms-and-instant-battles.md](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md). What is actually confirmed:

- A symmetric 16×16 relation matrix (nation record `+0x26`) with states `0` peace, `1` trade, `2` alliance, `3` war, and **negative values as cooldown counters** — `−8` after breaking trade, `−24` after breaking an alliance, `−18` after ending a war — that thaw quarterly (`+1`, and `min(0, v+3)` with probability 1/3) **[confirmed]**.
- **Maximum 3 trade partners** per nation; trade is refused while a cooldown is active or while allied/at war **[confirmed]**.
- **Alliances and wars are contagious**: allying with a nation drags you into its wars; declaring war drags in the target's allies **[confirmed]**.
- **Attacking anything auto-declares war** (`TUnitMap_SelectUnit` sets the relation to 3 before resolving) **[confirmed]**.
- Post-battle peace terms: the loser ends **all** trade agreements and **all** alliances and pays reparations — unless the victor is the weaker nation on `population × unity` or on total army strength, in which case it is an "honourable peace with no reparations or penalties" **[confirmed]**.
- `reparations = W/4 + random(W/4) + cityCount × 10`, where `W` is the loser's nation `+0x44C` field. That field is the **tax base**, not wealth; wealth is the separate `+0x430`, `Σ population × 3000` ([`nation-tax-base-and-city-economy-fields.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/nation-tax-base-and-city-economy-fields.md)). **[confirmed]**: the one observed payment ([`diplomatic-reparations-and-more-captures.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/diplomatic-reparations-and-more-captures.md), Ptolemaic `999 → −1270`) has now been checked against the save's own field values. With W = 6,188 and 48 cities the formula's range is 2,027–3,573, and the observed 2,269 falls inside it. The `random` term means only a range can be checked, not an exact value. The "drop the poorest partner" rule when a fourth trade partner opens also keys on the tax base.
- The AI-to-AI reparation trigger is a 2-in-5 chance after a decisive AI-vs-AI field battle, gated on the loser's unity > 500 and city count > 7 **[confirmed]**.
- Hotseat: `THVHBatPols` is a freely negotiated talent payment between two human seats after a battle, with no formula **[confirmed]**.

Still genuinely **[designed]**: the AI's *willingness* to offer or accept a treaty outside the two hard triggers above (unnamed AI code, out of scope per the roadmap), and any numeric "opinion" score layered on top of the confirmed state machine.

How faithfully to follow this model versus the opinion-score design was **open question Q3** in [design-audit.md](design-audit.md) — now answered: both, as the `diplomacy.model` ruleset flag (see "Two shipped presets" above).

### AI — **[designed, intentionally out of scope for RE by the project's own standing decision]**

A rule-based (not ML) heuristic AI, tunable via per-nation "personality" parameters in scenario data (`aggression`, `expansionDrive`, `loyaltyToAlliances`, each 0–1):

1. **Economy phase**: recruit/build up to an affordability threshold scaled by `expansionDrive`, prioritizing whichever unit types the effectiveness matrix suggests counter a currently-visible threat.
2. **Military phase**: evaluate each border city's threat level (visible enemy strength within N tiles) against its own garrison; reinforce, hold, or — if `aggression` and a favorable strength ratio both clear a threshold — attack.
3. **Diplomacy phase**: seek peace if losing and strength ratio is poor; consider alliance offers from nations with a shared enemy.
4. **Victory-awareness**: nations close to the scenario's victory condition weight their decisions toward securing it (e.g., a domination-victory AI prioritizes attacking weak neighbors over turtling).

This is intentionally simple to start — a heuristic scoring function per candidate action, pick the highest score, no search/lookahead — because it's easy to reason about, easy to tune via data, and easy to unit-test (given a world state, assert the AI picks the expected action class). More sophistication (lookahead, learning) is explicitly future work, not needed for a first playable version.

### Victory conditions — **[the original's is confirmed; the alternatives are designed]**

The original's own condition *was* recoverable, contrary to this section's earlier claim: `THumanFalls_InitializeForm` (`0x00455E38`) tests the nation's city count against **334, the total number of cities on the map**, and awards *"You have conquerred the Mediterranean, a unique achievement."* — total conquest is the only win. The same screen compares the year against **250 BC** (start is 270 BC) and reports the reign's length, a candidate hard time limit **[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]**. Whether total conquest should be the shipped default was **open question Q5** in [design-audit.md](design-audit.md) — now answered: yes, as the `classical-faithful` default; `improved` defaults to a friendlier condition (see "Two shipped presets" above).

Scenario-configurable, default set to ship **[designed]**:

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

### Army and fleet markers scale with size — **[confirmed]**

Decompiled after this requirement was first raised from a confirmed-in-play recollection: `TUnitMap_SelectUnit`'s marker arithmetic proves the original map cell code itself encodes a **three-tier size band**, not just owner — see [`decompiled-unit-map-orders-and-record-fields.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-unit-map-orders-and-record-fields.md):

```
armyMarker  = owner + (troopsThousands < 25 ? 200 : troopsThousands < 50 ? 216 : 232)
fleetMarker = owner + (shipCount       < 25 ? 300 : shipCount       < 50 ? 316 : 332)
```

Three army tiers (< 25,000 / 25,000–49,999 / ≥ 50,000 troops) and three fleet tiers (< 25 / 25–49 / ≥ 50 ships), each 16-wide (one slot per nation), which is also why `(code − 200) % 16` / `(code − 300) % 16` recovers the owner regardless of tier — the band was hiding in plain sight in the same arithmetic that gave the owner. `TUnitMap_SelectUnit` accepts exactly `200..247` for armies and `300..347` for fleets, matching. The reimplementation ships these exact three tiers per type: `army.tier1.icon`/`army.tier2.icon`/`army.tier3.icon`, `fleet.tier1.icon`/`fleet.tier2.icon`/`fleet.tier3.icon`, resolved the same way as every other asset key (principle 5 above) so a different art pack can reskin (but not re-tier — the thresholds are the original's, not a style choice) freely. The current `MapViewer` (read-only inspector) does not yet do this: `DrawArmy`/`DrawFleet` scale only with zoom level, not troop/ship count — the reimplementation should not regress behind evidence that's now in hand.

### City markers — **[open]**, not the same claim

The map cell code does **not** size-band cities the way it does armies/fleets: a city's code (20–199, per [`terrain-move-cost-table-in-dat.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/terrain-move-cost-table-in-dat.md)) is purely its identity/index, with no population term in the arithmetic. Whether the original's *rendering* code separately picks a different city sprite by `PopulationThousands` — the thing a player would actually see on screen — has not been decompiled or confirmed either way. Worth checking by eye in the running original (see the reply to "what else is worth investigating"); until then this stays `[designed]`, not `[confirmed]`: a small ordered set of tiers (village → town → city → metropolis, or similar) keyed off `PopulationThousands`, `city.tier1.icon` … `city.tierN.icon`, plus a separate `city.capital.icon` (capital status is orthogonal to population, already implied by `NationRecord.CapitalCityIndex`). If a future decompilation pass finds the original has no such visual distinction for cities at all, drop this section rather than keep a `[designed]` requirement evidence has actively contradicted.

## Original-save compatibility — **[confirmed format, designed policy]**

The SAV/DAT format itself is essentially fully mapped ([`decompiled-sav-file-layout.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-sav-file-layout.md) and everything built on it). Import policy: an imported original `.sav` always maps onto the shipped `classical-mediterranean` World and the `classical-faithful` Ruleset — those are the only numbers the imported state was ever balanced against. Attempting to load an original save into a game already using a different Ruleset or World is rejected with a clear message, not silently reinterpreted. This keeps `IC2.Data`'s hard-won parsers directly useful (the import path *is* `IC2.Data`, essentially unchanged) without requiring every custom ruleset to somehow stay compatible with 1996 byte layouts.

## Testing and determinism

- One seeded RNG service, injected everywhere randomness is used (combat rolls, weather events, AI tie-breaking) — never `System.Random` called directly in gameplay code.
- **Golden fixtures from real play**, not just hand-written unit tests: the project already has exact, documented before/after numbers for real game sessions —
  - the Rome/Gaul battle's exact per-type troop losses ([`full-battle-resolution-rome-vs-gaul.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/full-battle-resolution-rome-vs-gaul.md)),
  - the melee 40%-cap hits from six recorded exchanges ([`battle-recording-melee-cap-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-recording-melee-cap-confirmed.md)),
  - the army-to-army transfer's exact reciprocal conservation ([`army-to-army-transfer-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/army-to-army-transfer-confirmed.md)),
  - the Galatia elimination cascade's exact city-by-city transfer pattern ([`galatia-elimination-and-city-resupply-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/galatia-elimination-and-city-resupply-confirmed.md)).
  
  Each becomes a regression test: load the equivalent starting state into the new engine, run the equivalent action, assert the same numbers come out. This is unusually strong test coverage for a reimplementation, and it's a direct payoff of this session's save-diffing work.
- Every `Ruleset`/`World` file should round-trip through (de)serialization in a test, and a minimal synthetic `World`+`Ruleset` (a 3-city, 2-nation toy scenario) should exist purely for fast unit tests that don't depend on the full classical map.

## User interface — **[designed]**

The original is pop-up-heavy: nearly every action (`TArmyToArmy`, the city resupply dialog, recruitment, diplomacy) opens a separate modal `TForm` on top of the strategic map, stacking dialogs as play goes on. The new design replaces most of that with a **persistent contextual side panel** next to the map — select something, the panel updates in place, nothing stacks or gets lost behind another window. Two moments stay as genuine full-screen/modal interruptions because the original's own design already earned them: the **battle result summary** (a real payoff moment, worth a dedicated screen) and the **hotseat handoff** (needs to be unmissable between human turns).

Screen/flow:

1. **Main menu** → New Game / Load / Settings (asset-pack selection lives here) / Quit.
   - **New Game's first and most prominent choice is the ruleset**: a two-card chooser, `Classical Faithful` versus `Improved`, shown before or alongside scenario selection — not a dropdown buried in an advanced-options panel. Each card carries a short, plain-language summary of what it changes (diplomacy, victory condition, human/AI symmetry, bug reproduction, and — the one visible in play most often — what happens to a defeated army: destroyed outright versus scattered and able to be pursued later). `Classical Faithful` is the default highlight, consistent with it being the only ruleset original saves can import onto. Only after the ruleset is picked does the flow continue into `Scenario` selection, human/AI seat assignment, and AI personality sliders.
2. **Main game screen** — the dominant, near-always-visible view:
   - **Top bar**: calendar (week/season/year), active nation/seat (load-bearing for hotseat), End Turn.
   - **Map**: directly extends the existing `MapViewer` (Godot `Control`, zoom/pan/click-to-select already built) — rendering isn't replaced, just given commands instead of being read-only.
   - **Context panel** (right side, or below on narrow screens): swaps content by selection — a city (recruit, tax rate, garrison, the confirmed troop/money transfer-slider mechanic from `TArmyToArmy`/the resupply dialog), an army/fleet (move, mobilize, attack → triggers instant battle resolution, transfer, disband), or a nation overview (treasury, unity).
   - **Bottom toolbar**: the original's city/army/fleet-type filter icons ([`menu-and-toolbar-inventory.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/menu-and-toolbar-inventory.md)), reused as map-overlay toggles.
   - **News log**: persistent and dismissible, not modal — built from the confirmed news-log content ([`decompiled-news-log-identified.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-news-log-identified.md)), browsed often but never blocking.
3. **Battle result** (dedicated modal): the confirmed aggregate summary the shipped instant resolver actually produces — winner/loser, casualties, the loser's fate (destroyed or scattered, per `combat.onDefeat`), captured money/supplies, unity swing. **Reserved, not built now**: a selectable alternate battle presentation — e.g. a tactical replay or animation, which might look nothing like the original's own battle screen and could itself be a ruleset/scenario-level choice — sitting on top of the detailed resolver already held in reserve (see Combat). Nothing in `BattleResult` or this screen should be built in a way that forecloses adding that later; it's future work, not part of any milestone below, but the seam should exist.
4. **Diplomacy screen**: the original's peace/trade/ally/war grid per nation ([`menu-and-toolbar-inventory.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/menu-and-toolbar-inventory.md)'s International Relations screen) — already a clear, working UI pattern, reused as-is.
5. **Hotseat handoff**: a blocking "Pass to [Nation]" screen between human turns, with the optional blind-info-hiding mode from the multiplayer design above.

A concept mockup of the main game screen (real data, not lorem — Rome's actual 99,882-troop army composition, the actual Rome/Gaul battle numbers, real news-log text) is published as an artifact for visual reference during implementation: https://claude.ai/code/artifact/a10a8d52-fb69-4790-9394-ba6e80459aaf. It's a static/interactive mockup, not implementation — Godot scenes should follow its layout intent, not its markup.

## The build harness (once this design is agreed)

Given the "large autonomous chunks" preference, the actual build should proceed as a backlog of milestones, each with a hard, checkable **definition of done** — a CLI-runnable demo plus passing tests — so progress is self-verifiable without a human watching every step.

Proposed milestone order:

Revised after the audit (`design-audit.md` §4 found missing milestones, three dependency inversions, and seven *done when* criteria that an autonomous loop could not actually check). Naval is now a first-class milestone (audit Q2), and battle resolution is the original's instant resolver (audit Q1), so milestone 7's acceptance test changed.

1. **Data model + loader**: `World`/`Ruleset`/`Scenario`/`SaveGame` types, JSON (de)serialization, the toy 3-city/2-nation fixture, **and the fixtures file** — every exact number in `docs/reports/` transcribed once, so later milestones assert against data rather than hunting for it. *Done when*: round-trip tests pass and the fixtures file loads.
2. **Turn/calendar engine**: week/season/year advance, active-seat rotation (including hotseat). *Done when*: a CLI advances N turns on the toy scenario and prints a calendar state matching a hand-computed expected sequence.
3. **Economy**: tax, upkeep, tribute, loyalty drift, **supply purchase, per-army/fleet money purses**. *Done when*: Rome's `base = 2,440` reproduces both the 15% and 20% income figures; ship upkeep `= 3 × ships`; the 13-unit Roman roster's regular upkeep computes to **442**; that army's 482 tons reads **100%**; a 100-ton supply purchase costs **20** talents at a city the buyer does not own and nothing at its own (Q9).
4. **Recruitment and mercenaries**: standing recruitment, the pool, hire-from-army-purse, the two upkeep formulas. *Done when*: `(troops/200) × price[type]` reproduces the solved values; the Felsina hire (6,438 "very good") produces the report's cost and empties the slot; a mercenary unit's upkeep is its regular cost × quality / 5.
5. **Strength functions** (`armyPower`, `fleetPower`, siege defender strength). Extracted early because milestones 6, 7 and 9 all consume them. *Done when*: unit tests cover the archers-tripled rule, the `+0x26` power weights, and the morale multiplier.
6. **Movement and terrain**. *Done when*: the 12-entry terrain table drives costs (Plain 1, Forest 2, Mountains 4, River 4); a marker tile blocks the walk; an unaffordable step aborts, and additionally zeroes moves for an AI seat only.
7. **Naval**: construction countdown, condition and repair, transport, sea movement, join/split/scuttle. *Done when*: a 10-ship order costs 100 talents and launches after 24 ticks at 100% condition with 50 tons; an army of `> ships × 500` troops is refused embarkation; repair of N points costs `ships × N / 5`; a fleet carrying an army refuses repair, scuttle, split and join.
8. **Battle resolution** (the original's instant resolver, all three variants — field, siege, naval — plus the `improved`-ruleset `combat.onDefeat` scatter behaviour for field/naval). *Done when*, with a **fixed seed** and exact assertions: the higher-power side wins and ties go to the defender; winner casualties equal `loserPower × 40 / winnerPower`; absorbed supplies cap at `troops / 100`; every survivor is at least "average"; unity moves ±25 (±`floor(ships/2)` at sea) and clamps at 990; under `classical-faithful` the loser's army/fleet is destroyed outright; under `improved` the loser survives at the mirrored casualty ratio and relocates 2–4 tiles away onto a valid unoccupied tile, its moves zeroed for the turn, with a boxed-in fallback to the destroyed outcome; sieges are unaffected by `combat.onDefeat` regardless of ruleset. *Not* the Rome/Gaul per-type numbers — those came from the tactical path and are out of scope for this resolver by design.
9. **City capture/siege and defection cascade**. *Done when*: a scripted scenario reproduces the Galatia-elimination pattern city by city (2 "falls to" with population/fortification loss, 7 "defects from" without), and the loyalty floors (40 / 65 / toward 90) hold.
10. **City orders**: fortification, including the `> 100` in-progress encoding and a siege wiping a pending order. *Done when*: a fortify order costs `population × points`, reads back as in-progress, and is cleared by an attack.
11. **Diplomacy** (the original's confirmed model). *Done when*: the relation state machine round-trips all four states; the three break-cooldowns are applied; the 3-trade-partner cap is enforced; alliance and war contagion fire; the quarterly thaw converges; and `reparations = W/4 + random(W/4) + cities × 10` is exact under a fixed seed. No AI required.
12. **AI** (heuristic, start simple). *Done when*: **50 fixed seeds** each run an all-AI toy scenario to a victory condition **or a stated turn cap**, with zero crashes, stalls, or illegal commands.
13. **Victory conditions**. *Done when*: one test per shipped condition (including the original's all-cities condition and the 250 BC limit), plus a scenario-custom goal firing.
14. **Army/unit management**: join/split armies, join/split/rename/disband units. *Done when*: the 20-unit, 100,000-troop, 198-army and battalion-size merge caps are all enforced, and a merge averages quality.
15. **Original-save import bridge** using `IC2.Data` directly. *Done when*: every save in the sample set imports and round-trips through the new world model without state loss.
16. **New-format save/load**. *Done when*: a mid-game state round-trips byte-for-byte after N turns, plus a forward-compat test on an older version file.
17. **News log**: the 40-slot log and the confirmed message texts. Each slot holds at most 60 bytes of ASCII text, and slot 0 is the oldest. Each round ends with a blank line and a week header. Only the reparations amount is comma-grouped. Trade and alliance offers are dialogs, not news **[confirmed/derived: news-log-format-and-messages.md]** (T10, T42). *Done when*: each confirmed event type appends its literal message.
18. **Godot UI**, incrementally layered on the by-then-solid headless engine — map rendering already exists and needs extending to commands, not replacing. *Done when*: a scripted headless run loads a scenario, issues one order of each type through the command layer, and ends a turn; the New Game flow presents the ruleset choice first, as the two-card `Classical Faithful` / `Improved` chooser, before scenario/seat selection.
19. **Scenario authoring docs and a couple of example custom scenarios**. *Done when*: the example scenarios load and run 10 turns headlessly.
20. **Packaging/polish**. *Done when*: the packaged build launches and loads a scenario on a machine without the dev toolchain.

How this list is built: as the tasks in [task-catalogue.md](task-catalogue.md) in [task-catalogue.md](task-catalogue.md), through the pipeline in [build-process.md](build-process.md). The user is consulted when a genuinely ambiguous design gap appears that this document doesn't cover, or a decision is more product/taste than engineering (art direction, a UX call, a scope trade-off) — [build-process.md §4.5](build-process.md#45-when-to-escalate-to-the-user). Everything else — including every `[designed]` placeholder above — is implemented autonomously, precisely because it's already documented as a deliberate, revisitable choice rather than an unstated assumption.

## Open questions genuinely left for later

A few things were deliberately deferred rather than decided here, because they don't block starting the build:

- Exact reparations/diplomacy formula tuning — ships as a placeholder, easy to retune once players notice it feels off.
- Whether hotseat's "blind handoff" screen should be on or off by default per scenario type — a UX call, not an engineering one.
- Whether a future in-game scenario/world editor is worth building, and when.
- Whether network multiplayer is ever pursued — the determinism-first design keeps the door open without committing effort now.
- **A selectable, optional tactical/animated battle screen**, built on the held-in-reserve detailed resolver, sitting alongside the shipped aggregate battle-result screen (see "User interface" and Combat). Not a milestone in this backlog — the requirement is only that nothing built now (`BattleResult`, the instant resolver's seams) forecloses adding it later.
- Exact tuning of the `improved` ruleset's new `combat.onDefeat` scatter parameters (survivor fraction, scatter-tile range) — shipped with a documented placeholder default, meant to be retuned from actual play rather than derived from any original-game evidence, since the original has no partial-defeat outcome to measure against.
