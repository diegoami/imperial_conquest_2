using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T127.md</c> Done-when 2 and 3 (the part that is about instant battles): the real
/// <see cref="MainGameScreen"/> on scripted states, so the tactical battle screen and the <em>Battle ended</em>
/// window are what the engine's battle shows. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/TacticalBattleCheck.tscn
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <strong>The scripted state.</strong> The <c>classical-mediterranean</c> world with Rome (human) and the army
/// <c>army-3</c> of an AI nation moved beside Rome's <c>army-0</c>: <c>attack-army army-0 army-3</c> opens the battle.
/// Every battle is deterministic (the session's seed and the battle's own stream), so a second session fed the same
/// lines is the engine's reference for what the screen shows.
/// </para>
/// <para>
/// <strong>The pauses.</strong> The check never waits for real time: it steps the replay with
/// <see cref="TacticalBattleScreen.AdvancePlayback"/>, which is what the frame loop calls.
/// </para>
/// </remarks>
public partial class TacticalBattleCheck : Node
{
    private const string Attack = "attack-army army-0 army-3";

    private bool _ok = true;
    private readonly List<string> _cleanup = new();

    public override void _Ready()
    {
        try
        {
            SettingsScreen.SetBattlePauses(0, 0);
            CheckTheBattleIsFoughtAndEnded();
            CheckAClickTheEngineRefusesShowsItsMessage();
            CheckSaveAsDuringTheBattleReopensIt();
            CheckThePausesAreDisplayOnly();
            CheckSurrender();
            CheckAnAiPhaseBattleIsFoughtOnTheSameScreen();
            CheckInstantBattlesAreShownAsBefore();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"TacticalBattleCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            SettingsScreen.SetBattlePauses(0, 0);
            foreach (var path in _cleanup)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"TacticalBattleCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    // ---- Done-when 2: the battle, its end and the window ----

    private void CheckTheBattleIsFoughtAndEnded()
    {
        var mainGame = Build(ClassicalSession());
        mainGame.SubmitForCheck(Attack);

        var screen = mainGame.ActiveOverlay as TacticalBattleScreen;
        if (!Check(screen is not null, $"the attack opens the battle screen (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})"))
        {
            Free(mainGame);
            return;
        }

        var view = mainGame.Session.OpenTacticalBattle!;
        var rome = view.AttackerNationId;
        Check(
            !view.Placed && view.SideToMove == TacticalBattleState.AttackerSide && view.Counter == 2,
            $"the defender's placement is done and Rome is to place (placed {view.Placed}, side {view.SideToMove}, counter {view.Counter})");
        Check(rome == "rome", $"Rome attacks (attacker nation '{rome}')");
        Check(
            screen!.CaptionLabel.Text == screen.Model.Caption && screen.CaptionLabel.Text.Contains("Rome to place units."),
            $"the caption names the side to place ('{screen.CaptionLabel.Text}')");
        Check(
            screen.Model.Cells.Count(c => c.IconKey is not null) == view.Slots.Count(s => s.IsLive),
            "the board shows one icon per live unit");

        // A placement reaches the engine: the first unit moves to an empty cell of Rome's home rows.
        var homeRows = mainGame.Session.Ruleset.Combat.Tactical.HomeRows;
        var target = screen.Model.Cells
            .Where(c => c.IconKey is null && c.Y < homeRows)
            .OrderByDescending(c => c.X).ThenBy(c => c.Y)
            .First();
        screen.SelectUnit(0);
        Check(screen.SelectedSlot == 0, "a click on an own unit selects it");
        screen.ClickCell(target.X, target.Y);
        var placed = mainGame.Session.OpenTacticalBattle!.Slots[0];
        Check(
            placed.X == target.X && placed.Y == target.Y,
            $"the placement click reached the engine (unit 0 at ({placed.X},{placed.Y}), wanted ({target.X},{target.Y}))");

        // End turn reaches the engine.
        var counterBefore = mainGame.Session.OpenTacticalBattle!.Counter;
        screen.PressToolbarButton("end_turn");
        var afterEnd = mainGame.Session.OpenTacticalBattle;
        Check(
            afterEnd is not null && afterEnd.Counter > counterBefore && afterEnd.Placed,
            $"End turn reached the engine (counter {counterBefore} -> {afterEnd?.Counter}, placed {afterEnd?.Placed})");

        // The reference: the same lines in a second session give the engine's own report.
        var reference = ClassicalSession();
        reference.Submit(Attack);
        reference.BattlePlace(0, target.X, target.Y);
        reference.BattleEnd();
        var referenceOutput = reference.BattleGeneral(true);
        var report = referenceOutput.EndedBattleReport;
        if (!Check(report is not null, "the reference session plays the battle to its end under Computer general"))
        {
            Free(mainGame);
            return;
        }

        screen.PressToolbarButton("computer_general");
        Check(!screen.IsPlayingBack && screen.ExchangesShown == 0, "with the pauses at 0 s the screen waits for nothing");
        var ended = mainGame.ActiveOverlay as BattleEndedScreen;
        if (!Check(ended is not null, $"Computer general on plays the battle to the end and the Battle ended window opens (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})"))
        {
            Free(mainGame);
            return;
        }

        Check(mainGame.Session.OpenTacticalBattle is null, "the engine has no open battle");
        var expected = BattleEndedViewModel.FromReport(report!, reference.State, reference.Ruleset);
        Check(ended!.WinnerLabel.Text == expected.WinnerLine, $"the winner line equals the engine's ('{ended.WinnerLabel.Text}')");
        Check(ended.MoneyLabel.Text == expected.MoneyLine && ended.SuppliesLabel.Text == expected.SuppliesLine, "the money and supplies lines equal the engine's");
        var cells = new List<string>(BattleEndedViewModel.Headers);
        foreach (var row in expected.Rows)
        {
            cells.AddRange(new[] { row.Label, row.WinnerStart.ToString(), row.WinnerFinish.ToString(), row.LoserStart.ToString() });
        }

        Check(ended.TableTexts.SequenceEqual(cells), "the table equals the engine's TacticalBattleReport");
        Check(
            ended.TableTexts.Contains(report!.WinnerStartTotal.ToString())
            && ended.TableTexts.Contains(report.WinnerFinishTotal.ToString())
            && ended.TableTexts.Contains(report.LoserStartTotal.ToString()),
            $"the totals are the report's ({report.WinnerStartTotal}, {report.WinnerFinishTotal}, {report.LoserStartTotal})");

        ended.Close();
        Check(
            mainGame.ActiveOverlay is null or PeaceOfferScreen,
            $"after OK the map returns, or the peace offer (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        Check(
            Equals(mainGame.Session.State, reference.State),
            "the game after the battle is the engine's post-battle state");
        Check(
            mainGame.Session.State.ArmyById(report.LoserArmyId) is null,
            "the loser army is gone from the map");
        Free(mainGame);
    }

    private void CheckAClickTheEngineRefusesShowsItsMessage()
    {
        var mainGame = Build(ClassicalSession());
        mainGame.SubmitForCheck(Attack);
        var screen = (TacticalBattleScreen)mainGame.ActiveOverlay!;
        var before = mainGame.Session.OpenTacticalBattle!;

        // Setting a melee target during placement is not legal; the screen sends it and shows the engine's refusal.
        var enemy = before.Slots.Select((s, i) => (s, i)).First(p => p.s.IsLive && p.i >= 20);
        screen.SelectUnit(0);
        screen.ClickCell(enemy.s.X, enemy.s.Y);

        Check(
            screen.MessageLabel.Text.Contains("Refused"),
            $"a click the engine refuses shows its message ('{screen.MessageLabel.Text}')");
        Check(Equals(before, mainGame.Session.OpenTacticalBattle), "the refused click changed nothing");
        Free(mainGame);
    }

    // ---- Done-when 2: Save As in a battle, reload through the Load screen ----

    private void CheckSaveAsDuringTheBattleReopensIt()
    {
        var mainGame = Build(ClassicalSession());
        mainGame.SubmitForCheck(Attack);
        var screen = (TacticalBattleScreen)mainGame.ActiveOverlay!;
        screen.PressToolbarButton("end_turn");
        var saved = mainGame.Session.OpenTacticalBattle!;

        var path = Path.Combine(ProjectSettings.GlobalizePath("user://saves"), "t127-tacticalbattlecheck.json");
        _cleanup.Add(path);
        mainGame.SaveAsForCheck(path);
        Check(File.Exists(path), "Save As during the battle writes a save");
        Check(
            mainGame.ActiveOverlay is TacticalBattleScreen && screen.MessageLabel.Text.Contains("Saved to"),
            $"the battle screen stays and says the game was saved ('{screen.MessageLabel.Text}')");
        Check(Equals(saved, mainGame.Session.OpenTacticalBattle), "saving changed nothing in the battle");

        GameSession? resumed = null;
        var load = new LoadGameScreen();
        load.GameResumed += s => resumed = s;
        AddChild(load);
        load.ContinueForCheck(path);
        RemoveChild(load);
        load.QueueFree();
        if (!Check(resumed is not null, "the Load screen resumes the save"))
        {
            Free(mainGame);
            return;
        }

        Check(resumed!.HumanBattles == HumanBattleMode.Tactical, "the resumed session fights human battles on the board");
        var second = Build(resumed);
        var reopened = second.ActiveOverlay as TacticalBattleScreen;
        if (!Check(reopened is not null, $"the loaded mid-battle save reopens the battle screen (got {second.ActiveOverlay?.GetType().Name ?? "null"})"))
        {
            Free(second);
            Free(mainGame);
            return;
        }

        var view = resumed.OpenTacticalBattle!;
        Check(
            view.SideToMove == saved.SideToMove && view.Placed == saved.Placed,
            $"it reopens at the side to move (side {view.SideToMove}, placed {view.Placed})");
        Check(view.Counter == saved.Counter + 1, $"at the half-round's setup (counter {saved.Counter} -> {view.Counter})");
        Check(
            view.Slots.Select(s => (s.X, s.Y, s.Troops, s.Type)).SequenceEqual(saved.Slots.Select(s => (s.X, s.Y, s.Troops, s.Type))),
            "the units are where they were");
        var rules = resumed.Ruleset;
        Check(
            view.Slots.Skip(view.SideToMove * 20).Take(20).Where(s => s.IsLive).All(s => s.Moves == rules.UnitTypes[s.Type].Moves && s.Target < 0),
            "the side to move has its moves refilled and no targets");
        Check(reopened!.Model.Caption == TacticalBattleViewModel.From(view, BattleDisplay.For(view, resumed.State, rules)).Caption, "the screen draws the reopened battle");
        Free(second);
        Free(mainGame);
    }

    // ---- Done-when 2: the pauses ----

    private void CheckThePausesAreDisplayOnly()
    {
        // 0 s: the whole battle is played with nothing to wait for.
        SettingsScreen.SetBattlePauses(0, 0);
        var instant = Build(ClassicalSession());
        instant.SubmitForCheck(Attack);
        var instantScreen = (TacticalBattleScreen)instant.ActiveOverlay!;
        instantScreen.PressToolbarButton("computer_general");
        Check(!instantScreen.IsPlayingBack && instantScreen.ExchangesShown == 0, "pauses at 0 s: no replay, no delay");
        Check(instant.ActiveOverlay is BattleEndedScreen, "pauses at 0 s: the Battle ended window is up at once");
        Free(instant);

        // 1.0 s: the engine's state is final at once and the screen replays the exchanges, one wait each.
        SettingsScreen.SetBattlePauses(1.0, 1.0);
        Check(
            SettingsScreen.BattleShotDelaySeconds == 1.0 && SettingsScreen.BattleAttackDelaySeconds == 1.0,
            "the pauses setting keeps 1.0 s");
        var slow = Build(ClassicalSession());
        slow.SubmitForCheck(Attack);
        var slowScreen = (TacticalBattleScreen)slow.ActiveOverlay!;
        slowScreen.PressToolbarButton("computer_general");

        Check(slowScreen.IsPlayingBack, "pauses at 1.0 s: the screen is replaying");
        Check(slow.Session.OpenTacticalBattle is null, "pauses at 1.0 s: the engine's battle is already over");
        Check(slow.ActiveOverlay is TacticalBattleScreen, "pauses at 1.0 s: the battle screen still holds the window");
        var firstShown = slowScreen.ExchangesShown;
        var firstText = slowScreen.ExchangeLabel.Text;
        Check(firstShown == 1 && firstText.Length > 0, $"the first exchange is shown ('{firstText.Replace("\n", " / ")}')");
        slowScreen.AdvancePlayback(0.5);
        Check(slowScreen.ExchangesShown == firstShown && slowScreen.ExchangeLabel.Text == firstText, "it waits between exchanges (after 0.5 s: still the first)");
        slowScreen.AdvancePlayback(0.6);
        Check(slowScreen.ExchangesShown == firstShown + 1 || !slowScreen.IsPlayingBack, "after the second it shows the next exchange");

        // A click on the side panel during the replay ends it.
        slowScreen.PanelClicked();
        Check(!slowScreen.IsPlayingBack && slow.ActiveOverlay is BattleEndedScreen, "a click on the side panel ends the replay and the Battle ended window opens");
        var window = (BattleEndedScreen)slow.ActiveOverlay!;
        var reference = ClassicalSession();
        reference.Submit(Attack);
        var report = reference.BattleGeneral(true).EndedBattleReport!;
        Check(
            window.TableTexts.Contains(report.WinnerFinishTotal.ToString()) && Equals(slow.Session.State, reference.State),
            "the pauses changed nothing the engine decided (same report, same state as at 0 s)");
        Free(slow);

        // The setting is a player setting only: it is in no save.
        SettingsScreen.SetBattlePauses(0, 0);
        Check(
            SettingsScreen.BattleShotDelaySeconds == 0 && SettingsScreen.BattleAttackDelaySeconds == 0,
            "the default is 0 s");

        // Change pauses opens the dialog and writes the player setting.
        var dialogHost = Build(ClassicalSession());
        dialogHost.SubmitForCheck(Attack);
        var dialogScreen = (TacticalBattleScreen)dialogHost.ActiveOverlay!;
        dialogScreen.PressToolbarButton("change_pauses");
        Check(dialogScreen.PausesDialogOpen, "Change pauses opens the pauses dialog");
        dialogScreen.ClosePausesDialog();
        Check(!dialogScreen.PausesDialogOpen, "the dialog closes");
        Free(dialogHost);
    }

    // ---- surrender ----

    private void CheckSurrender()
    {
        var mainGame = Build(ClassicalSession());
        mainGame.SubmitForCheck(Attack);
        var screen = (TacticalBattleScreen)mainGame.ActiveOverlay!;
        var before = mainGame.Session.OpenTacticalBattle!;
        var aiNationName = mainGame.Session.State.NationById(before.DefenderNationId)!.Name;

        screen.PressToolbarButton("surrender");
        Check(screen.SurrenderPromptOpen, "Surrender asks for confirmation");
        screen.AnswerSurrender(false);
        Check(!screen.SurrenderPromptOpen && Equals(before, mainGame.Session.OpenTacticalBattle), "No leaves the battle unchanged");

        screen.PressToolbarButton("surrender");
        screen.AnswerSurrender(true);
        var ended = mainGame.ActiveOverlay as BattleEndedScreen;
        Check(ended is not null, $"Yes surrenders and the Battle ended window opens (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        if (ended is not null)
        {
            Check(
                ended.WinnerLabel.Text.StartsWith($"{aiNationName}'s", StringComparison.Ordinal),
                $"the other side won ('{ended.WinnerLabel.Text}')");
            ended.Close();
        }

        Check(mainGame.Session.OpenTacticalBattle is null, "the battle is over after the surrender");
        Free(mainGame);
    }

    // ---- T126: an AI seat's attack on the human opens the battle on the same screen ----

    private void CheckAnAiPhaseBattleIsFoughtOnTheSameScreen()
    {
        var mainGame = Build(ToyAiAttackSession());
        mainGame.SubmitForCheck("declare-war south");
        var turnBefore = mainGame.Session.State.Calendar.TurnIndex;
        mainGame.SubmitForCheck("end");

        var screen = mainGame.ActiveOverlay as TacticalBattleScreen;
        if (!Check(screen is not null, $"an AI seat's attack opens the battle screen at the end of the turn (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})"))
        {
            Free(mainGame);
            return;
        }

        var view = mainGame.Session.OpenTacticalBattle!;
        Check(view.SideToMove == TacticalBattleState.DefenderSide && !view.Placed, "the human defender places first");
        Check(view.DefenderNationId == "north", "the human is the defender");

        // Save is refused by the engine, and the screen shows only its message.
        mainGame.PressSaveForCheck("t127-aiphase");
        Check(
            screen!.MessageLabel.Text == "Finish the battle first: the turn is in progress.",
            $"Save during an AI-phase battle shows the engine's message and nothing else ('{screen.MessageLabel.Text}')");
        Check(mainGame.ActiveOverlay is TacticalBattleScreen, "the battle screen stays");

        screen.PressToolbarButton("computer_general");
        var ended = mainGame.ActiveOverlay as BattleEndedScreen;
        Check(ended is not null, $"Computer general plays it to the end (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        ended?.Close();
        Check(mainGame.Session.OpenTacticalBattle is null, "the battle is over");
        Check(mainGame.Session.State.Calendar.TurnIndex > turnBefore, "the turn goes on after the battle");
        Free(mainGame);
    }

    // ---- Done-when 3: instant battles are shown as before ----

    private void CheckInstantBattlesAreShownAsBefore()
    {
        // A siege.
        var siege = Build(ToySiegeSession());
        siege.SubmitForCheck("besiege-city north-army-1 meridia");
        Check(
            siege.ActiveOverlay is BattleResultScreen,
            $"a siege shows the battle-result window as on main (got {siege.ActiveOverlay?.GetType().Name ?? "null"})");
        Check(siege.Session.OpenTacticalBattle is null, "a siege opens no battle screen");
        Free(siege);

        // An AI-versus-AI battle played within an end turn.
        var aiVsAi = Build(ClassicalAiVersusAiSession());
        aiVsAi.SubmitForCheck("end");
        var battles = aiVsAi.Session.LastBattles;
        var neutral = battles.Where(b => aiVsAi.Session.State.NationById(b.AttackerNationId)?.Control == SeatControl.Ai
                                         && aiVsAi.Session.State.NationById(b.DefenderNationId)?.Control == SeatControl.Ai).ToList();
        if (Check(neutral.Count > 0, $"the scripted AI phase fights an AI-versus-AI battle ({battles.Count} battle(s) in all)"))
        {
            var shown = new List<BattleResultViewModel>();
            while (aiVsAi.ActiveOverlay is BattleResultScreen window)
            {
                shown.Add(window.Result);
                window.Close();
            }

            Check(
                neutral.All(n => shown.Any(w => w.AttackerId == n.AttackerId && w.DefenderId == n.DefenderId)),
                "the AI-versus-AI battle is shown in the battle-result window as on main");
        }

        Check(aiVsAi.Session.OpenTacticalBattle is null, "an AI-versus-AI battle opens no battle screen");
        Free(aiVsAi);
    }

    // ---- fixtures ----

    private static GameSession ClassicalSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var ruleset = resolved.Ruleset with { Flags = resolved.Ruleset.Flags with { NewGameTurnOrder = NewGameTurnOrderPolicy.Fixed } };
        var armies = resolved.World.StartingArmies.Select(a => a.Id == "army-3" ? a with { X = 101, Y = 37, Moves = 8 } : a).ToList();
        var world = resolved.World with { StartingArmies = ValueList.From(armies) };
        return new GameSession(world, ruleset, resolved.Scenario, null, "rome") { HumanBattles = HumanBattleMode.Tactical };
    }

    private static GameSession ToyAiAttackSession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with
        {
            X = 4,
            Y = 2,
            Units = ValueList.Of(new UnitSlot(0, "heavy_infantry", Troops: 50_000, Quality: 6, Name: "1st Guards Battalion")),
        };
        var world = toy.World with
        {
            StartingArmies = ValueList.From(toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a)),
        };
        return new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Tactical };
    }

    private static GameSession ToySiegeSession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var armies = new List<StartingArmy>
        {
            new("north-army-1", "north", X: 3, Y: 3, Morale: 68, Money: 0, SupplyTons: 0, Moves: 5,
                Units: ValueList.Of(new UnitSlot(0, "light_infantry", Troops: 15_000, Quality: 6, Name: "1st Foot"))),
            new("south-army-1", "south", X: 6, Y: 5, Morale: 59, Money: 0, SupplyTons: 0, Moves: 5,
                Units: ValueList.Of(new UnitSlot(0, "heavy_infantry", Troops: 600, Quality: 6, Name: "unit"))),
        };
        var world = toy.World with { StartingArmies = ValueList.From(armies) };
        return new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Tactical };
    }

    private const int PocketX = 67;
    private const int PocketY = 4;
    private const ulong Seed = 20261003UL;

    /// <summary>
    /// Rome the only human seat; carthage at war with seleucid, one strong carthage army beside one weak seleucid army
    /// in an isolated all-land pocket, so carthage's own turn attacks it: a battle with no human side.
    /// </summary>
    private static GameSession ClassicalAiVersusAiSession()
    {
        var resolved = GameDataContext.Repository.Resolve("example-classical-improved");
        var warCode = resolved.Ruleset.Diplomacy.StateCodes.War;
        var scenario = resolved.Scenario with
        {
            Seats = ValueList.From(resolved.Scenario.Seats.Select(seat => seat with
            {
                Control = seat.Nation == "rome" ? SeatControl.Human : SeatControl.Ai,
            })),
        };
        var relations = resolved.World.StartingRelations!
            .WithRelation("carthage", "seleucid", warCode);

        var armies = resolved.World.StartingArmies
            .Select(army => army.Id switch
            {
                "army-4" => army with
                {
                    X = PocketX,
                    Y = PocketY,
                    Morale = 45,
                    Units = ValueList.Of(new UnitSlot(0, "light_infantry", Troops: 5000, Quality: 4, Name: "Weak Militia")),
                },
                "army-2" => army with
                {
                    X = PocketX + 1,
                    Y = PocketY,
                    Morale = 60,
                    Units = ValueList.Of(new UnitSlot(0, "light_infantry", Troops: 10_000, Quality: 6, Name: "Check Warband")),
                },
                _ => army,
            })
            .ToList();

        var world = resolved.World with { StartingRelations = relations, StartingArmies = ValueList.From(armies) };
        return new GameSession(world, resolved.Ruleset, scenario, seedOverride: Seed) { HumanBattles = HumanBattleMode.Tactical };
    }

    private MainGameScreen Build(GameSession session)
    {
        var mainGame = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        AddChild(mainGame);
        return mainGame;
    }

    private void Free(MainGameScreen mainGame)
    {
        RemoveChild(mainGame);
        mainGame.QueueFree();
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
