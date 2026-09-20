# Reading a screen recording: `/parse-recording`

A recording is evidence the save files cannot carry. A save is a **state**; a recording is the **transitions and the panels between two states** — the numbers the game prints in its own UI, the order it does things in, and the events it narrates. This document is the source of truth for the `/parse-recording` skill, the same relationship [evidence-pipeline.md](evidence-pipeline.md) has to `/process-evidence`.

## What changed, and why this document exists

`evidence-pipeline.md` used to say raw recordings with no note *"are lower priority and can wait until one is written."* **That is no longer true.** Writing session notes by hand is the most expensive part of producing evidence and the easiest to skip — three recordings on this machine went a week without notes, and were nearly excluded from the evidence releases on the grounds that nothing mapped them. They turned out to be cited in four reports.

So the input contract is:

> **a recording and the saves either side of it.** Timestamps help and are never refused, but on a run whose saves and recordings interleave, §1 derives them.

**The 2026-09-20 revision.** The Ptolemy run (`run-1-ptolemy`: 22 recordings, 45 saves, one unbroken game year) arrived with **no notes and no timestamps**, and it was read anyway — because file modification times align the two sets automatically. That run also showed that guessing a timestamp is the wrong move when a save diff can *name* the moment worth looking at. Both are now part of the recipe, as §1 and §2.

## Prerequisites

| Prerequisite | Where it comes from |
| --- | --- |
| **ffmpeg and ffprobe** | Already installed at `%LOCALAPPDATA%\ReTools\ffmpeg-master-latest-win64-gpl\bin\`, alongside Ghidra — see [operating-guide.md §1.3](operating-guide.md#13-local-toolchain-outside-both-repositories). **Do not `pip install imageio-ffmpeg`**; it was installed once by mistake and is not needed. |
| **Pillow** | Already present. Used to downscale frames and to build contact sheets. |
| **The recording** | Either the user's local `recordings/` folder (path from `assets.local.ini`) or a release asset — see [operating-guide.md §1.2](operating-guide.md#12-the-original-game-files). |
| **The saves either side** | Same. They are what the frames get correlated against; a recording read on its own produces observations, not findings. |

A release tag instead of local paths is fetched with:

```bash
gh release download <tag> --repo diegoami/imp_conquest_fixtures --dir <cache> --pattern "..."
```

## 1. Align the saves to the recordings before extracting anything

**A recording's modification time is when it stopped; its duration is how long it ran.** Those two give its start, and every save written inside that window is then placed to the second. This needs no notes, no timestamps, and no cooperation from the player.

```python
end   = datetime.fromtimestamp(os.path.getmtime(recording))
start = end - timedelta(seconds=ffprobe_duration(recording))
# any save whose mtime falls in [start, end] was written during this recording
```

Run this **first, over the whole run**, and print a table: recording, length, start, end, and the saves written during it. It costs one `ffprobe` per file and it turns an undifferentiated pile into a map. On the Ptolemy run it produced 22 rows in a few seconds and made the run's whole structure obvious:

- `IPnnn.sav` — turn *n*, **before** the player's orders
- `IPnnnB.sav` — the same week, **after** them
- `IP1 nnn.mp4` — that turn being played

**The `before`/`after` pair is the most valuable thing a run can contain.** It isolates the player's orders from everything the engine does between turns, which makes a delta attributable to one or the other. Check for such a pattern before assuming a run is just a sequence.

Two things this alignment will also tell you, and both matter more than they look:

- **Where the gaps are.** On the Ptolemy run every recording *stops* before the turn is ended, so the between-turn processing — quarterly economy, AI moves, the news log — is in none of the 22 videos. Better to know that in the first minute than after extracting 300 frames looking for it. When a run has this gap, **say so in the report and ask the player to record through the transition**.
- **Which recording to open at all.** Length and save density are a decent proxy for how much happened.

## 2. Let the save diff choose the timestamp

Do not scrub a video looking for something interesting. **Diff the saves either side first**, find the change worth explaining, then go to the video for the moment that explains it.

```bash
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --compare-saves <before>.sav <after>.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-city <save>.sav <City>
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-nation <save>.sav <Nation>
```

This inverts the old order and is strictly better: the diff tells you a number moved, and the frame tells you *what the game called it* while it moved. A frame found this way arrives already reconciled, which is the difference between an observation and a finding (§5). The Ptolemy run's strongest result — thirteen recruitment orders against a 13-point rise in mobilization — came from reading the diff first and the frame second.

`--compare-saves` reports raw byte offsets and is noisy; `--inspect-city` / `--inspect-nation` are semantic and are usually what you want.

## 3. Extract, coarse then fine

**Coarse pass — sample the whole recording cheaply.** For a recording of a few minutes, one frame every ten seconds is 20–30 frames, enough to find every dialog that opened:

```bash
FF="$LOCALAPPDATA/ReTools/ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe"
"$FF" -v error -i "<recording>.mp4" -vf "fps=1/10,scale=1280:-1" -frames:v 32 out/o_%02d.png
```

**Then build a contact sheet and read that**, rather than 30 separate images:

```python
sheet = Image.new('RGB', (cols * sw, rows * sh))
for i, im in enumerate(frames):
    sheet.paste(im.resize((sw, sh), Image.LANCZOS), ((i % cols) * sw, (i // cols) * sh))
```

One sheet costs a single read and shows which frames are worth full resolution. Panel *text* will not be legible at sheet scale — that is fine, it is not what the sheet is for. You are looking for *which frames have a dialog open*.

**Fine pass — full resolution, one frame at a time**, only at the timestamps the sheet or the save diff picked out:

```bash
"$FF" -v error -ss 180 -i "<recording>.mp4" -frames:v 1 hi/t180.png
```

`-ss` before `-i` seeks first and is much faster. `-frames:v` bounds the output — always set it. For a tactical battle, where exchanges resolve in under a second, go to `fps=2` over a bounded window instead of sampling.

**Work in the scratchpad**, never in a repository or the game directory. Frames are derived data; they are regenerated in seconds and must never be committed.

## 4. What the panels are worth

Ranked by how much they settle per frame:

| Panel | Carries |
| --- | --- |
| **Combat resolution** | Both sides' troops, quality, morale and the result of one exchange — the densest frame in the game, and the only source for tactical constants |
| **Army recruits** | The recruitment table **with readiness as words**, the per-unit initial and quarterly cost, and the `Mobilize` / `Disband` actions. Confirmed `quality = state / 4` at five states in one frame |
| **Unit / army information** | Troops, quality, morale, moves, shots — the fields that map directly onto `ArmyRecord` |
| **City details** | Loyalty, fortification, population, supply — cross-checks `FUN_0044A98C`'s own inputs against the UI's labels |
| **News log** | The game's own words for an event, which is how a news literal gets confirmed rather than guessed |
| **Nation / diplomacy** | Relations, treasury, unity, tax, leader name, mobilized percentage |
| **Any dialog with a live preview** | The tax dialog shows `New income` updating against `New tax` **before** committing. A pair of frames either side of a slider move gives two points on a function the saves can never show, because the save only ever holds the committed value |

Two under-used sources:

- **The news log** states in the game's own language what a save diff can only infer — *"falls to"* against *"defects from"* is the distinction T17's whole capture-versus-defection split rests on, and it came from a video frame.
- **The title bar.** It carries the acting nation and its leader, on every single frame. That is how `Cleopatra` and `Sennacherib` were recovered for a world export whose every `leaderName` reads `"(unassigned -- drawn at New Game)"`.

**A word ladder against a stored number is worth stopping for.** The game stores readiness as a state code and displays it as `not ready` / `very poor` / `poor`; it stores unity as a number and displays it as `normal`. Any frame showing a word where the save holds a number is a free point on a mapping — and several such rows in one panel is most of a ladder.

## 5. The rule that makes this evidence rather than anecdote

**A frame is an observation. A frame reconciled against the saves either side is a finding.**

Always state which it is. A number read off a panel is `[confirmed]` for *that moment*; a rule inferred from two panels is `[derived]` and must say so. If a frame disagrees with the save diff, **the disagreement is the finding** — do not quietly pick one. That is how `battle-quality-promotion-and-morale-array-decompiled.md`'s mislabelled field was caught, and the Ptolemy report carries an unresolved 3-talent treasury gap for the same reason.

**Prefer a reading that could have come out wrong.** Thirteen orders raising mobilization by exactly 13 confirms integer division *because* a real-valued division would have given 32 or 33 at that nation's wealth. The same observation on a small nation would have been consistent with both and settled nothing. When a frame offers a choice of which instance to check, **check the one where the candidate rules disagree**.

Record the recording's filename and the frame timestamps in the report, so the next reader can re-extract the same frames. `-ss 200 … f_040.png` means 240 seconds in; say so rather than making them recompute it.

## 6. Output

A report in the research repo, following its `[confirmed]` / `[derived]` / `[designed]` discipline, committed and pushed to `main` without asking. If the recording settles something a build-repo document currently claims otherwise, that is a **finding to report**, never a silent edit — it goes through the bug list ([build-process.md §4.6](build-process.md#46-bugs-and-follow-ups)), exactly as `/process-evidence` stage 2 requires.

State plainly **how much of the run was examined**. "Two of twenty-two recordings, four timestamps" is an honest header and stops the next reader assuming the run is exhausted.

## The actual skill file

`.claude/skills/parse-recording/SKILL.md` is a local, git-ignored install. If it is missing, recreate it verbatim from this block.

````markdown
---
name: parse-recording
description: Read a screen recording of the original game into research findings — frame extraction, panel reading, and correlation against the saves either side. Takes a recording and its save pair; derives its own timestamps and needs no written session notes.
---

# /parse-recording [recording] [saves] [timestamps]

Full context: `docs/recording-analysis.md` in `imperial_conquest_2` — read it before starting. It has
the alignment method, the extraction recipe, what each panel is worth, and the
observation-versus-finding rule.

## Input

A recording and the saves either side of it. Timestamps are welcome but **no longer required** — step
1 derives them.

- **No save pair** — the recording can still be read, but say plainly in the report that the findings
  are unreconciled observations.
- **A release tag instead of local paths** — fetch with
  `gh release download <tag> --repo diegoami/imp_conquest_fixtures --dir <cache> --pattern "..."`.
- **A whole run rather than one recording** — do step 1 over all of it before opening any video.

## Steps

1. **Align first.** For every recording, `start = mtime - ffprobe duration`; every save whose mtime
   falls inside that window belongs to it. Print the table. It reveals the run's structure (look for a
   before/after save pair per turn) and, just as usefully, **what is in the gaps between recordings**.
2. **Let the save diff pick the timestamp.** `IC2.Inspect --inspect-city` / `--inspect-nation` on the
   pair either side, and go to the video for the moment that explains the change. Do not scrub.
3. **Coarse pass**: `fps=1/10` scaled to 1280 across the recording, assembled into one contact sheet,
   read once to find which frames have a dialog open.
4. **Fine pass**: full resolution, `-ss <t> -frames:v 1`, only where step 2 or 3 pointed.
5. **Reconcile**: for every number read off a panel, say whether the save agrees. **A disagreement is
   the finding** — report it, never resolve it silently. Prefer instances where the candidate rules
   would give different answers.
6. Write or update a research-repo report, `[confirmed]` / `[derived]` / `[designed]` tagged, naming
   the recording and the frame timestamps so the frames can be re-extracted, and stating how much of
   the run was examined. Commit and push to its `main` without asking.
7. Move the cited recording to `recordings-processed/` if it was local and unprocessed.
8. Report back: what was confirmed, what was only observed, what contradicted an existing claim, and
   anything that needs another look at the video.

## What not to do

- Do not commit frames anywhere. They are derived data, regenerated in seconds.
- Do not infer a rule from a single frame and tag it `[confirmed]`. One frame is one moment.
- Do not `pip install imageio-ffmpeg`. ffmpeg and ffprobe are already at
  `%LOCALAPPDATA%\ReTools\ffmpeg-master-latest-win64-gpl\bin\`.
- Do not patch a build-repo document because a recording contradicts it — file it (build-process.md
  §4.6) and let the owning task fix it.
````
