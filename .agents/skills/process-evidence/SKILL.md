---
name: process-evidence
description: Process Imperial Conquest 2 saves, recordings or notes into research findings, or evaluate unevaluated research reports against this project's contracts. Use for requested evidence intake.
---

# Process IC2 evidence with Codex

Read `docs/codex.md` and `docs/evidence-pipeline.md`, including the fenced skill
under `## The actual skill file`. That document is the process source of truth.
Read `docs/recording-analysis.md` when a recording needs analysis.

Retain the sequence: prerequisites and report ranges; stage 1 research findings;
then stage 2 implications for this repository. Carry the exact stage 1 findings
and changed commits into stage 2. Evidence and provenance decide claims; missing
evidence is not permission to invent a value or confirmation.

The contract's Opus dispatches are explicit model choices. Codex collaboration
does not imply access to Opus: use the user's available chosen route, or report
the missing route and ask before changing it. Never run stage 2 ahead of stage 1.

Codex cannot assume Claude Remote Control is present. Without an explicitly
available cross-session messaging tool, prepare the contract's EXPLORE request
for the user to relay and state that it has not been sent. Continue independent
evidence items as the contract permits.

Keep original game files and private evidence outside this repository. Research
writes use the research checkout; implications here use their own review branch.
Report upstream defects through the bug lane rather than patching unrelated code.
Record `Evaluated: <file> @ <commit>` for the exact evaluated revision in the
contract's GitHub destination only when the invoked workflow authorizes that write.
