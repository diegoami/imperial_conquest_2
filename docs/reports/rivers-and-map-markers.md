# Rivers, cities, and unit markers in the world grid

This analysis reads the full-version DAT, `7.sav`, and screenshots `7.1.png`–`7.5.png` as data. It does not execute the original game. The five screenshots have previously been registered to the 320 × 140 grid; their map origins and alignment counts are in [saves and screenshots](saves-and-screenshots.md).

## River tiles

Across the five registered screenshots, every sampled tile with a save map value of `6`–`11` contains dark-blue pixels (`260/260` tiles), while none of the `703` sampled ordinary forest tiles (`4`) does. The original river color is approximately `#000080`. Values `6`–`11` are therefore river shapes, not six kinds of forest. The dominant tile color can still be green when a narrow river bends along one side.

Neighboring river cells in the initial DAT identify the shapes:

| Value | Connected sides | Most common matching-neighbor pattern | Count with that pattern / total |
| ---: | --- | --- | ---: |
| `6` | east–west | `EW` | 335 / 362 |
| `7` | north–south | `NS` | 229 / 254 |
| `8` | north–east | `NE` | 164 / 165 |
| `9` | east–south | `ES` | 92 / 94 |
| `10` | south–west | `SW` | 165 / 167 |
| `11` | north–west | `NW` | 96 / 100 |

The exceptions are mostly map-edge or endpoint cells and a few intersections with other markers. Together these are **1,142 river cells**. The Godot viewer now paints a dark-blue line through each shape over green terrain. The save value `1`, which appears on 115 cells in `7.sav` after replacing initial water value `0`, also appears dark blue in the visible screenshot sample; its precise role remains unknown, so the viewer treats it only as a darker water variant.

## City and unit overlays

In both the initial DAT and `7.sav`, exactly **334 cells** have values from `20` through `199`, and all 334 coincide with the parsed city coordinates. These values are city map markers. More specifically, every `7.sav` city satisfies `map code = 20 + owner code + 16 × variant`, with variants 0–4. The owner code is the separate city-record word at `+18`; its relation to the map code holds for **334/334 cities**. The variant's meaning (city size, icon, or another display category) still needs confirmation.

Sampling the corners of registered city tiles gives the following screenshot colors by owner code: `0` purple `#800080`, `1` red `#FF0000`, `2` olive `#808000`, `3` navy `#000080`, `4` white, `5` lime, `6` maroon, `7` aqua, `9` navy, `10` green, `11` teal, `12` blue, and `15` gray. [Later screenshots](rome-city-recruitment-and-nations.md) provide the complete code-to-nation and icon-color mapping, including `8` yellow, `13` magenta, and `14` red.

The remaining sparse high-valued cells are consistent with unit overlays:

| Input | Values `200`–`299` | Codes `333` and `335` | Interpretation |
| --- | ---: | ---: | --- |
| Initial DAT | 15 | 2 | Candidate armies and fleets |
| `7.sav` | 10 | 2 | Candidate armies and fleets |

The observed high codes `333` and `335` are at water cells; for example, `7.sav` has code `333` at `(69, 79)`, where `7.2.png` shows a boat icon. Codes `200`–`299` are on land or river; for example, `7.sav` has code `232` at `(102, 44)` near Rome, where the registered `7.1.png` view shows an army icon. The army codes fit `200 + owner code + 16 × variant`: code `232` implies owner `0` (Rome), and adjacent code `222` implies owner `6` (Gaul). Later save-record matching confirms the owner residue for visible army markers. The two fleet codes fit `332 + owner code`, giving owner `1` for code `333` and owner `3` for code `335`, consistent with their western and Egyptian locations, but the fleet-owner formula still needs record-level confirmation. The map code is not a unique entity ID; two army cells share code `201` in `7.sav`. Army troop counts are now decoded from save records; orders remain unknown.

The Godot viewer keeps original data outside Git, offers the initial DAT and locally available saves in a source selector, and draws original-coordinate city, army, fleet, and river overlays using new code-native shapes. It does not copy original sprites or screenshots into the repository. A later [army-table analysis](army-records-and-roman-roster.md) matches save army records to 106 of 108 army-coded map cells across the available saves; the two exceptions share cells with fleet icons. It also confirms that the owner residue is the army record's owner code in the directly matched cases.

## Next checks

1. Correlate fleet markers with their post-city records and screenshot detail panels; the army table and Roman roster are now located.
2. Verify the meaning of water value `1` and how overlapping army and fleet entities are represented.
3. Confirm the city-marker variant meanings and compare exact icon shapes with original resources before calling the displayed icons compatible.
