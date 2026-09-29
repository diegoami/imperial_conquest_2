# Imperial Conquest 2 research and reimplementation

A modern, moddable reimplementation of *Imperial Conquest 2* (1996) — the game design, the build/release plans, and all code. Does **not** contain the game's executables, data, help files, sounds, or saves; supply files from your own installation when using the tools that read them.

**The reverse-engineering research lives in a separate repository: [diegoami/imperial-conquest-2-research](https://github.com/diegoami/imperial-conquest-2-research)** — save/DAT format writeups, decompiled formulas, evidence-based reports. This repo cites those reports directly.

## Quick start

### 1. Install

- The **.NET 10 SDK**.
- **Godot 4.7.2, .NET edition**: the download named `Godot_v4.7.2-stable_mono_win64.zip`, not the standard build, which cannot run C#. Unzip it anywhere. It contains two executables: `Godot_v4.7.2-stable_mono_win64.exe` and `..._console.exe`. Use the `_console` one from a terminal, because it prints errors there.

No original game files are needed to build, test or play: the game reads the shipped data under `data/`.

### 2. Build everything

From the repository root:

```bash
dotnet build IC2.sln                        # the engine, data parsers, CLI, inspector and tests
dotnet build godot/IC2.MapViewer.csproj     # the Godot game UI: NOT part of IC2.sln, so build it separately
```

Build both. `IC2.sln` deliberately leaves the Godot project out, because building it needs Godot's SDK. Godot does not compile the C# itself when you launch it from a terminal, so rebuild the Godot project after every pull.

### 3. Test (optional)

```bash
dotnet test IC2.sln
```

The data tests that read your own original game files skip without `assets.local.ini` ([below](#the-research-inspector-tools-ic2inspect)). CI runs the same: [Actions](https://github.com/diegoami/imperial_conquest_2/actions).

### 4. Play in Godot

In PowerShell, from the repository root, with the path to your unzipped Godot:

```powershell
$godot = "C:\path\to\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
& $godot --headless --path godot --import     # first time only: imports the textures and scenes
& $godot --path godot                         # the game: main menu, New Game, Load, Settings
```

In bash it is the same, with `godot` standing for that executable: `godot --path godot`.

Alternatively, start the Godot editor, import `godot/project.godot` and press **Play** (F5). The editor builds the C# itself.

The research inspector is the same project with its scene named: `& $godot --path godot res://MapViewer.tscn`. It reads your own original DAT and saves, so it needs `assets.local.ini` ([below](#the-research-inspector-tools-ic2inspect)).

### 5. Play in the terminal

Pick a scenario and the nation you play, and the AI plays every other seat:

```bash
dotnet run --project src/IC2.Cli -- --scenario classical-mediterranean --seat rome
```

- `--scenario <id>` loads a scenario from `data/scenarios/`; without it, the CLI loads `toy-3city`.
- `--ruleset <id>` replaces the scenario's ruleset: `classical-faithful` or `improved`.
- `--seat <nation>` makes that nation yours, for example `rome` or `carthage`.
- `--seed <n>` overrides the scenario's seed; every random draw in the game comes from it.

An unknown id is rejected with the list of valid ids. Type `help` for the commands. `save <path>` writes the game to a file, and `load <path>` (or `--load <path>` at start) resumes it. A scripted run on the small toy world: `dotnet run --project src/IC2.Cli -- --script tests/fixtures/cli/demo.txt`.

## Current state

**[`v0.4.0`](https://github.com/diegoami/imperial_conquest_2/releases/tag/v0.4.0) — *Playable with a UI, from source*** is the latest release. The Godot project plays a game with the map, the context panel, the news log and the battle-result, diplomacy and hotseat-handoff screens, and saves and resumes it. `IC2.Cli` plays the same engine headless. What each release adds is in [release-plan.md §2](docs/release-plan.md#2-the-release-ladder).

Progress lives on GitHub, not in this file: each [task issue](https://github.com/diegoami/imperial_conquest_2/issues?q=label%3Atask)'s `status:*` label, and the [pull requests](https://github.com/diegoami/imperial_conquest_2/pulls). What is built and what is next is in the [task catalogue](docs/task-catalogue.md). How to query the board: [Where the build stands](https://github.com/diegoami/imperial_conquest_2/wiki/Where-the-build-stands) on the wiki.

Operating the project — the task loop, the skills, where everything lives, bugs: [docs/operating-guide.md](docs/operating-guide.md).


## Building the reimplementation

`IC2.Engine` is the headless game engine; its rules are being filled in task by task ([task-catalogue.md](docs/task-catalogue.md)). `IC2.Cli` is a scriptable play harness: T41 shipped the thin demo above, and T23 extends it to every command type plus the view models the Godot UI binds to.

The shipped data is the 334-city `classical-mediterranean` world and scenario, exported from the original by T29, with the `classical-faithful` and `improved` rulesets. `toy-3city` is a deliberately small 3-city / 2-nation fixture for tests, and the `example-*` scenarios are authoring examples ([docs/scenario-authoring.md](docs/scenario-authoring.md)).

`tests/fixtures/corpus.json` is the evidence base the rules are built against: every exact number from the research reports, transcribed once, each with its source report and tag. `tests/fixtures/known-reports.json` pins the real report filenames so a typo'd or invented citation fails CI offline, without the research repo being cloned.

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

### Godot map viewer (part of the inspector)

The Godot project's main scene is the game (above). The research inspector is its `res://MapViewer.tscn` scene: after setting `assets.local.ini`, run that scene. It draws the original's 320×140 map and 334 cities from your DAT and a save, with a click on a city/army/fleet marker for its decoded fields, a nation selector, and the save's calendar header. It is read-only and never touches the engine.

## Further reading

- [Operating guide](docs/operating-guide.md) — start here: where everything lives, how the sessions, skills and pipeline are run, what's still open.
- [Game design](docs/game-design.md) and its [design audit](docs/design-audit.md) — what the reimplementation will be, and what the evidence actually supports.
- [Task catalogue](docs/task-catalogue.md) — the build tasks and their dependency graph.
- [Build process](docs/build-process.md) — how tasks are dispatched, reviewed, merged and documented by the agent pipeline.
- [Evidence pipeline](docs/evidence-pipeline.md) — the `/process-evidence` skill that turns new saves/recordings/notes into research-repo findings and then game-design implications.
- [Investigations](docs/investigations/README.md) — this repository's own evidence write-ups.
- [Release plan](docs/release-plan.md) — how tasks turn into version tags and releases.
- Research repository ([diegoami/imperial-conquest-2-research](https://github.com/diegoami/imperial-conquest-2-research)): [roadmap](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/roadmap.md), [research notes](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/research.md), and the full [reports index](https://github.com/diegoami/imperial-conquest-2-research/tree/main/docs/reports) — the evidence-based findings the design cites throughout.
