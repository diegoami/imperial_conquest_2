> Guidance for OpenCode. Claude Code uses CLAUDE.md.

# Imperial Conquest 2 — OpenCode working agreement

This repository is worked by two agent harnesses, alternately, never at the same time: Claude Code (see `CLAUDE.md`) and OpenCode (this file). The tool-neutral contract — status labels, the task catalogue, branches, bugs, git conventions — is `docs/build-process.md`. Where this file and that document disagree about OpenCode mechanics, this file wins.

## Roles
- **Implementer: DeepSeek V4.1 Flash** (`opencode/deepseek-v4.1-flash`). Writes the design, the code and the tests; replies on GitHub signed `— Implementer (DeepSeek V4.1 Flash)`.
- **Reviewer: Luna** (`opencode/gpt-5.6-luna#high`), a fresh-context subagent given that model id explicitly. She verifies against the real code rather than trusting the description, and posts her verdict signed `— Luna (GPT-5.6, high)`.
- Both post through the owner's GitHub account (no separate bot identity), so the signature line is the only marker of authorship.
- A BLOCK is not overridden by the implementer — it goes to the owner.

## The process — two stages
**DESIGN.** Before implementation, write the proposal as a GitHub issue: the problem, findings with `file:line` references, the design, and open questions. Have Luna review that issue and comment; iterate until she posts an explicit **AGREE**. Do not implement before that. For work already specified by an entry in `docs/task-catalogue.md`, that entry is the design: the issue confirms it and Luna's AGREE confirms it.

**IMPLEMENTATION.** Implement the agreed design on a branch in a worktree and open a PR that references the issue. Have Luna review the PR against the agreed design; fix and iterate until she posts an explicit **AGREE**. **The owner merges; this harness never merges and never approves on the owner's behalf.**

## Worktrees — never the main checkout
One task in flight at a time. The main checkout `C:\Users\diego\projects\imperial_conquest_2` belongs to the main session. Dispatcher and agents use `C:\Users\diego\projects\ic2-work\`.

- **The dispatcher creates the worktree** before dispatch: implementer `ic2-work\T<nn>` on branch `task/T<nn>-<slug>` from `origin/main`; reviewer `ic2-work\T<nn>-review` detached at `origin/task/T<nn>-<slug>`. Agents never create worktrees and never mutate the main checkout's git state.
- **Dispatch is spawn → move.** Spawn the subagent in the background, then immediately move its session to the worktree directory (the `session_move` tool; through Code Mode `execute` if it is not exposed directly). A foreground subagent cannot be moved, so dispatches are background and the main session waits for the completion notification.
- **Every agent prints the where-I-worked block in its first tool call and final report**: `git rev-parse --show-toplevel`, `rev-parse --short HEAD`, `rev-parse --abbrev-ref HEAD` (or `HEAD` detached), and `git diff --name-only origin/main...HEAD`. If the toplevel names the main checkout, the move lost its race: stop, discard the run, re-dispatch.
- **The implementer detaches** (`git checkout --detach`) before it finishes, so the branch is free for the reviewer. **The reviewer removes its own worktree** when done; the main session removes any leftovers after the merge.
- **Sweep the diff inline.** Every review finding must name a file present in `gh pr view <pr> --json files`; a review naming anything else reviewed the wrong tree. Discard it, say so, and re-dispatch the reviewer — never relay it to the implementer.

## Verification gates
- Check: `dotnet build IC2.sln --nologo -v q` — expect 0 warnings, 0 errors.
- Unit (engine): `dotnet test tests/IC2.Engine.Tests --nologo -v q` — expect 2,493 passed.
- Unit (data), canonical: `$env:IC2_FIXTURES_DIR=<clone>; dotnet test tests/IC2.Data.Tests --nologo -v q` — expect 126 passed, 0 failed, 0 skipped. Clone recipe: `gh repo clone diegoami/ic2-test-fixtures <clone>`.
- Full: `$env:IC2_FIXTURES_DIR=<clone>; dotnet test IC2.sln --nologo -v q`.
- A worktree never has `assets.local.ini` (git-ignored, per-checkout), so the data tests skip there by default; always set `IC2_FIXTURES_DIR` in a worktree.
- Run the full suite **three times** before pushing anything that touches primary logic, and read the pass **count**, not the absence of a FAIL. A docs-only change needs one run.

## Principles
- Keep reviewer requirements separate from owner decisions; put owner decisions to the human with a recommended default.
- Reproduce every finding before acting, and your own claims before publishing them. When a check fails, suspect your harness first.
- For each passing check, say what it would have caught had the code been wrong — never let implementer and reviewer share a blind spot.
- A passing test is not a working feature: assert what a person would notice.
- A threshold from one measurement is a coin toss.
- Flag out-of-scope defects rather than fixing them silently.
- Show diffs, not whole files.

## Scope
Claude Code reads `CLAUDE.md` and, by default, does not read this file. OpenCode reads this file and never `CLAUDE.md`. If a tool ever reads both, this line decides: the OpenCode process is here, the Claude process is in `CLAUDE.md`. One source of truth per idea.
