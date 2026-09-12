# The melee 40%-cap formula, confirmed exactly against five new recorded exchanges

Following up on `decompiled-combat-formula-structure.md`'s open item ("simulate the recorded battle to check the formula"). The user pointed to a stretch of `recordings/bandicam 2026-09-12 05-27-11-464.mp4` starting around 1:48 with "a few interactions." Frame extraction (via a newly-installed portable `ffmpeg`, 1 frame/second) found **six clean, fully-specified combat resolution panels** — far better data than the two spot-checks in `battle-observation.md`, which was explicitly a partial sample ("spot checks rather than a complete frame-by-frame transcription").

## The six new exchanges

| Attacker | Atk troops | Defender | Def troops | Atk loss | Def loss |
| --- | ---: | --- | ---: | ---: | ---: |
| 1st Guards Battalion (heavy infantry) | 5,000 | Ligurian mercenaries (light infantry) | 4,318 | 9 | 1,728 |
| 1st Dragoons Battalion (heavy cavalry) | 1,557 | Insubre mercenaries (light infantry) | 6,214 | 68 | 2,155 |
| 3rd Guards Battalion (heavy infantry) | 5,900 | Insubre mercenaries (light infantry) | 7,168 | 42 | 2,868 |
| 6th Guards Battalion (heavy infantry) | 5,021 | Spanish mercenaries (heavy infantry) | 606 | 16 | 243 |
| 5th Guards Battalion (heavy infantry) | 3,725 | 1st Dragoons Battalion (heavy cavalry) | 373 | 21 | 150 |
| 2nd Lancers Battalion (light cavalry, shooting) | 1,500 | 1st Dragoons Battalion (heavy cavalry) | 411 | — | 38 |

(Unit quality wasn't shown in these compact combat-resolution panels; where the same battalion's full info panel was also captured nearby — 3rd Guards and 6th Guards, both "good" — quality is known, otherwise unconfirmed.)

## The defender-loss cap, confirmed exactly

`decompiled-combat-formula-structure.md` found the melee formula caps each side's loss at `floor(troops × 4/10) + 1` — read from pseudocode, never checked against a real number. Checking all five melee exchanges:

| Defender troops | Observed defender loss | `floor(0.4 × troops) + 1` | Match |
| ---: | ---: | ---: | --- |
| 4,318 | 1,728 | 1,728 | **exact** |
| 6,214 | 2,155 | 2,486 | below cap |
| 7,168 | 2,868 | 2,868 | **exact** |
| 606 | 243 | 243 | **exact** |
| 373 | 150 | 150 | **exact** |

**Four of five land exactly on the formula, down to the `+1`.** The one exception (6,214 troops, loss 2,155, cap 2,486) is exactly what the formula predicts should happen when the attacker is too weak to force the cap: that exchange has by far the weakest attacker relative to its defender (1,557 heavy cavalry vs. 6,214 light infantry — a much larger defender pool than any other exchange), so the raw random-roll term came in under the ceiling instead of hitting it. This is the strongest, most precise confirmation of any combat constant so far in this project — not just "the shape matches," but the literal formula reproduced to the exact integer four separate times.

Attacker losses in every exchange are far below their own 40% caps (all under 5% of attacker troops), consistent with a strong unit type (heavy infantry/cavalry) attacking a weaker one (light infantry, or a much smaller heavy-cavalry remnant) — the raw formula output simply doesn't get close to the ceiling on the winning side.

## What this does not establish

- The un-capped raw formula's exact output (only the cases hitting the cap are pinned exactly; the one sub-cap case, and every attacker-side loss, are still just "plausible," not independently re-derived from the random-roll term).
- Unit quality for four of the six exchanges (not shown in the compact panel, not independently confirmed from another frame).
- The attacker/defender axis orientation of the 5×5 effectiveness matrix — every attacker here was fighting a weaker unit type, so this data doesn't distinguish between the matrix's two possible readings the way a closer, more symmetric matchup would.

## Reproduction

```text
ffmpeg -ss 108 -i "recordings/bandicam 2026-09-12 05-27-11-464.mp4" -vf fps=1 -frames:v 40 frames/f_%03d.png
ffmpeg -ss 148 -i "recordings/bandicam 2026-09-12 05-27-11-464.mp4" -vf fps=1 -frames:v 120 frames2/f_%03d.png
```
Frames `frames/f_040.png` (the shot), `frames2/f_025.png`, `f_035.png`, `f_030.png`, `f_045.png`, `f_050.png` (the five melees) show the resolution panels directly.

## Next checks

1. The user offered to record more battles — a controlled recording with quality visible for both sides on a closer, more evenly-matched fight (not one side heavily favored) would be the best next target: it would exercise the un-capped formula branch and could resolve the matrix orientation question this data couldn't.
2. Re-derive the raw (pre-cap) formula output for the one sub-cap exchange to check the random-roll term itself, not just the cap.
3. Extract more frames across the rest of this same recording to build a larger exchange dataset for free, since this stretch alone yielded six usable data points from under three minutes of footage.
