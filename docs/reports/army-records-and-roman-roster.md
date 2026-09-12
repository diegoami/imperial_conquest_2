# Save army table and Roman roster at (100, 42)

The user supplied three screenshots of the Roman army at `(100, 42)` in `11_supply.sav`: two composition lists and a panel reading **13 units**, **48,173 troops**, **482** tons of supply, and **296** money. This report compares those observations with the save bytes. The original game was not run for this analysis. Screenshots and saves remain outside Git.

## Repeating save layout

The 334-city table ends at `0x18A5C`. In the known **SAV** files, a little-endian 16-bit army count begins there. It is `10` in `11_supply.sav`. The first record begins at `0x18A5E`, and the next begins 656 bytes later at `0x18CEE`. All ten records are 656 bytes apart. Their coordinates match the ten army positions on the map, and their owner words match the map marker's owner residue `(code - 200) % 16` in all ten cases.

| Offset within each 656-byte record | Width | Current interpretation | Evidence |
| ---: | ---: | --- | --- |
| `+0`, `+2` | 2 each | `x`, `y` | All ten `11_supply.sav` records match map positions |
| `+4` | 2 | owner code | All ten match their map marker owner residue; Roman army is `0` |
| `+6` | 2 | moves remaining | `8` matches the `11_supply.8.png` army information panel |
| `+8` | 2 | candidate morale value | `9` displays as “very high”; thresholds remain unknown |
| `+10` | 2 | supplies, tons | `403 → 482` controlled transfer; user panel reads `482` |
| `+12` | 2 | money | Roman field is `296`, matching the user panel |
| `+14` | 2 | unknown | Do not label yet |
| `+16` | 640 | twenty 32-byte unit slots | Thirteen occupied slots in the Roman army; exact screenshot match |

Each unit slot contains an unknown word at `+0`, a type code at `+2`, troop count at `+4`, quality code at `+6`, and a 24-byte name area beginning at `+8`. A slot with zero troops can still retain a name or filler bytes, so zero-troop slots are not shown as active units. The observed names are NUL-terminated ASCII. This screenshot identifies type codes `0` as light infantry, `1` as heavy infantry, `3` as light cavalry, and `4` as heavy cavalry. [Later recruitment screenshots](rome-city-recruitment-and-nations.md) identify code `2` as archers. Quality codes `6`, `7`, `8`, and `9` map to **average**, **good**, **very good**, and **elite** respectively. Code `5` occurs elsewhere but is not identified here.

## First army in `11_supply.sav`

The first record is the Roman army at `(100, 42)`, owner `0`. Its header has moves `8`, candidate morale value `9`, supply `482`, and money `296`. Its thirteen nonzero unit slots reproduce the user's list, although the original screen sorts the list differently from the file order:

| File slot | Name | Type | Troops | Quality |
| ---: | --- | --- | ---: | --- |
| 0 | 1st Foot Battalion | light infantry | 4,210 | average |
| 1 | 1st Guards Battalion | heavy infantry | 4,900 | very good |
| 2 | 2nd Guards Battalion | heavy infantry | 4,920 | good |
| 3 | 3rd Guards Battalion | heavy infantry | 5,747 | good |
| 4 | 1st Dragoons Battalion | heavy cavalry | 774 | good |
| 5 | 7th Guards Battalion | heavy infantry | 2,583 | good |
| 6 | 2nd Dragoons Battalion | heavy cavalry | 1,539 | very good |
| 7 | 2nd Lancers Battalion | light cavalry | 900 | good |
| 8 | 6th Guards Battalion | heavy infantry | 4,787 | good |
| 9 | 5th Guards Battalion | heavy infantry | 3,571 | good |
| 10 | 4th Guards Battalion | heavy infantry | 5,300 | elite |
| 11 | 8th Guards Battalion | heavy infantry | 3,442 | average |
| 12 | 2nd Foot Battalion | light infantry | 5,500 | average |

The troop counts sum exactly to **48,173**. The next slot has zero troops but retains a `4th Guards Battalion` name; this is not counted as a current unit. `11.sav` has the same army structure and troop roster, with supply `403` before the user's 79-ton transfer.

The later army-information screenshot breaks that total into **9,710 light infantry**, **35,250 heavy infantry**, **0 archers**, **900 light cavalry**, and **2,313 heavy cavalry**; all five totals match the parsed slots. It identifies the underlying terrain at `(100, 42)` as **River**, matching river code `9` in the initial DAT. The displayed regular cost of **442 talents per quarter**, mercenary pay of **0**, and supply percentage of **100%** are not yet decoded as stored fields or formulas. See the [Ptolemaic player and Week 9 report](ptolemaic-player-and-week9.md) for the full panel and new save pair.

## Cross-save checks and limits

The same count plus 656-byte-record layout parses all ten available saves (`1`, `4`–`11`, and `11_supply`). Across these, **106 of 108** army records directly match an army-coded map cell with the same owner. The other two are in `8.sav` and `9.sav`: an army record shares coordinates with a fleet-coded cell (`333`), so the fleet icon hides the army marker. This is evidence that map codes are display overlays, not a complete entity list.

An independent battle cross-check supports the troop fields: the first Roman army in `7.sav` is at `(102, 44)` with **14 units and 50,700 troops**, while in `8.sav` it remains at `(102, 44)` with **11 units and 39,941 troops**. Those are the exact pre-battle and surviving troop totals reported in the [battle observation](battle-observation.md). The inspector also parses the first army in every available save without an active-unit layout error.

The initial **DAT** differs at this boundary: its word at `0x18A5C` is `100`, and treating it as the SAV army count does not produce valid records. The new army parser therefore applies to known saves only. Unknown header and unit-slot words, fleet records, quality `5`, combat stats, and army orders still need decoding. The Godot viewer now displays a save army's known roster when its flag is clicked, and the inspector can reproduce the Roman record with:

```text
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-army saves/11_supply.sav 100 42
```
