# Rome city, recruitment queue, and nation colors in `11_supply.sav`

The user supplied five screenshots named `11_supply.1.png`–`11_supply.5.png` and explained their pairing with `11_supply.sav`: screenshot 1 is Rome's information panel, screenshots 2–3 show troops being recruited in Rome, and screenshots 4–5 give nation colors left to right and names top to bottom for identifiers `0`–`15`. This analysis reads the images and save as data. Neither the original game nor its executable was run. The source files remain outside Git.

## Rome information panel

Rome is city record **85** at `(101, 43)`. The panel reads: controlled by Rome, allegiance to Rome, population **181,000 (100%)**, loyalty **excellent**, fortification **78% (85,000)**, tribute **317 talents**, and supply **1,731 tons**. The 34-byte city record in `11_supply.sav` supports the following fields:

| Record offset | Raw word | Interpretation | Confidence |
| ---: | ---: | --- | --- |
| `+18` | 0 | controller = nation 0, Rome | High; also follows captured-city changes |
| `+20` | 0 | allegiance = nation 0, Rome | Strong candidate; controller and this field differ in 24 of 334 cities in this save, including Rome towns previously captured by Gaul |
| `+22` | 95 | numeric value associated with displayed loyalty **excellent** | Candidate; adjective thresholds not decoded |
| `+24` | 1,731 | city supplies in tons | High; screenshot and controlled 79-ton transfer agree |
| `+26` | 78 | fortification percentage | High; exact screen value |
| `+28` | 181 | current population in thousands | High; exact screen value after ×1,000 |
| `+30` | 181 | candidate reference population in thousands | Medium; `181/181 = 100%` matches the panel, and current ≤ reference for all 334 cities in this save |
| `+32` | 317 | tribute in talents | High; exact screen value |

The parenthetical **85,000** beside fortification has not been located in this city record. It may be derived or stored elsewhere; do not interpret it as the `+30` word, which is 181. Rome's allegiance and controller being identical cannot alone distinguish their fields, but captured cities whose `+18` controller changes while `+20` remains with the original nation support the `+20` interpretation.

## Recruiting troops

The screenshot quantities and types match twelve consecutive nonempty 8-byte entries in the save at `0x1A718`–`0x1A777`. Each entry has a first word whose precise meaning is unknown, a troop type code, a troop quantity, and city index **85** (Rome). The section has **40 slots**; the remaining 28 are empty in this save. Its start is 794 bytes after the end of the fixed-size army table in all ten available saves. Those intervening bytes include the fleet count and additional structures not fully mapped, so the parser currently accepts the observed two-fleet save layout only.

| Slot | Screenshot type | Type code | Troops | First word | Screenshot label |
| ---: | --- | ---: | ---: | ---: | --- |
| 0 | Light infantry | 0 | 15,000 | 18 | very poor |
| 1 | Light infantry | 0 | 15,000 | 18 | very poor |
| 2 | Heavy infantry | 1 | 6,000 | 18 | very poor |
| 3 | Light cavalry | 3 | 7,000 | 18 | very poor |
| 4 | Heavy cavalry | 4 | 2,500 | 18 | very poor |
| 5 | Light infantry | 0 | 15,000 | 18 | very poor |
| 6–10 | Archers | 2 | 3,500 each | 18 | very poor |
| 11 | Light cavalry | 3 | 7,000 | 2 | not ready |

This establishes type code **2 = archers**, completing the five type labels seen in these screens. The first-word difference correlates with the two displayed labels in this one save, but its general semantics and transition rules are not yet proven. In particular, the word should not be treated as a direct quality score or countdown without a controlled comparison. The Godot viewer shows the known type and troop quantities; the inspector also prints the raw state code.

## Nation identifiers and exact icon colors

Using the user's stated positional pairing, the colored icon strip and the name list yield this complete mapping. Color samples are from inside each icon, avoiding the black outlines and anti-aliased edges. Some nations share colors.

| Code | Nation | RGB hex |
| ---: | --- | --- |
| 0 | Rome | `#800080` |
| 1 | Carthage | `#FF0000` |
| 2 | Seleucid | `#808000` |
| 3 | Ptolemaic | `#000080` |
| 4 | Macedonia | `#FFFFFF` |
| 5 | Numidia | `#00FF00` |
| 6 | Gaul | `#800000` |
| 7 | Greece | `#00FFFF` |
| 8 | Celtiberia | `#FFFF00` |
| 9 | Illyria | `#000080` |
| 10 | Dacia | `#008000` |
| 11 | Bithynia | `#008080` |
| 12 | Galatia | `#0000FF` |
| 13 | Armenia | `#FF00FF` |
| 14 | Media | `#FF0000` |
| 15 | Thracia | `#808080` |

The new screenshots correct three provisional viewer colors: codes **8**, **13**, and **14**. The code-to-name order also agrees with the previously transcribed Nations menu, now with explicit numeric identifiers. The interface can name city controllers, city allegiance, and army owners instead of showing raw nation codes.

From the repository root, this command reproduces the Rome city and queue summary using only the local save:

```text
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-city saves/11_supply.sav Rome
```
