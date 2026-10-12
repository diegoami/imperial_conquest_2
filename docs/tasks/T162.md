#### T162 Recruit unit lists every city's units in training and shows when each is ready

- **Design milestone**: M18 (the Godot screens). Planned from bug [#1001](https://github.com/diegoami/imperial_conquest_2/issues/1001): the user, playing v0.5.x on 2026-10-11, could not find the units they had recruited after loading a game, and "would not know how to demobilize them". The engine keeps the queue through a save and a load (checked through the CLI); the app shows it only one city at a time and never says when a unit can be mobilized. **Labels**: `lane:ui release:v0.5.3`
- **Branch**: `task/T162-recruit-all-cities`, from `origin/main` · **Implementer and reviewer**: the user's choice ([CLAUDE.md](../../CLAUDE.md) rule 17; the standing pair of 2026-10-11)
- **Start after**: none · **Merge after**: [T161](T161.md) (#1000), because its starting queues are what this view first shows, and both touch the recruitment view's tests.
- **Owns**:
  - `godot/UI/Dialogs/RecruitUnitDialog.cs`, `godot/UI/RecruitmentPanelViewModel.cs` and `godot/UI/Dialogs/StrategyDialogModels.cs`: **only** the All cities entry, the readiness text, and acting on a row from All cities.
  - Tests: `tests/IC2.Engine.Tests/Ui/RecruitmentPanelViewModelTests.cs` and new tests beside it; one new headless check under `godot/Checks/`, or the existing `StrategyDialogsCheck.cs` extended (its recruitment steps only).
- **Scope**:
  - **Protects**: a player can always find every unit they have in training, see when each can be mobilized, and mobilize or disband it, after a load as in a running game.
  - **The rule**:
    - **All cities.** The city dropdown gains an **All cities** entry, the original's own: `RecruitUnitDialog.cs:158-159` records today that the clone leaves it out. It lists every unit in training for the nation, each with its city (`RecruitmentPanelViewModel.TrainingForNation` already exists, :179). The dialog opens on All cities when the player has units in training at any city other than the capital, so nothing is hidden behind the dropdown; otherwise it opens as today. The implementer states which rule it used.
    - **Mobilize and Disband from All cities** act on the selected row exactly as they do from that row's city view, through the same commands (`mobilize`, disband). The receiving army is the one `MobilizationReceivingArmy.Find` gives for that row's city, shown as today.
    - **Readiness.** Each row shows "ready" when it can be mobilized (`IsReady`), and otherwise the weeks until it can be: readiness climbs by the ruleset's step each week to the human's threshold (the ruleset values the engine already uses; no new constant). Recruit (a new order) stays per city: All cities offers no Recruit button.
  - **Out of scope**: the starting queues ([T161](T161.md)); the AI's recruitment; the order screens of other dialogs.
- **Done when**:
  1. **All cities lists everything.** With units in training at two cities, All cities lists all of them with their cities, in queue order; a city view lists only its own, as today.
  2. **After a load.** A headless check recruits at a non-capital city, saves, loads, opens Recruit unit, finds the unit (by the dialog's opening view or All cities), and mobilizes it once ready. It fails on `origin/main`.
  3. **Mobilize and Disband from All cities** submit the same command as from the city view, and the result matches; Mobilize stays disabled for a unit that is not ready, and a test pins each case.
  4. **Readiness text.** A ready unit reads "ready"; one that is not reads the weeks remaining, tested at the boundary (one week before ready, and ready).
  5. **Nothing regresses.** Build, test and all Godot headless checks are green, and the diff lists only Owns paths.
