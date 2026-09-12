# A seasonal weather-event system, not treasury collection

Corrects a guess in `decompiled-turn-and-calendar-sequencing.md`, which called `FUN_00451304` (the step run once per weekly tick, right after city/army/fleet processing) "likely treasury/tax collection" based only on its position in the sequence. Decompiling it shows something different and more interesting.

## What it actually does

1. Resets a per-fleet flag (stride matches `SaveFleetTable`'s 26-byte record) to 0 for every fleet — plausibly a "storm/event affected this fleet already this week" guard, given what follows.
2. Clears a map-overlay value (cells reading `1`) within a ±10 tile box around each of 20 tracked locations — likely fog-of-war or a "recently affected" highlight being reset before new events are rolled.
3. **Rolls seasonal random weather events across the map.** For each of 20 locations, rolls a random check whose odds and effect radius depend on the season (and, for Spring/Autumn, the week within the season):

| Season | Week | Odds per check | Radius parameter |
| --- | --- | ---: | ---: |
| Spring | < week 6 | 1-in-15 | 4 |
| Spring | ≥ week 6 | 1-in-30 | 3 |
| Summer | — | 1-in-40 | 2 |
| Autumn | < week 7 | 1-in-30 | 3 |
| Autumn | ≥ week 7 | 1-in-15 | 4 |
| Winter | — | **1-in-5** | 5 |

On a hit, it triggers an event (`FUN_00451bc`, not decompiled) at a location within the radius, weighted toward closer cells (an inner exact-diamond region always qualifies; cells outside it still have a 50/50 chance).

## Why this matters

**Winter is by far the stormiest season** (roughly 8× more frequent event checks than Summer, and the largest effect radius) — this is almost certainly the underlying source of the fleet "lost at sea" / "damaged in a storm" mechanic found in `decompiled-turn-and-calendar-sequencing.md`, where deployed fleets had worse odds specifically in Winter. That report found the *symptom* (fleets checking for bad weather more often in Winter); this is the *system* generating weather events across the whole map that fleets are likely reacting to, not a separate coincidental seasonal rule.

## What this does not establish

- What `FUN_004511bc` (the actual event-application function) does — damage a city, sink a fleet in that area, or something else entirely. Not decompiled this pass.
- Whether this system affects anything beyond fleets (e.g. city supply, army movement) — the connection to the fleet-storm mechanic is inferred from the seasonal pattern matching, not from a direct call-graph link between this function and the fleet-damage code.
- What the 20 tracked locations (`DAT_00479540`) represent — plausibly national capitals or coastal cities, not confirmed.
- Where treasury/tax collection actually happens in the weekly tick — this function is not it; that code is still unlocated.

## Reproduction

Found via `ExportAddresses.java` on `0x00451304`, the previously-uncalled-out step in `FUN_004514ec`'s weekly tick sequence.

## Next checks

1. Decompile `FUN_004511bc` to see the actual weather event's effect.
2. Find the real treasury/tax-collection code, since this wasn't it — search for writes to the nation `Treasury` field (`+0x438` per `rome-city-recruitment-and-nations.md`) within the weekly tick's call graph.
3. Identify the 20 tracked locations at `DAT_00479540`.
