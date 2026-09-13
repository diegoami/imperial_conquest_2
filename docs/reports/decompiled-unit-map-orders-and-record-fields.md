# The full strategic order set (`TUnitMap_*`), and three corrected record fields

`delphi_symbols.tsv` contains ten `TUnitMap_*` methods that appear nowhere in `docs/reports/` or `docs/game-design.md`: `JoinArmies`, `SplitArmy`, `Fortify`, `SupplyFleet`, `RepairFlt`, `FleetToFleetTransfer`, `JoinFleets`, `SplitFleet`, `ScuttleFleet`, `ChangeUnitDetails`. Decompiling them (plus `SelectUnit`, `DisbandArmy`, `SupplyArmy`, `RecruitMercenaries` and the dialog classes they open) recovers the game's complete strategic order set, several caps and costs never previously seen, an entire naval-transport subsystem, and — as a side effect of reading the code that touches them — corrections to three record fields this project had labelled wrongly.

All addresses are from `%LOCALAPPDATA%\ReTools\all_app_functions.txt`. Runtime array bases used throughout: armies `0x0047C1EC` (stride 656), fleets `0x0049C26C` (stride 26), cities `0x0047959C`-relative (stride 34), nations `0x00474670` (stride 1172), map `0x0045E870` (column-major, 280-byte columns). Every stride matches the SAV layout already confirmed in `decompiled-sav-file-layout.md`, which is what makes the field offsets below directly transferable to save bytes.

## Part 1: three corrected record fields

### Army record `+8` is the covered map cell, not morale

`army-records-and-roman-roster.md` labelled `+8` a "candidate morale value" because the Roman army's read `9` and the panel said morale "very high". The code says otherwise, in four independent places:

- `FUN_00449f08` (army creation): `army[+8] = map[x][y]` — it stores the cell the new army's marker is about to cover.
- `FUN_0044AB90` (army removal) and `FUN_0044D420` (each movement step): write `army[+8]` back into the map to restore the covered cell, then save the newly covered cell into `army[+8]`.
- `TInformation_ShowArmyDetails` (`0x0043C33C`) prints the panel's `Terrain -` line as `terrainTable[army[+8]]` — see `terrain-move-cost-table-in-dat.md`. The Roman army's `9` is terrain code 9 = `River`, and that same report already noted the DAT cell at `(100, 42)` is `9`. The "morale 9" reading was a coincidence of the two.
- `army[+8] == -1` is the sentinel for **"this army is aboard a fleet"** (no map cell covered) — used by `JoinArmies`, `FUN_0044AB90`, and the AI army scan.

### Army record `+14` is morale

The same panel function prints `Morale -` from `army[+14]` (`DAT_0047C1FA`), tiered as

```c
t = army[+14] - 51;  if (t < 0) t = army[+14] - 48;
tierName = moraleNames[t >> 2];          // 11-byte strings at 0x00479428
```

`battle-quality-promotion-and-morale-array-decompiled.md` had already flagged `+14` as a real, changing, wrongly-padded byte (68 and 66 in two saves) and called it "army experience"; it is the army's morale stat, and it is what seeds each unit's tactical-battle morale (`random(quality × 4) + army[+14]`). A freshly split army gets `army[+14] = 0x3B = 59` (`FUN_00449F08`).

It is also a direct multiplier on both army-strength functions:

```c
FUN_0044A930(army) = (Σ troops, archers counted ×3) / 80 × army[+14]      // siege strength
FUN_0044A8CC(army) = (Σ powerWeight[type] × troops / 100) / 80 × army[+14] // field-battle strength
```

`powerWeight` is unit-type-table field **`+0x26`** (LI 20, HI 100, Ar 40, LC 60, HC 120) — one of the two fields `unit-type-stat-table-in-dat.md` left unidentified. It is a per-type combat-power weight.

### Fleet record `+20` is condition %, not a city index

`fleet-order-at-caere.md` labelled `+20` `CityIndex` because the controlled Caere order wrote `82` there and Caere is city 82. That is right *while the fleet is under construction* and wrong afterwards:

- `FUN_0044A004` (order placed): `fleet[+8] = owner; fleet[+10] = 24; fleet[+18] = ships; fleet[+20] = buildCityIndex; fleet[+22] = 0xFFFF; treasury -= ships × 10`.
- `FUN_0044A050` (construction completes): news `"<nation> finishes a new fleet at <cityName[fleet[+20]]>"`, the fleet is placed on the map, then **`fleet[+20] = 100`**, `fleet[+10] = 0xFFFF`, `fleet[+14] = 50` (supplies), `fleet[+16] = 0` (money), `fleet[+24] = 0`.
- `TRepairFleet_*` (`0x00440AC4`…) displays `fleet[+20]` as `"N %"`, clamps a repair order to `[0, 100 − fleet[+20]]`, charges `ships × points / 5` talents, and adds the points to `fleet[+20]`.
- `FUN_0044AA54` (naval strength) computes `ships × fleet[+20] / 10`.
- `FUN_0044B4F8` (naval combat damage) reduces `fleet[+18]` and `fleet[+20]` by the same proportion.

This reading explains `fleet-order-at-caere.md`'s own data *better* than the CityIndex reading did. In `1_rome_270_winter_7.sav` the four fleets at `(0,0)` are **under construction**, so their `+20` really is a build-city index and matches the owner of that city exactly (6-for-6 in `fleet-owner-field-confirmed.md`); the two fleets with real map coordinates read `97` and `100` — condition percentages, not Cales and Capua. The "`CityIndex` drifts as a fleet acts" observation in `field-recruitment-uniform-attrition-and-fleet-drift.md` is a fleet's condition changing, and the impossible `166` for the Athens fleet is a build-city index, not a percentage.

The rest of the fleet record follows from the same functions: `+0/+2` x,y · `+8` owner · **`+10` construction countdown (24 at order, `0xFFFF` once launched — also the "is on the map" flag used by `FUN_0044AD38`)** · `+12` moves · `+14` supplies · `+16` money · `+18` ships · `+20` condition % · `+22` carried army index (`0xFFFF` = none) · `+24` covered map cell.

### City record fields, from `TInformation_ShowCityDetails` (`0x0043BE5C`)

Not a correction, but the panel code names most of the 34-byte record in one place, which `roadmap.md` §2 still lists as open: `+6` owner ("Controlled by") · `+8` allegiance ("Allegiance to") · `+10` loyalty (tier string = `names[loyalty / 10]`) · `+12` supplies, tons · `+16` fortification % (`"(under construction)"` appended when > 100) · `+18` population in thousands (printed as `value × 1000`, with `value × 100 / cityRecord[+20]` as a "% of maximum") · `+20` maximum population · `+22` tribute.

## Part 2: the order set

### Army orders

| Order | Function | Rules recovered |
| --- | --- | --- |
| **Join armies** | `TUnitMap_JoinArmies` `0x004472FC` | Both armies must be the active nation's and co-located. Neither may be aboard a fleet (`army[+8] == -1` → *"An army on a fleet cannot be combined with another."*). Combined units ≤ **20**, combined troops ≤ **100,000**. Units are moved one at a time, supplies and money add, the emptied army is deleted, and the survivor's **moves are zeroed**. |
| **Split army** | `TUnitMap_SplitArmy` `0x0044755C` | Needs ≥ 2 units. Opens `TSplitArmyUnit`. `FUN_00449F08` creates the new record: cap **198 armies** total (`< 0xC6`), moves `0` for a human nation and `1` for an AI one, supplies/money `0`, morale `59`. |
| **Disband army** | `TUnitMap_DisbandArmy` `0x004476AC` | *"An army must be near its own city to disband."* Confirmation prompt. The army's **money** goes to the national treasury and its **supplies** to the nearby city's stock. |
| **Change unit details** | `TUnitMap_ChangeUnitDetails` `0x00447644` | Opens `TChangeArmyUnits` — rename, split, join and disband individual units inside one army. |
| **Supply army** | `TUnitMap_SupplyArmy` `0x00446F50` | Opens `TAFSupply` (see below). |
| **Recruit mercenaries** | `TUnitMap_RecruitMercenaries` `0x00446FF4` | Opens `TRecruitMercs` (see below). |
| **Army-to-army transfer** | `TUnitMap_ArmyToArmyTransfer` `0x00447288` | Already confirmed in `army-to-army-transfer-confirmed.md`. |
| **Fortify a city** | `TUnitMap_Fortify` `0x00448004` | See below. |

**Unit-level join/split** (`TChangeArmyUnits_JoinUnits` `0x00444E8C`, `TSplitArmyUnit_OK` `0x004444CC`): only **regular** units may be joined (*"You can only join regular units together."*), only units of the **same type** (*"You can only combine units of the same type."*), and the combined troop count must not exceed the type's **standard battalion size** — unit-type-table field `+0x1A` (LI 15,000 · HI 6,000 · Ar 3,500 · LC 7,000 · HC 2,500), which is what that field is actually *for*. The merged unit's quality is the **arithmetic mean** of the merged units' qualities. A split unit inherits type and quality and is auto-named with the next free ordinal for its type across all of the nation's armies and the city garrison (`1st/2nd/3rd/Nth` + `Foot`/`Guards`/`Bowmen`/`Lancers`/`Dragoons` + `Battalion`) — which is exactly the naming pattern seen in every roster in `army-records-and-roman-roster.md`.

### Fleet orders

| Order | Function | Rules recovered |
| --- | --- | --- |
| **Join fleets** | `TUnitMap_JoinFleets` `0x00447A48` | Combined ships < **100** (*"There are more than 100 ships in these fleets combined."*). Neither may carry an army. Ships, supplies and money add; the survivor's **moves are zeroed**; the absorbed fleet is deleted. |
| **Split fleet** | `TUnitMap_SplitFleet` `0x00447CAC` | Needs ≥ **20** ships. Cannot split a fleet carrying an army. Can fail with *"You can not make any more fleets at this time."* (fleet-table cap). |
| **Fleet-to-fleet transfer** | `TUnitMap_FleetToFleetTransfer` `0x004479F4` | Opens `TFleetToFleet` — reciprocal ships/supply/money transfer, the naval twin of `TArmyToArmy`. |
| **Scuttle fleet** | `TUnitMap_ScuttleFleet` `0x00447E34` | Must be near one of your own cities; cannot scuttle while carrying an army; confirmation prompt. The fleet's **money** goes to the treasury and its **supplies** to the city. |
| **Repair fleet** | `TUnitMap_RepairFlt` `0x004478C8` | *"The fleet can only be repaired at one of your cities."*, and not while carrying an army. Cost **`ships × points / 5`** talents; repairing **zeroes the fleet's moves**. |
| **Supply fleet** | `TUnitMap_SupplyFleet` `0x004477FC` | Opens `TAFSupply` against a city or another fleet. |
| **Build fleet** | `TBuildFleet_*` `0x0045583C` | Order size clamped to **[10, 100]** ships. Cost `ships × 10`, upkeep `ships × 3`, capacity `ships × 500` — all three already confirmed; the **10-ship minimum order** is new. Construction counter starts at **24** ticks. |

### Naval transport — an entire subsystem `game-design.md` is silent on

`TUnitMap_SelectUnit` (`0x004466CC`) is where a click on the map becomes an order, and it contains the embark rule:

```c
// army selected, then a friendly fleet clicked
if (fleet[+22] == -1) {                                  // fleet not already carrying an army
    if (fleet[+18] < armyTroops / 500)
        "The army is too large for this fleet ?"
    else
        FUN_0044B79C(army, fleet);                       // embark
}
```

So a fleet carries **one army**, up to **500 troops per ship** — the same `ships × 500` number `fleet-order-at-caere.md` recorded as the order dialog's "5,000 soldiers", now confirmed as a real enforced capacity rather than a display value. `FUN_0044B79C` zeroes the fleet's moves, sets `fleet[+22] = armyIndex`, restores the army's covered map cell and sets `army[+8] = -1`, snaps the army's coordinates to the fleet's, zeroes the army's moves, and — for an AI nation only — trims the army to `ships × 500` troops if it is over capacity (`FUN_0044F8FC`). Everything downstream keys off those two fields: an embarked army cannot be joined, a carrying fleet cannot be repaired, scuttled, split or joined, and deleting a carrying fleet deletes the army with it (`FUN_0044AD38` → `FUN_0044AB90`).

### City fortification — a build order, and the field's dual encoding

`TUnitMap_Fortify` (`0x00448004`) → `TFortifyCity` (`0x004404C0`):

- Refused if the city is under siege (*"You cannot fortify a city which is under siege."*), if fortification is already **100** (*"This city cannot be fortified any further."*), or if it is already > 100 (*"This city is already being fortified."*).
- The dialog offers `0 … (100 − current)` percentage points at **`cityRecord[+18] × points`** talents — i.e. the price scales with the city's population in thousands.
- `TFortifyCity_OK` writes **`fortification += points × 100`** and deducts the cost. That is the dual encoding: a value ≤ 100 is a finished fortification percentage, a value > 100 encodes an order in progress, and `TInformation_ShowCityDetails` appends `"(under construction)"` for exactly that case.
- A siege attempt wipes a pending order: `FUN_0044B27C` begins with `if (fort > 100) fort = fort % 100;`.

### Supply is bought, not moved — `TAFSupply`

`TAFSupply` (`0x0043EDC0`) is the dialog `galatia-elimination-and-city-resupply-confirmed.md` found on video but could not name. `TAFSupply_TransferSupply` (`0x0043FF98`):

```c
target.supplies      += amount;          // army[+10] or fleet[+14]
city.supplies        -= amount;
treasury[cityOwner]  += amount / 5;      // the SELLING city's owner is paid
target.money         -= amount / 5;      // from the army's/fleet's own purse
```

**Supply costs 1 talent per 5 tons, paid out of the army's or fleet's own money to the supplying city's owner** — which can be another nation. `TAFSupply_ChangeBuyAmount` (`0x0043FE4C`) supplies the caps:

- **Army supply capacity = `troops / 100` tons.** This confirms exactly the "~98–100 troops per ton" candidate in `galatia-elimination-and-city-resupply-confirmed.md`: Army 0 (99,882 troops → 998 t) showed `204 t (20%)` and `344 t (34%)`; Army 2 (28,227 → 282 t) showed `184 t (65%)`; the Roman army in `army-records-and-roman-roster.md` (48,173 → 481 t) showed `482 t (100%)`. `TInformation_ShowArmyDetails` prints the percentage as `supplies × 10000 / troops`, matching all four.
- **Fleet supply capacity = `ships × 8` tons.**
- Purchase is also capped at the buyer's `money × 5` and at the city's stock.
- `TAFSupply_ChangeMoney` moves talents between the national treasury (or a co-located fleet) and the army/fleet, capped at **1,000** money per army or fleet.

### Mercenary hire cost — solved

`TRecruitMercs_RecruitMercUnit` (`0x00441360`), against the 12-byte pool record of `mercenary-pool-record.md` (`+0` label, `+2` type, `+4` troops, `+6` quality):

```c
hireCost = (troops × priceTable[type][+0x24]) / 1000 × quality;
if (army.money < hireCost) "Your army has too little money to pay these mercenaries."
```

It is the **same** unit-type table as standing recruitment (decompilation-plan item 2's open question), using the *quarterly* price field `+0x24` (LI 1 · HI 2 · Ar 1 · LC 3 · HC 4), and it is paid from the **army's** purse, not the treasury. The hired unit is appended with its **`Label` copied into the army unit slot's `+0` word**, and its name read from a 20-byte-stride name table at `0x0049CC94` — so `mercenary-pool-record.md`'s unidentified `Label` is a name-table index, and the army unit slot's `+0` word (listed as unexplained in `battle-quality-promotion-and-morale-array-decompiled.md`) is the **regular/mercenary marker**: `0` = regular, non-zero = mercenary. `TInformation_ShowArmyDetails` uses exactly that split for its two upkeep lines:

```c
regularsCost  = Σ over slots with slot[+0] == 0 :  (troops / 200) × price[type]
mercenaryPay  = Σ over slots with slot[+0] != 0 : ((troops / 200) × price[type] × quality) / 5
```

Computing `regularsCost` over the 13-unit Roman roster in `army-records-and-roman-roster.md` gives `21+48+48+56+12+24+28+12+46+34+52+34+27 = 442`, an **exact match** to that report's screenshot value of "442 talents per quarter", which it had listed as not yet decoded. Mercenary upkeep is the same figure scaled by `quality / 5`. Also confirmed: the fleet-space check on hiring an army that is currently embarked (*"This fleet has too little space for these mercenaries."*), and the 100,000-troop army cap.

## Part 3: map marker codes encode owner *and* size — closing the 333/335 question

`roadmap.md` §2 has carried "the difference between fleet marker values `333` and `335`" as open since `fleet-order-at-caere.md`. Both marker-writing functions answer it:

```c
FUN_0044A80C(army):   t = troops / 1000;
                      marker = owner + (t < 25 ? 200 : t < 50 ? 216 : 232);
FUN_0044A878(fleet):  s = ships;
                      marker = owner + (s < 25 ? 300 : s < 50 ? 316 : 332);
```

Three size bands each, 16 nations wide. Because every band base is a multiple of 16 above 200/300, `(code − 200) % 16` and `(code − 300) % 16` both still give the owner in every band — which is why `army-records-and-roman-roster.md`'s owner rule worked — but the band itself is a **size class**, which no prior report had. `TUnitMap_SelectUnit` accepts exactly `200..247` for armies and `300..347` for fleets, matching.

Checked against the two fleets in `fleet-order-at-caere.md`: the 90-ship fleet at `(46, 69)` reads `333` → band 332, owner 1 = **Carthage**; the 70-ship fleet at `(189, 93)` reads `335` → band 332, owner 3 = **Ptolemaic**. `fleet-owner-field-confirmed.md` independently identified the `(189, 93)` fleet's `+8` owner word as **3, Ptolemaic**, and the Carthaginian fleet (49 ships by the later save, which would then read `317`) as owner **1**. Two markers, two independent owner confirmations, exact agreement. `333` and `335` are not two fleet states — they are two different nations' large fleets.

A marker is only written when `army[+8] >= 0` (the army is not aboard a fleet), which is how embarked armies vanish from the map.

## What this does not establish

- The one discrepancy this pass found and could not resolve: the code says buying supply costs `amount / 5` from the army's money, but the three frames tabulated in `galatia-elimination-and-city-resupply-confirmed.md` show Army 0's money unchanged at 256 across a 100-ton purchase (which should cost 20 talents). Either the frames are ordered differently than assumed or there is a path where the charge is skipped. **A controlled before/after save pair around one supply purchase would settle it** and is worth doing before any of this is implemented.
- Fleet record `+4` and `+6` remain unidentified (`+6` was the word `fleet-owner-field-confirmed.md` noted as non-zero only for the Carthage fleet).
- The exact fleet-table capacity behind *"You can not make any more fleets at this time."*
- `FUN_0044F8FC` (the AI over-capacity troop trim) was not decompiled.

## Reproduction

```text
grep -n "TUnitMap_\|TAFSupply_\|TFortifyCity_\|TRepairFleet_\|TChangeArmyUnits_" delphi_symbols.tsv
# then extract each address from all_app_functions.txt (functions are named FUN_<addr> in that dump)
```

Save-side checks with the existing tooling:

```text
dotnet run --project src/IC2.Inspect -- --list-fleets saves/1_rome_270_winter_7.sav
dotnet run --project src/IC2.Inspect -- --inspect-army saves/11_supply.sav 100 42
```

## Next checks

1. The supply-purchase money discrepancy above, via a controlled same-turn save pair.
2. Re-label `SaveFleetTable.CityIndex` as a dual-purpose `BuildCityOrCondition` field (and `SaveArmyTable`'s `MoraleValue` at `+8` as `CoveredCell`, `+14` as `Morale`) in `IC2.Data` — this report is evidence, not a code change; the parser still carries the old labels.
3. Confirm the fortification `+= points × 100` encoding directly in save bytes by issuing a fortify order and saving immediately.
