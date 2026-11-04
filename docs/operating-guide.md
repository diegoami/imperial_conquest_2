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

## Additional campaigns

### Resuming the owner's v0.9.0 playtest

On 2026-11-02 (Europe/Berlin), the owner requested installation of the published
[v0.9.0 prerelease](https://github.com/diegoami/imperial_conquest_2/releases/tag/v0.9.0).
The official installer checksum was verified before upgrading the existing
per-user installation. The uninstall registry reported `DisplayVersion=0.9.0`,
and the installed managed executable reported
`1.0.0+88c1989b21b5ecea52243ffe764c82201c2d26f5`, matching the reviewed tag.
The existing Start-menu shortcut was verified at
`Programs\Imperial Conquest 2\Open Imperial Conquest.lnk`; search Start for
**Open Imperial Conquest**, or run:

```powershell
& "$env:LOCALAPPDATA\Programs\Imperial Conquest 2\game\IC2.MapViewer.exe"
```

The installer completed without a restart. No game was launched and no player
saves were modified during this installation. The owner chose to playtest
tomorrow or later; this is an installation receipt, not human playtest acceptance.
Wait for their observations rather than launching games or taking over the screen.
The installer log is a local diagnostic at
`rendered/release/v0.9.0-local-install/install.log` (ignored, not a durable artifact).

For the independent verdict and its reasoning, read the
[focused recheck on #1716](https://github.com/diegoami/imperial_conquest_2/pull/1716#issuecomment-6565328372):
all C1–C8 were MET, including independent copied-package checks. The correction
in #1719 restored placeholder chariot regeneration without changing tests,
runtime or shipped asset bytes; a negative control reproduced the missing-file
failure. The official [tag build](https://github.com/diegoami/imperial_conquest_2/actions/runs/41361801073)
produced the published ZIP and installer. Consult GitHub labels/milestones for
current work; this receipt does not replace them. The release notes retain the
pre-existing Windows headless audio-check limitation (#1718), deferred original
Windows acceptance (T184–T186/#1355), and stronger Improved Computer general's
earlier v0.10 allocation (#1362/#1363), moved by the owner to v0.11 on 2026-11-04. Do not turn installation or automated package
checks into a claim that those deferred acceptance checks were performed.

### Campaign selection

New Game includes Italy 500 BC, an opt-in Italy variant with barbarian arrivals,
and Near East openings around 1350 BC and 750 BC. The historical campaigns use
the existing five troop roles with period names/art; chariots are gameplay
proxies, not a separate combat model. Regional centres, political groupings and
all numerical setup are explicit abstractions. See the
[historical brief](design/near-east-campaigns.md) and
[reproducible source/authoring guide](../tools/NearEast/README.md).
Campaigns with barbarians use native JSON saves; original-SAV export refuses
the unsupported role rather than silently discarding it.

## Observer campaigns

Select **Observer mode (experimental)** on New Game's player screen, or choose
**Game → Observe campaign** from a strategic campaign after resolving battles
and offers. Observation starts paused with all nations computer controlled.
**Play**, **Pause** and **Next round** use the existing AI and turn rules; the
next-round control stops at the next calendar advance. You can inspect the map,
nations, news and latest battles without automatic result dialogs interrupting.
Opening a dialog or leaving the screen pauses progression.

While paused, choose a surviving non-barbarian nation and **Take control** to
join its next turn. Intervening nations play normally and ordinary turn-start
rules apply. Saving preserves the campaign's controller assignments; loading an
observer campaign starts paused. These controls are owner-designed extensions,
not evidence of an original observer mode. See [T278](tasks/T278.md).

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

## Working rules

- A question is not a request to edit files. Answer it, propose a fix, wait.
- Upstream defects go through the bug list; design decisions are the user's.
- A rule question this repository needs answered from the original (a decompile, a live run) is delegated as an issue in [`diegoami/imperial-conquest-2-research`](https://github.com/diegoami/imperial-conquest-2-research/issues), linked from the issue it blocks; it is not investigated here.
- No status snapshots in documents. In reverse-engineering work, separate observation from inference and cite the exact file, hash or address.
- Keep the user informed in short updates; link every issue, PR and file you name.

For strategic-AI comparisons, use the [decision evidence and limits](investigations/original-ai-control-and-support.md#army-decision-comparison-after-t248). The owner’s 2026-10-31 decision reserves reachable resupply/garrison/emergency for Improved; Faithful uses original destinations and swing/stay-put with no emergency. Matching branch inputs are distinct from final endpoints; autosave moves may have refilled, so compare position, loyalty and troops or seat-end hooks. The [research `4bacb8e`](https://github.com/diegoami/imperial-conquest-2-research/blob/4bacb8e/docs/reports/2026-11-02-remake-11f713e2-recheck.md) recheck of remake `11f713e2` replays 145 anchored turns without error: capital-army rows fall from 410 to 4 (the later-attributed war rows below), enemy-city rows from 17 to 1 (Sidon), and other-city rows to zero. Sidon measures a third D734 siege caller, EBE8 garrison fallback → DBA8 at `0x44ECD6`, with score 84, nine moves and war with the city owner; [PR #1731](https://github.com/diegoami/imperial_conquest_2/pull/1731), commit `e1772c52`, adds that guarded city contact to both remake garrison construction paths. The [later v0.9.1 recheck at research `570aff8`](https://github.com/diegoami/imperial-conquest-2-research/blob/570aff8/docs/reports/2026-11-03-remake-v091-recheck.md) independently confirms Sidon: enemy-city rows fall from one to zero over 145 anchored turns, with 144/145 turns retaining the same reported difference counts. A new Ptolemaic run finds the separate no-command full-budget gate omission ([#1739](https://github.com/diegoami/imperial_conquest_2/issues/1739)): one 19,300-troop army has eight moves against allowance ten. The [later Ptolemaic position attribution](https://github.com/diegoami/imperial-conquest-2-research/blob/bc8d720/docs/reports/2026-11-03-ptolemaic-longrun-positions.md) separates four causes across ten rows: the no-command budget gate, pending-human-battle capital defence (#1741), resupply-city table ties (#1742), and new-fleet launch placement (#628). Five further rows depend on draws, without identifying the particular random calls. These Wine/code findings do not establish native Windows or full state/RNG parity. The earlier four port/hunt fleet rows remain separate from the newly measured launch defect. Native Windows AI-contact and global-RNG parity remain unproved. The [diplomacy follow-up at research `264098b`](https://github.com/diegoami/imperial-conquest-2-research/blob/264098b/docs/reports/2026-11-03-diplomacy-longrun.md) attributes the four war rows to war/alliance rolls and battle/treaty changes to protection, rather than a measured deterministic diplomacy defect. Across seven starts and three rounds (330 seat-turns), it reports 326 matching, three roll-only and one after-divergence seat, with zero deterministic differences; hooked/plain saves agree in 21/21 rounds. This Wine sample does not measure distributions or native Windows parity. Its code-only D3 trade-order finding motivates T274/[#1732](https://github.com/diegoami/imperial_conquest_2/issues/1732): Original AI prioritizes first-sweep non-leading nations, then still-eligible skipped war leaders in table order if live capacity remains; Improved keeps its single scan. The initial scan shares the existing strict running-war predicate and retains its leader set across higher-priority war/alliance writes, while trade eligibility/caps remain live; this is a bounded correction within the scored driver, not full monolithic diplomacy parity. The report's intake did not independently recheck the individual battle/treaty/protection chains. The bounded Seleucid turn-26 replay in [research `e449768`](https://github.com/diegoami/imperial-conquest-2-research/blob/e449768/docs/reports/2026-11-02-seleucid-turn26-overthrow.md) confirms human overthrow under Wine, native Windows and remake `11f713e2` from the same start with the logged seed. Research independently verified identical executable bytes and both message screenshots; the public v2 asset uses a Windows game-window crop. This intake did not rerun or download them. The full campaign’s unpainted Wine battle window was not exactly reproduced and remains unexplained. Selected-seat watch continuation is an approved T83 design (owner decision on [PR #375](https://github.com/diegoami/imperial_conquest_2/pull/375), review N2/N3; `GameSession.cs:105–112`, distinguished from final-human hotseat exit at `1296–1312`). Native confirmation covers only this bounded overthrow, not other AI contacts, unmodified retail, global RNG or whole campaigns. Work and release allocation remain on GitHub labels.

The [released v0.9.3 recheck](investigations/original-ai-control-and-support.md#released-v093-recheck-cross-class-search-boundary) confirms live-map launch, the Caspian AI build-city correction and embarked-offer landing in the measured Wine replays. T276 covered same-class failed searches only. T277 extends the initial signal to no-ferry and adjacent-embark cases and continues capital defence while a tactical battle is pending (#1758/#1741); instant resolution remains unchanged. Adjacent-fleet failure signalling is code-confirmed but unmeasured. Human Build fleet #600 remains separate; no native Windows or general RNG parity is established.

For SAV export, the [current compatibility evidence](game-design.md#original-save-compatibility--confirmed-format-designed-policy) separates canonical native-state encoding from identity preservation. The October 31 callback, lifecycle and canary studies are bounded static/emulation evidence, with no original save written or Windows run. Preserve the existing export gates and named #1191/#1193–#1195 ownership; GUI indirect calls, unexercised state paths and native acceptance remain distinct gaps.
