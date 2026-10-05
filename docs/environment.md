# Environment

What this machine provides to the build, beyond the toolchain that [operating-guide.md](operating-guide.md) describes.

## Model quota availability: quota-tracker

Adopted from harness_imperial (L50) by the user's decision of 2026-10-05; [CLAUDE.md](../CLAUDE.md) rules 17 and 18 apply it.

A local service, quota-tracker, reports how much subscription quota is left on these providers: claude, openai (the ChatGPT plan, used through OpenCode), zai (the GLM Coding Plan), opencode_go (OpenCode Go) and openrouter (prepaid credit). Check it before choosing, recommending or delegating to a model, whenever you need to know whether a provider can be used right now.

### Querying

The service is read-only, on localhost, with no authentication. Results are cached for 60 s.

- `curl -s localhost:8765/quota` lists every provider.
- `curl -s localhost:8765/quota/<provider>` gives one provider.
- `curl -s localhost:8765/best` lists the providers with quota left, most headroom first.
- `curl -s localhost:8765/avoid` lists the providers that are out of quota, with when each is usable again.
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
| openrouter | `openrouter/deepseek/deepseek-v4-pro` (no script name yet) | `openrouter/deepseek/deepseek-v4.1-flash` (no script name yet) |

Heavy models run at `medium` rather than `high`, or lighter when medium is not needed (the user's decision of 2026-10-05). Where a model offers no `medium`, the table names the variant chosen.

Facts that affect availability:

- **GPT-5.6 Luna has its own weekly limit.** For light work, openai stays usable while the `gpt-5.6-luna:7d` window in `/quota/openai` is under 95%, even when openai itself is exhausted.
- **openrouter is prepaid credit.** Its windows never reset, and `remaining_usd` is the balance.
- **GLM's Coding Plan also has a 5-hour window.** When it runs out, an OpenCode run fails with "Usage limit reached for 5 hour" (seen on 2026-10-04); the `5h` window in `/quota/zai` shows it beforehand.

### If the service isn't running

Check it with `curl -sf localhost:8765/health`. If that fails, the service is the owner's to start. Tell the user, and go on without it. Count a usage-limit error as `exhausted` (CLAUDE.md rule 17).

### Don't

- Don't read or edit quota-tracker's configuration file: it holds account tokens.
- If a provider shows `error` about an expired cookie or token, tell the user. Renewing it needs their browser or login.
