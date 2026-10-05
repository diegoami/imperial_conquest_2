# Environment

What this machine provides to the build, beyond the toolchain that [operating-guide.md](operating-guide.md) describes.

## Model quota availability: quota-tracker

Adopted from harness_imperial (L50) by the user's decision of 2026-10-05; [CLAUDE.md](../CLAUDE.md) rules 17 and 18 apply it.

A local service, quota-tracker, reports how much subscription quota is left on these providers: claude, openai (the ChatGPT plan, used through OpenCode), zai (the GLM Coding Plan), opencode_go (OpenCode Go), alibaba (the Alibaba Token Plan, OpenCode provider `alibaba-token-plan`) and openrouter (prepaid credit). Check it before choosing, recommending or delegating to a model, whenever you need to know whether a provider can be used right now.

### Querying

The service is read-only, on localhost, with no authentication. Results are cached for 60 s.

- `curl -s localhost:8765/quota` lists every provider.
- `curl -s localhost:8765/quota/<provider>` gives one provider.
- `curl -s localhost:8765/best` lists the providers with quota left, most headroom first.
- `curl -s localhost:8765/avoid` lists the providers that are out of quota, with when each is usable again.
- `curl -s 'localhost:8765/usage?since=7d'` sums the recorded usage; `curl -s 'localhost:8765/usage/sessions?since=7d&model=…&effort=…'` lists it per session. Both filter by `provider`, `model` and `effort`; `since` takes `90m`, `24h`, `7d`, `4w` or `all`.
- Add `?refresh` to bypass the cache.

Each provider reports these fields:

- `status`:
  - `ok`: under 80% used;
  - `low`: 80% or more;
  - `exhausted`: 95% or more, so don't use it until `available_at` / `available_in`;
  - `error`: it couldn't be checked, and `error` says why;
  - `not_configured`.
- `headroom_pct`: the percent left on its most-used window.
- `windows[]`: every limit, with `name`, `used_pct`, `resets_at` (unix seconds) and `resets_in`.

### Models per provider, and this repository's names for them

The scripts' names are `-Reviewer` / `-Model` values for `scripts/external-review.ps1` and `scripts/external-implement.ps1`. Model ids always come from the provider's live list (`opencode models <provider>`), never from memory.

| Provider | Heavy | Light |
| --- | --- | --- |
| claude | Claude Opus (an Agent's `opus`) | Claude Sonnet (`sonnet`) |
| openai | `sol`: `openai/gpt-6-sol`, effort `low`, or `medium` where it earns it ([build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)) | `luna`: `openai/gpt-5.6-luna`, effort `high` (CLAUDE.md rule 18) |
| zai | `glm`: `zai-coding-plan/glm-5.3`, effort `low` (it offers only `low`, `high` and `max`) | `glm-flash`: `zai-coding-plan/glm-5.3-flash`, effort `high` |
| opencode_go | `deepseek-pro`: `opencode-go/deepseek-v4-pro`, effort `high` (it offers only `high` and `max`) | `deepseek-flash`: `opencode-go/deepseek-v4.1-flash` |
| alibaba | `qwen`: `alibaba-token-plan/qwen3.8-max`, effort `low` (it offers `low`, `medium`, `xhigh`); and, as the second route of the same names, `deepseek-pro`: `alibaba-token-plan/deepseek-v4-pro` (`high`) and `glm`: `alibaba-token-plan/glm-5.3` (`low`) | `qwen-flash`: `alibaba-token-plan/qwen3.8-flash`, effort `medium` (no `high`); `deepseek-flash` / `deepseek`: `alibaba-token-plan/deepseek-v4.1-flash`. No light GLM (Z.AI has `glm-5.3-flash`) |
| openrouter | `openrouter/deepseek/deepseek-v4-pro` (no script name yet) | `openrouter/deepseek/deepseek-v4.1-flash` (no script name yet) |

Heavy models run at `medium` rather than `high`, or lighter when medium is not needed (the user's decision of 2026-10-05). Where a model offers no `medium`, the table names the variant chosen.

Facts that affect availability:

- **GPT-5.6 Luna has its own weekly limit.** For light work, openai stays usable while the `gpt-5.6-luna:7d` window in `/quota/openai` is under 95%, even when openai itself is exhausted.
- **openrouter is prepaid credit.** Its windows never reset, and `remaining_usd` is the balance.
- **openrouter's free models have their own allowance**, separate from the credit: `free_model_daily_requests` in `/quota/openrouter` (`used`, `limit`, `remaining`). See [The free OpenRouter models](#the-free-openrouter-models-advisory-only).
- **GLM's Coding Plan also has a 5-hour window.** When it runs out, an OpenCode run fails with "Usage limit reached for 5 hour" (seen on 2026-10-04); the `5h` window in `/quota/zai` shows it beforehand.

### The free OpenRouter models (advisory only)

Adopted by the owner's decision of 2026-10-06, as a supplement only: for smaller tasks and additional reviews, a second opinion next to a regular model, never the main model for important work.

| Script name | Model | Effort | Notes |
| --- | --- | --- | --- |
| `nemotron` | `openrouter/nvidia/nemotron-3-ultra-550b-a55b:free` | `medium` (it offers `medium`, `high`) | the stronger one |
| `north-mini` | `openrouter/cohere/north-mini-code:free` | `high` (light) | coding-focused, faster |
| `inkling` | `openrouter/thinkingmachines/inkling:free` | `medium` | through OpenCode only, not the raw API |
| `laguna` | `openrouter/poolside/laguna-s-2.1:free` | `medium` | often rate-limited |

- **Advisory only.** In `scripts/external-review.ps1` they are `-Reviewer` values whose comment is headed "<Task or Plan> review (<Name>, advisory — not counted)". They never apply a status label (`-ApplyLabel` is refused, exit 1), never count toward a review tier, and belong to no model family, so they never exclude and are never excluded. No implementer script offers them. Their output is checked like any unreviewed contribution ([build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)).
- **One shared allowance**: 1,000 requests a day and about 20 a minute across every free model, and each agent step is one request, so one review can take tens. Read what is left with `curl -s localhost:8765/quota/openrouter` (`free_model_daily_requests.remaining`). The script skips an advisory run (exit 3) when 50 or fewer are left, or when the tracker does not answer.
- **A 429 is skipped, never retried.** Free models come and go and get rate-limited. A 429 or rate-limit error in OpenCode's own `Error: ` stderr lines (never the model's words) ends the run with exit 3, "rate-limited, skipped".
- **Nothing private reaches them.** Free providers may log and train on prompts. This repository and `diegoami/imperial-conquest-2-research` are public; the private fixtures repository `diegoami/ic2-test-fixtures`, the original game's DAT and saves, `assets.local.ini` and any key never go to these models. The script refuses (exit 1) a brief that contains any of the following. The refusal names only the rule that matched and never echoes the matched text:
  - `ic2-test-fixtures`, `assets.local.ini` or `IC2_FIXTURES_DIR`;
  - a `.dat` or `.sav` path or save name;
  - a common secret prefix (`sk-`, `sk-or-`, `sk-ant-`, `ghp_`, `gho_`, `github_pat_`, `xoxb-`, `AKIA…`, `Bearer …`);
  - an assignment to a `…KEY`, `…TOKEN`, `…SECRET` or `…PASSWORD` name;
  - a long high-entropy string (a 40-hex commit id and a 64-hex hash pass);
  - the literal value of a key variable in the environment (`ALIBABA_TOKEN_PLAN_API_KEY`, `OPENROUTER_API_KEY` and any `…KEY`/`…TOKEN`/`…SECRET`/`…PASSWORD` variable, compared without printing).

  It also refuses `-FixturesDir` and runs OpenCode without `IC2_FIXTURES_DIR`. **The brief is the only leak path the guard covers**: the model is told to read the PR, which is public on GitHub like the rest of this repository, and its agent cannot reach outside its worktree. So keep the brief to public material, and never paste a fixture's content, a save's bytes or a log that may carry a key into it.
- **The key** is OpenRouter's entry in the copied `auth.json` of the usual data folder (`…\ic2-opencode-1x\data`); the owner sets it. Never read or print it. A check, which spends one free request: in PowerShell, `$env:XDG_DATA_HOME = "$env:USERPROFILE\.local\share\ic2-opencode-1x\data"`, then `opencode models openrouter | Select-String ':free'` and `$null | opencode run -m openrouter/nvidia/nemotron-3-ultra-550b-a55b:free "Reply with just: ok"`.

### The Alibaba Token Plan

Adopted by the owner's decision of 2026-10-05, for three uses: the DeepSeek route when OpenCode Go is low, Qwen as a model family of its own (implementer `qwen-flash`, reviewers `qwen` and `qwen-flash`), and GLM-5.3 when Z.AI is out.

- **One pool.** Every model draws on one monthly credit pool: `curl -s localhost:8765/quota/alibaba`, window `month`; the provider is `alibaba` in `/avoid`, `/best` and `/quota`.
- **Night discount**, 22:00–08:00 UTC+8 (14:00–00:00 UTC): DeepSeek costs 50% fewer credits and Qwen 60% fewer. Long, deferrable runs on Alibaba are cheaper then.
- **Routes.** `external-review.ps1` and `external-implement.ps1` take `-Route auto|go|zai|alibaba`. `auto` (the default) runs DeepSeek on OpenCode Go and GLM on Z.AI unless `/avoid` lists `opencode_go` or `zai`, and then the same model on Alibaba; when the tracker does not answer, the usual route. The name and the family do not change with the route. The route is printed, logged, and named on the posted review's signature line and the "implemented by:" line.
- **Never use** Kimi or MiniMax on Alibaba: they are Team-plan only.
- **The key** is the environment variable `ALIBABA_TOKEN_PLAN_API_KEY`, a Windows **user** variable. Both scripts copy it from the user environment into their own process when the process lacks it (a session started before it was set), never printing it. It never goes into any `auth.json`: a key there overrides the variable and can break the provider. A run on an `alibaba-token-plan/…` model gets its own OpenCode data folder, `%USERPROFILE%\.local\share\ic2-opencode-1x\data-alibaba` (beside the other providers' `data`; cache and state are shared), created when missing; nothing is copied into it, and a run that finds an `auth.json` there stops without reading it (the owner's decision of 2026-10-05). Runs on other providers keep `data` and its copied `auth.json` as before. An Alibaba run's session is read with `python scripts/read-opencode-session.py <ses_…> --alibaba`, and its OpenCode log is under `data-alibaba\opencode\log\`. Never print, copy or log it, and never read an `auth.json`.

Errors from it are never retried blind; the scripts stop (exit 1, not the chain's exit 3) with the cause:

- **`Invalid API-key`**: a stale `alibaba-token-plan` key overrides the variable: an `auth.json` in that run's `XDG_DATA_HOME` (for the scripts, `%USERPROFILE%\.local\share\ic2-opencode-1x\data-alibaba\opencode`, which must hold none; by hand, whichever folder `XDG_DATA_HOME` named) or a provider key in `~\.config\opencode\opencode.json`. Stop and report which folder; the owner removes the entry.
- **`Provider not found`**: the variable isn't in the process's environment. In WSL it comes from `~/.config/ai-keys.env` (loaded by `~/.bashrc` and `~/.profile`) and, for commands started from Windows, from `WSLENV`; on Windows it is a user variable. Restart the session or shell so it picks it up; if it is still missing, tell the owner. Never read or print `~/.config/ai-keys.env`.

A one-prompt check, which bills a little: in PowerShell, `$env:XDG_DATA_HOME = "$env:USERPROFILE\.local\share\ic2-opencode-1x\data-alibaba"`, `$env:ALIBABA_TOKEN_PLAN_API_KEY = [Environment]::GetEnvironmentVariable('ALIBABA_TOKEN_PLAN_API_KEY','User')`, then `$null | opencode run -m alibaba-token-plan/qwen3.8-flash "Reply with just: ok"`. `opencode models alibaba-token-plan --verbose` lists the models and their variants.

### If the service isn't running

Check it with `curl -sf localhost:8765/health`. If that fails, the service is the owner's to start. Tell the user, and go on without it. Count a usage-limit error as `exhausted` (CLAUDE.md rule 17).

### Don't

- Don't read or edit quota-tracker's configuration file: it holds account tokens.
- If a provider shows `error` about an expired cookie or token, tell the user. Renewing it needs their browser or login.
