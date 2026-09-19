# Reading a screen recording: `/parse-recording`

A recording is evidence the save files cannot carry. A save is a **state**; a recording is the **transitions and the panels between two states** — the numbers the game prints in its own UI, the order it does things in, and the events it narrates. This document is the source of truth for the `/parse-recording` skill, the same relationship [evidence-pipeline.md](evidence-pipeline.md) has to `/process-evidence`.

## What changed, and why this document exists

`evidence-pipeline.md` used to say raw recordings with no note *"are lower priority and can wait until one is written."* **That is no longer true.** Writing session notes by hand is the most expensive part of producing evidence and the easiest to skip — three recordings on this machine went a week without notes, and were nearly excluded from the evidence releases on the grounds that nothing mapped them. They turned out to be cited in four reports.

So the input contract is now:

> **a recording, the saves either side of it, and rough timestamps.**

No written notes required. Timestamps are not a formality — they are what stops a three-hour video becoming 10,800 frames. *"Something happens around 3:20 and again near the end"* is enough.

This is not a new capability. [`battle-recording-melee-cap-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-recording-melee-cap-confirmed.md) was produced exactly this way from a pointer that read *"around 1:48, a few interactions"* — and yielded **six fully-specified combat exchanges** from under three minutes of footage.

## Prerequisites

| Prerequisite | Where it comes from |
| --- | --- |
| **ffmpeg** | `pip install imageio-ffmpeg` — a bundled binary, no system install and no PATH change. Get its path with `python -c "import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())"`. A system ffmpeg on PATH works equally well. |
| **Pillow** | Already present. Used only to downscale frames to a readable size. |
| **The recording** | Either the user's local `recordings/` folder (path from `assets.local.ini`) or a release asset — see [operating-guide.md §1.2](operating-guide.md#12-the-original-game-files). |
| **The saves either side** | Same. They are what the frames get correlated against; a recording read on its own produces observations, not findings. |

## The extraction recipe

Three steps, and the middle one is the one people get wrong.

**1. Extract at 1 frame per second, around the timestamp — never the whole file.**

```bash
FF=$(python -c "import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())")
"$FF" -v error -ss 200 -i "<recording>.mp4" -vf fps=1 -frames:v 60 frames/f_%03d.png
```

`-ss` before `-i` seeks first and is much faster. `-frames:v` bounds the output — always set it. One frame per second is the right default: the game is turn-based and its panels stay on screen for seconds at a time. Go to `fps=2` only for a tactical battle, where exchanges resolve quickly.

**2. Downscale before reading.** Frames are 1920×1080 and ~1.6 MB each. Reading them at full size wastes context for no gain.

```python
from PIL import Image
im = Image.open('frames/f_005.png')
im.thumbnail((1500, 1500))
im.save('frames/small_005.png')
```

**3. Read the thumbnails, not the frames.** Skim first, then go back at full resolution only for a panel whose digits are genuinely ambiguous.

**Work in the scratchpad**, never in a repository or the game directory. Frames are derived data; they are regenerated in seconds and must never be committed.

## What the panels are worth

Ranked by how much they settle per frame:

| Panel | Carries |
| --- | --- |
| **Combat resolution** | Both sides' troops, quality, morale and the result of one exchange — the densest frame in the game, and the only source for tactical constants |
| **Unit / army information** | Troops, quality, morale, moves, shots — the fields that map directly onto `ArmyRecord` |
| **City details** | Loyalty, fortification, population, supply — cross-checks `FUN_0044A98C`'s own inputs against the UI's labels |
| **News log** | The game's own words for an event, which is how a news literal gets confirmed rather than guessed |
| **Nation / diplomacy** | Relations, treasury, unity, tax |

The **news log is the most under-used**. It states in the game's own language what a save diff can only infer — *"falls to"* against *"defects from"* is the distinction T17's whole capture-versus-defection split rests on, and it came from a video frame.

## The rule that makes this evidence rather than anecdote

**A frame is an observation. A frame reconciled against the saves either side is a finding.**

Always state which it is. A number read off a panel is `[confirmed]` for *that moment*; a rule inferred from two panels is `[derived]` and must say so. If a frame disagrees with the save diff, **the disagreement is the finding** — do not quietly pick one. That is how `battle-quality-promotion-and-morale-array-decompiled.md`'s mislabelled field was caught.

Record the recording's filename and the frame timestamps in the report, so the next reader can re-extract the same frames. `-ss 200 … f_040.png` means 240 seconds in; say so rather than making them recompute it.

## Output

A report in the research repo, following its `[confirmed]` / `[derived]` / `[designed]` discipline, committed and pushed to `main` without asking. If the recording settles something a build-repo document currently claims otherwise, that is a **finding to report**, never a silent edit — it goes through the bug list ([build-process.md §4.6](build-process.md#46-bugs-and-follow-ups)), exactly as `/process-evidence` stage 2 requires.

## The actual skill file

`.claude/skills/parse-recording/SKILL.md` is a local, git-ignored install. If it is missing, recreate it verbatim from this block.

````markdown
---
name: parse-recording
description: Read a screen recording of the original game into research findings — frame extraction, panel reading, and correlation against the saves either side. Takes a recording, its save pair, and rough timestamps; needs no written session notes.
---

# /parse-recording [recording] [saves] [timestamps]

Full context: `docs/recording-analysis.md` in `imperial_conquest_2` — read it before starting. It has
the extraction recipe, what each panel is worth, and the observation-versus-finding rule.

## Input

A recording, the saves either side of it, and rough timestamps of interest. Any of the three may be
approximate or missing:

- **No timestamps** — ask for them before extracting. A full-length extraction is almost never the
  right move; say so rather than doing it.
- **No save pair** — the recording can still be read, but say plainly in the report that the findings
  are unreconciled observations.
- **A release tag instead of local paths** — fetch with
  `gh release download <tag> --repo diegoami/imp_conquest_original --dir <cache> --pattern "..."`.

## Steps

1. Locate ffmpeg (`python -c "import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())"`). If it
   is missing, `pip install imageio-ffmpeg` — a bundled binary, no system install.
2. Extract at `fps=1` around each timestamp, into the scratchpad. **Bound it with `-frames:v`.** Never
   extract a whole recording without saying why.
3. Downscale to ~1500px with Pillow and read the thumbnails. Go back to full resolution only for
   digits that are genuinely ambiguous.
4. Dump the saves either side with `IC2.Inspect --to-json` and diff them.
5. Reconcile: for every number read off a panel, say whether the save diff agrees. **A disagreement
   is the finding** — report it, never resolve it silently.
6. Write or update a research-repo report, `[confirmed]` / `[derived]` / `[designed]` tagged, naming
   the recording and the frame timestamps so the frames can be re-extracted. Commit and push to its
   `main` without asking.
7. Move the cited recording to `recordings-processed/` if it was local and unprocessed.
8. Report back: what was confirmed, what was only observed, what contradicted an existing claim, and
   anything that needs another look at the video.

## What not to do

- Do not commit frames anywhere. They are derived data, regenerated in seconds.
- Do not infer a rule from a single frame and tag it `[confirmed]`. One frame is one moment.
- Do not patch a build-repo document because a recording contradicts it — file it (build-process.md
  §4.6) and let the owning task fix it.
````
