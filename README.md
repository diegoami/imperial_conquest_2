# Imperial Conquest 2 research and reimplementation

A modern, moddable reimplementation of *Imperial Conquest 2* (1996) — the game design, the build/release plans, and all code. Does **not** contain the game's executables, data, help files, sounds, or saves; supply files from your own installation when using the tools that read them.

**The reverse-engineering research lives in a separate repository: [diegoami/imperial-conquest-2-research](https://github.com/diegoami/imperial-conquest-2-research)** — save/DAT format writeups, decompiled formulas, evidence-based reports. This repo cites those reports directly.

## Current status

Being built by a multi-agent pipeline driven from GitHub issues. **As of commit `fea39d8`: wave 0 and the domain model are merged.** T01/T05 gave `IC2.sln` (pre-declaring `IC2.Data`, `IC2.Inspect`, `IC2.Engine`, `IC2.Cli` and their test projects) plus CI on every push/PR. **T02** then landed the core domain model — `World`, `Ruleset`, `Scenario`, `SaveGame` and the `GameState` tree as immutable records with strict, typed JSON loading, `_provenance` on every ruleset value, and a toy 3-city / 2-nation world under `data/`. There is still nothing to play: this is data types and a loader, not gameplay.

- **Live tracker**: [issue #29](https://github.com/diegoami/imperial_conquest_2/issues/29). **Full guide to checking progress and what's actually runnable at each stage**: [`build-orchestration-plan.md` §0](docs/build-orchestration-plan.md#0-where-things-stand-and-what-you-can-test) — short version, nothing playable before `IC2.Cli` lands, nothing visual before the Godot screens do.
- **To pause the build for any reason**: [§7.5](docs/build-orchestration-plan.md#75-user-initiated-pause) — `gh issue edit 29 --add-label orchestrator:pause`, from any session, no need to track down a running agent.
- **To build and test what exists right now**:
  ```bash
  dotnet build IC2.sln   # 0 warnings, 0 errors
  dotnet test IC2.sln    # 60 tests, green (59 engine + 1 data)
  ```
- **CI**: [Actions tab](https://github.com/diegoami/imperial_conquest_2/actions).

## Building the reimplementation

`IC2.Engine` (the headless game engine) currently holds T02's domain model and serialization layer only — no gameplay rules yet. `IC2.Cli` (a scriptable play harness) is still a scaffolded stub; see the status section above for what to expect at each build stage. Once `IC2.Cli` lands (T23), this section will carry its usage.

The shipped data files are `data/worlds/toy-3city.json`, `data/rulesets/toy-ruleset.json` and `data/scenarios/toy-3city.json` — a deliberately small 3-city / 2-nation fixture for tests. The real 334-city `classical-mediterranean` world and the `classical-faithful` ruleset are exported later, by T29.

## The research-inspector tools (`IC2.Inspect`)

Predates the reimplementation — a read-only C#/.NET CLI and Godot viewer for original `.DAT`/`.sav` files, built during the reverse-engineering phase and still useful for inspecting real save data. Point it at your own files:

```ini
# assets.local.ini (copy from assets.example.ini; git-ignored, machine-specific)
[assets]
directory = C:\path\to\imp_conq_original
```

Saves go in a `saves` subfolder of that directory; screenshots (`<save-number>.<image-number>.png`) and recordings (`.mp4`) in sibling `screenshots`/`recordings` folders — both git-ignored, reference material only.

```text
dotnet build src/IC2.Inspect/IC2.Inspect.csproj
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --save "saves/1,sav.sav"
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --compare-saves saves/1.sav saves/4.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-city saves/11_supply.sav Rome
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-army saves/11_supply.sav 100 42
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-nation saves/11_ptol.sav Ptolemaic
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-turn saves/12_ptol.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --list-armies saves/11_supply.sav Rome
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --list-fleets saves/11_supply.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --list-mercenaries saves/11_supply.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --to-json saves/11_supply.sav rendered/11_supply.json
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --render-map rendered/map.svg
```

Run from the repository root. `--compare-saves` reports hashes, map transitions, and city/army/fleet-record byte changes between two saves. `--inspect-city`/`--inspect-army`/`--inspect-nation`/`--inspect-turn` print one entity's decoded fields. `--list-armies`/`--list-fleets`/`--list-mercenaries` enumerate without needing coordinates. `--to-json` dumps everything known about a save (turn, all nations, all cities with garrisons, all armies, all fleets, all mercenary offers) to one file. `--render-map` writes an SVG from the DAT's terrain/cities. `--config <path>` selects a different INI for automation. Never executes or modifies the original game.

### Godot map viewer (part of the inspector, not the reimplementation's UI yet)

After setting `assets.local.ini`, open `godot/project.godot` with Godot .NET 4.7.2 and run the project — draws the 320×140 map and 334 cities, click a city/army/fleet marker for its decoded fields, a nation selector, and the save's calendar header. Read-only; no turns or commands yet (that's what `IC2.Cli`/the Godot screens in the build plan will add).

## Further reading

- [Project handover](docs/HANDOVER.md) — concise current-state summary, start here for "what's next."
- [Game design](docs/game-design.md) and its [design audit](docs/design-audit.md) — what the reimplementation will be, and what the evidence actually supports.
- [Build orchestration plan](docs/build-orchestration-plan.md) — how the build is split into agent-run tasks; **§0 is the status/testing guide**.
- [Release plan](docs/release-plan.md) — how tasks turn into version tags and releases.
- Research repository ([diegoami/imperial-conquest-2-research](https://github.com/diegoami/imperial-conquest-2-research)): [roadmap](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/roadmap.md), [research notes](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/research.md), and the full [reports index](https://github.com/diegoami/imperial-conquest-2-research/tree/main/docs/reports) — 46 evidence-based findings the design cites throughout.
