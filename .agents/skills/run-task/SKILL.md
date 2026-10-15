---
name: run-task
description: Run an Imperial Conquest 2 build task or triaged fix through implementation, independent review, rework and merge. Use for requested task execution, in the main session.
---

# Run an IC2 task with Codex

From the repository root, read `docs/codex.md` for the tool mapping. Read
`docs/build-process.md` section 4 and the skill contract in Appendix C (from
`## Appendix C:` to its closing markdown fence). Apply that contract with the
Codex mappings; do not use a git-ignored Claude skill as the source of truth.

Read the requested `docs/tasks/T<nn>.md` or triaged fix issue. Carry its complete
entry, exact Owns paths and necessary process excerpts into each brief. Preflight
the Owns list before implementation. Do not change scope to work around a blocker.

Retain user-selected models, isolated worktrees, independent model families,
running notes, full review findings, rework limits, machine claims and merge gates.
An OpenAI implementer passes `-ExcludeModel sol` to the existing external reviewer
and chooser scripts, so Luna and Sol cannot review its work. Use a named eligible
reviewer, not an implicit default. A self-check is not independent review.

When the user chooses a Codex subagent, use available collaboration tools with a
self-contained brief and verify its working directory. If those tools or the
chosen model are unavailable, report that constraint and obtain a new choice;
do not silently switch providers. Read a delegated run's final report before retrying.

Run the loop only for the requested tasks. Stop at escalation; retain the branch
and notes. Apply post-merge documentation claims only after the merge occurred.
