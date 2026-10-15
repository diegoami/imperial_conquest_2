---
name: parse-recording
description: Analyze a recording of the original Imperial Conquest 2 game using extracted frames and the saves on either side. Use for original-game recording evidence, with or without written notes.
---

# Parse an IC2 recording with Codex

Read `docs/codex.md` and `docs/recording-analysis.md`, including the fenced skill
under `## The actual skill file`. Follow its save alignment, timestamp selection,
dialog detection, coarse/fine extraction, panel-reading and output requirements.

Use ffmpeg and the inspector against configured evidence paths. Inspect extracted
frames with the available image-viewing tool. No written note is required; derive
timestamps as the contract describes. A frame alone does not prove a rule: correlate
the saves, distinguish observations from inferences and cite filenames/timestamps.

Keep recordings, saves and extracted frames out of version control. Put scratch
output under `rendered/` or the configured evidence workspace. Preserve the source
files. Do not invent a missing asset path, timing or game value.

If the source skill requires a research agent or Opus, retain that model choice
and use an available authorized route; report a missing capability before choosing
another model. Feed findings into `$process-evidence` only when evidence intake is
within the user's request; recording analysis alone does not dispatch build tasks.
