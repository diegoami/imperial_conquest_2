# Later saves and screenshot cross-checks

The local asset directory contains `saves/1.sav`, `4.sav` through `11.sav`, `11_supply.sav`, and eight screenshots named `screenshots/7.1.png` through `7.8.png`. The user's convention is that `N.x.png` depicts `N.sav`. Screenshots and saves remain outside this public repository. This analysis read the files as data; it did not run or modify the original game. `7.6.png`–`7.8.png` and the associated video are covered in the [battle observation](battle-observation.md); the second recording and `8.sav`–`10.sav` are covered in the [strategic report](strategic-recording-and-summer-saves.md). The [controlled supply report](controlled-army-supply-transfer.md) compares the two `11` saves.

| Save | Bytes | SHA-256 | Last visible week label | Final 55-byte trailer `+40` |
| --- | ---: | --- | ---: | ---: |
| `1.sav` | 131,313 | `d20971d5c73a39c82bb9a6c76413394d1d6fc8174bf5ae857a8161eaba5d7c10` | 1 | 1 |
| `4.sav` | 131,496 | `e1d5f0489f4f24544a70af8c26b14a0d2d14e3acba20a614a0ae79da412d0b32` | 3 | 3 |
| `5.sav` | 130,077 | `3581c50662719bf9dee3682aff1001cd6f00cb0a228c3df717c39b3e8cb8b4bf` | 7 | 7 |
| `6.sav` | 130,077 | `ecc3008e2d6f69118d42e52e0b503710111b56c951a667198e71c0287d50c321` | 9 | 9 |
| `7.sav` | 129,421 | `733a16aa7ee5aeacbca9e689b0d5465c277af5402683860df0e8dfb8fde2c93b` | 11 | 11 |
| `8.sav` | 128,765 | `e45d3b17fd4b7b95d74a3a2919ec2dd4089f708a60318fb0ca24698db0c78bf1` | 1 Summer | 1 |
| `9.sav` | 128,765 | `2171344c4cbef87f90944d02c873a21d507475e8dbefa6f4cac7cb27bb069e25` | 3 Summer | 3 |
| `10.sav` | 129,421 | `2d42dc110a3f2cd8058c4e7d6626c299fca17c4885ee4bb058767d2dd6d917b3` | 5 Summer | 5 |
| `11.sav` | 129,421 | `e21ce740295c51f55758c26776e6e485f0a7e7e1106bc133d2f0bf0c0073f77d` | 7 Summer | 7 |
| `11_supply.sav` | 129,421 | `7608ea62372f8dacba3af38698e7462b111b96e488062ab2c3479ca34128d6f0` | 7 Summer | 7 |

The final 55 bytes align across all ten saves. Against `1.sav`, `4.sav` differs there only at `+40`; `5.sav` through `7.sav` differ at `+34` (`0 → 1`) as well as `+40`. The exact `+40` match across the saves strongly identifies it as the displayed week value, at least while it fits in one byte. `7.sav` to `8.sav` changes the displayed label from Week 11 Spring to Week 1 Summer while `+40` resets from 11 to 1 and `+44` changes `0 → 1`. Across all saves, `+44` is 0 for Spring and 1 for Summer, strongly supporting a season index. The role of `+34`, later season values, and year storage remain unknown. The observed labels advance two weeks between adjacent later saves, but that does not establish a universal turn length. There is no Week 5 Spring file in this set; `4.sav` to `5.sav` skips from displayed Week 3 to Week 7. Save lengths are not monotonic, so post-city sections cannot be compared by fixed absolute offsets without first locating their boundaries.

The shared city table is stable in position. Between `4.sav` and `5.sav`, Tarquinii and Ariminum have city word `+18` change `0 → 6`; between `5.sav` and `6.sav`, Caere has the same change. The game's news and `7.x.png` information panel say these Rome cities fell to Gaul. Alongside the earlier Sidon `3 → 2` capture from Ptolemaic to Seleucid, this strongly supports city word `+18` as an owner code, with `0 = Rome`, `6 = Gaul`, `3 = Ptolemaic`, and `2 = Seleucid`. These code labels are evidence-based but still need cross-checking for all nations. The widespread city word `+24` updates persist: 333 records change from `4.sav` to `5.sav`, 330 from `5.sav` to `6.sav`, and 329 from `6.sav` to `7.sav`. A later [controlled supply transfer](controlled-army-supply-transfer.md) identifies it as city supplies; its turn-by-turn production/consumption rules remain unknown. In `9.sav`→`10.sav`, Tarquinii, Caere, and Ariminum reverse owner word `+18` from `6 → 0`. Two embedded news entries describe Tarquinii and Ariminum as defecting from Gaul to Rome; no equally clear Caere entry was identified in the inspected news tail.

## Screenshot registration and terrain values

Each `7.x.png` view contains a tiled main map. Sampling 67 × 38 visible tiles at 32 screen pixels each and matching the sea/land pattern against the column-major DAT grid identifies these world-coordinate origins:

| Screenshot | Top-left map cell `(x, y)` | Sea/land matches among 2,546 sampled tiles |
| --- | ---: | ---: |
| `7.1.png` | `(87, 29)` | 2,539 |
| `7.2.png` | `(54, 72)` | 2,545 |
| `7.3.png` | `(157, 83)` | 2,532 |
| `7.4.png` | `(113, 23)` | 2,538 |
| `7.5.png` | `(191, 43)` | 2,538 |

The near-exact alignment independently validates the 320 × 140 dimensions, column-major storage, and screenshot coordinate direction. A few mismatches can be caused by city/unit icons, darkened tiles, and image sampling; they are not evidence of a different layout.

Comparing the dominant screen color for tiles at these registered coordinates gives a robust initial mapping:

| DAT cell value | Original screen color | Interpretation | Direct sample |
| ---: | --- | --- | --- |
| `0` | Blue `#0000ff` | Sea/water | 812 of 837 visible value-0 tiles in `7.2.png` had exact blue as their dominant color; 22 more were dark blue |
| `2` | Green `#00ff00` | Open green land | 593 of 609 in `7.2.png` |
| `3` | Yellow `#ffff00` | Desert/sand | 740 of 751 in `7.2.png` |
| `4` | Dark green `#008000` | Forest | 403 of 407 in `7.4.png` |
| `5` | Gray `#808080` | Mountains | 293 of 297 in `7.2.png` |

The interpretation names come from the visible tile art as well as color. Later [river and marker analysis](rivers-and-map-markers.md) identifies values `6`–`11` as six river shapes: their green tile background can be dominant even though every sampled tile contains dark-blue river pixels. Values `20`–`199` coincide exactly with city positions, and sparse `200`–`299` values plus observed `333`/`335` align with candidate army and fleet icons. Their deeper encodings remain to be recovered. The repository viewer uses approximate terrain colors and new vector markers, not original art.

The screenshots' information panel visibly reaches `Week 11 Spring 270BC`, matching the week label and trailer byte in `7.sav`. This validates the user's screenshot/save pairing for this set, though the screenshots may have been captured minutes after the save and are not a binary snapshot of every game action.

## Next checks

1. Check water value `1` and the meaning of city and unit marker codes against further screenshots, help descriptions, and map art.
2. Locate the start and count of the news list and dynamic post-city records, which shift between later saves.
3. Use one-city screenshots or controlled captures to confirm the full owner-code table and explain how city supplies at `+24` change during a turn.
