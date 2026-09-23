# Does supply affect army morale? Yes — the rule is in the turn tick

**Status: solved.** Army morale at `ArmyRecord +14` is driven directly by the army's supply
percentage, once per turn, inside the weekly/turn tick `FUN_004514ec` (`0x004514EC`). The rule was
never traced before because [`decompiled-turn-and-calendar-sequencing.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-turn-and-calendar-sequencing.md)
covered that function's supply-consumption and readiness/moves effects but did not follow the two
writes to `+0xe` a few lines further down.

Same tagging convention as `design-audit.md` / `game-design.md`: **[confirmed]** (direct RE evidence,
cited), **[derived]** (extrapolation), **[designed]** (new design, no RE evidence), **[open]** (not
established either way).

## Why this was re-opened

A prior pass concluded there was *no* supply→morale link, on two grounds: the **tactical** per-unit
morale formula (`clamp(random(quality×4) + armyMorale[armyIdx], …)`, adjusted `±2`/`−3` per melee
exchange, array `DAT_004A0350`) has no supply term, and one save snapshot showed two armies both at
morale 68 with 0% and 65% supply.

Both observations are correct and neither is evidence against a link. The tactical array is a
*different field* that only exists mid-battle (`design-audit.md` §2.9 already flags the two-morales
naming hazard), and a single snapshot of two armies cannot separate "supply has no effect" from "two
armies happened to be at the same point on different trajectories" — morale here is a slow-moving
accumulator with a hard ceiling that most armies sit at.

The user's recollection — a Thracian army that ran out of supplies and then visibly lost morale over
the following turns — is exactly right, and is reproduced below in 13 consecutive saves.

## Sources and method

- **Saves:** `C:\Users\diego\Documents\imp_conq_original\saves\1_thracia_271_{spring,summer,autumn}_N.sav`,
  13 files, one per turn — the army evidence. Plus `1_cartago_271_*.sav`, 10 files, a second run
  branching from the same `spring_1` state in which a fleet was deliberately starved at sea until it
  sank — the fleet evidence.
- **Decompiled code:** `%LOCALAPPDATA%\ReTools\all_app_functions.txt` (the whole-application dump
  described in `decompilation-plan.md`), line numbers given for reproducibility.
- **DAT constants:** `C:\Users\diego\Documents\imp_conq_original\Imperial Conquest 2.dat`, byte
  offsets given.
- Fields were read straight out of the save bytes at the offsets `IC2.Data`'s `SaveArmyTable` /
  `SaveFleetTable` already document, cross-checked against `IC2.Inspect --list-armies`. (Two of the
  13 saves — `spring_3` and `autumn_1` — currently **abort** `IC2.Inspect`; see
  [Parser bug](#a-real-parser-bug-found-on-the-way) below.)

**Recordings and screenshots checked and ruled out.** The six `recordings\bandicam *.mp4` files are
all stamped `2026-09-12 23:56` – `2026-09-13 01:03`, and `screenshots\` holds only two Carthaginian
frames. The Thracian saves are stamped `2026-09-13 19:30` – `19:35`. No footage covers this session;
the finding below rests on save bytes and decompiled code only.

## One save = one turn = two weeks [confirmed]

This matters, because the per-turn rate is what the numbers below measure. Three independent
confirmations, all from the tail of `FUN_004514ec` and the save data:

1. The calendar update at the end of the tick is `week = (week + 2) % 12`, with the season advancing
   on the wrap to 1 and the BC year decrementing on the wrap to season 0
   (`all_app_functions.txt:54663-54677`) — so a season is 12 weeks = **6 turns**. The save series is
   exactly 6 spring saves, 6 summer saves, then `autumn_1`.
2. The fleet construction countdown is decremented by 2 per call (`+10 -= 2`). Macedonia's fleet
   ordered in `summer_1` reads `24, 22, 20, 18, 16, 14, 12` across the seven saves from `summer_1` to
   `autumn_1` — **exactly one tick per save**, no more, no less.
3. The army supply drain is a fixed per-turn amount (below) and steps exactly once per save.

## The mechanic [confirmed]

`FUN_004514ec` (`0x004514EC`), army loop, `all_app_functions.txt:54501-54529`. Array base
`DAT_0047C1EC`, stride `0xA4` dwords = **656 bytes** = `SaveArmyTable.RecordLength`; `puVar13 + 0xe`
is therefore `ArmyRecord +14`, morale. Helpers confirmed by reading them:
`FUN_00448FD0(a,b) = min(a,b)`, `FUN_00448FD8(a,b) = max(a,b)`, `FUN_0044A698(i) =` total troops of
army `i` (sum of the 20 unit slots' `+4` words, floored at 1).

```c
troops   = FUN_0044a698(i);                                  // 0x0044A698
moves    = 10 - min(5, troops / 20000);                       // army[+6]

if (army[+8] == -1)                                           // aboard a fleet
    army[+10] = max(0, army[+10] - troops / 200);              // supplies
else
    army[+10] = max(0, army[+10] - ((90 - seasonVal) * troops) / 20000);

pct = army[+10] * 10000 / troops;                             // the panel's supply %

if (pct < 10) {                                               // ---- MORALE DECAY ----
    army[+14] = max(51, army[+14] - 2);
    army[+6] -= 1;                                            // and one fewer move
}
if (pct > 15 && army[+14] < 70)                               // ---- MORALE REGEN ----
    army[+14] += 1;
```

In words:

| Condition (supply %, **after** this turn's consumption) | Effect on `ArmyRecord +14` per turn |
| --- | --- |
| `pct < 10` | **−2**, clamped at a floor of **51** (`0x33`); also **−1 move** |
| `10 ≤ pct ≤ 15` | **no change** — a dead band |
| `pct > 15` | **+1**, clamped at a ceiling of **70** (`0x46`) |

Three details worth keeping:

- **The percentage is computed after consumption**, so the value that drives the change is the value
  the save then shows. That is what makes the table below predictable turn-for-turn.
- **51 and 70 are the real bounds of the field.** They are not arbitrary: the army panel prints
  morale as a tier via `moraleNames[(v - 51) >> 2]` with a `v - 48` fallback below 51
  (`TInformation_ShowArmyDetails`, `all_app_functions.txt:41052-41058`, string table
  `DAT_00479428`, 11-byte entries). `51 … 70` is exactly five 4-wide tiers. A fresh army from
  `TUnitMap_SplitArmy` starts at **59** (`all_app_functions.txt:48739`), mid-range.
- **The decay is asymmetric with the regen — 2:1.** An army recovers morale half as fast as it loses
  it. Climbing from the 51 floor back to the 70 ceiling takes 19 turns of good supply; falling from
  70 to 51 takes 10 turns of starvation.

### Seasonal supply consumption [confirmed]

The season record is a 10-byte struct — 8-byte name then a `word` — at **DAT offset `0x1F7D8`**,
matching the code's `&DAT_004794a0 + season*10` (name) and `&DAT_004794a8 + season*10` (value):

```
0x1F7D8:  "Spring\0\0" 0x0032(50)  "Summer\0\0" 0x0050(80)
          "Autumn\0\0" 0x0050(80)  "Winter\0\0" 0x0014(20)
```

So per-turn consumption `= ((90 − seasonVal) × troops) / 20000` becomes:

| Season | `seasonVal` | Consumption per turn | For the 22,000-troop Thracian army |
| --- | --- | --- | --- |
| Spring | 50 | `troops / 500` | 44 t |
| Summer | 80 | `troops / 2000` | 11 t |
| Autumn | 80 | `troops / 2000` | 11 t |
| Winter | 20 | `troops × 7 / 2000` | 77 t |

The Thracian army's three consecutive spring steps are `142 → 98 → 54 → 10`, i.e. **−44, −44, −44** —
an exact match to the table value solved independently from the DAT. Winter costs 7× summer, which
is the same seasonal shape the city food term in the same function shows (`seasonVal − 40`: Spring
`+10`, Summer/Autumn `+40`, Winter `−20`, i.e. winter is a net food *loss*).

An army **aboard a fleet** uses the flat `troops / 200` instead, with no seasonal term — 5× the
summer rate, year-round.

## The data: Thracia, army index 6, 271 BC

Nation code **15** (Thracia), army index 6 in every save, `(160, 30)` in every save. Capacity is
`troops / 100` = 220 t; `pct = supplies × 10000 / troops`.

| Save | Supplies (t) | Supply % | Morale `+14` | Δ morale | Rule that fired | Predicted |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `spring_1`  | 142 | 64 % | 65 | — | (initial state) | — |
| `spring_3`  |  98 | 44 % | 66 | +1 | `pct > 15` → +1 | 66 ✓ |
| `spring_5`  |  54 | 24 % | 67 | +1 | `pct > 15` → +1 | 67 ✓ |
| `spring_7`  |  10 |  4 % | 65 | −2 | `pct < 10` → −2 | 65 ✓ |
| `spring_9`  |   0 |  0 % | 63 | −2 | `pct < 10` → −2 | 63 ✓ |
| `spring_11` |   0 |  0 % | 61 | −2 | `pct < 10` → −2 | 61 ✓ |
| `summer_1`  |   0 |  0 % | 59 | −2 | `pct < 10` → −2 | 59 ✓ |
| `summer_3`  |   0 |  0 % | 57 | −2 | `pct < 10` → −2 | 57 ✓ |
| `summer_5`  |   0 |  0 % | 55 | −2 | `pct < 10` → −2 | 55 ✓ |
| `summer_7`  |   0 |  0 % | 53 | −2 | `pct < 10` → −2 | 53 ✓ |
| `summer_9`  |   0 |  0 % | 51 | −2 | `pct < 10` → −2 | 51 ✓ |
| `summer_11` |   0 |  0 % | 51 |  0 | `max(51, 49)` → **floor** | 51 ✓ |
| `autumn_1`  |   0 |  0 % | 51 |  0 | `max(51, 49)` → **floor** | 51 ✓ |

**12 of 12 transitions predicted exactly, including the floor.** The user's recollection is confirmed
in full, and the `spring_5 → spring_7` step is the one that shows the rule is a *percentage*
threshold and not "supply reached zero": supply was still 10 t there, but 10 t of a 220 t capacity is
4 %, already under the 10 % line, so morale had started falling one turn **before** the tanks ran dry.

The `moves` column corroborates independently: `10 − min(5, 22000/20000) = 9`, and the saves read
**9** in `spring_3`/`spring_5` (supplied) and **8** from `spring_7` onward (the `pct < 10`
`moves -= 1`). `spring_1` reads 8 but so does every other army in that save while later saves read
9/10 — `spring_1` is the session's starting state, not a post-tick state.

### Confounds ruled out

The concern was that a battle could have moved morale by some other path (the tactical `±2`/`−3`
melee rule, or the instant resolver's unity-adjacent effects). It did not — this army did nothing at
all for 13 turns:

- **Position identical** in all 13 saves: `(160, 30)`. No movement, so no attack and no siege.
- **The entire 640-byte unit-slot block is byte-identical** in all 13 saves —
  SHA-256 prefix `d55d02c58cf6` for every one. Seven units, `22,000` troops, unchanged names,
  unchanged types, unchanged qualities. A battle cannot leave troop counts untouched (the melee
  formula always applies losses to both sides), and it cannot leave qualities untouched either
  (post-battle promotion). No combat, no attrition, no reinforcement, no promotion, no merge.
- **The only other field that moved** is money: `100 → 46` at `summer_1` and `46 → 0` at `autumn_1` —
  both season boundaries, i.e. the already-documented quarterly upkeep in `FUN_00451B40`, and the
  reason this army could never buy its way out (supply costs 1 talent per 5 tons).
- **`FUN_00451304`**, the only other function the tick calls between the army loop and the calendar
  update, is the seasonal weather system (map-marker cleanup plus season/week-keyed event
  frequencies). It writes no army field.

### And it is the only supply-driven morale path in the binary

Grepping the whole-application dump for every access to the morale word as an indexed global
(`DAT_0047C1FA`, = `0x0047C1EC + 0xE`) returns nine sites, and none of them is a second decay rule:

| Site (`all_app_functions.txt`) | What it is |
| --- | --- |
| `38084`, `38092` | `+= 3` on battle entry, in `FUN_00437DE4` (`TBattleMap_StartBattle`'s copy-in), **only for a side whose own nation is computer-controlled** (nation `+0x490 == 0`), unclamped, once per battle; the human side gets nothing. *(Corrected 2026-09-23: this row first read "to each side", after the source report's own first reading. See [`battle-quality-promotion-and-morale-array-decompiled.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-quality-promotion-and-morale-array-decompiled.md) §"The morale formula", research `1762c84`.)* |
| `38135`, `38162` | seeds the per-unit tactical array `DAT_004A0350` — the known tactical formula |
| `41054`, `41056` | the panel's tier display (`−0x33`, fallback `−0x30`) |
| `48739` | `= 0x3B` (59), new-army initialisation |
| `49216`, `49245` | `(troops / 0x50) × morale`, the two army-strength formulas |

Plus the two writes in `FUN_004514ec` itself (which use a walking pointer, not the indexed form, and
so do not appear in that grep). That is the complete set.

## Fleets: condition *is* the analog, and it degrades the same way [confirmed]

Three questions were asked. All three have code answers, and the `1_cartago_271_*` series supplies
the empirical test as well (§4 below).

### 1. Is there an undiscovered fleet morale field? No.

`FleetRecord` is 26 bytes and every one is now accounted for — `+0/+2` x,y · `+4` · `+6` · `+8` owner
· `+10` construction countdown / `0xFFFF`-once-launched · `+12` moves · `+14` supplies · `+16` money
· `+18` ships · `+20` build city, then condition % once launched · `+22` carried army · `+24` covered
cell. The only two words `IC2.Data` still marks `?` are `+4` and `+6`, and both are ruled out
directly from the save data:

- **`+4` is written to `0xFFFF` unconditionally at the top of every fleet's turn**
  (`all_app_functions.txt:54540`). All 13 saves agree: every *launched* fleet reads `−1`, and the two
  freshly-ordered fleets (`summer_1[2]`, `autumn_1[3]`) read `0` until their first tick flips them to
  `−1`. A field reset every turn cannot accumulate morale.
- **`+6` reads `0`, then `77`, then `65`** for the Carthaginian fleet as it moves, and stays `0` for
  the parked Ptolemaic fleet — it tracks position, not a unit stat.

### 2. Does condition degrade with low fleet supply? Yes, but on different terms.

Same function, fleet loop, `all_app_functions.txt:54537-54632`. Array base `DAT_0049C26C`, stride
`0x1A` = 26 bytes. The order matters and is easy to get wrong — the **storm pass runs first, on every
at-sea fleet, regardless of supply**; the supply penalty is a smaller rider applied afterwards:

```c
fleet[+14] = max(0, fleet[+14] - fleet[+18]);        // supplies -= ships, EVERY turn, every fleet

if (fleet[+10] == -1) {                              // launched and at sea
    // ---- 1. STORM PASS (54553-54592) — unconditional, NOT supply-driven ----
    dmg = max(1, FUN_0040284c(100 - fleet[+20]) / 10);    // scales with damage already taken
    if (season == winter) dmg = min(5, dmg * 2);
    if (fleet[+24] == 1)  dmg = min(8, dmg * 3);
    if (FUN_004494e4(fleet[+8], &fleet[+0]) < 0) {        // "away from friendly coast" test
        dmg = dmg * 2 + 1;                                // always ODD on this branch
        if (season == winter && FUN_0040284c(20) == 0) dmg = 30;
    } else dmg /= 2;
    if (dmg < 6) fleet[+20] -= dmg;
    else         FUN_0044b4f8(i, 100, dmg + 100);         // heavier: costs SHIPS as well

    // ---- 2. DEATH CHECK (54593) ----
    if (fleet[+20] < 40) { news("A fleet belonging to X is lost at sea."); destroy(i); }
    else if (dmg > 5)    { news("A fleet belonging to X is damaged in a storm."); }

    // ---- 3. MOVES (54607-54616) ----
    fleet[+12] = 30 - (fleet[+18] - 50) / 10;
    if (fleet[+22] >= 0)                                  // carrying an army
        fleet[+12] -= troops(fleet[+22]) / 100 / fleet[+18] + 1;

    // ---- 4. OUT OF SUPPLY (54618-54623) ----
    if (fleet[+14] == 0) {
        fleet[+12] -= 3;                                  // three fewer moves
        fleet[+20] -= FUN_0040284c(2);                    // condition -= random(0..1)
    }
    // ---- 5. DAMAGE SLOWS YOU DOWN (54624-54631) ----
    if (fleet[+20] < 70) fleet[+12] -= (70 - fleet[+20]) >> 2;
}
```

> **The heavier branch's arithmetic, read at instruction level (added 2026-09-23, research `3f6ca09`).**
> The call is `FUN_0044b4f8(i, 100, dmg + 100)` (`0x00451823`–`0x00451832`: `DX = 100`,
> `CX = dmg + 100`). Inside that function the second argument is the numerator and the third is the
> divisor, so `r = max(1, 10000 / (dmg + 100))` and `d = r² / 100`. `d` **falls** as `dmg` rises: 86
> at `dmg` 7, 72 at 17, and 57 at the winter spike's 30. The fleet loses `ships × d / 300` ships and
> `condition × d / 300` condition. **An army aboard is hit too, which the pseudocode above does not
> show.** It takes `FUN_0044AE20(army, d)`, and when `d > 70`, which is every heavy storm except the
> spike, it also loses `unitCount × d / 250 + 1` whole units. All of this happens before the death
> check. **[confirmed:
> [`supply-driven-morale-and-fleet-attrition.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/supply-driven-morale-and-fleet-attrition.md),
> the 2026-09-23 addition, and
> [`decompiled-diplomacy-peace-terms-and-instant-battles.md` §"`FUN_0044B5D0` and `FUN_0044B4F8`, instruction by instruction"](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md#fun_0044b5d0-and-fun_0044b4f8-instruction-by-instruction-2026-09-23);
> the values of `d` derived]**. The merged `FleetAttritionRule` inverts the ratio and skips the army
> ([#292](https://github.com/diegoami/imperial_conquest_2/issues/292)).

Two consequences of that ordering are worth keeping. The **death check precedes the supply penalty**,
so `−random(0..1)` can leave a fleet below 40 without killing it until the *next* turn's check. And
the storm term `random(100 − condition) / 10` **escalates as the fleet degrades**, which makes naval
attrition a death spiral rather than a linear decline — the dominant effect by far, with the supply
rider adding at most 1 per turn on top.

So the **answer to the user's question is yes, condition is the fleet's analog to army morale** — and
this is more than an analogy. `FUN_0044AA54` computes naval strength as `ships × condition / 10`,
structurally identical to the army-strength formulas' `(troops / 80) × morale`: in both cases the
size term is scaled by the softer stat. The `0x46` (70) constant even recurs as the threshold below
which condition starts costing moves, mirroring morale's ceiling.

But the two mechanics are **not** the same rule, and a faithful implementation must not share code:

| | Army morale `+14` | Fleet condition `+20` |
| --- | --- | --- |
| Trigger | supply **percentage** `< 10 %` | supply **exactly 0** (absolute, not a %) |
| Decay | **−2**, deterministic | **−`random(0..1)`**, i.e. averages `−0.5` |
| Floor | **51**, hard-clamped | **none in this path** — falls until the fleet dies |
| Regeneration in the tick | **+1/turn** when `pct > 15`, up to 70 | **none** — only paid repair restores it |
| Applies when | always | **only at sea** (`+10 == -1`); a fleet in port is exempt |
| Move penalty | `−1` | `−3`, plus `−(70 − condition)/4` for damage |
| Death | none — 51 is survivable indefinitely | condition `< 40` → *"is lost at sea"*, fleet destroyed |

The fleet mechanic is the harsher of the two in outcome (starvation is eventually lethal) but far
slower per turn, and it costs money to undo rather than healing free.

### 3. The "repair" value in the UI — already covered, not a new field [confirmed]

Checked on the coordinator's lead. `TRepairFleet` (`0x00440AC4` …,
`all_app_functions.txt:43341-43480`) is a dialog over `+20` and nothing else. The "repair" number the
player adjusts lives at **form offset `0x206`** — a dialog-local scratch counter, never written to any
record:

- `TRepairFleet_InitializeForm` shows current `fleet[+20]` as `"N %"` and sets `0x206 = 0`.
- `TRepairFleet_ChangeRepair` steps it by `±1` / `±10`, clamped to `[0, 100 − fleet[+20]]`.
- `TRepairFleet_PrintNumbers` shows `fleet[+20] + repair` as the resulting `"N %"` and
  `ships × repair / 5` as the price.
- `TRepairFleet_OK` commits: `fleet[+20] += repair`, `fleet[+12] = 0` (moves zeroed),
  `treasury -= ships × repair / 5`.

So **option (1)**: "repair" is condition %'s repair dialog, exactly as `design-audit.md` §1.2 already
records it (`ships × points / 5` talents, zeroes moves). There is no repair-in-progress counter and
no separate persisted field. A separate AI-side auto-repair exists at
`all_app_functions.txt:53140-53146` (if `condition < 95`, pay `(100 − condition) × ships / 5` and set
condition to 100), which is the same price formula.

### 4. The empirical fleet test — a Carthaginian fleet starved at sea until it sank [confirmed]

No fleet in the *Thracian* series ever reaches 0 supply, so that series cannot test this. A second
series does: `1_cartago_271_*.sav` (10 saves, `spring_1` … `summer_9`), the user's own run in which a
fleet's supply was deliberately allowed to run out at sea. Both series branch from the same
`spring_1` state, so the fleet starts identical in each — 90 ships, 80 t, condition 85 — and here it
is simply never resupplied.

Carthaginian fleet, index 0, owner 1, **90 ships in every save** (so `FUN_0044B4F8` never fired — the
storm damage stayed under 6 throughout). Base moves `= 30 − (90 − 50)/10 = 26`.

| Save | Supply | Cond | Δ cond | Moves | Moves predicted |
| --- | ---: | ---: | ---: | ---: | --- |
| `spring_1`  |  80 | 85 | — | 25 | (session start, pre-tick) |
| `spring_1b` |  80 | 85 | — | 23 | (mid-turn reload, 2 moves spent) |
| `spring_5`  | **0** | 79 | −6 / 2 turns | 23 | `26 − 3` ✓ |
| `spring_7`  | **0** | 76 | −3 | 23 | `26 − 3` ✓ |
| `spring_11` | **0** | 66 | −10 / 2 turns | 22 | `26 − 3 − (70−66)>>2` ✓ |
| `summer_1`  | **0** | 62 | **−4** | 21 | `26 − 3 − (70−62)>>2` ✓ |
| `summer_3`  | **0** | 56 | **−6** | 20 | `26 − 3 − (70−56)>>2` ✓ |
| `summer_5`  | **0** | 51 | −5 | 19 | `26 − 3 − (70−51)>>2` ✓ |
| `summer_7`  | **0** | 48 | −3 | 18 | `26 − 3 − (70−48)>>2` ✓ |
| `summer_9`  | — | — | **destroyed** | — | *"lost at sea"* |

Three separate confirmations come out of this table.

**a. The `supply == 0` → `−3 moves` term, isolated exactly.** Seven of seven post-tick saves match to
the move, and the term is deterministic, so unlike the condition term it is directly separable. The
**control** is the Ptolemaic fleet in the same ten saves: 70 ships, supplied, condition 100, predicted
`30 − (70−50)/10 = 28` with no penalties — and it reads **28 in all ten saves**. Same formula, same
turns, one starved and one not.

**b. The `supply == 0` → `−random(0..1)` condition term, isolated by parity.** This looked
untestable, because the storm pass dominates. It isn't, because of the shape of the two terms. Ships
never changed, so `dmg < 6` held every turn, and `FUN_004494e4` clearly returned `< 0` (open sea)
given the magnitudes — which forces storm damage onto the `dmg × 2 + 1` branch, i.e. **always odd**,
and bounded to `{3, 5}`. The supply rider adds `{0, 1}`. So each turn's total must lie in
`{3, 4, 5, 6}`, and **any even total proves the supply roll came up 1**.

All five single-turn observations — `−3, −4, −6, −5, −3` — fall inside `{3,4,5,6}`, and **two of them
are even**. The two-turn gaps agree too (`−6 = 3+3`, `−10 = 5+5`). The supply term is real and
behaves as the code says.

**c. Destruction below condition 40, straight from the game's own news log.** `summer_7` leaves the
fleet at condition **48**; in `summer_9` the fleet record is simply gone (2 fleets, not 3), with the
ship count never having dropped — so not naval combat, which reduces `+18`. Reading the news-log ring
buffer out of `1_cartago_271_summer_9.sav` directly gives the literal string:

```
A fleet belonging to Carthage is lost at sea.
```

which is exactly the message assembled at `all_app_functions.txt:54595-54597`, immediately before
`FUN_0044AD38` deletes the fleet. That is the destruction branch firing, confirmed end to end.

**What this does *not* show.** The fleet's death was driven mainly by the **storm** spiral, not by
starvation: over the seven turns from condition 79 to 48, the storm term alone accounts for roughly
`−28 … −35` of the `−31` observed, while the supply rider contributes at most `−7` and on average
about `−3.5`. Zero supply is an aggravating factor and a real one, but a fleet parked at sea away
from friendly coast will rot and sink whether or not it is fed. The starvation penalty that actually
bites in play is the **−3 moves**, which is large against a base of 26 and which, combined with the
damage-driven move loss, leaves a dying fleet progressively less able to reach a port to repair.

## A real parser bug found on the way

`SaveArmyTable.Parse` throws `InvalidDataException` on `1_thracia_271_spring_3.sav` (*"Army 10 has
invalid coordinates or owner code"*) and `1_thracia_271_autumn_1.sav` (*"Army 9 …"*), which aborts
`IC2.Inspect` for the whole file — two of the thirteen saves in this series could not be read by the
tool at all and had to be decoded from raw bytes.

The cause is the owner check `owner > 15` in `SaveArmyTable.cs:42`. Both offending records hold owner
`0xFFFF`, and `autumn_1`'s army 9 additionally has **zero troops** in all 20 slots. This is the same
`0xFFFF` no-owner sentinel that `galatia-elimination-and-city-resupply-confirmed.md` already had to
special-case for the *capital* field — a tombstone for an army that was merged or eliminated during
the turn and whose slot has not been compacted yet. Two nearby records support that reading: in
`spring_3` the `0xFFFF` record is a byte-for-byte duplicate of the Egyptian army at `(41, 62)` that
had just been resupplied, and `autumn_1`'s is an emptied shell at `(38, 52)` where an army stood one
save earlier.

Not fixed here — this is an investigation, not a code change — but it should be: the parser should
treat `owner == 0xFFFF` as a tombstone and skip the record rather than reject the entire save. **[derived]**

## Conclusions

1. **Supply drives army morale, directly and by a simple rule.** `[confirmed]` — `FUN_004514ec`
   `0x004514EC`, army loop at `all_app_functions.txt:54501-54529` (the two morale writes at
   `54517-54525`), writing `ArmyRecord +14`. Verified empirically on 12 of 12 consecutive turn transitions in
   `1_thracia_271_*.sav` with every confound excluded. `pct < 10` → `−2` floored at 51;
   `pct > 15` → `+1` capped at 70; `10…15` inert.
2. **The earlier "no link" conclusion was measuring the wrong field.** `[confirmed]` — the tactical
   array `DAT_004A0350` genuinely has no supply term; the strategic field `+14` does. The two-armies-
   at-68 snapshot is consistent with the rule (both were at or near the 70 ceiling, which most
   adequately-supplied armies reach and stay at — nine of the fourteen armies in this series sit at
   exactly 70 for the whole summer).
3. **Seasonal supply consumption is `((90 − seasonVal) × troops) / 20000`, with the season table
   read out of the DAT at `0x1F7D8`.** `[confirmed]` — Spring 50, Summer 80, Autumn 80, Winter 20;
   winter costs 7× summer. Armies aboard a fleet use a flat `troops / 200` with no seasonal term.
4. **Fleet condition is the naval analog and degrades on zero supply at sea** — `−3 moves` and
   `−random(0..1)` condition per turn, no floor, no free regeneration, lethal below 40.
   `[confirmed]` **from code and empirically**, on `1_cartago_271_*.sav`: the moves term matches 7/7
   against a supplied control fleet that matches its own formula 10/10, the condition term is
   isolated by the parity argument above, and the destruction branch is confirmed by the literal
   *"A fleet belonging to Carthage is lost at sea."* string in `summer_9`'s news log.
   **There is no undiscovered fleet morale field** — `[confirmed]`, the record is fully labelled and
   both remaining `?` words are ruled out from save data.
5. **Naval attrition is a storm-driven death spiral, and zero supply only aggravates it.**
   `[confirmed]` — `random(100 − condition) / 10` escalates as the fleet degrades, doubled away from
   friendly coast and again in winter. This, not starvation, is what actually sank the Carthaginian
   fleet. Worth stating plainly because it is easy to over-read the Cartago series as "no supply
   kills fleets": no supply costs a fleet **3 of its 26 moves** and about half a condition point per
   turn; the sea does the rest.
6. **"Repair" is the condition dialog, not a new value.** `[confirmed]`.

### Still open

- **The storm-damage constants, end to end.** `FUN_004494e4` (the "away from friendly coast" test
  that doubles damage) and `fleet[+24] == 1` (which triples it) are both inferred from magnitudes
  here, not decompiled. The Cartago series is consistent with the doubling branch being active
  throughout but cannot prove which predicate selected it.
- **The 2:1 decay-to-regen asymmetry is confirmed as code but unexplained as design.** Worth a
  deliberate decision in `game-design.md` rather than being inherited silently, since it makes
  starvation roughly twice as expensive to undo as to incur.
- **`FleetRecord +6`.** Ruled out as morale, but not positively identified. It tracks something
  position-shaped.
- **Auto-resupply.** Several armies and fleets in this series hold a constant supply percentage for
  many turns despite the consumption rule (e.g. army 0 at 80 % then 95 % for nine turns), so
  something resupplies units near friendly cities outside the dialog path. Not traced here; it does
  not affect the Thracian army, which was never resupplied.
- **`IC2.Data`'s `0xFFFF` army-owner tombstone**, above.
