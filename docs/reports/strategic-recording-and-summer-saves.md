# Strategic-map recording and saves 8–10

The user supplied a 1:43.138 strategic-map recording and `8.sav`, `9.sav`, and `10.sav`. We viewed the recording in a video player and inspected the saves as data. We did not run the original game binary or modify the source files. The recording and saves are local reference material and are not committed to this public repository.

## Evidence and scope

| Local file | Bytes | SHA-256 | Latest visible save label |
| --- | ---: | --- | --- |
| `recordings/bandicam 2026-09-12 05-42-33-198.mp4` | 49,867,635 | `e1ae6b3527261810501c228afb3b8cf5a5d28a1e72bea800951664cd2d01db40` | — |
| `saves/8.sav` | 128,765 | `e45d3b17fd4b7b95d74a3a2919ec2dd4089f708a60318fb0ca24698db0c78bf1` | Week 1 Summer 270 BC |
| `saves/9.sav` | 128,765 | `2171344c4cbef87f90944d02c873a21d507475e8dbefa6f4cac7cb27bb069e25` | Week 3 Summer 270 BC |
| `saves/10.sav` | 129,421 | `2d42dc110a3f2cd8058c4e7d6626c299fca17c4885ee4bb058767d2dd6d917b3` | Week 5 Summer 270 BC |

The recording is H.264 at 1920 × 1080 with AAC audio. Its sampled frames show the strategic map around Italy and the Mediterranean, with land/water tiles, cities, and a left information/news pane. Around 00:20 the left pane displays details for a selected map object; around 00:40 a menu is open; around 01:00 a small numeric city-management dialog is open; and around 01:40 a Windows save dialog is open. The preview scale does not support reliable transcription of the small labels or numeric values in those panels. These observations confirm UI workflow, not the semantics of an unknown save field. The 8→10 chronology is established by the saves' embedded calendar labels; the recording does not provide a verified exact timestamp for each save.

## Calendar and world changes

The previous `7.sav` ends at **Week 11 Spring 270 BC**. The sequence then reads **Week 1 Summer**, **Week 3 Summer**, and **Week 5 Summer**. The byte at `+40` of the aligned final 55-byte save trailer reads `11, 1, 3, 5` respectively, matching the displayed week within a season. It is therefore a week-of-season candidate, not a monotonically increasing campaign-turn counter. Trailer `+44` is `0` in all five Spring saves and `1` in all three Summer saves; it is a strong candidate for the season index (`0 = Spring`, `1 = Summer`). Between `7.sav` and `8.sav`, these are the only two bytes in the aligned trailer that change. Autumn/Winter values and year storage remain unverified.

| Comparison | Changed map cells | Changed city records | City word `+24` changes | City owner word `+18` |
| --- | ---: | ---: | ---: | --- |
| `7.sav` → `8.sav` | 124 | 307 | 279 | None |
| `8.sav` → `9.sav` | 70 | 251 | 250 | None |
| `9.sav` → `10.sav` | 73 | 78 | 77 | Tarquinii, Caere, Ariminum: `6 → 0` |

Map changes include 115 cells changing `1 → 0` in 7→8, 60 changing `0 → 1` in 8→9, and 60 changing `1 → 0` in 9→10. These recurrent transitions may reflect dynamic map state, but their meaning is unresolved. The season boundary also changes city bytes `+22` in 83 records and `+28` in 94 records, a pattern absent at that scale in the next two comparisons. This makes a season-linked update plausible, not proven.

The previously established owner codes are `0 = Rome` and `6 = Gaul`. By `10.sav`, Tarquinii, Caere, and Ariminum all change from Gaul to Rome. The embedded news explicitly says “Tarquinii defects from Gaul to Rome.” and “Ariminum defects from Gaul to Rome.” No equally clear Caere message was identified in the inspected tail of the news list. This shows at least two ownership changes were reported as defections; it would be premature to call all three captures or assume identical causes.

`8.sav` contains “Rome destroys army of Gaul.” following the recorded Rome–Gaul battle; see the [battle observation](battle-observation.md). The exact army record and post-battle state change are still to be located.

## Next checks

1. Locate the city-management action shown around 01:00 in the Delphi form stream and help topics. Match a controlled before/after save pair before assigning its dialog values to city bytes.
2. Verify the candidate season byte `+44` with a future Summer→Autumn sample and locate the year field with a year-boundary sample.
3. Locate the army records and news slots in post-city save data, especially the Gaul army removed after battle and the two explicitly reported defections.
4. Retain local saves immediately before and after one strategic action to separate routine turn processing from the action's own bytes.
