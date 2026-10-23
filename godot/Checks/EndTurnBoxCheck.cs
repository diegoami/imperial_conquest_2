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
            CheckConfirmedAutoSupport();
            CheckAutoSupportHotseatSeparation();
            CheckAutoSupportFromFleet();
            CheckFleetAutoSupportFromTown();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"EndTurnBoxCheck: unhandled exception: {ex}");
            _ok = false;
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"EndTurnBoxCheck: exiting with code {exitCode}.");
        CheckExit.Quit(this, exitCode);
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

            DismissOfferNotices(mainGame);
            Check(mainGame.ActiveOverlay is null, "END TURN closes the box");
            Check(beforeCommands == 1, "END TURN submits exactly one command");
            Check(mainGame.Session.State.Calendar.TurnIndex == beforeTurn + 1, "END TURN advances the turn");
        }
        finally
        {
            RemoveMainGame(mainGame);
        }
    }

    // Fix #917: ending the turn can start a turn with the engine's OK-only offer notice. It is dismissed
    // here so the assertions below still fail for any other overlay.
    private static void DismissOfferNotices(MainGameScreen mainGame)
    {
        while (mainGame.ActiveOverlay is MainGameScreen.OfferNoticeBox notice)
        {
            notice.Close();
        }
    }

    private void CheckConfirmedAutoSupport()
    {
        SettingsScreen.Commit(new UserSettings
        {
            AutoSupplyArmies = true, AutoFundArmies = true, AutoSupplyTargetPercent = 100,
            AutoPurseTargetTalents = 900, AutoTreasuryReserveTalents = 2_000,
        });
        var mainGame = AddMainGame(BuildAutoSession(warning: true));
        try
        {
            var initial = mainGame.Session.State.ArmyById("north-land-warning")!;
            var snapshots = new List<(int Supply, int Money, int Turn)>();
            mainGame.CommandIssued += _ =>
            {
                var army = mainGame.Session.State.ArmyById(initial.Id)!;
                snapshots.Add((army.SupplyTons, army.Money, mainGame.Session.State.Calendar.TurnIndex));
            };
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            Check(mainGame.ActiveOverlay is EndTurnBox && snapshots.Count == 0,
                "auto support waits for warning confirmation");
            ((EndTurnBox)mainGame.ActiveOverlay!).MakeMoreMovesButton.EmitSignal(Button.SignalName.Pressed);
            Check(snapshots.Count == 0 && mainGame.Session.State.ArmyById(initial.Id) == initial,
                "Make more moves does not auto supply or fund");

            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            ((EndTurnBox)mainGame.ActiveOverlay!).EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            Check(snapshots.Count == 3, $"confirmed auto support submits buy, transfer-money, then end ({snapshots.Count})");
            if (snapshots.Count >= 3)
            {
                Check(snapshots[0].Supply > initial.SupplyTons && snapshots[0].Money == 0,
                    "own-city supply command is accepted before money");
                Check(snapshots[1].Money == 500 && snapshots[1].Turn == snapshots[0].Turn,
                    "treasury reserve limits the accepted purse transfer to 500");
                Check(snapshots[2].Turn == snapshots[0].Turn + 1,
                    "ordinary end command runs after auto support");
            }
            Check(mainGame.LastMessageForCheck.Contains("Auto support:", StringComparison.Ordinal),
                "the confirmed turn shows a compact auto-support summary");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }

        SettingsScreen.Commit(new UserSettings { AutoFundArmies = true,
            AutoSupplyFleets = true, AutoRepairFleets = true,
            AutoPurseTargetTalents = 900, AutoTreasuryReserveTalents = 2_000 });
        var unwarned = AddMainGame(BuildAutoSession(warning: false));
        try
        {
            var commands = 0;
            unwarned.CommandIssued += _ => commands++;
            unwarned.MenuBar.PressItemForCheck("game.end_turn");
            DismissOfferNotices(unwarned);
            Check(unwarned.ActiveOverlay is null && commands == 2,
                "no-warning route funds once then ends once without a box");
        }
        finally
        {
            RemoveMainGame(unwarned);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckAutoSupportHotseatSeparation()
    {
        SettingsScreen.Commit(new UserSettings { AutoFundArmies = true,
            AutoPurseTargetTalents = 900, AutoTreasuryReserveTalents = 2_000 });
        var mainGame = AddMainGame(BuildAutoSession(warning: false, hotseat: true));
        try
        {
            var otherBefore = mainGame.Session.State.ArmyById("south-auto")!.Money;
            var otherFleetBefore = mainGame.Session.State.FleetById("south-fleet-auto")!;
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox box)
                box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            Check(mainGame.Session.State.ArmyById("north-land-warning")!.Money == 500,
                "global auto funding acts for the active human nation");
            Check(mainGame.Session.State.ArmyById("south-auto")!.Money == otherBefore,
                "global auto funding does not pre-fund the next hotseat nation");
            Check(mainGame.Session.State.FleetById("south-fleet-auto")!.SupplyTons == otherFleetBefore.SupplyTons
                  && mainGame.Session.State.FleetById("south-fleet-auto")!.ConditionPercent == otherFleetBefore.ConditionPercent,
                "global fleet support does not act for the next hotseat nation");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckAutoSupportFromFleet()
    {
        SettingsScreen.Commit(new UserSettings { AutoSupplyArmies = true });
        var mainGame = AddMainGame(BuildAutoSession(warning: true, fleetProvider: true));
        try
        {
            var fleetBefore = mainGame.Session.State.FleetById("north-fleet-1")!.SupplyTons;
            var armyBefore = mainGame.Session.State.ArmyById("north-land-warning")!.SupplyTons;
            var afterBuy = (Army: armyBefore, Fleet: fleetBefore);
            var commands = 0;
            mainGame.CommandIssued += _ =>
            {
                commands++;
                if (commands == 1)
                    afterBuy = (mainGame.Session.State.ArmyById("north-land-warning")!.SupplyTons,
                        mainGame.Session.State.FleetById("north-fleet-1")!.SupplyTons);
            };
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            ((EndTurnBox)mainGame.ActiveOverlay!).EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            Check(commands == 2 && afterBuy.Army > armyBefore && afterBuy.Fleet < fleetBefore,
                "confirmed auto supply uses the existing fleet-provider buy command before end");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckFleetAutoSupportFromTown()
    {
        SettingsScreen.Commit(new UserSettings
        {
            AutoSupplyFleets = true, AutoRepairFleets = true,
            AutoTreasuryReserveTalents = 4_900,
        });
        var mainGame = AddMainGame(BuildFleetAutoSession());
        try
        {
            var fleetId = "north-fleet-1";
            var fleetBefore = mainGame.Session.State.FleetById(fleetId)!;
            var first = (Supply: fleetBefore.SupplyTons, Condition: fleetBefore.ConditionPercent, Moves: fleetBefore.Moves);
            var second = first;
            var commands = 0;
            mainGame.CommandIssued += _ =>
            {
                commands++;
                var fleet = mainGame.Session.State.FleetById(fleetId)!;
                if (commands == 1) first = (fleet.SupplyTons, fleet.ConditionPercent, fleet.Moves);
                if (commands == 2) second = (fleet.SupplyTons, fleet.ConditionPercent, fleet.Moves);
            };
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox box)
                box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            Check(commands == 3, $"fleet support submits town supply, repair, then end ({commands})");
            Check(first.Supply > fleetBefore.SupplyTons && first.Condition == fleetBefore.ConditionPercent,
                "the town supplies the fleet before repair");
            Check(second.Condition == 100 && second.Moves == 0,
                "repair reaches the target and consumes remaining moves before End turn");
            Check(mainGame.LastMessageForCheck.Contains("Auto fleets:", StringComparison.Ordinal),
                "fleet support reports its supply and repair result");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckUnwarnedSeatEndsImmediately()
    {
        var control = BuildSession(warning: false);
        control.Submit("end");
        var mainGame = AddMainGame(BuildSession(warning: false));
        try
        {
            var beforeTurn = mainGame.Session.State.Calendar.TurnIndex;
            var commands = 0;
            mainGame.CommandIssued += _ => commands++;
            mainGame.MenuBar.PressItemForCheck("game.end_turn");

            DismissOfferNotices(mainGame);
            Check(mainGame.ActiveOverlay is null, "a seat with no warning has no box");
            Check(commands == 1, "a seat with no warning submits end immediately");
            Check(mainGame.Session.State.Calendar.TurnIndex == beforeTurn + 1, "an unwarned seat advances immediately");
            Check(mainGame.Session.State == control.State
                  && mainGame.Session.State.RandomSeed == control.State.RandomSeed,
                "automation-off End turn retains the exact state and RNG of one ordinary end");
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

    private static GameSession BuildAutoSession(bool warning, bool hotseat = false, bool fleetProvider = false)
    {
        var original = BuildSession(warning);
        var state = original.State;
        state = state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a with { Money = 0 })),
            Cities = ValueList.From(state.Cities.Select(c => c.Owner == "north"
                ? c with { SupplyTons = 600 } : c)),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "north"
                ? n with { Treasury = 2_500 } : n)),
        };
        if (hotseat)
        {
            var south = WarningArmy("south-auto", embarked: false, money: 0)
                with { Nation = "south", SupplyTons = 1_000 };
            var southFleet = state.Fleets.Single(f => f.Id == "north-fleet-1") with
            {
                Id = "south-fleet-auto", Nation = "south", X = 0, Y = 3,
                Ships = 10, SupplyTons = 0, ConditionPercent = 50,
                ConstructionTicksRemaining = null, CarriedArmyId = null,
            };
            state = state with
            {
                Armies = ValueList.From(state.Armies.Append(south)),
                Fleets = ValueList.From(state.Fleets.Append(southFleet)),
                Nations = ValueList.From(state.Nations.Select(n => n.Id == "south"
                    ? n with { Control = SeatControl.Human, Treasury = 2_500 } : n)),
            };
        }
        if (fleetProvider)
        {
            state = state with
            {
                Cities = ValueList.From(state.Cities.Select(c => c.Owner == "north"
                    ? c with { SupplyTons = 0 } : c)),
                Fleets = ValueList.From(state.Fleets.Select(f => f.Id == "north-fleet-1"
                    ? f with { SupplyTons = 600 } : f)),
            };
        }
        var save = new SaveGame(state.SchemaVersion, "auto-support-check", "auto-support-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        return new GameSession(original.World, original.Ruleset, original.Scenario, save);
    }

    private static GameSession BuildFleetAutoSession()
    {
        var original = BuildSession(warning: false);
        var state = original.State with
        {
            Cities = ValueList.From(original.State.Cities.Select(c => c.Owner == "north"
                ? c with { SupplyTons = 500 } : c)),
            Fleets = ValueList.From(original.State.Fleets.Select(f => f.Id == "north-fleet-1"
                ? f with { SupplyTons = 0, ConditionPercent = 50, Ships = 10,
                    ConstructionTicksRemaining = null, CarriedArmyId = null, Moves = 8 } : f)),
            Nations = ValueList.From(original.State.Nations.Select(n => n.Id == "north"
                ? n with { Treasury = 5_000 } : n)),
        };
        var save = new SaveGame(state.SchemaVersion, "fleet-auto-check", "fleet-auto-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        return new GameSession(original.World, original.Ruleset, original.Scenario, save);
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
