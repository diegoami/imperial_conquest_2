# Design audit: what `game-design.md` misses, over-claims, and silently decided

`docs/game-design.md` was written quickly on top of a large but unevenly-explored evidence base. One failure mode has already been caught in play: its Movement section originally invented a generic per-terrain movement-cost table from "how these games usually work", without checking; decompiling the real code overturned it. This audit does that check systematically — it re-reads the recovered Delphi symbol table for mechanics nobody ever looked at, re-opens every `[confirmed]`/`[derived]` claim against its cited evidence, collects the judgment calls that are genuinely the user's to make, and reviews the build-milestone backlog.

Same tagging convention as `game-design.md`: **[confirmed]** (direct RE evidence, cited), **[derived]** (extrapolation), **[designed]** (new design, no RE evidence), **[open]** (not established either way).

Three new reports came out of this pass and carry the evidence for everything cited below:

- [terrain-move-cost-table-in-dat.md](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/terrain-move-cost-table-in-dat.md)
- [decompiled-unit-map-orders-and-record-fields.md](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-unit-map-orders-and-record-fields.md)
- [decompiled-diplomacy-peace-terms-and-instant-battles.md](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md)

Small, clearly-wrong things were corrected in place rather than only flagged: `game-design.md`'s Movement, Recruitment, Diplomacy and Victory sections, and correction notes in [`army-records-and-roman-roster.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/army-records-and-roman-roster.md), [`fleet-order-at-caere.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/fleet-order-at-caere.md) and [`decompiled-army-movement-and-river-cost.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-army-movement-and-river-cost.md). `IC2.Data`'s two mislabelled record fields have since been fixed too, and the corrected parser re-validated against real saves (see part 4 of the unit-map report). Everything larger is listed here for a decision.

---

## 1. Missing mechanics — things the game does that the design is silent on

Method: every class prefix in `%LOCALAPPDATA%\ReTools\delphi_symbols.tsv` (31 classes, 282 methods) was listed, cross-referenced against `docs/reports/` and `game-design.md`, and anything neither mentioned was decompiled out of `all_app_functions.txt`. Two whole classes turned out to be non-gameplay (`TCellAuto` is a cellular-automaton toy with a `SaveBMP` button; `TBattleDelays` is a settings dialog for the tactical pacing pauses — incidentally the user-facing control for the delay diagnosed in [`battle-freeze-diagnosed-procmon.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-freeze-diagnosed-procmon.md)). The rest are below.

Explicitly checked for and **not found**: there is **no leader/general system** (`TPickLeaders` is the nation-setup screen that assigns each of the 16 nations to a human or the computer, and a per-nation leader *name* string; leaders have no stats and no battlefield effect), **no technology or research**, **no espionage**, and **no city improvements other than fortification**. Those four can be marked closed rather than left as unexamined possibilities.

### 1.1 Naval transport and naval combat — an entire subsystem **[confirmed]**

Nothing in `game-design.md` mentions armies travelling by sea or fleets fighting each other.

- A fleet carries **one army**, capacity **500 troops per ship**, enforced with *"The army is too large for this fleet ?"* (`TUnitMap_SelectUnit` `0x004466CC`). Embarking sets `army[+8] = -1` and `fleet[+22] = armyIndex`; the army leaves the map and moves with the fleet.
- An embarked army cannot be joined; a carrying fleet cannot be repaired, scuttled, split or joined; destroying the fleet destroys the army.
- **Naval battle** (`FUN_0044B5D0`): `strength = ships × condition / 10 (+ carriedArmyPower / 50)`, plus a `random(4) × 10%` bonus; higher strength wins, ties to the defender, **the loser's fleet is annihilated**; the winner loses ships and condition proportional to how close the fight was; unity moves `± floor(loserShips / 2)`. News: *"`X` sinks fleet of `Y`."*
- A fleet **docked at its own city cannot be attacked**.

### 1.2 Fleet condition, repair, scuttling, construction **[confirmed]**

- Every fleet has a **condition percentage** (record `+20`) that is a direct multiplier on its combat strength and is damaged by battle. `TRepairFleet` restores it at **`ships × points / 5` talents** and zeroes the fleet's moves.
- **Scuttle** (`TUnitMap_ScuttleFleet`) must be done near one of your own cities and returns the fleet's money to the treasury and its supplies to the city.
- **Build orders are clamped to 10–100 ships** (`TBuildFleet_ChangeFleetSize`) and take a **24-tick construction countdown** (record `+10`), during which the record sits at `(0,0)` with `+20` holding the build city.
- **Join fleets** caps at 100 ships combined; **split fleet** needs ≥ 20 ships; neither works while carrying an army.

### 1.3 Supply is an economy, not a resource pool **[confirmed]**

`game-design.md` has no supply system at all beyond a one-line mention of seasonal consumption.

- **Army supply capacity = `troops / 100` tons**; **fleet capacity = `ships × 8` tons** (`TAFSupply_ChangeBuyAmount`). The capacity formula reproduces all four percentage readings on record exactly — see the new report.
- **Supply is bought, not transferred**: **1 talent per 5 tons**, debited from the **army's or fleet's own money purse** and credited to the **selling city's owner's treasury** — which may be a different nation.
- Armies and fleets each carry their own **money purse, capped at 1,000 talents**, moved to/from the treasury (or a co-located fleet) in the same dialog.

### 1.4 City fortification is a paid, queued build order **[confirmed]**

- `TUnitMap_Fortify` → `TFortifyCity`: buy `0 … (100 − current)` percentage points at **`population(thousands) × points` talents**.
- The city record's fortification word does **double duty**: `≤ 100` is a finished percentage, `> 100` encodes an order in progress (written as `fort += points × 100`), and the city panel appends *"(under construction)"*.
- A siege attempt **wipes the pending order** (`fort %= 100`).
- Refused while the city is under siege, or at 100%.

### 1.5 Army and unit management orders **[confirmed]**

- **Join armies**: ≤ 20 units and ≤ 100,000 troops combined, neither aboard a fleet, survivor's moves zeroed, money and supplies pooled.
- **Split army**: needs ≥ 2 units; hard cap of **198 armies** in play; a new army starts with morale 59, no money, no supplies, and **0 moves for a human nation / 1 move for an AI one**.
- **Disband army**: only near one of your own cities; money → treasury, supplies → that city.
- **Unit-level join** (inside one army): regulars only, same type only, and the merged troop count must not exceed that type's **standard battalion size** (unit-type table `+0x1A`) — which is what that previously-purpose-less field is for. The merged unit's quality is the **arithmetic mean** of the merged qualities.
- **Unit-level split/rename**, with the auto-naming scheme (`Nth Foot/Guards/Bowmen/Lancers/Dragoons Battalion`, ordinal counted across the whole nation) that every roster in [`army-records-and-roman-roster.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/army-records-and-roman-roster.md) exhibits.

### 1.6 Diplomacy is fully recoverable, not a dead end **[confirmed]**

See §2.7 — this is both a missing mechanic and a wrong claim. Briefly: a symmetric 16×16 relation matrix at nation `+0x26` with states peace/trade/alliance/war and **negative cooldown values**; a **maximum of 3 trade partners**; alliance and war **propagate to allies**; attacking anything **auto-declares war**; and a confirmed **reparation formula**.

### 1.7 Post-battle peace negotiation, including a human-vs-human variant **[confirmed]**

`TBattlePols` offers the loser's terms after a battle (end all trade, end all alliances, pay reparations) — or *"An honourable peace with no reparations or penalties"* when the victor is the weaker nation on population×unity or total army strength. `THVHBatPols` is the **hotseat** equivalent: a freely negotiated talent payment between two human seats. `game-design.md`'s hotseat design has no such step.

### 1.8 The original has its own instant battle resolver **[confirmed]**

`FUN_0044AEE4` resolves an army-vs-army attack **without any tactical battle whenever both nations are computer-controlled** — a straight power comparison, loser annihilated, winner taking `loserPower × 40 / winnerPower` casualties, every surviving unit promoted to at least "average" with a further 1-in-4 promotion, `±25` unity. This matters a lot for §3's first open question, because `game-design.md` assumes no such model exists in the original.

It also contains the **AI-to-AI reparation trigger** the project has been hunting: a 2-in-5 chance after a decisive AI-vs-AI battle, gated on the loser's unity > 500 and city count > 7.

### 1.9 Victory condition and end year **[confirmed]**

`THumanFalls_InitializeForm` tests `cityCount < 334` versus *"You have conquerred the Mediterranean, a unique achievement."* — the original's win condition is **holding every city on the map**. The same screen compares the current year against **250 BC** and reports the reign length as `270 − year`, confirming the 270 BC start and a candidate hard end year.

### 1.10 Smaller confirmed details with no home in the design

- **Map markers encode owner *and* size**: armies `200/216/232 + owner` for `<25k / 25–50k / ≥50k` troops, fleets `300/316/332 + owner` for `<25 / 25–50 / ≥50` ships. This closes [`roadmap.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/roadmap.md)'s `333`-vs-`335` open item: both are large fleets, of Carthage and Ptolemaic respectively.
- **Unit slot `+0` is the regular/mercenary marker** (0 = regular, non-zero = a mercenary name-table index), which drives two different upkeep formulas and blocks unit merging.
- **Mercenary hire cost = `(troops × quarterlyPrice[type]) / 1000 × quality`**, paid from the **army's** purse.
- **Mercenary upkeep = `(troops / 200) × price[type] × quality / 5`**, versus `(troops / 200) × price[type]` for regulars. The regular formula reproduces the Roman army's screenshot value of **442 talents/quarter exactly** over its 13 published units.
- **Unit-type table field `+0x26`** (LI 20 · HI 100 · Ar 40 · LC 60 · HC 120) is the per-type **combat-power weight** used by field-battle strength — one of the two fields [`unit-type-stat-table-in-dat.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/unit-type-stat-table-in-dat.md) left unidentified.
- **Terrain table**: 12 cell codes, `Sea` 1 / `Sea` 3 / `Plain` 1 / `Desert` 1 / `Forest` 2 / `Mountains` 4 / `River` ×6 at 4.

---

## 2. Audit of existing `[confirmed]`/`[derived]` claims

Ordered worst-first. Items marked **fixed in place** have already been corrected in `game-design.md`.

### 2.1 Movement: "only river-coded tiles cost movement points" — **wrong [fixed in place]**

`game-design.md` (and [`decompiled-army-movement-and-river-cost.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-army-movement-and-river-cost.md) behind it) read `FUN_0044D420`'s guard `if (2 <= cell <= 11)` as "the confirmed river range", because [`rivers-and-map-markers.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/rivers-and-map-markers.md) and [`roadmap.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/roadmap.md) §2 both record rivers as values **6–11**. `2..11` is *all land terrain*. The table the guard indexes is now extracted from the DAT file at `0x1F622`: Plain 1, Desert 1, Forest 2, Mountains 4, River 4. Forests and mountains **do** slow you down. The correction session over-corrected: the original generic assumption was directionally right and was replaced with a narrower claim that the evidence does not support either.

This is worth dwelling on, because it is the same failure in both directions: neither the original assumption nor its replacement was checked against the actual table, which was one string search away.

### 2.2 Movement: "insufficient moves zeroes the army's remaining moves" — **AI-only [fixed in place]**

The zeroing branch is guarded by `(&DAT_00474B00)[activeNation × 0x494] == '\0'`, the computer-controlled flag. For a human player the walk just stops.

### 2.3 Movement ruleset default: "`moveCost` defaulting to 0 for everything except a river type, `[designed placeholder]`" — **superseded [fixed in place]**

Real values now exist; no placeholder is needed.

### 2.4 Diplomacy: "dead end in the original's code … genuinely not recoverable" — **wrong [fixed in place]**

The prior conclusion came from decompiling `TPolitics_MakePeace` alone and finding no treasury math in it. The treasury math is in `TBattlePols` / `FUN_00450C68`, both named in the same symbol table. The whole player-facing model and the reparation formula are recoverable. `game-design.md`'s Diplomacy section was written from scratch on a false premise.

### 2.5 Reparations: "`[derived]` … the exact formula was never isolated" — **now confirmed [fixed in place]**

`reparations = W/4 + random(W/4) + cities × 10`, where `W` is nation field `+0x44C` (wealth). Caveat kept honest: this is confirmed **as code**, and is consistent in shape and magnitude with the single observed `−2269` payment, but the observation was **not** re-derived from the save's actual field values. Tagged `[confirmed formula, unverified against the one observation]` rather than plain `[confirmed]`.

### 2.6 Victory conditions: "`[designed, never reverse-engineered]`" — **partly wrong [fixed in place]**

The original's condition is in `THumanFalls_InitializeForm`. The *designed* alternatives in `game-design.md` remain fine as alternatives; what was wrong was the claim that nothing was recoverable.

### 2.7 Recruitment: "mercenary hire with the same cost shape plus a distinct pool **[confirmed: mercenary-pool-record.md]**" — **citation did not support it [fixed in place]**

[`mercenary-pool-record.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/mercenary-pool-record.md) is a save-diff report about the 50-slot pool record; it says nothing about cost. [`decompilation-plan.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/decompilation-plan.md) item 2 explicitly listed *"the mercenary cost formula's exact table values (does it use this same table?)"* as **open** at the time `game-design.md` was written. The claim happens to be true — it is confirmed now, with the actual formula — but it was tagged `[confirmed]` against a report that does not contain the evidence. This is the exact pattern worth watching for: a plausible statement wearing a citation that does not carry it.

### 2.8 Recruitment: mercenary `Label` "non-gameplay-relevant, so this gap blocks nothing" — **wrong [fixed in place]**

`Label` is copied into the army unit slot's `+0` word, and that word is the regular/mercenary marker. It changes the unit's quarterly upkeep formula and blocks it from being merged with regulars. It is gameplay-relevant.

### 2.9 Combat: "the morale mechanic (`±2`/`−3` per exchange …) **[confirmed]**" — **right, but conflates two different morales [flagged, not fixed]**

There are two: the **strategic army morale** at army record `+14` (displayed as a tier on the army panel, seeds tactical morale, multiplies both army-strength formulas), and the **per-unit tactical morale array** `DAT_004A0350` that the `±2`/`−3` rule operates on. [`battle-quality-promotion-and-morale-array-decompiled.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-quality-promotion-and-morale-array-decompiled.md) calls `+14` "army experience", which made the two look unrelated. Implementing this without separating them will produce a subtle, hard-to-find bug. Not a wrong claim — a naming hazard worth a note when the combat model is built.

### 2.9a Combat/Supply: strategic morale is **driven by supply**, every turn — a mechanic neither document has **[confirmed, new]**

Separating the two morales (§2.9) exposed what the strategic one is actually *for*, and it is not combat bookkeeping. `FUN_004514ec` (`0x004514EC`), the turn tick, rewrites `ArmyRecord +14` on **every army, every turn**, from that army's supply percentage — computed after that turn's consumption:

- `pct < 10` → **morale `−2`**, hard-floored at **51**, and the army loses **one move**.
- `10 ≤ pct ≤ 15` → **no change** (a deliberate dead band).
- `pct > 15` → **morale `+1`**, capped at **70**.

`51 … 70` are the field's real bounds, and they are exactly the five 4-wide tiers the army panel prints via `moraleNames[(v − 51) >> 2]`; a fresh army starts at 59, mid-range. Note the **2:1 asymmetry** — recovery is half as fast as decay, so starvation costs roughly twice what it takes to undo.

Per-turn supply consumption is `((90 − seasonVal) × troops) / 20000`, with the season table read straight out of the DAT at `0x1F7D8` (Spring 50 · Summer 80 · Autumn 80 · Winter 20) — so winter costs **7×** summer. An army aboard a fleet uses a flat `troops / 200`, no seasonal term.

The naval side is the same idea on different terms, and must **not** share an implementation: a fleet at sea with **exactly 0** supplies loses `random(0..1)` **condition** per turn with no floor and no free regeneration, and is destroyed outright below 40 (*"is lost at sea"*). Condition occupies the structural role morale does — `ships × condition / 10` for naval strength against `(troops / 80) × morale` for land.

This matters for the design: supply stops being an inert logistics counter and becomes the main peacetime pressure on army strength, since `+14` multiplies both army-strength formulas. Confirmed empirically turn-for-turn, 12 of 12 transitions with every confound excluded, in [`investigations/thracia-supply-morale.md`](investigations/thracia-supply-morale.md). `game-design.md`'s one-line "seasonal consumption" mention should be replaced by this; it is also a strong candidate for a new report in the research repo.

### 2.10 Combat: quality promotion tagged `[confirmed, one battle's evidence]` — **should be `[derived]` [flagged]**

The cited report is explicit that the rule is empirical, from 13 units in one battle, with the exact implementing code never located, and that it cannot tell whether the rule generalises past the "average" tier. That is a textbook `[derived]`. Separately, a **second, code-level** promotion rule now exists on the instant-resolve path (promote to ≥ "average", then 1-in-4 further), which is *not* an adjacency rule — so "the" promotion rule is at least two rules on two code paths.

### 2.11 Combat: "the original's own placement-driven pairing doesn't translate to an instant-resolve model" — **premise now false [flagged — see open question Q1]**

The original *has* an instant-resolve model, with completely different and much simpler math. The design's proposed `"pairing": "largest-vs-largest"` invention is answering a question the original already answered differently. This is not a small correction; it is a design decision for the user.

### 2.12 World format: "not fixed to the 5 confirmed original terrain values **[confirmed: rivers-and-map-markers.md]**" — **understated [flagged]**

The engine's terrain table has **12 codes** and 6 distinct names. "5 values" came from how many were matched to screenshots, not from how many exist. The design's conclusion (tile types should be an open list) is unaffected and still right.

### 2.13 Claims that hold up under checking

Re-read against their cited reports and found to be as strong as stated: the calendar model (week `+2 mod 12`, season at the 11→1 wrap, year at Winter→Spring, quarterly billing on the season boundary); tax `income = base × rate / 100` with `base = 2,440` solved twice for Rome; ship upkeep `× 3`; recruitment `cost = (troops / 200) × price[type]`; the 100,000-troop army cap; the melee 40% cap `floor(0.4 × defenderTroops) + 1`; the 30,000 cap; the siege strength shape with archers tripled; the loyalty floors (40 forced capture / 65 defection / toward 90 when the allegiant nation recaptures); the cascading defection conditions; the nation-elimination cascade; the SAV layout; the news log as a 40-slot ring buffer; the `[open]` tags on the rebellion check `FUN_0044C204` and the weather-event effect `FUN_004511BC`.

One item to re-check rather than trust: [`decompiled-city-capture-resolution.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-city-capture-resolution.md) describes a **−20% defender penalty when owner ≠ allegiance**, while the siege entry point `FUN_0044B27C` applies a **×9/10 (−10%) defender reduction when the *attacking nation* equals the city's allegiance**. These may be two separate adjustments in two functions, or one of the two readings may be off. `FUN_0044A98C` (defender strength) was not decompiled this pass. **[open]**

Two record-field labels the project is carrying that the code contradicts, both corrected in their reports and **still wrong in `IC2.Data`** (not changed here — this is an audit, not a code task): `ArmyRecord +8` is the covered map cell, not morale (`+14` is morale); `FleetRecord +20` is the fleet's condition percentage once launched, and only a build-city index while under construction.

---

## 3. Open questions for the user

These are the judgment calls this audit ran into that are genuinely product decisions, not engineering ones. None of them was decided unilaterally. They are roughly in order of how much downstream work they gate.

**Q1 and Q2 have since been answered by the user** and folded into `game-design.md` (see the notes under each). **Q3, Q5, Q6 and Q8 are now also answered**, all the same way, folded into `game-design.md`'s new "Two shipped presets" table. Q4 is folded into that same table rather than left standalone. Q7 is answered independently. Q9 is the only one still genuinely open.

### Q1. Which battle model should auto-resolve actually use? — **ANSWERED: option A, the original's instant resolver**

> **User's decision: "use the original instant battle resolver, if possible."** It is possible — all three variants (field, siege, naval) are fully decompiled. Folded into `game-design.md`'s Combat section and milestone 8.
>
> **One consequence to be aware of**: the Rome/Gaul fixture (99,882 → 63,282, per-type) came from the *tactical* path and **cannot** be reproduced by this resolver, which annihilates the loser instead of producing a per-type attrition table. It has been removed as a milestone acceptance test and kept as evidence for a possible later optional "detailed" resolver, which the `Ruleset` formula-variant mechanism already supports adding without an engine change. The type-effectiveness matrix and the melee 40% cap move into that same reserve.

`game-design.md` chose instant auto-resolve, assuming the original had nothing of the kind and therefore inventing a pairing rule over the tactical exchange math. The original in fact has **two** resolvers, and the design is currently proposing a third.

| Option | What it means | Trade-off |
| --- | --- | --- |
| **A. Port the original's instant resolver** (`FUN_0044AEE4` / `FUN_0044B27C` / `FUN_0044B5D0`) | Single power comparison; loser's army annihilated; winner takes `loserPower × 40 / winnerPower` casualties; ties to the defender | Fully confirmed math, trivial to implement and test, no invented rules. But brutal (no partial defeats, no retreat) and it **cannot reproduce the Rome/Gaul golden fixture**, which came from the tactical path |
| **B. The design's current plan** | Run the tactical melee/shooting exchange loop headlessly with an invented pairing rule | Preserves the rich per-unit-type result the design's battle screen is built around, and can reproduce the Rome/Gaul fixture. But the pairing rule is `[designed]` and directly determines outcomes |
| **C. Both, as named ruleset variants** | `"resolution": "quick"` (A) for AI-vs-AI, `"detailed"` (B) when a human seat is involved | This is **exactly what the original does** — it is the most faithful option. Costs two engines and two test suites |
| **D. Restore a real tactical battle** | Keep the grid, placement and per-action play | The freeze that motivated dropping it was diagnosed as a hardcoded ~3.02 s pacing delay ([`battle-freeze-diagnosed-procmon.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-freeze-diagnosed-procmon.md)) that a reimplementation simply would not have. This reopens a scope decision you already closed — flagged only because the justification for closing it has weakened |

**Follow-up, new: what happens to the loser under option A, now that it's the only resolver in play.** Option A's `[confirmed]` math is annihilation, full stop — no partial defeats, no retreat, which reads as unusually harsh once it runs on *every* battle rather than the subset the original reserved it for. The user's follow-up decision: keep annihilation in `classical-faithful` (still fully confirmed, verbatim), and add a `[designed, no original analogue]` alternative in `improved` — the losing army/fleet survives at reduced strength and **scatters** 2–4 tiles away from the victor instead of being destroyed, with its moves zeroed for the rest of that turn so the victor (whose own move was already spent on the attack) cannot immediately give chase. Full spec in `game-design.md`'s Combat section, `combat.onDefeat` flag. Options D's tactical grid and option B/C's pairing rule stay declined for the reasons above; this only adds a third *outcome* on top of option A's math, not a fourth resolver. It also gives the held-in-reserve "detailed resolver" (type-effectiveness matrix, melee cap, tactical morale) a concrete future use beyond "possible": it is the natural foundation for the optional battle screen now noted in `game-design.md`'s "User interface" section, since it is the one candidate that can produce a per-unit-type exchange log to show.

### Q2. Is the naval subsystem in scope for a first playable version? — **ANSWERED: yes, full naval**

> **User's decision: "of course we need naval."** `game-design.md` now has a Naval subsystem section and milestone 7 covers construction, condition/repair, transport, sea movement, and join/split/scuttle; naval combat is one of the three variants in milestone 8.

Fleets, army transport (1 army, 500 troops/ship), condition and paid repair, scuttling, 24-tick construction, naval battles, and storm losses are all confirmed and all absent from the design and from the milestone list. On the classical Mediterranean map, amphibious movement is not a side feature — without it, large parts of the map are unreachable. Options: full naval in v1; movement-and-transport only (defer combat/repair/condition); or defer naval entirely and ship a land-only first release.

### Q3. How faithful should diplomacy be, now that the original's model is recoverable? — **ANSWERED: ship both, as a ruleset flag**

> **User's decision: ship both, as a prominent, user-selectable ruleset choice, not a one-off pick.** `classical-faithful` uses the confirmed model exactly as coded (option a). `improved` uses the confirmed model with the opinion score layered on top as the AI's decision input (option c) — the strongest of the three options on offer, since it keeps every confirmed rule intact and only adds the one genuinely-missing piece (the AI's *willingness*), rather than replacing recovered mechanics with an invented model (option b, now dropped). See `game-design.md`'s `diplomacy.model` flag.

The confirmed model is: 4 states, symmetric matrix, max 3 trade partners, negative cooldowns of −8 (broken trade) / −24 (broken alliance) / −18 (ended war) that thaw quarterly, alliances and wars contagious to allies, attacking = declaring war, AI nations refuse peace while at war but human seats always accept, and a concrete reparation formula.

### Q4. Keep per-army and per-fleet money purses? — **ANSWERED: ship both, as the same ruleset flag family**

> **User's decision: ship both.** `classical-faithful` keeps the per-army/per-fleet purses (cap 1,000) exactly as coded. `improved` centralises supply and mercenary purchases to the national treasury, trading logistical depth for less micromanagement. See `game-design.md`'s `economy.purses` flag.

The original gives every army and fleet its own **supply stock** and its own **money purse (cap 1,000)**. Buying supply and hiring mercenaries spend *that* purse, not the national treasury, and the purchase price is paid to whoever owns the selling city — real logistical depth and real micromanagement, since an army far from home can be unable to afford supply even when the treasury is full.

### Q5. Should "conquer every city" be the shipped default victory condition? — **ANSWERED: yes, as the `classical-faithful` default; `improved` defaults friendlier**

> **User's decision: ship both, `classical-faithful` keeping the original's only win condition as its default.** `improved` defaults to domination-over-hostiles or score-at-turn-limit with a shorter default turn limit; either way the player can still pick any shipped victory condition per scenario. See `game-design.md`'s `victory.default` flag.

That is the original's only win (334 of 334 cities), with a candidate hard end at 250 BC — roughly 20 in-game years. Faithful, but a very long and very demanding goal.

### Q6. Reproduce the original's human-versus-AI asymmetries? — **ANSWERED: `classical-faithful` reproduces them, `improved` normalises them**

> **User's decision: ship both.** `classical-faithful` keeps every confirmed seat-type asymmetry (movement-zeroing, split-army starting moves, embarkation trimming) exactly as coded. `improved` applies the same rule to every seat regardless of human/AI control — a real fairness difference in hotseat, where "human seat" is no longer synonymous with "the player". See `game-design.md`'s `seatAsymmetry` flag. With the tactical shell dropped, the "only AI-vs-AI battles resolve instantly" asymmetry is currently moot either way — the instant resolver is the only resolver until/unless a future optional battle screen (see `game-design.md`, "User interface") reintroduces an alternative.

Several confirmed rules differ by seat type, not by nation: only AI armies lose their whole turn's movement on a blocked step; a newly split AI army starts with 1 move, a human's with 0; an over-capacity army is trimmed on embarkation only for AI nations.

### Q7. Is city development a direction to expand, or stay at exactly one order? — **ANSWERED: leave the door open**

> **User's decision, via the plan's Q-D default**: ship a generic, data-driven `cityOrders` table with fortify as the only shipped entry, rather than hardcoding "fortify" as the only possible city order. This is a schema/moddability choice, not a faithful-vs-improved fork, so it applies to both rulesets identically.

Fortification is the original's only city improvement: a paid, queued, population-priced order, capped at 100%, wiped by a siege. The moddability goal makes "add more improvement types as ruleset data" cheap to support at no cost now.

### Q8. What is the policy on reproducing original bugs? — **ANSWERED: `classical-faithful` reproduces them, `improved` fixes them silently**

> **User's decision: ship both**, rather than picking one policy globally. `classical-faithful` reproduces the identified bugs verbatim — including the quarterly diplomatic-thaw loop only iterating the first 8 of each nation's 16 relation columns. `improved` fixes them silently (all 16 columns thaw). See `game-design.md`'s `bugPolicy.diplomaticThaw` flag; any further bug found later joins the same table rather than opening a new question.

Two are now identified. The quarterly diplomatic-thaw loop iterates only the **first 8 columns** of each nation's 16-entry relation row, so a cooldown between two nations both indexed ≥ 8 never decays. The fleet record's `+20` word does double duty as build-city-index and condition-percentage, which is fragile rather than wrong (an engineering hazard to document, not a ruleset choice).

### Q9. One evidence gap worth a five-minute play session

The code says buying supply costs `amount / 5` from the army's money purse, but the three frames tabulated in [`galatia-elimination-and-city-resupply-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/galatia-elimination-and-city-resupply-confirmed.md) show Army 0's money unchanged at 256 across a 100-ton purchase. One controlled same-turn save pair around a single supply purchase would settle it. Worth capturing before the economy milestone is implemented, since it is the difference between "supply is a cost" and "supply is free".

---

## 4. Review of the build harness and the 15-milestone backlog

> **Status: the milestone list in `game-design.md` has since been revised against this whole section** — it is now 20 milestones, with naval and battle resolution reflecting the answers to Q1 and Q2, the three dependency inversions fixed, a strength-functions milestone extracted because three later milestones consume it, the fixtures file pulled into milestone 1, and a checkable *done when* on every entry. The findings below are kept as the record of what was wrong with the original 15.

### 4.1 Missing milestones

Given §1, the backlog has no milestone for:

- **The naval subsystem** — transport, condition, repair, scuttle, construction countdown, naval battle. Milestone 5 is titled "Movement, supply, and fleets" but its *done when* mentions only river movement and the fleet owner field; nothing there would force any of the above to exist.
- **Supply as an economy** — capacities, the 1-talent-per-5-tons purchase, per-army/fleet purses. Milestone 3 ("Economy") lists tax, upkeep, tribute and loyalty drift only.
- **City orders** — fortification and its queued-order encoding.
- **Army/unit management** — join/split armies, join/split/rename/disband units, the battalion-size merge cap, the quality-averaging rule, the 198-army and 20-unit caps.
- **The news log** — a confirmed 40-slot ring buffer and a load-bearing element of the designed UI (§UI item 2), but no milestone produces it.
- **Mercenaries beyond cost** — the pool, the hire-from-army-purse rule, the regular/mercenary upkeep split. Milestone 4's *done when* is cost-formula tests only.

### 4.2 Ordering problems

- **M6 (city capture/siege) depends on M7 (battle resolution).** Siege resolution *is* a battle: attacker strength versus defender strength, using army morale and the archers-tripled rule. M6's *done when* ("a scripted scenario reproduces the Galatia-elimination pattern") cannot be met without the combat layer that arrives a milestone later. Swap them, or split "strength functions" out of M7 into an earlier milestone that both consume.
- **M8 (diplomacy) depends on M7 and M9.** Its *done when* — "two AI nations can reach peace and a reparations payment occurs" — requires AI nations (M9) and the battle that triggers the treaty (M7), since the AI-to-AI reparation is fired from inside the instant battle resolver. Either move M8 after M9 or restate its *done when* in terms the diplomacy layer can satisfy alone (e.g. the relation-matrix state machine, the cooldown values, the trade cap, the ally-contagion rules, and the reparation arithmetic as a pure function).
- **M12 (original-save import) arrives after the milestones that need it.** M3–M7's golden-fixture tests are described as "load the equivalent starting state into the new engine" — the real states live in original `.sav` files. Either move the import bridge to right after M1, or state explicitly that fixtures are hand-authored JSON transcribed from the reports (which is viable for most of them and keeps the tests independent of the user owning game files — probably the better answer, and worth saying out loud).

### 4.3 "Done when" criteria too vague to self-verify

The stated purpose of these criteria is that an autonomous loop can check them without human judgment. Several cannot be:

| Milestone | Problem | Suggested replacement |
| --- | --- | --- |
| 3 Economy | "golden-fixture tests against the real confirmed formulas pass" names no fixture | Assert exactly: Rome `base = 2,440` reproducing both the 15% and 20% income figures; ship upkeep `= 3 × ships`; the 13-unit Roman roster's regular upkeep `= 442`; a 100-ton supply purchase `= 20` talents (pending Q9) |
| 7 Battle | "reproduces the real recorded numbers **within the formula's own randomness bounds**" — an unbounded, unfalsifiable criterion | With a **fixed seed**, assert the exact Rome/Gaul per-type before/after numbers (99,882 → 63,282); separately assert `floor(0.4 × def) + 1` on the 4 confirmed capped exchanges and the 5th below-cap case |
| 9 AI | "runs to completion … across many seeds" — "many" unspecified, and with the original's all-cities victory condition it may never terminate | "N = 50 fixed seeds, each reaching a victory condition **or a stated turn cap** with no exception and no illegal command" |
| 10 Victory conditions | **no *done when* at all** | One test per shipped condition, plus a test that a scenario-custom goal fires |
| 11 Godot UI | **no *done when*** | A scripted headless smoke run that loads a scenario, issues one order of each type through the command layer, and ends a turn |
| 13 New-format save/load | **no *done when*** | Round-trip equality of a mid-game state after N turns, plus a forward-compat test on an older version file |
| 14, 15 | **no *done when*** | 14: the example scenarios load and run 10 turns headlessly. 15: the packaged build launches and loads a scenario on a clean machine |

### 4.4 Golden fixtures are badly under-used

The design names four. The reports contain at least a dozen more with exact numbers, several of which need **no original game files at all** — the numbers are published in the reports, so the test is pure data:

| Fixture | Source | Exact assertion |
| --- | --- | --- |
| Roman army upkeep | [`army-records-and-roman-roster.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/army-records-and-roman-roster.md) (roster published in full) | 13 units → **442** talents/quarter |
| Roman army supply % | same | 482 t / 48,173 troops → **100%** |
| Supply percentage triple | [`galatia-elimination-and-city-resupply-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/galatia-elimination-and-city-resupply-confirmed.md) | 204/998 → **20%**, 344/998 → **34%**, 184/282 → **65%** |
| Tax base | [`rome-tax-increase-and-sidon-capture.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/rome-tax-increase-and-sidon-capture.md), [`decompiled-quarterly-billing-and-economy.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-quarterly-billing-and-economy.md) | `2,440 × 15/100` and `× 20/100` |
| Fleet order | [`fleet-order-at-caere.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/fleet-order-at-caere.md) | 10 ships → **100** talents, capacity **5,000**, upkeep **30** |
| Fleet marker encoding | new, this pass | 90 ships owner 1 → **333**; 70 ships owner 3 → **335** |
| Terrain costs | new, this pass | the 12-entry table verbatim |
| Mercenary hire | [`mercenary-pool-record.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/mercenary-pool-record.md) + new cost formula | Felsina 6,438 "very good" → cost, then `0xFFFF` sentinel |
| Supply transfer conservation | [`controlled-army-supply-transfer.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/controlled-army-supply-transfer.md) | 79 tons, exactly reciprocal, nothing else changes |
| Mobilization conservation | [`city-units-army-transfer-and-mercenaries.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/city-units-army-transfer-and-mercenaries.md) | Rome 85,000 → 70,000; Masada's six units → 49,800 |
| Reparation | [`diplomatic-reparations-and-more-captures.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/diplomatic-reparations-and-more-captures.md) | Ptolemaic 999 → −1270 (range assertion, given the random term) |
| Elimination cascade | [`galatia-elimination-and-city-resupply-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/galatia-elimination-and-city-resupply-confirmed.md) | 9 cities, 2 "falls to" with population/fortification loss, 7 "defects from" without |

Recommendation: make "transcribe every exact number in `docs/reports/` into a fixtures file" an explicit early milestone task (part of M1), rather than leaving each milestone to find its own. It is a few hours of work that makes every later milestone's *done when* mechanically checkable.

### 4.5 One structural note on the harness

The operating mode says to surface to the user only on milestone completion, a genuine design gap, or a product/taste call — and that "every `[designed]` placeholder above is fair game to implement autonomously, precisely because it's already documented as a deliberate, revisitable choice rather than an unstated assumption". This audit found that four `[designed]` sections (Diplomacy, Victory, the battle pairing rule, the movement cost table) were **not** deliberate choices — they were assumptions made without checking whether evidence existed. Before the loop starts, it is worth adding one rule to the harness: **a `[designed]` tag is only valid if the document says what was searched and came up empty.** That single sentence would have caught all four.
