# Imperial Conquest 2

Cloud-first. The previous, desktop-only process (worktrees, delegated OpenCode runs, mandatory
cross-family review) is frozen on the `local` branch and is **not** in force here. Read
[`docs/cloud-mode.md`](docs/cloud-mode.md) first: it is the whole process on one page. Then
[`docs/operating-guide.md`](docs/operating-guide.md) for where things live.

## Rules that must never be forgotten

1. **Work in the checkout you are given.** No git worktrees, no second clones. Branch, commit, push, open a PR.
2. **Review is optional and belongs to the harness.** Use the default harness (`/code-review`, a subagent, or none) when it helps. No required reviewer family, fresh checkout, worktree, or posted verdict. Merge needs green CI; nothing else.
3. **Milestones get one big review, by another model.** The session's job ends at the freeze: claims in the plan, the `review/` and `review-base/` branches, and the review PR whose description is the brief. Never run the review yourself, start a reviewer CLI or `scripts/review-milestone.sh`, or post a verdict; the owner hands the PR to the other model ([docs/milestone-review.md](docs/milestone-review.md)).
4. **Status lives on GitHub labels only.** No document carries a status snapshot.
5. **Everything we produce stays reachable**: small files in the repo, big ones in a release or the Drive location ([docs/artifacts.md](docs/artifacts.md)). Original-game files, and anything derived from them, never enter the repo or a release (copyright).
6. **A question is not a request to edit files.** Answer it; propose a fix and wait.
7. **Upstream defects go through the bug list** (file an issue; do not patch another task's files).
8. **Research-repo work** (`imperial-conquest-2-research`): commit and push without asking.
9. **At session start**, check the triage queue (`gh issue list --label triage:needed --state open`; if GraphQL is blocked, the REST form in [docs/cloud-mode.md](docs/cloud-mode.md#github-from-the-cloud-rest-only)) and anything `status:in-progress`, and tell the user where things stand.
10. **Use the manual before GUI work.** Read the relevant original-game manual sections and screenshots through [the operating-guide entry point](docs/operating-guide.md#original-game-ui-reference-private). Cite the expected behavior and any specifically approved exception; do not infer permission for a different screen from a narrow convenience change. Find and record discrepancies proactively before asking the owner to rediscover them through playtesting.
11. **Author hills and passes deliberately.** For authored historical/random maps, reserve mountains for severe high relief, use forest terrain as the hill/foothill metaphor, and create usable plain passes through ranges. Follow [the owner's terrain-authoring rule](docs/design/terrain-authoring.md); correct tile placement rather than changing movement penalties.

## Task entry points (v0.11 shipped; the rest under #1808/#1809, milestone v0.12.0)

Shipped in v0.11.0: New Game setup, independent panels, campaign guidance, product naming (T291), Near East terrain revision 2 (#1822; the original worlds are frozen for old saves, new games use the `-r2` worlds, see `docs/design/near-east-passes.md`), the Diadochi campaigns (#1837; brief `docs/design/diadochi-campaigns.md`, authoring in `tools/Diadochi/`) and the observer pilot. Contracts with the owner's 2026-11-09 decisions: `docs/tasks/T292.md` (original assets), `T293.md` (eleven campaigns), `T294.md` (random maps); sub-issues #1837-#1853. Design context: `docs/design/v011-campaigns-and-random-maps.md`. **Resume after #1841 (China campaigns, T293.5):** the draft brief is on main ([docs/design/china-campaigns.md](docs/design/china-campaigns.md), PR #1892): a new 208x220 frame at 8x10, 15 regular seats at c.350 with a deliberate labelled reduction to 11 at 230, default seats Wei/Qin, Qin-Wei and Qin-Han opening wars, 20-year horizons, troop labels Crossbowmen/Halberdiers/Skirmishers/Light horse/Chariots. **Approved as drafted 2026-11-17; authoring (the India pipeline) is under way on the T293.5 branch.** Then the random-map chain (#1842-#1848, T294a-g). India (#1840) was delivered on main via PR #1891. Still awaiting the owner: the #1826 private fixture-repo write go, playthroughs of the new openings (Mediterranean, India), and the owner's decision on the classical world's Seleucid dominance if a Rome-first classical campaign matters. Read GitHub labels, not this file, for status.

Observer delivery is T285/T286/T287, [PR 1829](https://github.com/diegoami/imperial_conquest_2/pull/1829)
and `docs/observer-recordings.md`. The owner authorized **one
YouTube video first only**, on lynxsupercluster in a separate Open Imperial Conquest playlist;
Pages uses excerpts linked to that full campaign and to the repository/releases. No recurring
uploads before pilot review. Keep ai-player's GeoClick queues and uploader records untouched.
The recording guide and `artifacts/observer/v0.11/full-campaign-verification.json` locate the
verified full MP4 and private archive. The pilot is **done**: uploaded (receipt `artifacts/observer/v0.11/youtube-receipt.json`), public excerpt on the
`observer-media` release, Pages live at https://diegoami.github.io/imperial_conquest_2/ and checked in Chromium and
Firefox (`browser-check.json`). No recurring upload before the owner reviews the pilot; never create a second upload to resolve an uncertain upload identity.

## Token economy

- Never read a large file whole: `docs/task-catalogue.md` is an index (a task is `docs/tasks/T<nn>.md`); use `jq` on `data/worlds/*.json` and `data/rulesets/*.json` (`jq 'del(.cities)'`); never open `*.terrain.b64` or `tests/fixtures/corpus.json`; for the long design docs `grep -n` a heading, then `sed -n`.
- Scope searches (`grep -rn x src tests`), never `.`. Skip `bin/ obj/ .godot/ rendered/ screenshots/ recordings/ assets/packs/`.
- Quiet terminal: `dotnet build|test IC2.sln --nologo -v q`; report counts and the failing assertion with file:line.
- Minimal diffs; do not paste files back.
