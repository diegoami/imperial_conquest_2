# Environment

What this machine provides to the build, beyond the toolchain that [operating-guide.md](operating-guide.md) describes.

## Model quota availability: quota-tracker

Adopted from harness_imperial (L50) by the user's decision of 2026-10-05; [CLAUDE.md](../CLAUDE.md) rules 17 and 18 apply it.

A local service, quota-tracker, reports how much subscription quota is left on these providers: claude, openai (the ChatGPT plan, used through OpenCode), zai (the GLM Coding Plan), opencode_go (OpenCode Go), alibaba (the Alibaba Token Plan, OpenCode provider `alibaba-token-plan`), minimax (the minimax.io Token Plan, OpenCode provider `minimax`) and openrouter (prepaid credit). Check it before choosing, recommending or delegating to a model, whenever you need to know whether a provider can be used right now.

### Querying

The service is read-only, on localhost, with no authentication. Results are cached for 60 s.

- `curl -s localhost:8765/quota` lists every provider.
- `curl -s localhost:8765/quota/<provider>` gives one provider.
- `curl -s localhost:8765/best` lists the providers with quota left, most headroom first.
- `curl -s localhost:8765/avoid` lists the providers that are out of quota, with when each is usable again.
- `curl -s 'localhost:8765/usage?since=7d'` sums the recorded usage; `curl -s 'localhost:8765/usage/sessions?since=7d&model=…&effort=…'` lists it per session. Both filter by `provider`, `model` and `effort`; `since` takes `90m`, `24h`, `7d`, `4w` or `all`.
- Add `?refresh` to bypass the cache.
- **Pricing windows** (CLAUDE.md rule 20, the owner's decision of 2026-10-06): `curl -s localhost:8765/quota/alibaba | jq .pricing` gives Alibaba's discount window (`discount_now`, `next_change_at`, `discount_pct` per model: 22:00–08:00 UTC+8 daily, Qwen and DeepSeek models only, not GLM); `curl -s localhost:8765/quota/zai | jq .pricing` gives Z.ai's peak (`peak_now`, `next_change_at`, `multiplier`: Mon–Fri 14:00–18:00 UTC+8, `glm-5.3` at 3× quota at peak; `promo_off_peak_until` is the end of a promotion that keeps every hour off-peak, 2026-10-07 16:00 UTC). `next_change_at` and `promo_off_peak_until` are Unix times.

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
| alibaba | `qwen`: `alibaba-token-plan/qwen3.8-max`, effort `low` (it offers `low`, `medium`, `xhigh`); and, as the second route of the same names, `deepseek-pro`: `alibaba-token-plan/deepseek-v4-pro-0813` (`high`; the dated id, which gets the night discount) and `glm`: `alibaba-token-plan/glm-5.3` (`low`) | `qwen-flash`: `alibaba-token-plan/qwen3.8-flash`, effort `medium` (no `high`); `deepseek-flash` / `deepseek`: `alibaba-token-plan/deepseek-v4.1-flash`. No light GLM (Z.AI has `glm-5.3-flash`) |
| minimax | `mm-m3`: `minimax/MiniMax-M3`, variant `thinking` (it offers only `none` and `thinking`) | `mm-m2.7`: `minimax/MiniMax-M2.7` (no variants) |
| openrouter | `openrouter/deepseek/deepseek-v4-pro` (no script name yet) | `openrouter/deepseek/deepseek-v4.1-flash` (no script name yet) |

Heavy models run at `medium` rather than `high`, or lighter when medium is not needed (the user's decision of 2026-10-05). Where a model offers no `medium`, the table names the variant chosen.

**MiniMax and the Alibaba names** (the owner's decision of 2026-10-06). `external-review.ps1` (`-Reviewer`, `-ExcludeModel`) and `external-implement.ps1` (`-Model`) accept these names. No `auto` chain picks them: the main session names one explicitly, choosing case by case from quota-tracker and the model's strength. A `minimax/…` model's quota provider is `minimax`, so `/avoid` gates it like the others.

| Name | Id | Variant | Family | Key | Quota |
| --- | --- | --- | --- | --- | --- |
| `mm-m3` | `minimax/MiniMax-M3` | `thinking` (of `none`, `thinking`) | minimax | the `minimax` entry in the usual data folder's `auth.json`, set by the owner | `/quota/minimax` |
| `mm-m2.7` | `minimax/MiniMax-M2.7` | none (it offers none) | minimax | as above | `/quota/minimax` |
| `ali-deepseek-pro` | `alibaba-token-plan/deepseek-v4-pro-0813` | `high` (of `high`, `max`) | deepseek | `ALIBABA_TOKEN_PLAN_API_KEY` ([below](#the-alibaba-token-plan)) | `/quota/alibaba` |
| `ali-deepseek-flash` | `alibaba-token-plan/deepseek-v4.1-flash` | `high` (of `low`, `high`, `max`) | deepseek | as above | `/quota/alibaba` |
| `ali-glm` | `alibaba-token-plan/glm-5.3` | `low` (of `low`, `high`, `max`) | glm | as above | `/quota/alibaba` |

- **MiniMax is a vendor of its own.** It can review work by GLM, DeepSeek, Qwen, OpenAI or Claude models independently. `/quota/minimax` has a `5h` and a `7d` window. A one-prompt check, which bills a little: in PowerShell, `$env:XDG_DATA_HOME = "$env:USERPROFILE\.local\share\ic2-opencode-1x\data"`, then `$null | opencode run -m minimax/MiniMax-M3 --variant thinking "Reply with just: ok"`.
- **The `ali-*` names keep their model's family** (`vendors`), so `ali-glm` never reviews GLM's work and `ali-deepseek-*` never reviews DeepSeek's.
- **`ali-deepseek-pro` uses the dated `deepseek-v4-pro-0813`**, because only the dated id gets the night discount. The scripts' Alibaba route for `deepseek-pro` (`-Route alibaba`, or `auto` when OpenCode Go is avoided) uses it too.
- All five answered a probe on 2026-10-06, 19:43–19:44 CEST.

Facts that affect availability:

- **GPT-5.6 Luna has its own weekly limit.** For light work, openai stays usable while the `gpt-5.6-luna:7d` window in `/quota/openai` is under 95%, even when openai itself is exhausted.
- **openrouter is prepaid credit.** Its windows never reset, and `remaining_usd` is the balance.
- **openrouter's free models have their own allowance**, separate from the credit: `free_model_daily_requests` in `/quota/openrouter` (`used`, `limit`, `remaining`). See [The free OpenRouter models](#the-free-openrouter-models-advisory-only).
- **GLM's Coding Plan also has a 5-hour window.** When it runs out, an OpenCode run fails with "Usage limit reached for 5 hour" (seen on 2026-10-04); the `5h` window in `/quota/zai` shows it beforehand.

### Choosing a model: no fixed order

**Since T152 (2026-10-08, the user's decision, CLAUDE.md rule 17)** `scripts/Choose-Model.ps1` ranks by quota-tracker's `/recommend?tier=heavy|light`, never by headroom percentages: it maps each row's provider to an alias per role (an unmapped row is skipped), drops Claude while another candidate scores positive, keeps Luna and Sol out of implementation, puts a negative score last, prints each candidate's `score`, `confidence` and `reasons`, and stops with exit 3 when the tracker is loading too long or silent. `-RecommendFile` and `-RecommendUrl` make it testable offline (`-SelfTest`). The section below describes the pre-T152 chooser and is kept only for its history.

There is no fixed model order (the owner's decision of 2026-10-06). The main session chooses each run's model case by case, from live quota and the model's strength. `scripts/Choose-Model.ps1` does the mechanical half and chooses nothing:

- `pwsh scripts/Choose-Model.ps1 -Role reviewer -Tier complex -ExcludeModel <implementer>` prints the candidates best first, each with its reasons.
- `-Pick` prints the top name only.
- `-SelfTest` checks the ranking on fixture quotas.
- `-ExcludeFamily <name>[,<name>…]` (T150) drops every candidate of each named model's family, for either role. `external-implement.ps1` uses it for its substitute when the chain is spent: every failed family, plus `luna` (the whole OpenAI family), so the substitute is never OpenAI.

What it reads:

- **Names, ids, variants and families:** from `external-review.ps1` and `external-implement.ps1` themselves, so it never drifts from them.
- **Strength:** from the table below.
- **Quota:** quota-tracker's `/quota`, including the Alibaba discount and the Z.ai peak.

How it ranks:

1. The fit to the tier. `complex` puts heavy models first, `simple` light ones. `very-complex` ranks as `complex` and notes [build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)'s Claude Opus reviewer.
2. Status: `ok` before `low`.
3. Headroom divided by the cost factor now: Alibaba's discount, or Z.ai's peak or off-peak multiplier.

An exhausted provider, or one an answering tracker does not report, makes a model unavailable, unless the model has an Alibaba route with quota or is `luna` (its own window). An unknown headroom is scored `?` and ranked after every measured one. The implementer's family is left out of a review. When the tracker is silent, the ranking is by strength alone. The scripts' `auto` defaults are unchanged; any other model is named explicitly.

### Model strength

The chooser's table, edited by the owner. `heavy` and `light` follow [Models per provider](#models-per-provider-and-this-repositorys-names-for-them); `off` is never ranked. A name either script accepts and this table lacks is listed as unrated and never ranked; the chooser's self-test fails on it.

| Name | Strength | Notes |
| --- | --- | --- |
| `sol` | heavy | GPT-6 Sol; the complex tier's reviewer (§3.4) |
| `luna` | light | GPT-5.6 Luna; the simple tier's reviewer; its own weekly window |
| `glm` | heavy | GLM-5.3 on Z.ai |
| `ali-glm` | heavy | GLM-5.3 on Alibaba, no discount; the Z.ai peak's alternative |
| `glm-flash` | light | GLM-5.3 Flash on Z.ai |
| `deepseek-pro` | heavy | DeepSeek V4 Pro on Go (Alibaba when Go is avoided) |
| `ali-deepseek-pro` | heavy | DeepSeek V4 Pro 0813 on Alibaba, night discount |
| `deepseek` | light | DeepSeek V4.1 Flash, the reviewer name |
| `deepseek-flash` | light | DeepSeek V4.1 Flash, the implementer name |
| `ali-deepseek-flash` | light | DeepSeek V4.1 Flash on Alibaba, night discount |
| `qwen` | heavy | Qwen3.8 Max on Alibaba, night discount |
| `qwen-flash` | light | Qwen3.8 Flash on Alibaba, night discount |
| `mm-m3` | heavy | MiniMax M3, a family of its own |
| `mm-m2.7` | light | MiniMax M2.7 |
| `mimo-pro` | off | not on OpenCode Go's plan |
| `mimo-flash` | off | not on OpenCode Go's plan |

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
- **Shorter limits** (the owner's decision of 2026-10-06). OpenCode retries a rate-limited call inside the run, and the script cannot stop that. While it retries, the session's `updated` time stands still, so an advisory run is killed after **300 s idle** (not 600) and **1800 s in all** (not 3600) unless `-IdleTimeoutSec` or `-TotalTimeoutSec` is passed. A run killed this way exits 3 like any infrastructure failure and is not retried.
- **Nothing private reaches them.** Free providers may log and train on prompts. This repository and `diegoami/imperial-conquest-2-research` are public; the private fixtures repository `diegoami/ic2-test-fixtures`, the original game's DAT and saves, `assets.local.ini` and any key never go to these models. The script refuses (exit 1) a brief that contains any of the following. The refusal names only the rule that matched and never echoes the matched text:
  - `ic2-test-fixtures`, `assets.local.ini` or `IC2_FIXTURES_DIR`;
  - a `.dat` or `.sav` path or save name;
  - a common secret prefix (`sk-`, `sk-or-`, `sk-ant-`, `ghp_`, `gho_`, `github_pat_`, `xoxb-`, `AKIA…`, `Bearer …`);
  - an assignment to a `…KEY`, `…TOKEN`, `…SECRET` or `…PASSWORD` name, whatever its value, an empty one included;
  - a long high-entropy string (a 40-hex commit id and a 64-hex hash pass);
  - the literal value of a key variable in the environment (`ALIBABA_TOKEN_PLAN_API_KEY`, `OPENROUTER_API_KEY` and any `…KEY`/`…TOKEN`/`…SECRET`/`…PASSWORD` variable, compared without printing).

  It also refuses `-FixturesDir` and runs OpenCode without `IC2_FIXTURES_DIR`. **The brief is the only leak path the guard covers**: the model is told to read the PR, which is public on GitHub like the rest of this repository, and its agent cannot reach outside its worktree. So keep the brief to public material, and never paste a fixture's content, a save's bytes or a log that may carry a key into it.
- **The key** is OpenRouter's entry in the copied `auth.json` of the usual data folder (`…\ic2-opencode-1x\data`); the owner sets it. Never read or print it. A check, which spends one free request: in PowerShell, `$env:XDG_DATA_HOME = "$env:USERPROFILE\.local\share\ic2-opencode-1x\data"`, then `opencode models openrouter | Select-String ':free'` and `$null | opencode run -m openrouter/nvidia/nemotron-3-ultra-550b-a55b:free "Reply with just: ok"`.

### The Alibaba Token Plan

Adopted by the owner's decision of 2026-10-05, for three uses: the DeepSeek route when OpenCode Go is low, Qwen as a model family of its own (implementer `qwen-flash`, reviewers `qwen` and `qwen-flash`), and GLM-5.3 when Z.AI is out.

- **One pool.** Every model draws on one monthly credit pool: `curl -s localhost:8765/quota/alibaba`, window `month`; the provider is `alibaba` in `/avoid`, `/best` and `/quota`.
- **Night discount**, 22:00–08:00 UTC+8 (14:00–00:00 UTC; 16:00–02:00 in European summer time, 15:00–01:00 in winter): `qwen3.8-max` and `qwen3.8-flash` cost 60% fewer credits, and `deepseek-v4-pro-0813` and `deepseek-v4.1-flash` 50% fewer. `glm-5.3` gets no discount. Prefer long, deferrable Qwen and DeepSeek runs on Alibaba while `pricing.discount_now` is true (CLAUDE.md rule 20).
- **Z.ai's peak**, from 8 October 2026: `glm-5.3` costs 3× quota Mon–Fri 14:00–18:00 UTC+8 (08:00–12:00 in European summer time). While `/quota/zai`'s `pricing.peak_now` is true, prefer `ali-glm` or another provider for long runs.
- **Never use** Kimi or MiniMax on Alibaba: they are Team-edition only and fail on this plan. MiniMax runs on its own provider, `minimax`.
- **Routes.** `external-review.ps1` and `external-implement.ps1` take `-Route auto|go|zai|alibaba`. `auto` (the default) runs DeepSeek on OpenCode Go and GLM on Z.AI unless `/avoid` lists `opencode_go` or `zai`, and then the same model on Alibaba; when the tracker does not answer, the usual route. The name and the family do not change with the route. The route is printed, logged, and named on the posted review's signature line and the "implemented by:" line.
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
