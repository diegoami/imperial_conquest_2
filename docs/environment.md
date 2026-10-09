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
- **Pricing windows** (CLAUDE.md rule 20, the owner's decision of 2026-10-06): `curl -s localhost:8765/quota/alibaba | jq .pricing` gives Alibaba's discount window (`discount_now`, `next_change_at`, `discount_pct` per model: 22:00–08:00 UTC+8 daily, Qwen models only (DeepSeek is not used), not GLM); `curl -s localhost:8765/quota/zai | jq .pricing` gives Z.ai's peak (`peak_now`, `next_change_at`, `multiplier`: Mon–Fri 14:00–18:00 UTC+8, `glm-5.3` at 3× quota at peak; `promo_off_peak_until` is the end of a promotion that keeps every hour off-peak, 2026-10-07 16:00 UTC). `next_change_at` and `promo_off_peak_until` are Unix times.

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

**DeepSeek is not used** (the user's decision of 2026-10-09): no DeepSeek model, on any route, implements or reviews. OpenCode Go's heavy and light models are MiMo v2.6 Pro and Flash, tested on implement, fix and review tasks that day, and MiMo is a family of its own for the reviewer rule.

The scripts' names are `-Reviewer` / `-Model` values for `scripts/external-review.ps1` and `scripts/external-implement.ps1`. Model ids always come from the provider's live list (`opencode models <provider>`), never from memory.

| Provider | Heavy | Light |
| --- | --- | --- |
| claude | Claude Opus (an Agent's `opus`) | Claude Sonnet (`sonnet`) |
| openai | `sol`: `openai/gpt-6-sol`, effort `low`, or `medium` where it earns it ([build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)) | `luna`: `openai/gpt-5.6-luna`, effort `high` (CLAUDE.md rule 18) |
| zai | `glm`: `zai-coding-plan/glm-5.3`, effort `low` (it offers only `low`, `high` and `max`) | `glm-flash`: `zai-coding-plan/glm-5.3-flash`, effort `high` |
| opencode_go | `mimo-pro`: `opencode-go/mimo-v2.6-pro` | `mimo-flash`: `opencode-go/mimo-v2.6-flash` |
| alibaba | `qwen`: `alibaba-token-plan/qwen3.8-max`, effort `low` (it offers `low`, `medium`, `xhigh`) |
| minimax | `mm-m3`: `minimax/MiniMax-M3`, variant `thinking` (it offers only `none` and `thinking`) | `mm-m2.7`: `minimax/MiniMax-M2.7` (no variants) |
| openrouter | paid models at score 0 (prepaid credit); no script name, and no DeepSeek model | the free models: advisory reviewers only ([below](#the-free-openrouter-models-advisory-only)) |

Heavy models run at `medium` rather than `high`, or lighter when medium is not needed (the user's decision of 2026-10-05). Where a model offers no `medium`, the table names the variant chosen.

**MiniMax and the Alibaba names** (the owner's decision of 2026-10-06). `external-review.ps1` (`-Reviewer`, `-ExcludeModel`) and `external-implement.ps1` (`-Model`) accept these names. No `auto` chain picks them: the main session names one explicitly, choosing case by case from quota-tracker and the model's strength. A `minimax/…` model's quota provider is `minimax`, so `/avoid` gates it like the others.

| Name | Id | Variant | Family | Key | Quota |
| --- | --- | --- | --- | --- | --- |
| `mm-m3` | `minimax/MiniMax-M3` | `thinking` (of `none`, `thinking`) | minimax | the `minimax` entry in the usual data folder's `auth.json`, set by the owner | `/quota/minimax` |
| `mm-m2.7` | `minimax/MiniMax-M2.7` | none (it offers none) | minimax | as above | `/quota/minimax` |
| `ali-glm` | `alibaba-token-plan/glm-5.3` | `low` (of `low`, `high`, `max`) | glm | as above | `/quota/alibaba` |

- **MiniMax is a vendor of its own.** It can review work by GLM, MiMo, Qwen, OpenAI or Claude models independently. `/quota/minimax` has a `5h` and a `7d` window. A one-prompt check, which bills a little: in PowerShell, `$env:XDG_DATA_HOME = "$env:USERPROFILE\.local\share\ic2-opencode-1x\data"`, then `$null | opencode run -m minimax/MiniMax-M3 --variant thinking "Reply with just: ok"`.
- **The `ali-*` names keep their model's family** (`vendors`), so `ali-glm` never reviews GLM's work.
- All five answered a probe on 2026-10-06, 19:43–19:44 CEST.

Facts that affect availability:

- **GPT-5.6 Luna uses OpenAI's main quota, like Sol** (the user's decision of 2026-10-09; `/quota/openai` no longer has a separate `gpt-5.6-luna:7d` window). Only when OpenAI is exhausted does Luna's own limit matter: `/quota/openai` then lists `gpt-5.6-luna` in `when_exhausted.usable_models`, and Luna alone can still run. `Get-QuotaAvoid` reads that field and `Resolve-OpenCodeRoute` lets Luna through.
- **openrouter is prepaid credit.** Its windows never reset, and `remaining_usd` is the balance.
- **openrouter's free models have their own allowance**, separate from the credit: `free_model_daily_requests` in `/quota/openrouter` (`used`, `limit`, `remaining`). See [The free OpenRouter models](#the-free-openrouter-models-advisory-only).
- **GLM's Coding Plan also has a 5-hour window.** When it runs out, an OpenCode run fails with "Usage limit reached for 5 hour" (seen on 2026-10-04); the `5h` window in `/quota/zai` shows it beforehand.

### Choosing a model: no fixed order

`scripts/Choose-Model.ps1` chooses from quota-tracker's `/recommend`, never from headroom percentages (CLAUDE.md rule 17; T152, and #893 of 2026-10-09). It maps each row's provider to an alias per role (an unmapped row is printed and skipped) and keeps this repository's exclusions on top of the tracker's order:

- OpenAI never implements; Luna and Sol reach a review only through the tier rules ([build-process.md §3.4](build-process.md#34-why-the-reviewers-model-differs-from-the-implementers)).
- Claude, the orchestrator, is dropped while another candidate scores positive.
- **Alibaba is never chosen unless the user asks** (the user's decision of 2026-10-09: its monthly pool is 91% used until 2026-11-06); `-AllowAlibaba` puts it back.
- A negative score is ranked last and chosen only when nothing else is left; a row the tracker marks unusable is skipped.

Its modes:

- `pwsh scripts/Choose-Model.ps1 -Role implementer` (`/recommend?tier=heavy`) or `-Role reviewer` (`tier=light`, for Sol's substitutes) prints the candidates best first with `score`, `confidence` and `reasons`; `-Pick` prints the top alias only.
- `-Role pair` gives one delegated task's implementer and reviewer from one `tier=heavy` response: the tracker's `pair.implementer` when it survives the exclusions (else the top ranked implementer), and `pair.reviewer` when it is of another family (else the next `ranking` row of another family). No reviewer left means no other family has quota: it says so and exits 3, and the main session tells the user. The tracker's `free_reviewer` is printed as advisory only.
- `-ExcludeModel <implementer>` (reviewer and pair) and `-ExcludeFamily <name>[,<name>…]` drop whole families; `-SubstituteFamilies` is T150's chain-spent substitute filter (the failed families plus OpenAI).
- `-RecommendFile` and `-RecommendUrl` make it testable offline; `-SelfTest` runs the canned cases.
- A tracker still loading after `-RecommendRetryWaitSec`, or not answering, exits 3; the main session restarts the service (`systemctl --user restart quota-tracker` in WSL), retries, and tells the user if it still fails.

Every `external-implement.ps1` run takes the explicit `-Model` chosen this way; `-Model auto` was removed by #893. The main session logs the chosen row's `reasons` on the task's issue.

### Model strength

The chooser's table, edited by the owner. `heavy` and `light` follow [Models per provider](#models-per-provider-and-this-repositorys-names-for-them); `off` is never ranked. A name either script accepts and this table lacks is listed as unrated and never ranked; the chooser's self-test fails on it.

| Name | Strength | Notes |
| --- | --- | --- |
| `sol` | heavy | GPT-6 Sol; the complex tier's reviewer (§3.4) |
| `luna` | light | GPT-5.6 Luna; the simple tier's reviewer; its own weekly window |
| `glm` | heavy | GLM-5.3 on Z.ai |
| `ali-glm` | heavy | GLM-5.3 on Alibaba, no discount; only when the user asks (#893) |
| `glm-flash` | light | GLM-5.3 Flash on Z.ai |
| `qwen` | heavy | Qwen3.8 Max on Alibaba, night discount |
| `qwen-flash` | light | Qwen3.8 Flash on Alibaba, night discount |
| `mm-m3` | heavy | MiniMax M3, a family of its own |
| `mm-m2.7` | light | MiniMax M2.7 |
| `mimo-pro` | heavy | MiMo v2.6 Pro on OpenCode Go, a family of its own; implements and reviews (the user's decision of 2026-10-09) |
| `mimo-flash` | light | MiMo v2.6 Flash on OpenCode Go, the same family |

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

Adopted by the owner's decision of 2026-10-05, for Qwen as a model family of its own (implementer `qwen-flash`, reviewers `qwen` and `qwen-flash`) and GLM-5.3 when Z.AI is out. Its DeepSeek route is gone with DeepSeek itself (2026-10-09), and the plan is used only when the user asks (#893).

- **One pool.** Every model draws on one monthly credit pool: `curl -s localhost:8765/quota/alibaba`, window `month`; the provider is `alibaba` in `/avoid`, `/best` and `/quota`.
- **Night discount**, 22:00–08:00 UTC+8 (14:00–00:00 UTC; 16:00–02:00 in European summer time, 15:00–01:00 in winter): `qwen3.8-max` and `qwen3.8-flash` cost 60% fewer credits. `glm-5.3` gets no discount. When the user asks for an Alibaba run (#893: never otherwise), prefer a long, deferrable Qwen one while `pricing.discount_now` is true (CLAUDE.md rule 20).
- **Z.ai's peak**, from 8 October 2026: `glm-5.3` costs 3× quota Mon–Fri 14:00–18:00 UTC+8 (08:00–12:00 in European summer time). While `/quota/zai`'s `pricing.peak_now` is true, prefer another provider for long runs (not `ali-glm` unless the user asks: #893).
- **Never use** Kimi or MiniMax on Alibaba: they are Team-edition only and fail on this plan. MiniMax runs on its own provider, `minimax`.
- **Routes.** `external-review.ps1` and `external-implement.ps1` take `-Route auto|go|zai|alibaba`. `auto` (the default) runs MiMo on OpenCode Go and GLM on Z.AI. When `/avoid` lists `opencode_go` or `zai` the model is marked avoided (a review drops it; an implementer run warns), and it is never moved to Alibaba, which runs only when the user asks, with `-Route alibaba` (#893). When the tracker does not answer, the usual route. The name and the family do not change with the route. The route is printed, logged, and named on the posted review's signature line and the "implemented by:" line.
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

## ElevenLabs (sound generation)

`scripts/generate-sounds.ps1` ([T149](tasks/T149.md)) generates the authored pack's `sfx.*` sounds through
ElevenLabs' sound-generation endpoint, with the key in the user environment variable `ELEVENLABS_API_KEY`. It is
read like `OPENROUTER_API_KEY`: the process environment first, then the Windows user scope. It is never printed,
logged, written to a file or passed on a command line.

- **A real run spends the owner's credit**: `-Key <sfx key>` or `-All`. The default is a dry run that prints the
  prompts and makes no request. `-PostprocessOnly <key>` re-trims and re-normalises one key offline.
- **Show the no-key stop with `-NoKeyCheck`**, never by clearing the variable: the user-scope fallback finds
  the key, and a "no key" run made real, billable calls twice during T149 (the wiki's Agent-failures page).
  `-NoKeyCheck` stubs both key sources, refuses every network call, and spends nothing. `-SelfCheck` tests the
  conversion offline.
