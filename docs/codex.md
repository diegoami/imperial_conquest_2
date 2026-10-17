# Codex compatibility

Codex and Claude operate the same project process. `AGENTS.md` is the Codex and
OpenCode entry point; `CLAUDE.md` remains the shared rules and Claude Code entry
point. `operating-guide.md`, `build-process.md` and the evidence documents remain
the contracts. Tool mappings below adapt their execution without changing gates,
task ownership, model choices or the source of project status.

## Instruction and skill discovery

Start Codex at the repository root. It loads `AGENTS.md`; that file instructs it
to read the shared rules. Repository skills are tracked under `.agents/skills/`:

| Workflow | Codex invocation | Process source |
| --- | --- | --- |
| Task or fix | `$run-task T<nn>` or `$run-task #<issue>` | `build-process.md` section 4 and Appendix C |
| Evidence intake | `$process-evidence <input>` | `evidence-pipeline.md`, including its fenced skill |
| Recording analysis | `$parse-recording <input>` | `recording-analysis.md`, including its fenced skill |

These are short adapters, not copies of the process. Claude retains its
git-ignored `.claude/skills/` installations and local settings. Do not overwrite
them with the Codex versions. Restart or refresh the Codex session if newly added
skills are not listed; they can also be read explicitly by path.

OpenCode previously fell back to `CLAUDE.md` in the absence of `AGENTS.md`.
Its new entry point explicitly reads the same shared rules. Its external agent
definition and supplied role brief still control implementation/review duties;
startup triage and merging are main-session responsibilities.

Official discovery references: [repository instructions](https://learn.chatgpt.com/docs/agent-configuration/agents-md)
and [skills](https://learn.chatgpt.com/docs/build-skills).

## Tools and roles

| Contract terminology | Codex execution |
| --- | --- |
| Main session / Opus | The user's active Codex session can perform this role. This does not change an explicit model choice for a dispatched stage. |
| Read, Grep, Bash | Available file/shell tools; this Windows checkout uses PowerShell. Prefer scoped `rg`, bounded reads and `git ls-files`. Use Git Bash explicitly for Bash scripts. |
| Question tool | Available user-input tool, or a concise question when none exists. Preserve a choice already made for the current task. |
| Agent, fresh agent | Available collaboration tools only when the requested workflow authorizes delegation and the chosen model is available. Each implementer/reviewer gets the required separate worktree. |
| SendMessage to a delegated agent | Available collaboration messaging for an agent in the current team. Read its final report before retrying. |
| Remote Control / another Claude session | No assumed equivalent. Prepare a relayable request and report it unsent if the required integration is absent. |
| OpenCode implementation/review | Existing `external-implement.ps1`, `external-review.ps1` and `Invoke-OpenCodeWatched.ps1`; retain their monitoring, logs, notes and verdict rules. |

The standing choices in [CLAUDE.md rules 17–18](../CLAUDE.md#model-choice) authorize
delegated authors and direct Kimi review without asking again for each task/recheck.
The main session assesses difficulty and records the actual model and effort:

| Difficulty | User choice | Native Codex mapping |
| --- | --- | --- |
| Easy | Luna High | Available Luna family, `high` |
| Medium | Sol light | Available Sol family, `low` |
| Hard | Sol medium | Available Sol family, `medium` |
| Very hard | Sol high | Available Sol family, `high` |

Resolve identifiers from this session's tools; these family names are not version pins.
Where collaboration requires it, use `fork_turns="none"` with the model/effort override
and paste the full brief. Explicit per-task overrides persist through rework. Ask only
if the chosen capability/model/effort is unavailable; never silently substitute.
Local Claude permissions are not Codex permissions; actual sandbox/approval policy
applies to sibling worktrees and the research repository. Do not expand permissions.

The reviewer is direct **Kimi For Coding 1M**, `kimi-code-plan-cn/kimi-for-coding`, at
`https://api.kimi.com/coding/v1`, including plans and rechecks, never OpenCode Go.
Use an available direct review capability with the required isolated worktree, supplied
HEAD, full findings, posting and monitoring; this document adds no harness route.
If unavailable, diagnose/report and ask before another reviewer or route.
For an OpenAI author, existing reviewer/chooser scripts use `-ExcludeModel sol` to
exclude the entire OpenAI family, including Luna. Arbitrary native model identifiers
are not script aliases; do not add a `codex` alias or call the same family independent.

## Build and verification

Run from the actual checkout or worktree being checked:

```powershell
dotnet build IC2.sln --nologo -v q
dotnet test IC2.sln --no-build --nologo -v q
dotnet build godot/IC2.MapViewer.csproj --nologo -v q
```

Use the same configuration for build and test (`--configuration Release` for
both when checking Release). Solution tests spawn the existing CLI DLL from
`src/IC2.Cli/bin/<configuration>/<framework>/`; testing alone can execute an old
CLI, and some process tests return without exercising it when no DLL exists.
Build the whole solution first even when a test project builds successfully.

Godot is excluded from the solution; use the SDK version in its `.csproj` and
the corresponding .NET editor. UI changes also need the relevant Godot check
scenes and, where specified, visual evidence. Headless screenshots are unsupported.
Run Godot processes sequentially. Read the CI scene loop for completion-marker
and FAIL-line checks rather than treating exit code alone as success.

For the original-file tests, set `IC2_FIXTURES_DIR` to this machine's private
`ic2-test-fixtures` checkout for the CI corpus. `assets.local.ini` selects a local
original installation whose corpus can contain additional saves. Do not regenerate
committed expectations merely to make an arbitrary local corpus green. Missing
private-fixture prerequisites must be reported as skipped/unavailable coverage.

Offline harness checks (no model dispatch or GitHub posting):

```powershell
pwsh -NoProfile -File scripts/external-review.ps1 -SelfTest
pwsh -NoProfile -File scripts/external-implement.ps1 -SelfTest
pwsh -NoProfile -File scripts/Choose-Model.ps1 -SelfTest
pwsh -NoProfile -File scripts/Clean-Worktrees.ps1 -SelfTest
```

These verify harness mechanics; they do not constitute an independent PR review
or validate a provider's live login. Preserve the process's approving review and
green CI requirements. Documentation-only changes currently skip CI, so record
their local validation explicitly instead of claiming a CI run occurred.
