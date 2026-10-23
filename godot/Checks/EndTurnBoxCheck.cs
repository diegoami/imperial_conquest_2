using Godot;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
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
            CheckAutoMobilizeBeforeSupport();
            CheckAutoMobilizeRefusalKeepsSlot();
            CheckAutoMobilizeHotseatIsolation();
            CheckAutoRecruitAfterMobilize();
            CheckArmyTravelTargetControls();
            CheckArmyTravelContinuesAcrossTurns();
            CheckArmyTravelHotseatIsolation();
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

    private void CheckAutoMobilizeBeforeSupport()
    {
        SettingsScreen.Commit(new UserSettings { AutoMobilizeReady = true, AutoSupplyArmies = true });
        var mainGame = AddMainGame(BuildAutoMobilizeSession());
        try
        {
            var before = mainGame.Session.State.NationById("north")!.RecruitmentSlots;
            var commands = new List<string>();
            mainGame.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            Check(mainGame.ActiveOverlay is EndTurnBox && commands.Count == 0,
                "auto mobilize waits for End turn confirmation");
            ((EndTurnBox)mainGame.ActiveOverlay!).MakeMoreMovesButton.EmitSignal(Button.SignalName.Pressed);
            Check(commands.Count == 0
                  && mainGame.Session.State.NationById("north")!.RecruitmentSlots.SequenceEqual(before),
                "Make more moves leaves ready and unready slots alone");

            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            ((EndTurnBox)mainGame.ActiveOverlay!).EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            Check(commands.Count >= 3 && commands[0].StartsWith("> mobilize ", StringComparison.Ordinal)
                  && commands.Skip(1).Take(commands.Count - 2).Any(line => line.StartsWith("> buy ", StringComparison.Ordinal))
                  && commands[^1].StartsWith("> end", StringComparison.Ordinal),
                "mobilize runs before army support and one ordinary end");
            var remaining = mainGame.Session.State.NationById("north")!.RecruitmentSlots;
            Check(remaining.Count == 1 && remaining[0].StateCode < 24,
                "the ready slot was mobilized and the unready slot remains");
            Check(mainGame.LastMessageForCheck.Contains("Auto mobilize: 1", StringComparison.Ordinal),
                "the End-turn summary reports the accepted mobilization");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckAutoRecruitAfterMobilize()
    {
        var session = BuildTravelAutoSession();
        var cityId = session.State.NationById("north")!.RecruitmentSlots[0].TargetCityId;
        var army = session.State.ArmyById("north-land-warning")!;
        var destination = FreeLandNeighbour(session, army);
        session.SetArmyTravelTarget(new ArmyTravelTarget(army.Id, army.Nation,
            destination.X, destination.Y));
        var bounds = RecruitTroopBounds.For(session.Ruleset, "light_infantry");
        SettingsScreen.Commit(new UserSettings
        {
            AutoMobilizeReady = true, AutoRecruitEnabled = true, AutoSupplyArmies = true,
            AutoTreasuryReserveTalents = 0,
            AutoRecruitPlans = ValueList.Of(new AutoRecruitCityPlan(session.World.Id, cityId,
                "light_infantry", bounds.Minimum, 2)),
        });
        var mainGame = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            mainGame.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            Check(mainGame.ActiveOverlay is EndTurnBox && commands.Count == 0,
                "auto recruit waits for End turn confirmation");
            ((EndTurnBox)mainGame.ActiveOverlay!).MakeMoreMovesButton.EmitSignal(Button.SignalName.Pressed);
            Check(commands.Count == 0, "Make more moves issues no auto recruit order");

            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            ((EndTurnBox)mainGame.ActiveOverlay!).EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            var mobilize = commands.FindIndex(line => line.StartsWith("> mobilize ", StringComparison.Ordinal));
            var recruit = commands.FindIndex(line => line.StartsWith("> recruit-standing ", StringComparison.Ordinal));
            var support = commands.FindIndex(line => line.StartsWith("> buy ", StringComparison.Ordinal));
            var travel = commands.FindIndex(line => line.StartsWith("> move ", StringComparison.Ordinal));
            Check(mobilize == 0 && recruit == 1 && travel == 2 && support > travel
                  && commands[^1].StartsWith("> end", StringComparison.Ordinal)
                  && commands.Count(line => line.StartsWith("> recruit-standing ", StringComparison.Ordinal)) == 1,
                $"one travel order runs after mobilize and recruit, before support and end ({string.Join(" | ", commands)})");
            Check(mainGame.LastMessageForCheck.Contains("Auto recruit: 1", StringComparison.Ordinal),
                "the End-turn summary reports accepted auto recruitment");
            Check(mainGame.LastMessageForCheck.Contains("Army targets: 1 moved, 1 arrived", StringComparison.Ordinal)
                  && session.TravelTargetFor(army.Id) is null,
                $"the target arrives through an ordinary move and clears visibly ({mainGame.LastMessageForCheck})");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckArmyTravelTargetControls()
    {
        var session = BuildSession(warning: true);
        var army = session.State.ArmyById("north-land-warning")!;
        var destination = FreeLandNeighbour(session, army);
        var mainGame = AddMainGame(session);
        try
        {
            var commands = 0;
            mainGame.CommandIssued += _ => commands++;
            mainGame.SelectArmyForCheck(army.Id);
            mainGame.MenuBar.PressItemForCheck("unit_map.army_set_target");
            Check(mainGame.MapView.TravelTargetPickArmed, "Set travel target arms a map click");
            mainGame.MapView.PickTravelTargetForCheck(new MapClickContext(
                MapClickButton.Left, destination.X, destination.Y, true, false,
                new MapClickTarget(MapEntityKind.Army, "another-army", "south"),
                new MapClickSelection(MapEntityKind.Army, army.Id, army.Moves), 1, 0, 0, "north"));
            Check(mainGame.MapView.TravelTargetPickArmed && session.TravelTargetFor(army.Id) is null,
                "an army marker cannot become a travel target");
            mainGame.MapView.PickTravelTargetForCheck(new MapClickContext(
                MapClickButton.Left, destination.X, destination.Y, true, false, null,
                new MapClickSelection(MapEntityKind.Army, army.Id, army.Moves), 1, 0, 0, "north"));
            Check(session.TravelTargetFor(army.Id) is { } target
                  && target.X == destination.X && target.Y == destination.Y && commands == 0,
                "an open land click saves a fixed target without a game command");
            mainGame.MenuBar.PressItemForCheck("unit_map.army_cancel_target");
            Check(session.TravelTargetFor(army.Id) is null && commands == 0,
                "Cancel travel target clears the plan without a game command");
            var city = session.State.Cities.First();
            mainGame.MenuBar.PressItemForCheck("unit_map.army_set_target");
            mainGame.MapView.PickTravelTargetForCheck(new MapClickContext(
                MapClickButton.Left, city.X, city.Y, true, false,
                new MapClickTarget(MapEntityKind.City, city.Id, city.Owner),
                new MapClickSelection(MapEntityKind.Army, army.Id, army.Moves), 1, 0, 0, "north"));
            Check(session.TravelTargetFor(army.Id)?.CityId == city.Id && commands == 0,
                "a city click saves its fixed city ID without an attack or game command");
            var path = Path.Combine(Path.GetTempPath(), $"ic2-army-travel-check-{Guid.NewGuid():N}.json");
            try
            {
                mainGame.SaveAsForCheck(path);
                var loaded = SaveManager.LoadFile(path, session.World, session.Ruleset);
                var reopened = new GameSession(session.World, session.Ruleset, session.Scenario, loaded);
                Check(reopened.TravelTargetFor(army.Id) == session.TravelTargetFor(army.Id),
                    "the real Save As path persists a campaign target for reopening");
                mainGame.SubmitForCheck($"move {army.Id} {destination.X} {destination.Y}");
                Check(session.TravelTargetFor(army.Id) is null,
                    "a manual move attempt cancels that army's target");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        finally { RemoveMainGame(mainGame); }
    }

    private static (int X, int Y) FreeLandNeighbour(GameSession session, ArmyState army)
    {
        var world = session.World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        for (var y = Math.Max(0, army.Y - 1); y <= Math.Min(world.Height - 1, army.Y + 1); y++)
        for (var x = Math.Max(0, army.X - 1); x <= Math.Min(world.Width - 1, army.X + 1); x++)
        {
            if (x == army.X && y == army.Y) continue;
            if (world.TileTypeByCode(cells[y * world.Width + x])?.PassableByArmies != true) continue;
            if (session.State.Cities.Any(city => city.X == x && city.Y == y)) continue;
            if (session.State.Armies.Any(other => other.Id != army.Id && other.X == x && other.Y == y)) continue;
            if (session.State.Fleets.Any(fleet => !fleet.IsUnderConstruction && fleet.X == x && fleet.Y == y)) continue;
            return (x, y);
        }
        throw new InvalidOperationException("The army has no free neighbouring land for this check.");
    }

    private void CheckArmyTravelContinuesAcrossTurns()
    {
        var original = BuildSession(warning: false);
        var state = original.State with { Armies = ValueList.From(original.State.Armies.Select(army =>
            army.Id == "north-land-warning" ? army with { Moves = 1 } : army)) };
        var save = new SaveGame(state.SchemaVersion, "travel-two-turns", "travel-two-turns",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(original.World, original.Ruleset, original.Scenario, save);
        var army = session.State.ArmyById("north-land-warning")!;
        var destination = FreeLandRun(session, army);
        session.SetArmyTravelTarget(new ArmyTravelTarget(army.Id, army.Nation,
            destination.X, destination.Y));
        var mainGame = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            mainGame.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox first) first.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            var afterFirst = session.State.ArmyById(army.Id)!;
            Check(afterFirst.X != army.X || afterFirst.Y != army.Y,
                "the first confirmed turn makes partial progress toward a distant target");
            Check(session.TravelTargetFor(army.Id) is not null,
                "partial progress retains the campaign target");
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox second) second.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            Check(session.TravelTargetFor(army.Id) is null
                  && session.State.ArmyById(army.Id) is { } arrived
                  && arrived.X == destination.X && arrived.Y == destination.Y,
                "the next confirmed turn resumes and reaches the same fixed target");
            Check(commands.Count(line => line.StartsWith("> move ", StringComparison.Ordinal)) == 2,
                "one ordinary army move is issued per confirmed turn");
        }
        finally { RemoveMainGame(mainGame); }
    }

    private void CheckArmyTravelHotseatIsolation()
    {
        var session = BuildAutoSession(warning: true, hotseat: true);
        var south = session.State.ArmyById("south-auto")!;
        var target = new ArmyTravelTarget(south.Id, south.Nation, south.X, south.Y);
        session.SetArmyTravelTarget(target);
        var mainGame = AddMainGame(session);
        try
        {
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox box) box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            Check(session.TravelTargetFor(south.Id) == target,
                "north's confirmed turn leaves the next hotseat nation's army target untouched");
        }
        finally { RemoveMainGame(mainGame); }
    }

    private static (int X, int Y) FreeLandRun(GameSession session, ArmyState army)
    {
        var world = session.World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var clear = true;
            for (var step = 1; step <= 2; step++)
            {
                var x = army.X + dx * step;
                var y = army.Y + dy * step;
                if ((uint)x >= (uint)world.Width || (uint)y >= (uint)world.Height
                    || world.TileTypeByCode(cells[y * world.Width + x])?.PassableByArmies != true
                    || session.State.Cities.Any(city => city.X == x && city.Y == y)
                    || session.State.Armies.Any(other => other.Id != army.Id && other.X == x && other.Y == y)
                    || session.State.Fleets.Any(fleet => !fleet.IsUnderConstruction && fleet.X == x && fleet.Y == y))
                    clear = false;
            }
            if (clear) return (army.X + 2 * dx, army.Y + 2 * dy);
        }
        throw new InvalidOperationException("The army has no free two-cell land run for this check.");
    }

    private static GameSession BuildTravelAutoSession()
    {
        var original = BuildAutoMobilizeSession();
        var state = original.State with { Armies = ValueList.From(original.State.Armies.Select(army =>
            army.Id == "north-land-warning" ? army with { Moves = 8 } : army)) };
        var save = new SaveGame(state.SchemaVersion, "army-travel-check", "army-travel-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        return new GameSession(original.World, original.Ruleset, original.Scenario, save);
    }

    private void CheckAutoMobilizeRefusalKeepsSlot()
    {
        SettingsScreen.Commit(new UserSettings { AutoMobilizeReady = true });
        var mainGame = AddMainGame(BuildAutoMobilizeSession(refuse: true));
        try
        {
            var commands = new List<string>();
            mainGame.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox box)
                box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            Check(commands.Count == 2 && commands[0].StartsWith("> mobilize ", StringComparison.Ordinal)
                  && commands[1].StartsWith("> end", StringComparison.Ordinal),
                "a refused mobilization still allows one ordinary End turn");
            Check(mainGame.Session.State.NationById("north")!.RecruitmentSlots.Count == 2,
                "a refused ready slot remains in the training queue");
            Check(mainGame.LastMessageForCheck.Contains("Skipped: slot", StringComparison.Ordinal)
                  && mainGame.LastMessageForCheck.Contains("You cannot mobilise a unit at this time.", StringComparison.Ordinal),
                "the actual typed refusal is visible in the auto-mobilize summary");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckAutoMobilizeHotseatIsolation()
    {
        SettingsScreen.Commit(new UserSettings { AutoMobilizeReady = true });
        var original = BuildAutoSession(warning: false, hotseat: true);
        var state = original.State;
        var northCity = state.Cities.First(c => c.Owner == "north");
        var southCity = state.Cities.First(c => c.Owner == "south");
        state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id switch
        {
            "north" => n with { RecruitmentSlots = ValueList.Of(
                new RecruitmentSlot(northCity.Id, "light_infantry", 3_000, 24)) },
            "south" => n with { RecruitmentSlots = ValueList.Of(
                new RecruitmentSlot(southCity.Id, "light_infantry", 3_000, 24)) },
            _ => n,
        })) };
        var save = new SaveGame(state.SchemaVersion, "auto-mobilize-hotseat", "auto-mobilize-hotseat",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var mainGame = AddMainGame(new GameSession(original.World, original.Ruleset, original.Scenario, save));
        try
        {
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox box)
                box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            Check(mainGame.Session.State.NationById("north")!.RecruitmentSlots.Count == 0,
                "active human nation's ready slot is mobilized");
            Check(mainGame.Session.State.NationById("south")!.RecruitmentSlots.Count == 1,
                "next hotseat nation's ready slot is not mobilized early");
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

    private static GameSession BuildAutoMobilizeSession(bool refuse = false)
    {
        var original = BuildSession(warning: true);
        var state = original.State;
        var city = state.Cities.First(c => c.Owner == "north");
        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(c => c.Id == city.Id
                ? c with { SupplyTons = 600 } : c)),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "north"
                ? n with { RecruitmentSlots = ValueList.Of(
                    new RecruitmentSlot(city.Id, "light_infantry", 2_000, 0),
                    new RecruitmentSlot(city.Id, "light_infantry", 3_000, 24)) } : n)),
        };
        var ruleset = original.Ruleset;
        if (refuse)
        {
            var full = state.Armies[0] with { Units = ValueList.From(Enumerable.Range(0, 20)
                .Select(i => new UnitSlot(0, "light_infantry", 1_000, 6, $"Regulars {i}"))) };
            state = state with { Armies = ValueList.Of(full) };
            ruleset = ruleset with { ArmyManagement = ruleset.ArmyManagement with { MaxArmies = 1 } };
        }
        var save = new SaveGame(state.SchemaVersion, "auto-mobilize-check", "auto-mobilize-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        return new GameSession(original.World, ruleset, original.Scenario, save);
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
