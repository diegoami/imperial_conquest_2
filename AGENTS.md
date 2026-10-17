# Imperial Conquest 2 — Codex and OpenCode entry point

Read `docs/operating-guide.md` and `CLAUDE.md`. They remain the shared project
rules; this file adds the tool mapping in `docs/codex.md`, rather than copying
or replacing those rules. Claude Code continues to use `CLAUDE.md`.

Determine your role from the current request or dispatch brief before acting.
In an external implementer or reviewer run, the supplied brief and
`.opencode/agents/external-implementer.md` or `external-reviewer.md` define your
role. Main-session duties (startup triage, model selection, dispatch, posting,
labels and merging) belong to the main session, never to its delegated reviewer
or implementer. Preserve the brief's Owns list, worktree, HEAD and output format.

For Codex:

- The command tool already runs PowerShell. Run routine `git`, `gh`, build and
  read commands directly with the tool's working-directory argument; use
  `pwsh -File` or `pwsh -Command` only when a separate PowerShell process is
  necessary, such as when a script requires process isolation or a different
  PowerShell version. Keep local writes and network operations in separate
  tool calls when practical so reusable command approvals can match them.
- Repository skills are under `.agents/skills/`: `$run-task`, `$process-evidence`
  and `$parse-recording`. Read only the applicable skill and contract sections.
  Claude's local `.claude/skills/` installs and permissions stay independent.
- Use the tools actually available. Claude's Agent, SendMessage, Remote Control
  and question-tool names in the contracts describe Claude's implementation;
  they are not tools Codex can assume exist. See `docs/codex.md` for equivalents
  and what to do when a required capability is absent.
- Keep the user's model choice. Ask before a new task or review when that choice
  has not already been supplied. A quota recommendation is only a suggestion.
  An OpenAI implementation excludes the whole OpenAI reviewer family; for the
  existing scripts use `-ExcludeModel sol`. Do not substitute model names silently.
- Implement tasks in their own worktrees as the process requires, including when
  the main session implements. Reviewers verify the supplied HEAD and diff before
  reviewing. Do not treat a documentation adapter as permission to bypass a gate.
- Use scoped `rg`/`git ls-files` and bounded reads. Do not dump world data, terrain
  sidecars, fixture corpora, logs or credentials. Never read `auth.json`.
- Build `IC2.sln` before testing: process tests execute the built CLI. Build
  `godot/IC2.MapViewer.csproj` separately for UI changes. Use .NET 10 and the
  Godot version pinned by that project. Commands and fixture setup: `docs/codex.md`.

Status stays in GitHub labels. Contracts stay in the repository. Living reference
stays in the wiki. Preserve independent review, green CI, full findings, durable
running notes and the project's evidence/provenance rules.
