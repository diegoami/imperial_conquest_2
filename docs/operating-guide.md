# Operating guide

Where things live. The process is [cloud-mode.md](cloud-mode.md); conventions are [build-process.md](build-process.md).
The desktop-era guide (OpenCode setup, reviewer routing, watch monitors, standing model choices) is on the `local` branch.
Living reference (open questions, caveats, incidents) is in the [wiki](https://github.com/diegoami/imperial_conquest_2/wiki).

```bash
gh issue list --label task --label status:ready      # what can run next
gh issue list --label triage:needed --state open     # untriaged bugs and follow-ups
```

## Repositories

- **This repository** (`diegoami/imperial_conquest_2`): design, plans and all code (`IC2.Data`, `IC2.Inspect`, `IC2.Engine`, `IC2.Cli`, `godot/`).
- **Research** (`diegoami/imperial-conquest-2-research`): reverse-engineering reports (`docs/reports/`), roadmap, decompilation plan. `git pull --ff-only` before writing; commit and push without asking.
- **Bot** (`diegoami/ic2-conquest`): plays the original headless under Wine; its results arrive only as research reports.
- **Fixtures** (`diegoami/ic2-test-fixtures`, private): the whole save corpus plus the DAT, for CI via `IC2_FIXTURES_DIR`. Evidence releases: `diegoami/imp_conquest_fixtures`.

## Documents

| Document | What it is |
| --- | --- |
| [cloud-mode.md](cloud-mode.md), [build-process.md](build-process.md) | Process and conventions |
| [milestone-review.md](milestone-review.md), [release-plan.md](release-plan.md) | Milestone review, versions, release checklist |
| [artifacts.md](artifacts.md), [packaging.md](packaging.md), [environment.md](environment.md) | Artifacts, packaging, environment |
| [game-design.md](game-design.md), [design-audit.md](design-audit.md) | What is built; what the evidence supports |
| [task-catalogue.md](task-catalogue.md), [tasks/](tasks/) | Open tasks and their contracts |
| [evidence-pipeline.md](evidence-pipeline.md), [recording-analysis.md](recording-analysis.md), [investigations/](investigations/README.md) | Evidence processing and write-ups |

## Original-game UI reference (private)

The research-authored **original Imperial Conquest 2 v1.01 user manual** is the reference for original UI workflows. Its October 2026 source is `diegoami/ic2-conquest#133`; the private folder's `README.txt` identifies the edition and contents. Consult the relevant sections **and screenshots before GUI implementation or audit**. Later owner decisions, measured research corrections and deliberate Faithful/Improved policies must be cited specifically when they differ from the manual; a code comment or a narrow exception is not approval for a generally different screen.

The complete reference folder contains `manual.html` (self-contained), `manual.pdf`, `chapters/` with Markdown and used screenshots, `source/`, and `all-screenshots/`. On the desktop it is synced at `G:\My Drive\IC2\manual`. A private working copy belongs under the git-ignored `rendered/manual-reference/`:

```powershell
rclone copy "gdrive:IC2/manual" "rendered/manual-reference" --create-empty-src-dirs
rclone check "gdrive:IC2/manual" "rendered/manual-reference" --one-way
```

Verify the authenticated remote/root in each environment. Copy the whole folder so supporting material remains available; do not use a deleting sync. The [authenticated Drive HTML reference](https://drive.google.com/file/d/1P72gUKdGyE4kn7cM9tyUSHF5zX49uyap/view) is a fallback entry point. Preserve sharing permissions. The manual and original-game screenshots remain private and must never be committed or attached to public issues/releases.

For recruitment, start at `manual.html#recruiting-regular-units` (§5.3), or `chapters/05-cities.md`. The [manual-to-UI audit contract (#1551)](https://github.com/diegoami/imperial_conquest_2/issues/1551) defines the evidence table: manual section and expectation, current file/line and reproduction, preset/context, exact decision exception, priority and follow-up. Record source revisions and distinguish a verified match, unsupported difference, approved exception and unresolved evidence conflict. Do not rewrite the manual to fit the implementation. Tests that restate current code do not establish fidelity, and headless checks alone do not establish visual acceptance. Do the discovery first; reserve owner playtesting for the remaining acceptance.

The dated [manual-to-UI audit](investigations/manual-ui-reference-audit.md) maps the v0.8.3 baseline to concrete follow-ups, known gaps and specifically scoped exceptions. Use its evidence and limitations alongside the manual; implementation status remains on the linked GitHub issues.

## Original game files (never in a repo or release)

Configured by `assets.local.ini` (git-ignored, copied from `assets.example.ini`); absent in the cloud. Tests name fixtures by
bare filename and find them in `saves-processed/`, `saves/`, `releases/<tag>/`, or `IC2_FIXTURES_DIR`. Reports cite bare filenames;
the research repo's `docs/evidence-index.md` maps each to its release. Ask the user to configure the path; never guess it.

## Toolchain

.NET 10 SDK, `gh`, `jq`, Python 3. Godot 4.7.2 mono (console build) for `godot/`:
`godot --headless --path godot --quit-after 2 [res://Slice/Slice.tscn]`; headless cannot take screenshots, and opening the editor
dirties `godot/project.godot` (`pwsh scripts/check-godot-churn.ps1` reverts benign churn). Packaging needs the 4.7.2 mono export
templates ([packaging.md](packaging.md)); the installer needs Inno Setup 6.

For the later [T253/T254 evidence](investigations/original-ai-control-and-support.md#comparison-after-t253-and-positive-move-t254), keep zero-budget rendezvous planning separate from army movement, adjacent embark, carried landing and negative budgets. The measured human field-battle lifecycle retains a partial seat and its fleet pass before replaying the same seat; native Tactical pre-turn replay differs, while the approved Instant exception remains. Treaty timing is a separate AI–AI battle finding. These evidence updates change no owner-deferred Windows acceptance or release allocation.

## Working rules

- A question is not a request to edit files. Answer it, propose a fix, wait.
- Upstream defects go through the bug list; design decisions are the user's.
- A rule question this repository needs answered from the original (a decompile, a live run) is delegated as an issue in [`diegoami/imperial-conquest-2-research`](https://github.com/diegoami/imperial-conquest-2-research/issues), linked from the issue it blocks; it is not investigated here.
- No status snapshots in documents. In reverse-engineering work, separate observation from inference and cite the exact file, hash or address.
- Keep the user informed in short updates; link every issue, PR and file you name.

For strategic-AI comparisons, use the [decision evidence and limits](investigations/original-ai-control-and-support.md#army-decision-comparison-after-t248). Distinguish matching branch inputs from whole-turn outcomes and the older overlapping categories from the bounded T249 attribution. The owner's 2026-10-31 decision supersedes retaining #925 tiers in Faithful: reachable resupply/garrison/emergency belongs to Improved only; Faithful uses original destinations and the original no-progress swing/stay-put walk, with no emergency. The distance-1 army-contact tail attacks from an adjacent cell; it does not establish occupied-cell entry. Open work and release allocation remain on the linked issues, not an operating-guide status list.

For SAV export, the [current compatibility evidence](game-design.md#original-save-compatibility--confirmed-format-designed-policy) separates canonical native-state encoding from identity preservation. The October 31 callback, lifecycle and canary studies are bounded static/emulation evidence, with no original save written or Windows run. Preserve the existing export gates and named #1191/#1193–#1195 ownership; GUI indirect calls, unexercised state paths and native acceptance remain distinct gaps.
