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
            CheckDerivedSupportWhileInDebt();
            CheckAutoMobilizeBeforeSupport();
            CheckAutoMobilizeRefusalKeepsSlot();
            CheckAutoMobilizeHotseatIsolation();
            CheckLegacyPlansDoNotRecruit();
            CheckArmyTravelTargetControls();
            CheckArmyTravelContinuesAcrossTurns();
            CheckArmyTravelHotseatIsolation();
            CheckRetreatSupplyControlsAndEndTurn();
            CheckRetreatFleetSupplyAndRepair();
            CheckRetreatDangerCancelsBeforeCommands();
            CheckRetreatSplitSuggestionDoesNotAutoSplit();
            CheckRetreatReplacesFixedTargetOnlyAfterConfirmation();
            CheckRetreatHotseatIsolation();
            CheckRetreatContinuesToAnotherTown();
            CheckRallyPointControls();
            CheckRallyInvalidTargets();
            foreach (var automatic in new[] { false, true })
            foreach (var existingReceiver in new[] { false, true })
                CheckRallyMobilization(automatic, existingReceiver);
            foreach (var automatic in new[] { false, true })
                CheckRallyMobilization(automatic, existingReceiver: false, newArmyMoves: 0);
            CheckTravelSafetyStops();
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
            var expectedFunding = Math.Min(IC2.Engine.Economy.ArmyUpkeep.Compute(initial.Units, mainGame.Session.Ruleset),
                mainGame.Session.Ruleset.Economy.PurseCapPerUnit);
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
                Check(snapshots[1].Money == expectedFunding && snapshots[1].Turn == snapshots[0].Turn,
                    "accepted purse transfer uses quarterly upkeep, ignoring the legacy reserve");
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
            var ownArmy = mainGame.Session.State.ArmyById("north-land-warning")!;
            var expectedFunding = Math.Min(IC2.Engine.Economy.ArmyUpkeep.Compute(ownArmy.Units, mainGame.Session.Ruleset),
                mainGame.Session.Ruleset.Economy.PurseCapPerUnit);
            var otherFleetBefore = mainGame.Session.State.FleetById("south-fleet-auto")!;
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox box)
                box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            Check(mainGame.Session.State.ArmyById("north-land-warning")!.Money == expectedFunding,
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

    private void CheckDerivedSupportWhileInDebt()
    {
        var original = BuildFleetAutoSession();
        var nationId = original.State.ActiveNationId;
        var army = original.State.Armies.First(a => a.Nation == nationId) with { Money = 0 };
        var fleet = original.State.Fleets.First(f => f.Nation == nationId);
        var state = original.State with { Armies = ValueList.Of(army),
            Nations = ValueList.From(original.State.Nations.Select(n => n.Id == nationId
                ? n with { Treasury = -100, Wealth = 1000000, Unity = 900 } : n)) };
        var save = new SaveGame(state.SchemaVersion, "derived-debt", "derived-debt", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(original.World, original.Ruleset, original.Scenario, save);
        SettingsScreen.Commit(new UserSettings { AutoFundArmies = true, AutoRepairFleets = true,
            AutoPurseTargetTalents = 0, AutoFleetRepairTargetPercent = 0, AutoTreasuryReserveTalents = 100000 });
        var main = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            var cost = IC2.Engine.Economy.ArmyUpkeep.Compute(army.Units, session.Ruleset);
            var repairCost = fleet.Ships * (session.Ruleset.Naval.MaxConditionPercent - fleet.ConditionPercent)
                / session.Ruleset.Naval.RepairCostDivisor;
            main.CommandIssued += lines =>
            {
                var line = lines.FirstOrDefault() ?? string.Empty;
                commands.Add(line);
                if (line.StartsWith("> repair-fleet ", StringComparison.Ordinal))
                    Check(session.State.FleetById(fleet.Id)!.ConditionPercent == session.Ruleset.Naval.MaxConditionPercent
                          && session.State.FleetById(fleet.Id)!.Moves == 0
                          && session.State.NationById(nationId)!.Treasury == -100 - cost - repairCost,
                        "confirmed support funds quarterly upkeep and repairs fully in debt, without the legacy reserve");
            };
            main.MenuBar.PressItemForCheck("game.end_turn");
            if (main.ActiveOverlay is EndTurnBox box) box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(main);
            Check(commands.Count == 3 && commands[0].StartsWith("> transfer-money ", StringComparison.Ordinal)
                  && commands[1].StartsWith("> repair-fleet ", StringComparison.Ordinal)
                  && commands[2].StartsWith("> end", StringComparison.Ordinal), "derived debt support preserves order and one End turn");
        }
        finally { RemoveMainGame(main); SettingsScreen.Commit(new UserSettings()); }
    }

    private void CheckAutoMobilizeBeforeSupport()
    {
        SettingsScreen.Commit(new UserSettings { AutoMobilizeReady = true, AutoSupplyArmies = true });
        var mainGame = AddMainGame(BuildAutoMobilizeSession(lowerQualitySlots: true));
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
            Check(remaining.Count == 3 && remaining.Any(s => s.StateCode == 18)
                  && remaining.Any(s => s.StateCode == 22),
                "Average slot mobilized; unready, Very poor and Poor slots remain and train normally");
            Check(mainGame.LastMessageForCheck.Contains("Auto mobilize: 1", StringComparison.Ordinal),
                "the End-turn summary reports the accepted mobilization");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckLegacyPlansDoNotRecruit()
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
                "automation waits for End turn confirmation");
            ((EndTurnBox)mainGame.ActiveOverlay!).MakeMoreMovesButton.EmitSignal(Button.SignalName.Pressed);
            Check(commands.Count == 0, "Make more moves issues no auto recruit order");

            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            ((EndTurnBox)mainGame.ActiveOverlay!).EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            var mobilize = commands.FindIndex(line => line.StartsWith("> mobilize ", StringComparison.Ordinal));
            var recruit = commands.FindIndex(line => line.StartsWith("> recruit-standing ", StringComparison.Ordinal));
            var support = commands.FindIndex(line => line.StartsWith("> buy ", StringComparison.Ordinal));
            var travel = commands.FindIndex(line => line.StartsWith("> move ", StringComparison.Ordinal));
            Check(mobilize == 0 && recruit == -1 && travel == 1 && support > travel
                  && commands[^1].StartsWith("> end", StringComparison.Ordinal)
                  && commands.Count(line => line.StartsWith("> end", StringComparison.Ordinal)) == 1,
                $"legacy plan adds no recruitment between mobilization, travel, support and one end ({string.Join(" | ", commands)})");
            Check(!mainGame.LastMessageForCheck.Contains("Auto recruit:", StringComparison.Ordinal)
                  && session.State.NationById("north")!.RecruitmentSlots.Count == 1,
                "no legacy target replenishes the queue; the already placed unready slot remains");
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

    private void CheckRetreatSupplyControlsAndEndTurn()
    {
        SettingsScreen.Commit(new UserSettings());
        var session = BuildAutoSession(warning: true);
        var army = session.State.ArmyById("north-land-warning")!;
        var mainGame = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            mainGame.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            mainGame.SelectArmyForCheck(army.Id);
            Check(mainGame.RetreatSupplyButtonForCheck.Visible,
                "a selected owned army shows the Retreat and supply control");
            Check(mainGame.RetreatSupplyButtonForCheck.Icon is not null && mainGame.RetreatSupplyButtonForCheck.Text.Length == 0
                  && mainGame.Toolbar.IsAncestorOf(mainGame.RetreatSupplyButtonForCheck),
                "recovery is an icon in the toolbar instead of a full-width row");
            Check(mainGame.RallyPointSetButtonForCheck.Icon is not null && mainGame.RallyPointSetButtonForCheck.Text.Length == 0
                  && mainGame.Toolbar.IsAncestorOf(mainGame.RallyPointSetButtonForCheck),
                "rally point is a compact toolbar icon");
            Check(mainGame.MenuBar.GetParent().FindChild("CampaignDate", true, false) is Label,
                "the campaign date shares the menu row");
            Check(new[] { "unit_map.army_split_two", "unit_map.army_set_target" }.All(id =>
                    mainGame.CommandStrip.ButtonFor(id) is { Icon: not null, Text.Length: 0 } button && button.TooltipText.Length > 0),
                "quick split and travel target are icons with readable tooltips");
            mainGame.RetreatSupplyButtonForCheck.EmitSignal(Button.SignalName.Pressed);
            Check(session.RetreatSupplyPlanFor(army.Id, isFleet: false) is not null
                  && mainGame.RetreatSupplyStatusForCheck.Contains(army.Id, StringComparison.Ordinal)
                  && commands.Count == 0,
                "enabling per-army recovery stores a visible campaign plan without a command");
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            Check(mainGame.ActiveOverlay is EndTurnBox && commands.Count == 0,
                "Retreat and supply waits for End turn confirmation");
            ((EndTurnBox)mainGame.ActiveOverlay!).EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            var buy = commands.FindIndex(line => line.StartsWith("> buy ", StringComparison.Ordinal));
            var money = commands.FindIndex(line => line.StartsWith("> transfer-money ", StringComparison.Ordinal));
            Check(buy >= 0 && money > buy && commands[^1].StartsWith("> end", StringComparison.Ordinal)
                  && session.RetreatSupplyPlanFor(army.Id, isFleet: false) is null,
                $"recovery uses ordinary supply/funding before one end and clears at full supplies ({string.Join(" | ", commands)})");
            Check(mainGame.LastMessageForCheck.Contains("Retreat and supply:", StringComparison.Ordinal),
                "the confirmed turn reports recovery results");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckRetreatFleetSupplyAndRepair()
    {
        SettingsScreen.Commit(new UserSettings());
        var session = BuildFleetAutoSession();
        var fleetId = "north-fleet-1";
        var mainGame = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            mainGame.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            mainGame.SelectFleetForCheck(fleetId);
            mainGame.RetreatSupplyButtonForCheck.EmitSignal(Button.SignalName.Pressed);
            Check(session.RetreatSupplyPlanFor(fleetId, isFleet: true) is not null && commands.Count == 0,
                "a launched fleet can enter recovery without issuing a command");
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox box) box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            var supply = commands.FindIndex(line => line.StartsWith("> buy-fleet-supply ", StringComparison.Ordinal));
            var repair = commands.FindIndex(line => line.StartsWith("> repair-fleet ", StringComparison.Ordinal));
            Check(supply >= 0 && repair > supply && commands[^1].StartsWith("> end", StringComparison.Ordinal)
                  && session.RetreatSupplyPlanFor(fleetId, isFleet: true) is null
                  && session.State.FleetById(fleetId) is { } recovered
                  && recovered.ConditionPercent == session.Ruleset.Naval.MaxConditionPercent,
                $"fleet recovery supplies and repairs through ordinary commands before one end ({string.Join(" | ", commands)})");
            session.SetRetreatSupplyPlan(new RetreatSupplyPlan(fleetId, "north", IsFleet: true));
            mainGame.SubmitForCheck($"move-fleet {fleetId} 0 3");
            Check(session.RetreatSupplyPlanFor(fleetId, isFleet: true) is null,
                "a manual fleet move attempt cancels that fleet's recovery mode");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckRetreatDangerCancelsBeforeCommands()
    {
        SettingsScreen.Commit(new UserSettings());
        var source = BuildAutoSession(warning: true);
        var state = source.State;
        var enemy = state.Fleets.Single(f => f.Id == "north-fleet-1") with
        {
            Id = "south-threat", Nation = "south", X = 0, Y = 3,
            ConstructionTicksRemaining = null, CarriedArmyId = null,
        };
        state = state with
        {
            Fleets = ValueList.From(state.Fleets.Append(enemy)),
            Relations = state.Relations.WithRelation("north", "south", source.Ruleset.Diplomacy.StateCodes.War),
        };
        var save = new SaveGame(state.SchemaVersion, "recovery-danger", "recovery-danger",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(source.World, source.Ruleset, source.Scenario, save);
        session.SetRetreatSupplyPlan(new RetreatSupplyPlan("north-land-warning", "north", IsFleet: false));
        var mainGame = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            mainGame.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox box) box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            Check(session.RetreatSupplyPlanFor("north-land-warning", isFleet: false) is null
                  && commands.All(line => !line.StartsWith("> buy ", StringComparison.Ordinal))
                  && mainGame.LastMessageForCheck.Contains("enemy fleet", StringComparison.Ordinal),
                "a nearby enemy fleet cancels recovery before any supply command");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private void CheckRetreatSplitSuggestionDoesNotAutoSplit()
    {
        var source = BuildAutoSession(warning: true);
        var army = source.State.ArmyById("north-land-warning")!;
        var first = army.Units[0] with { Troops = 25_000, Name = "First" };
        var second = army.Units[0] with { Troops = 25_000, Name = "Second" };
        var state = source.State with
        {
            Armies = ValueList.From(source.State.Armies.Select(a => a.Id == army.Id
                ? a with { Units = ValueList.Of(first, second) } : a)),
        };
        var save = new SaveGame(state.SchemaVersion, "recovery-split", "recovery-split",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(source.World, source.Ruleset, source.Scenario, save);
        session.SetRetreatSupplyPlan(new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false));
        var mainGame = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            mainGame.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            mainGame.SelectArmyForCheck(army.Id);
            Check(mainGame.RetreatSplitButtonForCheck.Visible,
                "a multi-unit recovery army shows the split suggestion control");
            mainGame.RetreatSplitButtonForCheck.EmitSignal(Button.SignalName.Pressed);
            Check(mainGame.ActiveOverlay is SplitArmyDialog split
                  && split.NewArmyListForCheck.ItemCount > 0
                  && !string.IsNullOrEmpty(split.StrengthTextForCheck),
                "split suggestion opens an editable strength-balanced dialog");
            if (mainGame.ActiveOverlay is SplitArmyDialog editable)
            {
                var suggested = editable.NewArmyListForCheck.ItemCount;
                editable.UnstageUnitForCheck(0);
                Check(editable.NewArmyListForCheck.ItemCount < suggested,
                    "the player can fine-tune suggested whole units before confirming");
            }
            if (mainGame.ActiveOverlay is SplitArmyDialog dialog) dialog.CancelForCheck();
            Check(commands.Count == 0 && session.RetreatSupplyPlanFor(army.Id, isFleet: false) is not null,
                "cancelling the suggested split issues no command and keeps recovery active");
            var destination = FreeLandNeighbour(session, army);
            mainGame.SubmitForCheck($"move {army.Id} {destination.X} {destination.Y}");
            Check(session.RetreatSupplyPlanFor(army.Id, isFleet: false) is null,
                "a manual move attempt cancels recovery for that army");
        }
        finally { RemoveMainGame(mainGame); }
    }

    private void CheckRetreatReplacesFixedTargetOnlyAfterConfirmation()
    {
        var session = BuildSession(warning: true);
        var army = session.State.ArmyById("north-land-warning")!;
        var destination = FreeLandNeighbour(session, army);
        session.SetArmyTravelTarget(new ArmyTravelTarget(army.Id, army.Nation,
            destination.X, destination.Y));
        var mainGame = AddMainGame(session);
        try
        {
            var commands = 0;
            mainGame.CommandIssued += _ => commands++;
            mainGame.SelectArmyForCheck(army.Id);
            mainGame.RetreatSupplyButtonForCheck.EmitSignal(Button.SignalName.Pressed);
            Check(mainGame.ActiveOverlay is ConfirmPrompt
                  && session.TravelTargetFor(army.Id) is not null
                  && session.RetreatSupplyPlanFor(army.Id, isFleet: false) is null,
                "a fixed target triggers an explicit recovery replacement prompt");
            if (mainGame.ActiveOverlay is ConfirmPrompt no) no.AnswerForCheck(yes: false);
            Check(session.TravelTargetFor(army.Id) is not null && commands == 0,
                "No keeps the original travel target without issuing a command");
            mainGame.RetreatSupplyButtonForCheck.EmitSignal(Button.SignalName.Pressed);
            if (mainGame.ActiveOverlay is ConfirmPrompt yes) yes.AnswerForCheck(yes: true);
            Check(session.TravelTargetFor(army.Id) is null
                  && session.RetreatSupplyPlanFor(army.Id, isFleet: false) is not null
                  && commands == 0,
                "Yes replaces the fixed target with the per-unit recovery mode without a command");
        }
        finally { RemoveMainGame(mainGame); }
    }

    private void CheckRetreatHotseatIsolation()
    {
        var session = BuildAutoSession(warning: true, hotseat: true);
        var plan = new RetreatSupplyPlan("south-auto", "south", IsFleet: false);
        session.SetRetreatSupplyPlan(plan);
        var mainGame = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            mainGame.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            mainGame.MenuBar.PressItemForCheck("game.end_turn");
            if (mainGame.ActiveOverlay is EndTurnBox box) box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            DismissOfferNotices(mainGame);
            Check(session.RetreatSupplyPlanFor(plan.UnitId, isFleet: false) == plan
                  && commands.All(line => !line.Contains(plan.UnitId, StringComparison.Ordinal)),
                "north's turn leaves the next human seat's recovery plan and unit untouched");
        }
        finally { RemoveMainGame(mainGame); }
    }

    private void CheckRetreatContinuesToAnotherTown()
    {
        SettingsScreen.Commit(new UserSettings());
        var source = BuildAutoSession(warning: true);
        var state = source.State with
        {
            Armies = ValueList.From(source.State.Armies.Select(a => a.Id == "north-land-warning"
                ? a with { Moves = 1, SupplyTons = 0 } : a)),
            Fleets = ValueList.From(source.State.Fleets.Select(f => f.Id == "north-fleet-1"
                ? f with { SupplyTons = 0 } : f)),
            Cities = ValueList.From(source.State.Cities.Select(c => c.Id == "arx"
                ? c with { SupplyTons = 20 }
                : c.Id == "portus" ? c with { SupplyTons = 5_000 } : c)),
        };
        var save = new SaveGame(state.SchemaVersion, "recovery-two-towns", "recovery-two-towns",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(source.World, source.Ruleset, source.Scenario, save);
        var armyId = "north-land-warning";
        session.SetRetreatSupplyPlan(new RetreatSupplyPlan(armyId, "north", IsFleet: false));
        var mainGame = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            var maxSupplyAfterBuy = 0;
            mainGame.CommandIssued += lines =>
            {
                var line = lines.FirstOrDefault() ?? string.Empty;
                commands.Add(line);
                if (line.StartsWith($"> buy {armyId} ", StringComparison.Ordinal))
                    maxSupplyAfterBuy = Math.Max(maxSupplyAfterBuy,
                        session.State.ArmyById(armyId)?.SupplyTons ?? 0);
            };
            for (var turn = 0; turn < 6 && session.RetreatSupplyPlanFor(armyId, isFleet: false) is not null; turn++)
            {
                var first = commands.Count;
                mainGame.MenuBar.PressItemForCheck("game.end_turn");
                if (mainGame.ActiveOverlay is EndTurnBox box) box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
                DismissOfferNotices(mainGame);
                var thisTurn = commands.Skip(first).ToArray();
                Check(thisTurn.Count(line => line.StartsWith($"> move {armyId} ", StringComparison.Ordinal)) <= 1,
                    $"recovery issues at most one army move on turn {turn + 1}");
                if (turn == 0)
                    Check(thisTurn.Any(line => line.StartsWith($"> buy {armyId} arx ", StringComparison.Ordinal))
                          && session.RetreatSupplyPlanFor(armyId, isFleet: false)?.DestinationCityId == "portus",
                        "the first turn consumes Arx's limited stock then targets Portus");
            }
            var capacity = IC2.Engine.Economy.SupplyCapacity.ArmyDialogCapacityTons(
                session.State.ArmyById(armyId)!.TotalTroops, session.Ruleset);
            Check(session.RetreatSupplyPlanFor(armyId, isFleet: false) is null
                  && maxSupplyAfterBuy >= capacity,
                $"the army reaches another owned town and fills before End turn's normal consumption "
                + $"(saw {maxSupplyAfterBuy}/{capacity}; commands {string.Join(" | ", commands)})");
        }
        finally
        {
            RemoveMainGame(mainGame);
            SettingsScreen.Commit(new UserSettings());
        }
    }

    private static GameSession BuildRallySession(bool existingReceiver, int newArmyMoves = 2)
    {
        var original = BuildAutoMobilizeSession();
        var world = original.World with { Width = 16, Height = 16,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 256))) };
        var city = original.State.Cities.First(c => c.Owner == "north");
        var southCity = original.State.Cities.First(c => c.Owner == "south");
        var army = original.State.Armies[0] with
        {
            X = existingReceiver ? city.X + 1 : 10,
            Y = existingReceiver ? city.Y : 10,
            Moves = 0,
        };
        var state = original.State with
        {
            Armies = ValueList.Of(army), Fleets = ValueList<FleetState>.Empty,
            Nations = ValueList.From(original.State.Nations.Select(n => n.Id == "south"
                ? n with { Control = SeatControl.Human, RecruitmentSlots = ValueList.Of(
                    new RecruitmentSlot(southCity.Id, "light_infantry", 3_000, 24)) } : n)),
        };
        var save = new SaveGame(state.SchemaVersion, "rally-check", "rally-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var rules = original.Ruleset with { ArmyManagement = original.Ruleset.ArmyManagement with
            { NewArmyMovesHumanSeat = newArmyMoves, NewArmyMovesAiSeat = newArmyMoves } };
        return new GameSession(world, rules, original.Scenario, save);
    }

    private void CheckRallyPointControls()
    {
        SettingsScreen.Commit(new UserSettings());
        var session = BuildRallySession(false);
        var main = AddMainGame(session);
        try
        {
            var commands = 0;
            main.CommandIssued += _ => commands++;
            var before = session.State;
            void Pick(int x, int y) => main.MapView.PickTravelTargetForCheck(new MapClickContext(
                MapClickButton.Left, x, y, true, false, null, null, 1, 0, 0, "north"));
            main.RallyPointSetButtonForCheck.EmitSignal(Button.SignalName.Pressed);
            Check(main.MapView.TravelTargetPickArmed, "rally control arms the production map picker");
            Pick(12, 8);
            Check(session.RallyPointFor("north") == new NationRallyPoint("north", 12, 8)
                  && commands == 0 && session.State == before,
                "rally picker saves campaign metadata without issuing an order or changing RNG");
            main.RallyPointSetButtonForCheck.EmitSignal(Button.SignalName.Pressed);
            Check(main.ActiveOverlay is ConfirmPrompt, "rally replacement asks for confirmation");
            if (main.ActiveOverlay is ConfirmPrompt cancel) cancel.AnswerForCheck(false);
            Check(session.RallyPointFor("north") == new NationRallyPoint("north", 12, 8)
                  && !main.MapView.TravelTargetPickArmed, "cancel preserves the old rally point");
            main.RallyPointSetButtonForCheck.EmitSignal(Button.SignalName.Pressed);
            if (main.ActiveOverlay is ConfirmPrompt replace) replace.AnswerForCheck(true);
            Pick(13, 8);
            Check(session.RallyPointFor("north") == new NationRallyPoint("north", 13, 8),
                "confirmed rally replacement uses the next map selection");
            session.SetRallyPoint(new NationRallyPoint("south", 14, 9));
            var path = Path.Combine(Path.GetTempPath(), $"ic2-rally-ui-{Guid.NewGuid():N}.json");
            try
            {
                main.SaveAsForCheck(path);
                var save = SaveManager.LoadFile(path, session.World, session.Ruleset);
                var resumed = new GameSession(session.World, session.Ruleset, session.Scenario, save);
                Check(resumed.NationRallyPoints.SequenceEqual(session.NationRallyPoints),
                    "production Save As preserves both nations' rally settings for reload");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            var commandsBeforeClear = commands;
            main.RallyPointClearButtonForCheck.EmitSignal(Button.SignalName.Pressed);
            Check(session.RallyPointFor("north") is null && session.RallyPointFor("south") is not null
                  && commands == commandsBeforeClear, "clear removes only the active nation's rally point without an order");
        }
        finally { RemoveMainGame(main); }
    }

    private void CheckRallyInvalidTargets()
    {
        foreach (var foreignCity in new[] { false, true })
        {
            var session = BuildRallySession(false);
            var city = session.State.Cities.First(c => c.Owner == "south");
            session.SetRallyPoint(foreignCity
                ? new NationRallyPoint("north", city.X, city.Y, city.Id)
                : new NationRallyPoint("north", 999, 999));
            var main = AddMainGame(session);
            try
            {
                main.SubmitForCheck("mobilize 1 invalid-rally-created");
                Check(session.State.ArmyById("invalid-rally-created") is not null
                      && session.RallyPointFor("north") is null
                      && session.TravelTargetFor("invalid-rally-created") is null,
                    "new-army assignment clears stale foreign-city/out-of-world rally data without a hidden order");
            }
            finally { RemoveMainGame(main); }
        }
    }

    private void CheckRallyMobilization(bool automatic, bool existingReceiver, int newArmyMoves = 2)
    {
        SettingsScreen.Commit(new UserSettings { AutoMobilizeReady = automatic });
        var session = BuildRallySession(existingReceiver, newArmyMoves);
        var rally = new NationRallyPoint("north", 12, 8);
        var southRally = new NationRallyPoint("south", 14, 9);
        session.SetRallyPoint(rally);
        session.SetRallyPoint(southRally);
        var existingArmy = session.State.Armies[0];
        var previousTarget = new ArmyTravelTarget(existingArmy.Id, "north", 14, 12);
        if (existingReceiver) session.SetArmyTravelTarget(previousTarget);
        var southSlots = session.State.NationById("south")!.RecruitmentSlots;
        var mobilized = new List<IC2.Engine.Recruitment.Commands.RecruitMobilized>();
        session.EventsPublished += events => mobilized.AddRange(
            events.OfType<IC2.Engine.Recruitment.Commands.RecruitMobilized>());
        var main = AddMainGame(session);
        try
        {
            var commands = new List<string>();
            main.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
            if (!automatic)
            {
                main.SubmitForCheck("mobilize 1 rally-created");
                Check(commands.Count == 1 && !commands.Any(line => line.StartsWith("> move ", StringComparison.Ordinal)),
                    "manual mobilization assigns a rally target but does not move immediately");
            }
            main.MenuBar.PressItemForCheck("game.end_turn");
            Check(main.ActiveOverlay is EndTurnBox, "rally travel waits for confirmed End turn");
            if (main.ActiveOverlay is EndTurnBox cancel) cancel.MakeMoreMovesButton.EmitSignal(Button.SignalName.Pressed);
            Check(commands.Count == (automatic ? 0 : 1), "Make more moves issues no rally or mobilize orders");
            if (!automatic && !existingReceiver)
                Check(session.TravelTargetFor("rally-created") is { FromRally: true, X: 12, Y: 8 },
                    "real manual mobilization event wires the new army to its nation's rally target");

            main.MenuBar.PressItemForCheck("game.end_turn");
            if (main.ActiveOverlay is EndTurnBox confirm) confirm.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
            var eventRow = mobilized.Single();
            Check(eventRow.ArmyWasCreated != existingReceiver, "rally check exercises the real new/existing receiver branch");
            if (existingReceiver)
                Check(session.TravelTargetFor(existingArmy.Id) == previousTarget
                      && commands.All(line => !line.StartsWith($"> move {existingArmy.Id} ", StringComparison.Ordinal)),
                    "a mobilized unit joining an existing army preserves its manual plan and waits for moves");
            else
            {
                Check(session.TravelTargetFor(eventRow.ArmyId) is { FromRally: true, X: 12, Y: 8 },
                    "new armies retain the rally-origin target through the confirmed-turn pass");
                var moveIndex = commands.FindIndex(line => line.StartsWith($"> move {eventRow.ArmyId} ", StringComparison.Ordinal));
                Check(newArmyMoves == 0 ? moveIndex == -1 :
                    moveIndex > commands.FindIndex(line => line.StartsWith("> mobilize ", StringComparison.Ordinal))
                      && moveIndex < commands.FindIndex(line => line.StartsWith("> end", StringComparison.Ordinal))
                      && commands.Count(line => line.StartsWith($"> move {eventRow.ArmyId} ", StringComparison.Ordinal)) == 1,
                    $"rally dispatch respects the actual starting move budget ({newArmyMoves}); commands: {string.Join(" | ", commands)}");
            }
            Check(session.RallyPointFor("north") == rally && session.RallyPointFor("south") == southRally
                  && session.State.NationById("south")!.RecruitmentSlots.SequenceEqual(southSlots),
                "north rally processing preserves both settings and the other hotseat nation's ready units");
        }
        finally { RemoveMainGame(main); SettingsScreen.Commit(new UserSettings()); }
    }

    private void CheckTravelSafetyStops()
    {
        foreach (var hostile in new[] { false, true })
        {
            SettingsScreen.Commit(new UserSettings());
            var original = BuildRallySession(false);
            var army = original.State.Armies[0];
            var enemy = army with { Id = "hostile-contact", Nation = "south", X = army.X + 1 };
            var state = original.State with
            {
                Armies = hostile ? ValueList.Of(army, enemy) : ValueList.Of(army),
                Relations = original.State.Relations.WithRelation("north", "south", original.Ruleset.Diplomacy.StateCodes.War),
                Nations = ValueList.From(original.State.Nations.Select(n => n with { RecruitmentSlots = ValueList<RecruitmentSlot>.Empty })),
            };
            var session = new GameSession(original.World, original.Ruleset, original.Scenario,
                new SaveGame(state.SchemaVersion, "travel-stop", "travel-stop", state.ScenarioId, state.WorldId, state.RulesetId, state));
            var target = new ArmyTravelTarget(army.Id, "north", 14, 12);
            session.SetArmyTravelTarget(target);
            var main = AddMainGame(session);
            try
            {
                var commands = new List<string>();
                main.CommandIssued += lines => commands.Add(lines.FirstOrDefault() ?? string.Empty);
                main.MenuBar.PressItemForCheck("game.end_turn");
                if (main.ActiveOverlay is EndTurnBox box) box.EndTurnButton.EmitSignal(Button.SignalName.Pressed);
                Check(commands.All(line => !line.StartsWith("> move ", StringComparison.Ordinal)),
                    "hostile contact and zero moves never issue an automatic travel command");
                Check(hostile ? session.TravelTargetFor(army.Id) is null : session.TravelTargetFor(army.Id) == target,
                    "hostile contact cancels the plan; zero moves retains it for the next turn");
                Check(main.LastMessageForCheck.Contains(hostile ? "hostile contact" : "1 waiting", StringComparison.Ordinal),
                    "travel safety stop or wait is visible in the End-turn summary");
            }
            finally { RemoveMainGame(main); }
        }
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

    private static GameSession BuildAutoMobilizeSession(bool refuse = false, bool lowerQualitySlots = false)
    {
        var original = BuildSession(warning: true);
        var state = original.State;
        var city = state.Cities.First(c => c.Owner == "north");
        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(c => c.Id == city.Id
                ? c with { SupplyTons = 600 } : c)),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "north"
                ? n with { RecruitmentSlots = ValueList.From(new[] {
                    new RecruitmentSlot(city.Id, "light_infantry", 2_000, 0),
                    new RecruitmentSlot(city.Id, "light_infantry", 3_000, 24) }
                    .Concat(lowerQualitySlots ? new[] {
                    new RecruitmentSlot(city.Id, "light_infantry", 2_000, 16),
                    new RecruitmentSlot(city.Id, "light_infantry", 2_000, 20) }
                    : Array.Empty<RecruitmentSlot>())) } : n)),
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
