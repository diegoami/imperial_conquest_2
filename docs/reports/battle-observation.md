# Rome–Gaul battle: recorded behavior

This report describes a user-made battle recording and three screenshots associated with `7.sav`. The media and save stay in the local asset directory, outside Git. We inspected the video as data and in a video player; we did not run the game binary.

## Evidence

| Local file | Size | SHA-256 |
| --- | ---: | --- |
| `recordings/bandicam 2026-09-12 05-27-11-464.mp4` | 87,782,414 bytes | `f327d43aa892933f88d6c712c638b0aa719053a557bca0d0380dae20ca756162` |
| `screenshots/7.6.png` | 40,300 bytes | `0e4183c62dad2b6cfdba6724ebe26914abc14f723c3b464943ec23b9a9203174` |
| `screenshots/7.7.png` | 55,096 bytes | `74d586a29a28fd280574e0dd276bb77e92fbd19b2d04b7421fd8ff44ddedf0d0` |
| `screenshots/7.8.png` | 26,054 bytes | `54f09c0da45ece57204cdf818bffc4c4e2ec871ef4444147bc66175bba2c7cf5` |

The MP4 is H.264 video at 1920 × 1080 with AAC audio, duration 13:06.767. The screenshots show a 14 × 12 tactical grid at 32 screen pixels per tile. `7.6.png` says “Rome to place units”; `7.7.png` selects Rome's 1st Lancers Battalion (light cavalry, 900 troops, average quality, normal morale, 6 moves, 9 shots); and `7.8.png` says “Rome to move units.” These states precede the recorded battle's later events.

## Sampled recording states

Times are approximate video positions, not game-turn timestamps. They are spot checks rather than a complete frame-by-frame transcription.

| Time | Visible observation |
| --- | --- |
| 00:22 | Rome's 2nd Guards Battalion is heavy infantry with 5,099 troops, good quality, very high morale, 2 moves, and 0 shots. Its panel says it is set to attack the 2nd Foot Battalion, light infantry with 7,168 troops. |
| 04:02 | “Gaul to move units.” Ligurian mercenaries, light infantry with 2,590 troops, shoot at Rome's 5th Guards Battalion, heavy infantry with 3,682 troops. The panel reports 9 troop losses. |
| 06:03 | “Rome to move units.” The selected Gaul 1st Dragoons Battalion is heavy cavalry with 163 troops. |
| 08:04 | “Rome to move units.” Rome's 3rd Guards Battalion is heavy infantry with 5,857 troops, good quality, high morale, 2 moves, and 0 shots. |
| 10:04 | “Gaul to move units.” A 1st Lancers Battalion (light cavalry, 1,121 troops) attacks a 1st Dragoons Battalion (heavy cavalry, 1,103 troops). The panel reports attacker losses 154 and defender losses 134. |
| 13:00 | The result screen says Rome's army defeats Gaul's army. |

The visible labels establish placement, movement, attack, and shooting as distinct UI concepts. The recording also shows the initiative alternating between Rome and Gaul at sampled moments. A single attack or shot cannot establish damage formulas, action costs, or the complete phase order.

## Battle result

| Troop class | Rome start | Rome finish | Gaul start | Gaul finish |
| --- | ---: | ---: | ---: | ---: |
| Light infantry | 8,900 | 4,282 | 55,518 | 0 |
| Heavy infantry | 34,700 | 32,392 | 4,531 | 0 |
| Archers | 0 | 0 | 0 | 0 |
| Light cavalry | 2,400 | 914 | 1,298 | 0 |
| Heavy cavalry | 4,700 | 2,353 | 555 | 0 |
| **Total** | **50,700** | **39,941** | **61,902** | **0** |

Rome lost 10,759 troops by subtraction of the displayed totals. The result also says Rome captured **96 talents** and **619 tons of supplies**. `8.sav` later includes the news text “Rome destroys army of Gaul.” This supports the battle's strategic outcome but does not by itself identify the corresponding army records in the save.

## Next checks

1. Locate the two armies and their class totals in `7.sav` and `8.sav`; compare them with the result screen before naming record fields.
2. Find battle outcome and capture messages in the help/form resources and static executable references.
3. Collect controlled examples of one shot, one melee attack, and one movement order with before/after counts. Keep quality, morale, terrain, and unit type visible to test candidate rules.
