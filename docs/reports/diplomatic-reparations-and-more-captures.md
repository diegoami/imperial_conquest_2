# A clean diplomatic reparation, two more captures, and an open fleet-order question

Three more saves continue the same Rome session, again with notes but no screenshots this time:

```text
1_rome_270_autumn_9.sav: 10 ships will be built in Cerae. The two armies have been reorganized. Galatia destroys army of Seleucid.
1_rome_270_autumn_11.sav: Armies move north. One army conquers Brixia, the other one Verona. Seleucid destroys army of Ptolemaic.
    Ptolemaic sues Seleucid for peace, ends all agreements, pays reparation of 2269 talents. Laranda (Seleucid) falls to Galatia.
1_rome_270_winter_1.sav: Both armies resupply at Brixia and Verona. Macedonia forms an alliance with Illyria. Macedonia declares
    war on Greece. Macedonia destroys army of Greece. Greece sues Macedonia for peace, cancels agreements and pays 998 talents.
```

All four saves in this batch (`autumn_7_fleet`, `autumn_9`, `autumn_11`, `winter_1`) were exported with the new `IC2.Inspect --to-json` and cross-checked with `node`, rather than reading one field at a time.

## A diplomatic reparation matches the nation treasury field exactly

Ptolemaic's `NationRecord.Treasury`, `autumn_9 → autumn_11`:

```text
999 → -1270   (Δ = -2269)
```

The notes report Ptolemaic paying Seleucid a **2,269**-talent reparation that same turn. `999 - 2269 = -1270`, an exact match with no other nation-level field for Ptolemaic changing unexpectedly. This is an independent confirmation of the treasury field — a different nation, a different mechanism (AI diplomacy, not the player's own tax/mobilize actions) — landing exactly on a real quoted number.

The receiving side is less clean: Seleucid's treasury moved `-4596 → -1943` (`Δ = +2653`), not `+2269`. The extra 384 is presumably Seleucid's own ordinary income/expense that turn (they also fought a battle against Ptolemaic and captured a city from Laranda in this window). The same pattern repeats with the second reparation: Greece pays Macedonia 998 talents (per the `winter_1` note), but `autumn_11 → winter_1` shows Greece `154 → -1422` (`Δ = -1576`) and Macedonia `1087 → 3379` (`Δ = +2292`) — both far from a clean ±998, again presumably because a full turn of ordinary economic activity is mixed in. The lesson mirrors the [previous report](mobilization-movement-and-city-capture-modes.md): a clean read on a single mechanic needs either a payer with otherwise-quiet finances that turn (as Ptolemaic apparently was, having just lost a war) or a same-day before/after pair with no other activity.

## Two more forced captures, same pattern as before

Rome took Brixia and Verona this session (both from Gaul). Both match the [Sidon](rome-tax-increase-and-sidon-capture.md)/[Felsina](mobilization-movement-and-city-capture-modes.md) pattern exactly: owner flips, allegiance stays with the original nation, population and fortification drop, loyalty drops, tribute is untouched.

| | Brixia | Verona |
| --- | --- | --- |
| Owner | Gaul → Rome | Gaul → Rome |
| Allegiance | Gaul (unchanged) | Gaul (unchanged) |
| Population | 20,000 → 15,000 | 22,000 → 16,000 |
| Fortification | 37% → 27% | 45% → 33% |
| Loyalty | 74 → 45 | 70 → 48 |
| Tribute | 2 (unchanged) | 4 (unchanged) |
| Supplies | 200 → 150 | 220 → 160 |

This is now four independent forced captures (Sidon, Felsina, Brixia, Verona) all showing the same signature, making "population/fortification/loyalty drop, tribute untouched, allegiance retained" a well-supported rule rather than a one-off observation. One turn later (`winter_1`), both cities' population partially recovered (15,000→17,000; 16,000→19,000) while fortification and loyalty stayed essentially flat — consistent with slow post-conquest recovery, though only one data point.

Supply again does not cleanly conserve with the resupplying armies: Brixia and Verona lost 88 and 9 tons respectively that turn (`autumn_11 → winter_1`) while Rome's two armies gained 22 and 102 tons — neither the per-city nor the total (97 lost vs. 124 gained) lines up, reinforcing that multi-turn pairs aren't suited to isolating the resupply mechanic (as already noted in the mobilization report).

## Fleet record: a plausible init flag and a decrementing field

The Caere fleet record (index 3, from the [previous report](fleet-order-at-caere.md)) between `autumn_7_fleet` and `autumn_9`:

```text
before: 00 00 00 00 00 00 00 00 00 00 18 00 00 00 00 00 00 00 0A 00 52 00 FF FF 00 00
after:  00 00 00 00 FF FF 00 00 00 00 16 00 00 00 00 00 00 00 0A 00 52 00 FF FF 00 00
```

Two words changed: `+4` went `0 → 0xFFFF`, and `+10` went `24 → 22`. `ShipCount` (+18, still 10) and `CityIndex` (+20, still 82/Caere) are unchanged. The `+4` word matches the value already seen in all three other fleet records (`0xFFFF`), so a fresh order reading `0` there and flipping to the common `0xFFFF` after one turn is consistent with an "order now registered" flag rather than a real quantity. The `+10` word decreasing by exactly 2 in one turn is the mirror image of the city-garrison `StateCode` seen incrementing by 2 per turn in the [mobilization report](mobilization-movement-and-city-capture-modes.md) — both candidates for a per-turn counter, one counting up (elapsed time in a state) and one counting down (a plausible remaining-production-time counter), though neither is confirmed.

The note originally read "19 ships" (a typo for 10, confirmed by the user) — no new order was placed this turn, which is exactly why `ShipCount` stayed at 10, no second fleet record appeared, and Rome's treasury didn't move between `autumn_7_fleet` and `autumn_9` (`-795` in both, unlike the clean -100-talent cost recorded for the original order). The data is consistent; there was no discrepancy to resolve.

## Season boundary: Autumn's last week is also 11

Following the same pattern found for Summer→Autumn, `autumn_11 → winter_1` shows the week counter reset from 11 to 1 as the season changes. Both observed season transitions in this project now restart the week counter at 1 after week 11.

## Reproduction

```text
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_autumn_9.sav out/autumn_9.json
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_autumn_11.sav out/autumn_11.json
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_1.sav out/winter_1.json
```
Then compare nation treasuries, city records, and fleet records across the JSON files.

## Next checks

1. A same-day reparation payment (save immediately before/after accepting an AI peace offer, if the player can trigger or observe one directly) would give a truly clean payer/receiver pair, the diplomatic-payment equivalent of the fleet-order pair.
2. Track the Caere fleet record's `+10` word over more turns to see whether it reaches 0 when the ships actually complete and get an X/Y.
