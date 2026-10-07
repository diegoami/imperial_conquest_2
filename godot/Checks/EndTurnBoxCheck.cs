using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T118's headless end-turn warning check. It drives the real <see cref="MainGameScreen"/> and its
/// menu entry, rather than calling the warning query or submitting a command around the UI.
/// </summary>
public partial class EndTurnBoxCheck : Control
{
    private bool _ok = true;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;
        try
        {
            CheckWarningBoxAndAnswers();
            CheckUnwarnedSeatEndsImmediately();
            CheckFiveLineCap();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"EndTurnBoxCheck: unhandled exception: {ex}");
            _ok = false;
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"EndTurnBoxCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void CheckWarningBoxAndAnswers()
    {
        var mainGame = AddMainGame(BuildSession(warning: true));
        try
        {
            var beforeTurn = mainGame.Session.State.Calendar.TurnIndex;
            var beforeNation = mainGame.Session.State.ActiveNationId;
            var beforeCommands = 0;
            mainGame.CommandIssued += _ => beforeCommands++;

            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            Check(mainGame.ActiveOverlay is EndTurnBox, "a warning opens EndTurnBox");
            Check(mainGame.Session.State.Calendar.TurnIndex == beforeTurn, "opening the box does not end the turn");
            Check(mainGame.Session.State.ActiveNationId == beforeNation, "opening the box keeps the active seat");

            if (mainGame.ActiveOverlay is EndTurnBox box)
            {
                Check(box.CaptionLabel.Text == "End turn ?", "the box caption is 'End turn ?'");
                Check(
                    box.WarningLabels.Select(label => label.Text).SequenceEqual(
                        new[] { EndTurnWarnings.ArmyNeedsSupplies }),
                    "the box shows the army supply warning line");
                Check(box.MakeMoreMovesButton.Text == "MAKE MORE MOVES", "the box has MAKE MORE MOVES");
                Check(box.EndTurnButton.Text == "END TURN", "the box has END TURN");

                box.MakeMoreMovesButton.EmitSignal(Button.SignalName.Pressed);
                Check(mainGame.ActiveOverlay is null, "MAKE MORE MOVES closes the box");
                Check(beforeCommands == 0, "MAKE MORE MOVES submits nothing");
                Check(mainGame.Session.State.Calendar.TurnIndex == beforeTurn, "MAKE MORE MOVES leaves the turn unchanged");
            }

            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            Check(mainGame.ActiveOverlay is EndTurnBox, "a second End turn press runs the warning again");
            if (mainGame.ActiveOverlay is EndTurnBox secondBox)
            {
                secondBox.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            }

            Check(mainGame.ActiveOverlay is null, "END TURN closes the box");
            Check(beforeCommands == 1, "END TURN submits exactly one command");
            Check(mainGame.Session.State.Calendar.TurnIndex == beforeTurn + 1, "END TURN advances the turn");
        }
        finally
        {
            RemoveMainGame(mainGame);
        }
    }

    private void CheckUnwarnedSeatEndsImmediately()
    {
        var mainGame = AddMainGame(BuildSession(warning: false));
        try
        {
            var beforeTurn = mainGame.Session.State.Calendar.TurnIndex;
            var commands = 0;
            mainGame.CommandIssued += _ => commands++;
            mainGame.MenuBar.PressItemForCheck("game.end_turn");

            Check(mainGame.ActiveOverlay is null, "a seat with no warning has no box");
            Check(commands == 1, "a seat with no warning submits end immediately");
            Check(mainGame.Session.State.Calendar.TurnIndex == beforeTurn + 1, "an unwarned seat advances immediately");
        }
        finally
        {
            RemoveMainGame(mainGame);
        }
    }

    private void CheckFiveLineCap()
    {
        var mainGame = AddMainGame(BuildSession(warning: true, sixWarnings: true));
        try
        {
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            Check(mainGame.ActiveOverlay is EndTurnBox, "six triggers still open the warning box");
            if (mainGame.ActiveOverlay is EndTurnBox box)
            {
                Check(
                    box.WarningLabels.Select(label => label.Text).SequenceEqual(new[]
                    {
                        EndTurnWarnings.ArmyNeedsSupplies,
                        EndTurnWarnings.ArmyCannotPayMercenaries,
                        EndTurnWarnings.FleetNotDocked,
                        EndTurnWarnings.FleetNeedsSupplies,
                        EndTurnWarnings.ArmyNeedsSupplies,
                    }),
                    "six triggers show exactly the first five lines in query order");
            }
        }
        finally
        {
            RemoveMainGame(mainGame);
        }
    }

    private MainGameScreen AddMainGame(GameSession session)
    {
        var mainGame = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(mainGame);
        return mainGame;
    }

    private static void RemoveMainGame(MainGameScreen mainGame)
    {
        mainGame.GetParent()?.RemoveChild(mainGame);
        mainGame.QueueFree();
    }

    private static GameSession BuildSession(bool warning, bool sixWarnings = false)
    {
        var resolved = GameDataContext.Repository.Resolve("toy-3city");
        var ruleset = resolved.Ruleset with
        {
            Flags = resolved.Ruleset.Flags with { EndTurnWarningScope = EndTurnWarningScope.EveryUnit },
        };
        var initial = new GameSession(
            resolved.World, ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "north");

        var state = initial.State;
        if (sixWarnings)
        {
            var land = WarningArmy("north-land-warning", embarked: false, money: -1);
            var aboard = WarningArmy("north-sea-warning", embarked: true, money: -1);
            var fleet = state.Fleets.Single(fleet => fleet.Id == "north-fleet-1") with
            {
                X = 0,
                Y = 3,
                Ships = 10,
                SupplyTons = 1,
                ConditionPercent = 100,
                CarriedArmyId = aboard.Id,
            };
            state = state with
            {
                Armies = ValueList.Of(land, aboard),
                Fleets = ValueList.Of(fleet),
            };
        }
        else
        {
            // One 50,000-troop army: supply 99 is under the 20% line (500 × 99 = 49,500 < 50,000) and
            // supply 1,000 is over it, while the full purse keeps the mercenary-pay check silent (a
            // regular-only roster owes nothing). The fleet sits on Arx's tile, docked, supplied and
            // fresh, so it trips nothing.
            var land = WarningArmy("north-land-warning", embarked: false, money: 100_000)
                with { SupplyTons = warning ? 99 : 1_000 };
            var fleet = state.Fleets.Single(fleet => fleet.Id == "north-fleet-1") with
            {
                X = 2,
                Y = 1,
                SupplyTons = 1_000,
                ConditionPercent = 100,
            };
            state = state with
            {
                Armies = ValueList.Of(land),
                Fleets = ValueList.Of(fleet),
            };
        }

        var save = new SaveGame(
            state.SchemaVersion,
            "end-turn-box-check",
            "end-turn-box-check",
            state.ScenarioId,
            state.WorldId,
            state.RulesetId,
            state);
        return new GameSession(resolved.World, ruleset, resolved.Scenario, save);
    }

    // One regular 50,000-troop unit: check 1's supply test and check 2's purse test are driven only by
    // the army's own SupplyTons and Money, never by a unit type's price. South's own records are
    // dropped by every caller, so the AI phase after an `end` can open no battle window and the check
    // stays deterministic.
    private static ArmyState WarningArmy(string id, bool embarked, int money) => new(
        id,
        "north",
        X: embarked ? 0 : 2,
        Y: embarked ? 3 : 1,
        Moves: 0,
        Morale: 70,
        Money: money,
        SupplyTons: 99,
        CoveredTileCode: embarked ? null : 2,
        AboardFleetId: embarked ? "north-fleet-1" : null,
        Units: ValueList.Of(new UnitSlot(0, "light_infantry", 50_000, 6, "Regulars")));

    private void Check(bool condition, string description)
    {
        if (condition)
        {
            GD.Print($"PASS: {description}");
            return;
        }

        GD.PrintErr($"FAIL: {description}");
        _ok = false;
    }
}
