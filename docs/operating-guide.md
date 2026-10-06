# Operating guide

The agent-facing half of operating this project: where everything lives, how tasks are run, the
working rules, and the standing preferences. The process contract is [build-process.md](build-process.md)
and the tasks are in [task-catalogue.md](task-catalogue.md).

**Living reference is in the [wiki](https://github.com/diegoami/imperial_conquest_2/wiki)**, not here, because it goes stale faster than the code:
- [Where the build stands](https://github.com/diegoami/imperial_conquest_2/wiki/Where-the-build-stands) — how to read the board, and what becomes runnable when
- [Open questions](https://github.com/diegoami/imperial_conquest_2/wiki/Open-questions) — research-level items not yet established
- [Practical caveats](https://github.com/diegoami/imperial_conquest_2/wiki/Practical-caveats) — Godot headless churn, build order, Ghidra quirks, local corpus drift
- [Process incidents](https://github.com/diegoami/imperial_conquest_2/wiki/Process-incidents) — the numbered incidents that build-process.md's rules cite; append-only
- [Model trials](https://github.com/diegoami/imperial_conquest_2/wiki/Model-trials) — one entry per review a substitute took in Sol's place: the error, the probes, the substitute, what it caught or missed; and one per reviewer that named one blocking finding per round for two rounds running ([build-process.md §4.4](build-process.md#44-rework)) ([build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)); append-only
- [Dated reviews](https://github.com/diegoami/imperial_conquest_2/wiki/Review-2026-09-18-repository-and-direction)

Status itself lives only in GitHub labels ([build-process.md §5](build-process.md#5-status-lives-on-github)):

```bash
gh issue list --label task --label status:ready          # what can run next
gh issue list --label triage:needed --state open         # untriaged bugs and follow-ups
```

---

## 1. Where things live

### 1.1 The repositories

- **This repository**, [`diegoami/imperial_conquest_2`](https://github.com/diegoami/imperial_conquest_2): design, plans, and all code (`IC2.Data`, `IC2.Inspect`, `IC2.Engine`, `IC2.Cli`, `godot/`).
  - The main checkout is `C:\Users\diego\projects\imperial_conquest_2` and belongs to the main session.
  - Agents' worktrees live under `C:\Users\diego\projects\ic2-work\`.
- **The research repository**, [`diegoami/imperial-conquest-2-research`](https://github.com/diegoami/imperial-conquest-2-research): every reverse-engineering report (`docs/reports/`), the roadmap, the decompilation plan and the research notes.
  - Its local checkout is `C:\Users\diego\projects\RE-imperial-conquest-2`. Run `git pull --ff-only` before writing to it.
  - For static-analysis work, start from its `docs/decompilation-plan.md`, the live record of what has been decompiled.
- **The bot repository**, [`diegoami/ic2-conquest`](https://github.com/diegoami/ic2-conquest): a bot that plays the original headless under Wine, in WSL on the desktop (`/home/diego/projects/ic2-conquest`).
  - The user's IC2 CONQUEST EXPLORE session there runs [stage 0 experiments](evidence-pipeline.md#the-stages) from requests the main session sends it directly through Remote Control (`SendMessage`; the user relays one only when that session is not reachable; the user's decision of 2026-10-06).
  - Its results reach this repository only as research reports, through the research repository's findings intake. Nothing here reads its drafts or writes to it.

| Document | What it is |
| --- | --- |
| [README.md](../README.md) | The front door: what the project is, how to build it, the inspector tools |
| [operating-guide.md](operating-guide.md) | This document |
| [CLAUDE.md](../CLAUDE.md) | Auto-loaded into every Claude Code session: a pointer to this guide, and the rules that must never be forgotten |
| [build-process.md](build-process.md) | The process contract: roles, the task loop, review gates, bugs and follow-ups, prompt templates, `/run-task` |
| [task-catalogue.md](task-catalogue.md) | The index: the dependency graph and a stub per task linking to its entry |
| [tasks/](tasks/) | One file per task, `T<nn>.md`: the task's contract (Owns, Scope, Done when) |
| [game-design.md](game-design.md) | What is being built |
| [design-audit.md](design-audit.md) | What the evidence supports, and the design questions Q1–Q10 |
| [release-plan.md](release-plan.md) | Versions, release gates, release notes, the release checklist |
| [milestone-review.md](milestone-review.md) | A portable process: how a milestone is defined, frozen on a review branch, reviewed by an independent reviewer through a never-merged PR, and tagged |
| [evidence-pipeline.md](evidence-pipeline.md) | The `/process-evidence` pipeline and its skill text |
| [recording-analysis.md](recording-analysis.md) | Reading a screen recording: the `/parse-recording` pipeline, the ffmpeg recipe, and what each in-game panel is worth |
| [investigations/README.md](investigations/README.md) | Index of this repository's own evidence write-ups |

### 1.2 The original game files

The original game files are never in either repository, and neither is anything derived from them. The Ghidra project and the decompiled-text dumps under `%LOCALAPPDATA%\ReTools` stay local too.

`assets.local.ini` at this repository's root points at the user's own installation. It is git-ignored and copied from `assets.example.ini`. The installation is currently `C:\Users\diego\Documents\imp_conq_original`, which is a local git repository of its own. If the file is missing, ask the user to configure it; never guess a path.

Inside that directory:

| Folder | Holds |
| --- | --- |
| `saves/` + `saves-processed/` | Save files. Move a save to `saves-processed/` once a report cites it. |
| `recordings/` + `recordings-processed/` | Screen recordings (`.mp4`), same convention |
| `screenshots/` + `screenshots-processed/` | Screenshots (`<save-number>.<image-number>.png`), same convention |
| `notes/` | The user's session notes: a save pair, an optional recording, and the events observed between them |
| (root) | See below |

**Every `IC2.Data.Tests` test that names a fixture resolves it by name, never by folder** (T53, issue
#204): a save cited as `1_rome_270_winter_7.sav` is found by searching `saves-processed/`, then
`saves/`, then `releases/<tag>/` under the configured directory, first hit wins (one search order shared by `FixtureResolver` and `IC2.Inspect`'s `CorpusFileLocator` since T64; byte-identical copies of the same name count as one file, and differing copies are an error) — so moving a save into
`saves-processed/` once a report cites it, exactly the convention above, changes no test outcome. CI
never touches this directory; it sets `IC2_FIXTURES_DIR` to a fetched clone of the private
[`diegoami/ic2-test-fixtures`](https://github.com/diegoami/ic2-test-fixtures) repository, which the same
by-name resolver checks first when it is set.

**That repository holds the whole corpus, not a named subset** (issue #207, correcting T53's own first
attempt at this paragraph): it was originally scoped to just the saves referenced by name as string
literals, which missed that `CorpusSweepTests` and its siblings sweep **whatever corpus is configured**
against the full `CorpusFixtures/expected-corpus-outcomes.json` table — reading by directory, not by
name. A named-subset fixtures repository silently loses that coverage in CI the moment a test reads by
directory instead of by name, the same shape as the defect T53 exists to fix. The repository is now the
DAT plus all 99 saves the committed corpus table enumerates (13 MB; the 45 `IP*.sav` of the 2026-09-20 run were added on 2026-09-24, after T64 regenerated the table with them — a save that joins the table joins the fixtures repository in the same session) — see its own README for the same
reasoning. Nothing downstream — a test, a review, or CI — cares which folder a fixture currently sits
in, and nothing in CI has to guess which subset of the corpus a test will read by directory next.

**The evidence is published as GitHub releases, one per play-through**, in [`diegoami/imp_conquest_fixtures`](https://github.com/diegoami/imp_conquest_fixtures/releases) — `run-1-ptolemy`, `run-1-rome`, `run-1-cartago`, `run-1-thracia` and `legacy-probes`. Each release holds that run's saves, screenshots and any recording. **A recording no longer needs a note to be usable**: [recording-analysis.md §1](recording-analysis.md#1-align-the-saves-to-the-recordings-before-extracting-anything) aligns saves to recordings from their file timestamps alone.

This matters because **reports cite bare filenames** (`11_supply.sav`, `1_rome_270_winter_7.sav`), never paths. [`docs/evidence-index.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/evidence-index.md) in the research repository maps every cited filename to the release holding it, with a `gh release download` recipe — so a citation can be resolved **without this machine's local copy**. Read it before hunting for a file on disk.

**Evidence made on another machine lives only in a release.** The originals repo has a git-ignored `releases/<tag>/` cache and `scripts/fetch-release.sh` to fill it: saves, screenshots and notes by default (**under 10 MB for a whole run**), `--video` to add the recordings. The cache is disposable — GitHub is the source of truth.

The releases change nothing about the standing rule: **no save, screenshot, recording or game file enters either repository.** A release in a private repository is not this repository, and the index is a pointer, never a copy.

The root holds:
- `Imperial Conquest 2.exe`, `.dat` and `.hlp`;
- `WAVS/`, converted to 16-bit/44.1 kHz PCM; the untouched originals are in `WAVS - Copy/`;
- `patch_exe.py`, which builds instant-battle and message-pumping EXE variants for recording.

The in-game battle pacing delay is a per-nation preference (the `TBattleDelays` dialog) and can be set to zero from inside the game.

### 1.3 Local toolchain (outside both repositories)

- **General tools:** `gh` (authenticated as `diegoami`), `jq`, Python 3.14, Node, the .NET 10 SDK, Godot 4.7.2 (.NET), OpenCode with the GLM, DeepSeek (V4.1 Flash and V4 Pro), Luna and Sol models, and the free OpenRouter models as advisory reviewers only ([environment.md](environment.md#the-free-openrouter-models-advisory-only)), for `scripts/external-review.ps1`, and OpenRouter, for the image models and for Jev (`scripts/jev-ask.ps1`, §2.1's router). Jev's key is the `OPENROUTER_API_KEY` environment variable, which the script reads from the process and then from the Windows user scope, never from a file.
- **OpenCode setup, and its snags** (2026-10-01). The scripts run the npm CLI (`opencode-ai`, 1.18.x) by default, with their own data root per major version, `%USERPROFILE%\.local\share\ic2-opencode-1x` (`XDG_DATA_HOME` is `<root>\data`, with `<root>\cache` and `<root>\state` beside it; bug #540, [T98](https://github.com/diegoami/imperial_conquest_2/issues/541)). The desktop app's 2.x CLI runs only when `IC2_OPENCODE_EXE` names it, with the root `…\ic2-opencode-2x`, or on a machine without npm. `-WhatIf` on the three scripts prints the chosen CLI, why, and the argument line, and starts nothing; the `--version` probe runs in its own `…\ic2-opencode-probe` so that 2.x's startup log stays out of the desktop app's folders. The models are **OpenCode Go**, `opencode-go/…`, not Zen's `opencode/…` (the user's decision; fix #551), except the reviewers Luna and Sol, `openai/gpt-5.6-luna` (fix 575) and `openai/gpt-6-sol`, on the machine's OpenAI login; GLM on the Z.AI Coding Plan, `zai-coding-plan/…`; and the Alibaba Token Plan, `alibaba-token-plan/…` (Qwen always; DeepSeek and GLM by `-Route`, the owner's decision of 2026-10-05), whose runs use their own data folder, `<root>\data-alibaba`, which never holds an auth.json, and authenticate by `ALIBABA_TOKEN_PLAN_API_KEY` ([environment.md](environment.md)).
  - **Go comes only through a console login, never `opencode auth login`:** run `opencode console login` with `XDG_DATA_HOME` set to the root's `data` folder (`…\ic2-opencode-1x\data`). It prints a URL and a code; the user approves them in the browser. Check the result with `opencode console orgs`, and `opencode models opencode-go`, which should list about 29 models; if none appear, run `opencode models --refresh`. **The login lives in that directory's database, not in `auth.json`**, so copying `auth.json` doesn't carry it: every data directory, on every machine, needs its own console login. A 1.x root still in the old layout has its `<root>\opencode` folder moved to `<root>\data\opencode` by its next run, in one rename (all or nothing: a locked database fails it, and the run after retries), so its login carries over. Before an `opencode-go` run under 1.x, the scripts check the login with `opencode models opencode-go` and stop with an authentication cause if it is missing. **No way to give a 2.x root an OpenCode Go login is known**: 2.0.18 has no `console` subcommand. That is why 1.x is the default. API-key providers (Zen, OpenRouter, OpenAI) do sit in `auth.json`, which the scripts copy in. Never read, print or commit it.
  - **The prompt goes through stdin, from a file** (2026-10-06). `Invoke-OpenCodeWatched` writes the brief to `<title>.prompt.txt` in its log folder and starts `opencode run` with that file as stdin, never as a command-line argument, which Windows caps at 32,767 characters and which blocked three dispatches on 2026-10-06. `opencode run` reads a non-terminal stdin to its end and takes it as the message, on 1.x and 2.x alike, and the file's end is the end-of-file it waits for. Proved that day with a 39,113-character prompt whose only instruction followed 39,000 characters of padding, on 1.18.34 (deepseek-flash) and on 2.0.18 (a free OpenRouter model). So a brief may paste a whole task entry and a whole review (CLAUDE.md rule 15) at any length; the 32,000 guard remains for the command line itself, and `-WhatIf` prints `<prompt via stdin, N characters>`.
  - **The OpenCode desktop app** bundles its own 2.x CLI and once moved the shared default database to a schema the 1.x CLI can't read (`no such column: project_id`). Its own data directory keeps the scripts clear of that, and the scripts run on either version ([T98](https://github.com/diegoami/imperial_conquest_2/issues/541)): under 2.x they pass `--standalone` to every call, give the variant as `model#variant`, and set `PWD` to the run's directory. **The user's global `~/.config/opencode/opencode.json` allows `external_directory`**; the two agent files override it with `ask`, so the worktree guard holds only through them.
  - **Luna:** `opencode-go/gpt-6-luna` is served by a third-party proxy (`novita-openai`) that returns `Bad Request: {"model":"gpt-6-luna"}` mid-run in long agent loops, likely on assistant turns that only call tools ([#553](https://github.com/diegoami/imperial_conquest_2/issues/553)). The reviewer therefore uses **`openai/gpt-5.6-luna`**, the direct OpenAI route, which needs the OpenAI login in `auth.json` (the scripts copy it into their data directory; re-login with `opencode auth login` → OpenAI if it lapses).
  - **Early stops are not a context limit:** every model here has about 1M tokens of context (`opencode models opencode-go --verbose`); GLM's early stops are the model ending its turn ([#557](https://github.com/diegoami/imperial_conquest_2/issues/557), [#562](https://github.com/diegoami/imperial_conquest_2/issues/562)).
  - **Paths:** WSL has its own native OpenCode (`~/.opencode/bin/opencode`) with separate logins; the Windows `opencode` on WSL's PATH is a shim under `/mnt/c`.
- **Godot 4.7.2 mono** (matching `Godot.NET.Sdk/4.7.2` in `godot/IC2.MapViewer.csproj`) is at `C:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`.
  - `godot` is on PATH from Git Bash (a shim at `~/.local/bin/godot`), and `godot.cmd` from PowerShell. Both run the **console** build, so `print()` and script errors reach the terminal.
  - To run headless: `godot --headless --path godot --quit-after 2 [res://Scene.tscn]`. Since T24, the main scene is the game UI (`res://UI/AppRoot.tscn`), so the engine slice needs `res://Slice/Slice.tscn` and the research inspector `res://MapViewer.tscn` passed explicitly.
  - **Packaging** (`pwsh scripts/package.ps1`, `-Verify` to check the export) also needs Godot's **4.7.2 mono export templates**, installed from the editor under `%APPDATA%\Godot\export_templates\4.7.2.stable.mono\` ([packaging.md](packaging.md)).
  - **Headless screenshots do not work.** The headless display driver has no texture, so `GetViewport().GetTexture().GetImage()` throws "Parameter 't' is null" (#156). Visual evidence needs a short **windowed** run.
  - Opening the editor dirties `godot/project.godot` and `godot/MapViewer.cs` with whitespace churn. `scripts/check-godot-churn.ps1` reverts the benign churn and leaves real edits alone. Never run two Godot processes at once.
- **ffmpeg** is at `%LOCALAPPDATA%\ReTools\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe`. To extract frames: `ffmpeg -ss <startSeconds> -i "<recording.mp4>" -vf fps=1 -frames:v <N> <outdir>/f_%03d.png`, then read the frames. The tactical battle's combat-resolution panel gives exact per-exchange troop counts, and the user will record more battles on request.
- **Experiments run in the cloud; the desktop confirms.** A cloud environment for the research repository, with Wine, Xvfb and the fixtures repository attached, is where [evidence-pipeline.md](evidence-pipeline.md#the-stages)'s stage 0 runs the original game headless. The desktop, the Windows machine where the user plays, confirms: a result seen only under Wine gets one confirmation there before it settles a rule.
- **`%LOCALAPPDATA%\ReTools\`** holds:
  - Temurin JDK 21 (`jdk-21.0.12.1+1\`) and Ghidra 12.1.3 (`ghidra_12.1.3_PUBLIC\`);
  - the imported and analysed project (`ghidra_projects\IC2\`);
  - `scripts\`: `ExportFunctions.java`, `ExportAddresses.java` (works on mid-function addresses), `ExportAllInRange.java`, `FindXrefs.java`, `FindCallers.java`, `FindString.java`, `DumpMemory.java`, `DumpListing.java` (machine-code listings), `ImportDelphiSymbols.java`;
  - `delphi_symbols.tsv`/`.json`: 282 method names across 31 classes. Look an address up here before deriving one.
  - `all_app_functions.txt`: every function in 0x401000–0x460000, decompiled. Grep this before running Ghidra.
- **To run Ghidra headless:** set `$env:JAVA_HOME` to the JDK, then run `ghidra_12.1.3_PUBLIC\support\analyzeHeadless.bat %LOCALAPPDATA%\ReTools\ghidra_projects IC2 -process "Imperial Conquest 2.exe" -noanalysis -scriptPath <scriptsDir> -postScript <Script>.java <args...>`.

---

## 2. How to operate the project

### 2.1 Who does what

The **main session runs on Opus** and is the one the user talks to. It plans, runs tasks, triages bugs and follow-ups, runs `/process-evidence`, and brings design decisions and escalations to the user. There is no orchestrator agent.

| Role | Dispatched as | Works in | Reference |
| --- | --- | --- | --- |
| Main session | — | The main checkout. Plan and design changes go on a plan PR; [build-process.md §4.9](build-process.md#49-plan-prs-two-tiers) says who merges it. | [build-process.md §3.1](build-process.md#31-the-roles) |
| Implementer | An OpenCode run via `scripts/external-implement.ps1`, model per the catalogue, the cheap tier by default; a Claude subagent only on an architecture task | Its own worktree under `ic2-work\` | [build-process.md Appendix A](build-process.md#appendix-a-implementer-prompt-template) |
| Reviewer | By the PR's review tier, which Jev proposes and the main session confirms: Luna for a simple PR only, GPT-6 Sol for a complex one, a cold Claude Opus subagent for a very complex one (Sol plus the OpenCode pair, GLM-5.3 and DeepSeek V4 Pro, when Claude implemented it; when Sol cannot review there, Qwen3.8 Max stands in for it, still three reviews); never the implementer's model family. Optionally, on a small PR or a plan PR, an extra **advisory** review from a free OpenRouter model (`nemotron`, `north-mini`, `inkling`, `laguna`), never counted, never labelling, its findings verified before they are relayed | Its own worktree at the PR head | [build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers), [Appendix B](build-process.md#appendix-b-reviewer-prompt-template) |
| Researcher | Opus subagent: the `/process-evidence` stages and targeted research passes | The research repo's checkout; stage 2 in its own worktree here | [evidence-pipeline.md](evidence-pipeline.md) |
| Router | Jev (TypeSafe AI's typed-decision model, through OpenRouter, pinned to `jev-1.13`), called via `scripts/jev-ask.ps1`; no session, no worktree | On stage 0's outputs and on triage, for the decisions [§3](#3-standing-user-preferences)'s Jev preference names | [evidence-pipeline.md](evidence-pipeline.md#the-stages) |

### 2.2 Running tasks

`/run-task [T<nn> | #<issue> ...]` runs tasks, and `fix` bugs, end to end, one at a time ([build-process.md Appendix C](build-process.md#appendix-c-the-run-task-skill)):
1. The implementer builds the task.
2. An independent reviewer checks it.
3. Any rework goes back to the implementer, at most two rounds (one for a `fix`).
4. The main session merges it.
5. The main session applies the doc claims the merge made stale, and reports to the user.

Give it task ids to run them in order, or nothing to take the next ready task. It stops at any escalation.

**Checking which model an agent ran on.** The Agent tool's `opus` and `sonnet` aliases resolve to the newest model in the family, so entries need no version pin. To check, grep the subagent transcript, never read it whole. The transcripts are at `%USERPROFILE%\.claude\projects\<project>\<session-id>\subagents\agent-<id>.jsonl`; the task `.output` files are often empty. Use `grep -oE '"model" ?: ?"claude-[^"]*"' agent-<id>.jsonl | sort | uniq -c`. Don't spawn a probe agent: one cost about 54,000 tokens of startup context.

### 2.3 The two skills

Both are **local, git-ignored installs** under `.claude/skills/`, and the fenced text in the repository is the source of truth. If a skill is missing, reinstall it verbatim from its fenced block. Skills load when a session starts, so a newly installed skill needs a fresh session.

| Skill | Installed at | Reinstall from | What it does |
| --- | --- | --- | --- |
| `/run-task [T<nn> | #<issue> ...]` | `.claude/skills/run-task/SKILL.md` | [build-process.md Appendix C](build-process.md#appendix-c-the-run-task-skill) | Runs build tasks, and `fix` bugs, end to end |
| `/process-evidence [path]` | `.claude/skills/process-evidence/SKILL.md` | [evidence-pipeline.md](evidence-pipeline.md#the-actual-skill-file) | Turns new saves, recordings and notes into research findings, then into design implications. With no input it also evaluates research reports written or corrected on the research side and not yet evaluated here |
| `pwsh scripts/jev-ask.ps1 -StateFile <f> -QuestionsFile <q> [-ItemsDir <d>] [-DryRun]` | `scripts/jev-ask.ps1` (tracked; needs `OPENROUTER_API_KEY`) | — | Asks Jev named Choice, Score and yes-or-no questions over one state, or over every file in `-ItemsDir`; prints JSON, one entry per question: the answer, its probability and a bucket (act at 0.9 and above, discard below 0.1, claude between). Posts nothing. [Wiki: Jev](https://github.com/diegoami/imperial_conquest_2/wiki/Jev) |
| `/parse-recording [recording] [saves] [timestamps]` | `.claude/skills/parse-recording/SKILL.md` | [recording-analysis.md](recording-analysis.md#the-actual-skill-file) | Reads a screen recording into findings — frame extraction, panel reading, correlation against the saves either side. **Needs no written notes**, only rough timestamps |

### 2.4 Working rules

- **One task in flight at a time per machine.** Agents never work in the main checkout ([build-process.md §7](build-process.md#7-concurrency-single-instance-and-local-only)). With a second machine, each claims its tasks with a `machine:*` label, and two file-disjoint tasks may run at once ([build-process.md §8](build-process.md#8-two-machines)).
- **The primary machine** is this one, `C:\Users\diego\projects\imperial_conquest_2` (`IC2_MACHINE=desktop`). Its main session triages and opens plan PRs; another machine runs the tasks it claims, and may open a plan PR only for the entry of a task it has claimed ([build-process.md §8](build-process.md#8-two-machines)).
- **Branch or `main`, case by case.** A merge's routine doc claims go straight to `main`. New or substantive content goes to a branch for review: a design correction, a new mechanism, catalogue changes. When unsure, ask. Which plan PRs merge on the opener's authority, which on another session's review, and which wait for the user: [build-process.md §4.9](build-process.md#49-plan-prs-two-tiers).
- **Review is a label, not a GitHub review.** The reviewer applies `status:approved` or `status:rework`, and the main session reads the label.
- **Relay reviewer findings in full** on rework, never a hand-picked subset.
- **Merging** happens on GitHub's side: `gh pr merge --squash`. Afterwards, `git pull` in the main checkout is safe, because no agent uses it.
- **If a session is interrupted**, nothing is lost. The facts are in the labels and the PRs, and implementers push work in progress to their task branch. A new session picks up any task left in flight ([build-process.md §5](build-process.md#5-status-lives-on-github)).

### 2.5 Bugs and follow-ups

- **Bugs.** A defect in already-merged code is filed as a `bug` issue, and the task that found it is suspended. It is never patched from inside another task's Owns list. A bug whose fix stays within the files it names and changes no rule's outcome runs as a `fix`, with no catalogue entry ([build-process.md §4.10](build-process.md#410-the-fix-lane)).
- **Follow-ups.** Non-blocking review findings go into one `T<nn> follow-up` issue per merge.
- **Triage.** Both are filed with `triage:needed`, which is the main session's queue. Triage decides one of four outcomes: a `fix` ([build-process.md §4.10](build-process.md#410-the-fix-lane)), a correction task, folding the item into an upcoming task, or closing it with a reason. It records the outcome in a comment and removes the label. When a bug blocks a task, the catalogue records the dependency ([build-process.md §4.6](build-process.md#46-bugs-and-follow-ups)).

### 2.6 New evidence and how it reaches the build

`/process-evidence` runs two sequential Opus stages ([evidence-pipeline.md](evidence-pipeline.md)):
1. Stage 1 writes the research-repo report, straight to that repo's `main`.
2. Stage 2 checks every document claim the new evidence touches, on a review branch.

Each finding takes one of four routes:

- a corrected fact, or a closed `[open]` item, in `design-audit.md`, `game-design.md` or another document;
- a defect in merged code → a `bug` issue labelled `triage:needed`;
- a change to a not-yet-dispatched task's scope or DoD → a catalogue edit on the review branch;
- a design decision → posed to the user, never decided.

Targeted research passes (for example "what does the original do when upkeep can't be paid?") are dispatched the same way, straight to a researcher, when a task or a review needs an answer.

---

## 3. Standing user preferences

These are kept in step with the auto-memory feedback notes. When a preference changes, update it here in the same session.

- **Repeated decisions with known answers go to Jev, not to a session** (the user's decision of 2026-09-29). Jev (TypeSafe AI, through OpenRouter, pinned to `jev-1.13`) answers Choice, Score and yes-or-no questions over a state with a calibrated probability, and writes no text. The main session delegates to it, through the router script, whenever a decision repeats over many items, the possible answers are known up front, and no written explanation is needed. The uses this project has: routing stage 0's corpus mismatches to a rule family and to known-divergence versus defect; yes-or-no questions over a sweep table (did the outcome change, is the change monotonic); a proposed `fix`-versus-task label and playability verdict on a new bug, which the main session confirms at triage; a proposed review tier for a PR (`scripts/jev/review-complexity.json`, since 2026-10-03), which the main session confirms before the review ([build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)). The split is the one `newscollection2027` settled on: act on a probability at or above 0.9, discard below 0.1, and hand the middle to a Claude pass. Jev never decides a review verdict, a merge, or a label on its own, and nothing of it enters the engine or the coach, which stay deterministic and offline. The states it sees are derived from the private evidence and leave to OpenRouter; that is the user's decision. The router is `scripts/jev-ask.ps1`. At triage, the main session runs it on a new bug's title and body for the `fix`-versus-task question and the playability question (`pwsh scripts/jev-ask.ps1 -StateFile <issue.txt> -QuestionsFile scripts/jev/triage.json`, the state being `Title: <title>`, a blank line and the body; the questions `fix or task` and `playability`, from build-process.md §4.10 and §4.8, since 2026-10-06; an issue whose body records the user's decision to put it in the gate, or a block on a gate task, reads `release:v0.5.0`), and confirms or overrides what it proposes. For stage 0 it routes diffs and sweep tables in one batch (`-ItemsDir`), with no loop in the caller. What Jev accepts, its limits, its price and the data terms are on the [Jev wiki page](https://github.com/diegoami/imperial_conquest_2/wiki/Jev).
- **Commit and push reverse-engineering work without asking.** New reports, roadmap updates and research-repo fixes go straight to the research repo's `main`. Destructive git operations (force-push, amend, `reset --hard`) still need an explicit request.
- **A question is not a request to change files.** Answer it. If a fix turns up along the way, propose it and wait.
- **Branch or `main`, case by case.** Routine doc claims go straight to `main`; novel content goes on a branch for review; ask when unsure. Since 2026-09-28 a routine-tier plan PR merges on the opener's authority, and a contract-tier one on a cross-session review's approval (Sol, then GLM-5.3, then DeepSeek V4 Pro, then Qwen3.8 Max; a Claude review only when Claude did not write the PR, the user's decision of 2026-10-04); the user merges only when the review says `user decision` ([build-process.md §4.9](build-process.md#49-plan-prs-two-tiers)).
- **Upstream defects go through the bug list**: suspend, file, plan, resume. Never an ad-hoc patch across Owns lists.
- **Evidence goes through the two-stage pipeline**: `/process-evidence`, stage 1 then stage 2, never combined.
- **A rule question is an experiment before it is a recording request.** When a headless run of the original can answer it ([evidence-pipeline.md](evidence-pipeline.md#the-stages), stage 0), that comes first, and stage 1 receives its diff or table. The user will still record more battles on request ([§1.3](#13-local-toolchain-outside-both-repositories)), for what an experiment cannot produce: the tactical battle panel's per-exchange counts.
- **Relay reviewer findings in full** on rework.
- **An external reviewer posts to the PR.** Any review prompt handed to another model (a plan PR, a code PR, a milestone) tells it to post its result as one PR comment, starting with a "… review (<model>)" line and the verdict. The prompt also forbids editing, committing, pushing, merging, and closing keywords before `#<n>`. The main session reads it from GitHub, so the user never relays a review by hand. The external reviewers are **Luna** (GPT-6 Luna from 2026-09-25, DeepSeek before; GPT-5.6 Luna, `openai/gpt-5.6-luna`, since 2026-10-05, CLAUDE.md rule 18) and, since 2026-10-03, **GPT-6 Sol** for a complex PR and GLM-5.3 and DeepSeek V4 Pro (`deepseek-pro`) as Sol's substitutes and the OpenCode pair, and since 2026-10-05 Qwen3.8 Max (`qwen`) after them and Qwen3.8 Flash (`qwen-flash`) as the last re-check reviewer; since 2026-10-04 Luna reviews only a simple PR ([build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)), and since 2026-09-28 the main session runs them itself: `pwsh scripts/external-review.ps1 -Pr <n> [-Reviewer auto|luna|sol|glm|glm-flash|deepseek|deepseek-pro|qwen|qwen-flash] [-Route auto|go|zai|alibaba] -BriefFile <brief>` (`auto` by default: `luna` on `openai/gpt-5.6-luna`, the direct OpenAI route, at effort `high`, the user's decision of 2026-10-01, fix 575, which is the simple tier; the main session passes every other tier's reviewer explicitly, `sol` being `openai/gpt-6-sol` at `low` effort, `-Effort medium` at most and never `high`, used only where the tier names it, a re-check of named fixes going to Luna on a simple PR and to GLM-5.3, DeepSeek V4 Pro or Qwen3.8 Flash on a harder one instead (the user's decisions of 2026-10-03, 2026-10-04 and 2026-10-05), and `deepseek-pro`, `opencode-go/deepseek-v4-pro` at `high`, Sol's second substitute, and `qwen`, `alibaba-token-plan/qwen3.8-max` at `low`, its third; `-Route` moves glm and deepseek* to the Alibaba Token Plan, [environment.md](environment.md)) creates the detached worktree, runs the local OpenCode install with the read-only `.opencode/agents/external-reviewer.md` agent through `scripts/Invoke-OpenCodeWatched.ps1`, posts the review as the one comment, and with `-Issue <n> -ApplyLabel` applies the status label from the verdict. The model never writes to GitHub, and since fix 575 **the script never throws a review away**. Output with no review at all (no header line, only tool chatter) is the only failure, and falls back. A readable review is posted, normalised, and its verdict acted on. The header may carry Markdown or a trailing colon, and the verdict may follow a short where-I-worked block (the first 5 non-empty lines), with a `Verdict:` prefix or punctuation. A review that looks cut off (like #370), has an unreadable verdict, or has findings after its closing verdict is **posted whole, flagged** on its first line, gets **no label**, and the script exits **4**. The main session then reads it and decides. A review GitHub does not take (`gh pr comment` fails twice, or returns no comment URL; bug #645, seen in GitHub's outage of 2026-10-03) is reported as `NOT posted:`, saved under `rendered\`, gets no label, and the script exits **5**: the main session posts the saved file by hand. Closing keywords before `#<n>` are rewritten, not rejected. `-SelfTest` runs the parser over sample outputs. A review that arrives flattened onto one line but starts with the header and a verdict and ends with the same verdict is complete; the script restores its paragraph breaks and posts it.
- **An OpenCode run is watched, and falls back across models on an infrastructure failure only** (the user's decision of 2026-09-28). `scripts/Invoke-OpenCodeWatched.ps1` starts OpenCode with **stdin from a file, never an inherited pipe**: `opencode run` reads stdin whenever it is not a terminal and creates no session until stdin's end-of-file, which caused both startup hangs of 2026-09-28 (a background shell's stdin was a pipe that never closed). The run's stdin is the prompt file (its end is the end-of-file; since 2026-10-06 the prompt is never on the command line), and the helper commands (session list, export, the `--version` probe) get an empty file. Behind that, it kills the run when OpenCode creates no session within `-StartupTimeoutSec` (180 s), when the session's `updated` time does not advance for `-IdleTimeoutSec` (600 s for a review, 900 s for an implementer; it advances at each step of the run, not while a tool runs or a reply streams, so the limit sits above the longest single step), or when the run does not finish within `-TotalTimeoutSec` (3600 s for a review, 10800 s for an implementer). Anyone running `opencode run` by hand from a script never leaves it an inherited pipe: give stdin the prompt as a file (`< brief.txt` in bash, `Get-Content brief.txt -Raw |` in PowerShell) or close it (`</dev/null`, `$null |`). An infrastructure failure is OpenCode not found, no session within the startup timeout, a session idle past the idle timeout, no exit within the total timeout, a run that exits without a session, a non-zero exit, the fallback-to-default-agent guard, a tool call OpenCode's permission guard auto-rejected (a path outside the worktree: the rejection ends the run, with exit 0 under 1.x and 1 under 2.x, so the script checks it before the exit code and reads OpenCode's `permission requested: …; auto-rejecting` line and names the rejected path as the cause, [#501](https://github.com/diegoami/imperial_conquest_2/issues/501)), ; any other error stops the script without falling back. Two consecutive attempts that fail with the same cause (two startup hangs, two idle kills) stop the chain at once with exit 3 ("OpenCode unavailable: same failure twice: <cause>"), the in-script half of the two-failures rule below; different causes keep it going. Each external review runs the one model its PR's review tier names (Luna for a simple PR only, Sol for a complex one, GLM-5.3 and DeepSeek V4 Pro for the OpenCode pair beside Sol on a very complex PR that Claude implemented, with Qwen3.8 Max in Sol's place when Sol cannot review: three reviews either way, the owner's decision of 2026-10-05); when Sol cannot review, its substitutes, GLM-5.3, then DeepSeek V4 Pro, then Qwen3.8 Max (added on 2026-10-05), never Luna (the user's decision of 2026-10-04), and after them a cold Claude Opus reviewer unless Claude implemented, escalation if it did (fix 575, and the user's policy of 2026-10-03; [build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)). Since 2026-10-03 the tiers cover code PRs again: they replace the Claude Opus reviewer every code PR had from 2026-10-02 (after Luna approved T99 with no findings where Opus proved two blocking bugs, PR #576), and a very complex PR keeps Opus unless Claude implemented it (the user's decision); the posted header names the model that reviewed and the ones that failed before it, and when every model fails (or OpenCode is not installed) the script posts nothing and exits 3, and [build-process.md §4.9](build-process.md#49-plan-prs-two-tiers) says what comes next. The external implementer is `deepseek-flash` (OpenCode Go, or the Alibaba Token Plan when Go is out of quota, effort `high`; the user's decisions of 2026-10-01, fixes #551, #573 and 575), then `qwen-flash` (Alibaba; the user's decision of 2026-10-05); when every model fails (the script exits 3) or OpenCode is unavailable, the task falls back to **Claude Sonnet**, whatever the entry names (the user's decision of 2026-09-29: Sonnet is the implementers' fallback, Opus the reviewers'). An implementer that **stops and reports** (a contract gap, a Done-when it cannot meet) has not failed and is not retried on another model: that is the process working.
- **The main session watches all of its background work, not only OpenCode runs** (the Isle Wars lesson, adapted and adopted by the user's decision of 2026-10-04): reviews, implementer runs, subagents and long commands. One Monitor watches every job while any is running. It runs for 30 minutes at a time and is re-armed until the work ends, and it reads its job list from a file, so jobs are added without restarting it. Every 2 minutes it reads one **progress signal per job**, chosen by the kind of job:
  - **an OpenCode run** (`external-implement.ps1`, `external-review.ps1`): the size of the scripts' OpenCode log (`opencode.log` under the log directory named in the next bullet), plus the run's worktree HEAD and changed files;
  - **a Claude subagent**: the size of its transcript, `%USERPROFILE%\.claude\projects\<project>\<session>\subagents\agent-<id>.jsonl`, which grows with every tool call and reply, plus its worktree's HEAD and changed files. Its task `.output` file is not a signal: on this machine it stayed at 0 bytes while subagents ran (observed 2026-10-04);
  - **a long command**: its stdout and stderr, redirected to a file under `rendered/`, plus the CPU time of its process (`Get-Process -Id <pid>`), which rises while a silent command computes. A command started without such a file and a known PID is not started.

  The Monitor reports when a job is first seen and when it finishes, and flags as **possibly stuck** a job whose signal has not changed for 10 minutes. The checks run inside the Monitor rather than as conversation turns, so watching costs no tokens until something changes. The main session gives the user a short update at each change and never relies only on completion notices. **On a flag** it does, in order, telling the user each step:
  1. **Alive?** It checks that the job's process still exists (its PID, or `Get-Process opencode`, or the subagent's pending completion).
  2. **Working?** It reads the job's newest log lines: an OpenCode run's from the OpenCode log, a subagent's from the last entries of its transcript (`tail -n 5` piped through `jq`, never the whole file), a command's from its `rendered/` file, and compares its CPU time with the last check. If they show work in progress (a test run, a tournament run, a build), it notes what is running and keeps watching; the Monitor flags it again after another 10 minutes without change.
  3. **Stuck.** A dead process, or 20 minutes without change, is a stuck job:
     - an OpenCode run follows the failure handling in the next bullet (cause recorded in one comment, then the fallback);
     - a subagent is asked for its status once by SendMessage, and is stopped and re-dispatched from its pushed branch if nothing changes in the next 10 minutes;
     - a command is stopped and reported.

  **Avoid waiting on `while pgrep -f '<pattern>'`.** It can match the waiting shell itself whenever the pattern appears in that shell's own command line (as in `bash -c "while pgrep -f X; do sleep 5; done"`), and then it waits forever. Chain jobs in one background command, or wait on the exact PID with `while kill -0 <pid>`.
- **The main session watches every OpenCode run it starts** (the user's decision of 2026-09-28). It starts the script in the background with a watch on the session's start (the `session … started` line) and on its progress, and never waits on it blind. On a failure it reads the run's stderr (the files the script keeps and names) and the OpenCode log (`%USERPROFILE%\.local\share\ic2-opencode-1x\data\opencode\log\`, or `data-alibaba\opencode\log\` for a run on an `alibaba-token-plan/…` model; under `$env:IC2_OPENCODE_DATA_HOME\data\…` or `…\data-alibaba\…` when that variable sets the root: the scripts' runs keep their own data directory since fix #540, apart from the OpenCode desktop app's; read the session with `python scripts/read-opencode-session.py <ses_…>`, adding `--alibaba` for an Alibaba run) at once, records the cause in **one** comment on the PR or issue, and falls back immediately: an implementer from deepseek-flash to qwen-flash, then Claude Sonnet (the script runs both under `-Model auto` and exits 3 only when both fail); a review to its tier's fallback: from Sol to its substitutes once the run's files say whether the OpenAI account is out of quota (`The usage limit has been reached`), then a cold Claude Opus reviewer unless Claude implemented, escalation if it did ([build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)) (a review flagged with exit 4 does not fall back: the main session reads it). **Two failures of the same cause** (two startup hangs, two idle kills) mean OpenCode is not used for that role until the cause is fixed and the fix is merged: the main session goes straight to Claude.
- **The main session's own edits never land where an agent works.** A doc or plan edit goes in its own worktree on a new branch (`git worktree add ../ic2-work/<name> -b <branch> origin/main`), even when it is harmless. Reading via `git show` is not a substitute. Merges happen on GitHub's side (`gh pr merge`), and conflicts are resolved in an isolated worktree, so a merge never disturbs a running agent. Two incidents under the old shared-checkout model led to this: a doc fix landed on an implementer's task branch, and a skill file was written while a reviewer was active.
- **Don't start a rework round as the session winds down.** When the user stops for the day, that covers the current round only. If a review then returns a blocking finding, leave the task at `status:rework` with the full review on the PR, tell the user what is outstanding, and stop; the next session resumes it deliberately. A round is 10–40 minutes of agent time plus a re-review, and the user would rather resume on purpose than have work begin on the way out (said 2026-09-19).
- **Non-blocking review findings become one follow-up issue per merge**, and each item is folded into the next task that touches those files.
- **The v0.5.0 gate is taken first, until v0.5.0 is tagged** (the user's decision of 2026-10-05, *"Gate first"*, replacing the UI chain T24, T25, T27 of 2026-09-28 and 2026-10-03). Once the main session has moved the labels after the plan PR of 2026-10-04 merges, the next ready task or fix labelled `release:v0.5.0` comes before any other task, on whichever machine is running. v0.4.x patch items (T133, T134 and what the user lists after playing) may run alongside it; T131 and every other task wait until the tag unless the user lists them ([build-process.md §8](build-process.md#8-two-machines), "Which task next").
- **The playability gate, until v0.5.0** (2026-09-28, extended 2026-10-03, redefined 2026-10-04 when the user made v0.5.0 *Playable* and moved the battle to v0.6.0). A bug or follow-up that breaks play (a crash, an AI stall, a save that won't load, an order that can never succeed) or **blocks a normal game** (an order the original has that cannot be issued or does nothing; feedback missing or wrong so that play is guesswork; a rule that makes a normal game unwinnable) gets `release:v0.5.0` and becomes a task, a fold or a fix. Everything else is labelled `post-v0.5.0`. The three tests, what a normal game is, and the one triage pass over the open `post-v0.5.0` issues are in [build-process.md §4.8](build-process.md#48-the-playability-gate-until-v050).
- **Implementers run on OpenCode Go, then the Alibaba Token Plan** (2026-09-28, moved from Zen to Go on 2026-10-01 by the user's decision, fix #551): Claude credit is the scarce resource. The implementer is `deepseek-flash` (DeepSeek V4.1 Flash, effort `high`), then `qwen-flash` (Qwen3.8 Flash on the Alibaba Token Plan, the user's decision of 2026-10-05), then Claude Sonnet (fix 575); GLM implements nothing by default since fix 573, High-effort entries and failed rework rounds included; no free or Zen model; Claude Opus only on an architecture task; the reviewer's model **family** is never the implementer's (since 2026-10-03: OpenAI is `luna` and `sol`, GLM is `glm` and `glm-flash`, DeepSeek is `deepseek`, `deepseek-pro` and `deepseek-flash`, Qwen is `qwen` and `qwen-flash`, Claude is Sonnet and Opus), and an OpenCode review passes the implementing model as `-ExcludeModel` so the script drops that whole family from its chain ([build-process.md §3.3](build-process.md#33-model-selection), [§3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)).
- **No Haiku.** Claude reviewers are Opus or Sonnet (Fable only for pure templates); implementers are OpenCode models. Haiku was retired on 2026-09-27 after T26 ([build-process.md §3.3](build-process.md#33-model-selection)).
- **The main session runs the build directly** with `/run-task`. There is no orchestrator layer; it was retired on 2026-09-14 as more overhead than value for serial execution.
- **Agents stay inside their worktree, and never write scratch files to TEMP** (the user's decision of 2026-09-29). Scratch goes under the worktree's git-ignored `rendered/`, or is deleted before a commit; a brief never asks an agent to reach outside its worktree beyond the setup block Appendices A and B already carry, which an OpenCode run skips. The standing text is in [build-process.md Appendix A](build-process.md#appendix-a-implementer-prompt-template) and [Appendix B](build-process.md#appendix-b-reviewer-prompt-template) ([#501](https://github.com/diegoami/imperial_conquest_2/issues/501)).
- **Always include direct links** (the user's decision of 2026-09-29). Every issue, PR, review comment, commit, workflow run and file named in a reply to the user is a clickable link: the full GitHub URL for issues, PRs and comments, never a bare `#123`, and a repository-relative Markdown link for files, with `:line` where it helps. Instructions to the user say exactly what to open, where it is, and what to look for.
- **Anything waiting on the user is asked as a clear question** (the user's decision of 2026-10-01). When a decision is the user's (a merge sign-off, a choice between options, a cost), the main session asks it with the question tool: one question per decision, the options spelled out with their consequences, the recommended one first. It never only lists "waiting on you" items in prose.
- **No status snapshots in documents.** Status lives in GitHub labels only.
- When correcting a claim after user feedback, fix the document or report text itself, not only the chat.
- Do not re-suggest a Windows 9x VM on this machine: WSL2's Hyper-V claims VT-x, and the user will not disable WSL2.

---

## 4. Collaboration norms

- **Keep the user informed while working.**
  - Before starting, say what you intend to do.
  - Give short progress updates: what you examined, what the evidence shows, what changed and why, and what remains uncertain.
  - End with the outcome, the verification done, the remaining limitations, and where to see the result.
  - Don't go more than about a minute of active work without an update, and don't dump raw command output.
- **In reverse-engineering work, separate observations from inferences.** Name the save, screenshot, recording or binary structure that supports each conclusion. Record the exact file, hash, address or screenshot for each new field or formula, and mark an inferred meaning as a candidate until it's checked.
- **For routine save-format checks, sample three or four representative saves, not every save.** Pick samples that cover the relevant before/after event or format variation. Widen the sample only when a discrepancy or a specific question requires it, and say why.

---
