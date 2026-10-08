using Godot;
using IC2.Engine.Armies;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T112 Done-when 3, 4 and 5: the real <see cref="MainGameScreen"/> on a scripted classical state with
/// two adjacent Roman fleets at a Roman city, plus a split fleet, a join pair, a scuttle fleet and a
/// carrying pair. Every Fleet entry is opened from the menu and commits through the engine: Supply
/// fleet buys from the city,
/// Repair fleet repairs and zeroes the moves, Transfer ships moves ships/supply/money to the partner,
/// Split fleet adds a fleet holding exactly the staged ships/supply/money, Join fleets leaves one fleet,
/// Scuttle fleet with <strong>No</strong> submits nothing, and Fortify city changes the city's
/// fortification word. The check also proves the strip's Repair button and the menu entry run the same
/// table handler, and that no <see cref="ContextPanel"/> button issues a command. T112 R1 adds the
/// carrying pair: Transfer ships is refused while either fleet carries an army and OK commits nothing.
/// Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/FleetCityOrdersCheck.tscn --quit-after 900
/// </code>
/// </summary>
public partial class FleetCityOrdersCheck : Control
{
    private const int SettleFrames = 6;

    private const string Rome = "rome";
    private const string SupplyFleetId = "t112-supply";
    private const string PartnerFleetId = "t112-partner";
    private const string SplitFleetId = "t112-split";
    private const string SplitChildId = "t112-split-split";
    private const string JoinAId = "t112-join-a";
    private const string JoinBId = "t112-join-b";
    private const string CarryingFleetId = "t112-carrying";
    private const string CarryingPartnerId = "t112-carrying-partner";
    private const string CarryingArmyId = "t112-carrying-army";
    private const string ScuttleFleetId = "t112-scuttle";
    private const string CityId = "t112-city";
    private const string ScuttleCityId = "t112-scuttle-city";

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;
    private readonly List<Action> _steps = new();
    private int _stepIndex;
    private int _frame;
    private bool _ok = true;

    private int _commandsSeen;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        _session = BuildSession();
        _mainGame = new MainGameScreen { Session = _session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        _mainGame.CommandIssued += _ => _commandsSeen++;

        _steps.Add(CheckFixture);
        _steps.Add(SupplyFleet);
        _steps.Add(RepairFleet);
        _steps.Add(TransferShipsAllShipsRefused);
        _steps.Add(TransferShips);
        _steps.Add(EmbarkCarryingArmy);
        _steps.Add(TransferShipsWhileCarrying);
        _steps.Add(SplitFleet);
        _steps.Add(JoinFleets);
        _steps.Add(ScuttleNo);
        _steps.Add(ScuttleYes);
        _steps.Add(FortifyCity);
        _steps.Add(StripSharesTheRepairHandler);
        _steps.Add(NoContextPanelCommandButton);
        _steps.Add(Finish);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame < SettleFrames || _stepIndex >= _steps.Count)
        {
            return;
        }

        _frame = 0;
        try
        {
            _steps[_stepIndex++]();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"FleetCityOrdersCheck: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    private void CheckFixture()
    {
        Check(_session.State.NationById(Rome)!.Control == SeatControl.Human, "Rome is the human seat");
        Check(_session.State.FleetById(SupplyFleetId) is not null, "the supply fleet exists");
        Check(
            AdjacentPartner.Fleet(_session.State, SupplyFleetId)?.Id == PartnerFleetId,
            "the game's own partner for the supply fleet is the scripted partner");
        Check(
            _session.State.FleetById(SplitFleetId)!.Ships >= _session.Ruleset.Naval.SplitMinShips,
            "the split fleet has at least the minimum ships");
        Check(
            AdjacentPartner.Fleet(_session.State, JoinAId)?.Id == JoinBId,
            "the join pair is each other's own partner");
    }

    /// <summary>Done-when 3: Supply fleet buys from the adjacent own city and the fleet's stock moves.</summary>
    private void SupplyFleet()
    {
        _mainGame.SelectFleetForCheck(SupplyFleetId);
        var before = _commandsSeen;
        var supplyBefore = _session.State.FleetById(SupplyFleetId)!.SupplyTons;
        var cityBefore = _session.State.CityById(CityId)!.SupplyTons;

        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.fleet_supply"), "Supply fleet is wired from the menu");
        Check(_mainGame.ActiveOverlay is SupplyDialog, "Supply fleet opens its dialog");
        Check(_commandsSeen == before, "opening Supply fleet issues no command");

        var dialog = (SupplyDialog)_mainGame.ActiveOverlay!;
        dialog.SelectProviderForCheck(CityId);
        dialog.PressSupplyForCheck(100);

        Check(_commandsSeen == before + 1, $"Supply fleet submits one command ({_commandsSeen - before})");
        var after = _session.State.FleetById(SupplyFleetId)!.SupplyTons;
        var cityAfter = _session.State.CityById(CityId)!.SupplyTons;
        Check(after > supplyBefore, $"the fleet's supply rose ({supplyBefore} -> {after})");
        Check(cityAfter < cityBefore, $"the city's stock fell ({cityBefore} -> {cityAfter})");
        dialog.Close();
    }

    /// <summary>Done-when 3: Repair fleet charges <c>ships × points / 5</c> and zeroes the moves.</summary>
    private void RepairFleet()
    {
        _mainGame.SelectFleetForCheck(SupplyFleetId);
        var before = _commandsSeen;
        var fleetBefore = _session.State.FleetById(SupplyFleetId)!;
        var treasuryBefore = _session.State.NationById(Rome)!.Treasury;

        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.fleet_repair"), "Repair fleet is wired from the menu");
        Check(_mainGame.ActiveOverlay is RepairDialog, "Repair fleet opens its dialog");
        var dialog = (RepairDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ModelForCheck.CanRepair, "the repair dialog offers the repair");
        dialog.SetPointsForCheck(3);
        var expectedCost = fleetBefore.Ships * 3 / _session.Ruleset.Naval.RepairCostDivisor;
        dialog.OkForCheck();

        Check(_commandsSeen == before + 1, $"Repair fleet submits one command ({_commandsSeen - before})");
        var after = _session.State.FleetById(SupplyFleetId)!;
        Check(after.ConditionPercent == fleetBefore.ConditionPercent + 3, "the condition rose by the points");
        Check(after.Moves == 0, "the repair zeroed the fleet's moves");
        Check(
            _session.State.NationById(Rome)!.Treasury == treasuryBefore - expectedCost,
            $"the treasury paid {expectedCost} talents");
    }

    /// <summary>
    /// T112 R2: the destructive mixed staging — every one of A's ships toward B and B's money toward A — is
    /// refused by the model and commits nothing. OK issues no command and the post-state matches the
    /// pre-state exactly: before R2 the A → B line disbanded A and the following B → A line was lost.
    /// </summary>
    private void TransferShipsAllShipsRefused()
    {
        _mainGame.SelectFleetForCheck(SupplyFleetId);
        var before = _commandsSeen;
        var selectedBefore = _session.State.FleetById(SupplyFleetId)!;
        var partnerBefore = _session.State.FleetById(PartnerFleetId)!;

        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.fleet_transfer_ships"),
            "Transfer ships is wired from the menu for the all-ships staging");
        Check(_mainGame.ActiveOverlay is FleetTransferDialog, "Transfer ships opens its all-ships dialog");
        var dialog = (FleetTransferDialog)_mainGame.ActiveOverlay!;

        // A -> B every ship (30/30) and B -> A 50 talents: the two arrows the R2 finding names.
        dialog.AdjustShipsForCheck(selectedBefore.Ships);
        dialog.AdjustMoneyForCheck(-50);
        Check(dialog.ModelForCheck.MovesEverySelectedShip, "the model sees the A -> B line would move every ship");
        Check(
            dialog.ModelForCheck.RefusalMessage == FleetCityDialogModels.TransferAllShipsMessage,
            $"the model shows the all-ships refusal ({dialog.ModelForCheck.RefusalMessage})");

        dialog.OkForCheck();

        Check(_commandsSeen == before, $"the refused all-ships OK composes no command ({_commandsSeen - before})");
        Check(_mainGame.ActiveOverlay is FleetTransferDialog, "the refused all-ships dialog stays open");
        var selectedAfter = _session.State.FleetById(SupplyFleetId)!;
        var partnerAfter = _session.State.FleetById(PartnerFleetId)!;
        Check(
            selectedAfter == selectedBefore && partnerAfter == partnerBefore,
            "the all-ships post-state matches the pre-state exactly (no ships, supply or money moved)");

        dialog.CancelForCheck();
        Check(_mainGame.ActiveOverlay is null, "Cancel closes the refused all-ships dialog");
    }

    /// <summary>Done-when 3: Transfer ships moves ships, supply and money to the game's own partner.</summary>
    private void TransferShips()
    {
        _mainGame.SelectFleetForCheck(SupplyFleetId);
        var before = _commandsSeen;
        var selectedBefore = _session.State.FleetById(SupplyFleetId)!;
        var partnerBefore = _session.State.FleetById(PartnerFleetId)!;

        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.fleet_transfer_ships"),
            "Transfer ships is wired from the menu");
        Check(_mainGame.ActiveOverlay is FleetTransferDialog, "Transfer ships opens its dialog");
        var dialog = (FleetTransferDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ModelForCheck.PartnerFleetId == PartnerFleetId, "the dialog's partner is the game's pick");
        dialog.AdjustShipsForCheck(5);
        dialog.AdjustSupplyForCheck(20);
        dialog.AdjustMoneyForCheck(10);
        dialog.OkForCheck();

        Check(_commandsSeen == before + 1, $"Transfer ships submits one command ({_commandsSeen - before})");
        var selectedAfter = _session.State.FleetById(SupplyFleetId)!;
        var partnerAfter = _session.State.FleetById(PartnerFleetId)!;
        Check(selectedAfter.Ships == selectedBefore.Ships - 5, "the selected fleet lost five ships");
        Check(partnerAfter.Ships == partnerBefore.Ships + 5, "the partner gained five ships");
        Check(selectedAfter.SupplyTons == selectedBefore.SupplyTons - 20, "the selected fleet lost 20 tons");
        Check(partnerAfter.SupplyTons == partnerBefore.SupplyTons + 20, "the partner gained 20 tons");
    }

    /// <summary>
    /// T112 R1: boards the scripted army on the carrier so the next step sees a carrying fleet. The army
    /// stands on the land tile beside the carrier's water tile; <c>embark-army</c> is the engine's own
    /// command, exactly as a player's embark click would run it.
    /// </summary>
    private void EmbarkCarryingArmy()
    {
        Check(!_session.State.FleetById(CarryingFleetId)!.IsCarryingArmy, "the carrier starts empty");
        _mainGame.SelectFleetForCheck(CarryingFleetId);
        var output = _mainGame.SubmitForCheck($"embark-army {CarryingArmyId} {CarryingFleetId}");
        Check(
            _session.State.FleetById(CarryingFleetId)!.IsCarryingArmy,
            $"embark-army boards the scripted army on the carrier ({output.Count} output lines)");
    }

    /// <summary>
    /// T112 R1: Transfer ships is refused while the selected fleet carries an army — the model composes
    /// nothing and OK commits no command, leaving both fleets exactly as they were.
    /// </summary>
    private void TransferShipsWhileCarrying()
    {
        _mainGame.SelectFleetForCheck(CarryingFleetId);
        var before = _commandsSeen;
        var selectedBefore = _session.State.FleetById(CarryingFleetId)!;
        var partnerBefore = _session.State.FleetById(CarryingPartnerId)!;

        Check(
            AdjacentPartner.Fleet(_session.State, CarryingFleetId)?.Id == CarryingPartnerId,
            "the carrying pair's own partner is the scripted partner");

        Check(
            _mainGame.MenuBar.PressItemForCheck("unit_map.fleet_transfer_ships"),
            "Transfer ships is wired from the menu for a carrying fleet");
        Check(_mainGame.ActiveOverlay is FleetTransferDialog, "Transfer ships opens its dialog while carrying");
        var dialog = (FleetTransferDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ModelForCheck.PartnerFleetId == CarryingPartnerId, "the carrying pair's partner is the game's pick");
        Check(
            dialog.ModelForCheck.RefusalMessage == FleetCityDialogModels.TransferCarryingArmyMessage,
            $"the model shows the engine's carrying-army refusal ({dialog.ModelForCheck.RefusalMessage})");

        dialog.AdjustShipsForCheck(5);
        dialog.OkForCheck();

        Check(_commandsSeen == before, $"OK composes no command ({_commandsSeen - before})");
        var selectedAfter = _session.State.FleetById(CarryingFleetId)!;
        var partnerAfter = _session.State.FleetById(CarryingPartnerId)!;
        Check(
            selectedAfter.Ships == selectedBefore.Ships
            && partnerAfter.Ships == partnerBefore.Ships
            && selectedAfter.CarriedArmyId == selectedBefore.CarriedArmyId,
            "the carrying pair's ships and carried army are unchanged");

        dialog.CancelForCheck();
        Check(_mainGame.ActiveOverlay is null, "Cancel closes the refused transfer dialog");
    }

    /// <summary>
    /// Done-when 3: Split fleet moving ships with non-zero supply and money adds a fleet holding those
    /// ships, that supply and that money, and leaves the selected fleet with the rest.
    /// </summary>
    private void SplitFleet()
    {
        _mainGame.SelectFleetForCheck(SplitFleetId);
        var before = _commandsSeen;
        var parentBefore = _session.State.FleetById(SplitFleetId)!;
        var expectedCell = SplitPlacement.FleetCell(
            _session.State, _session.World, new GridPoint(parentBefore.X, parentBefore.Y));
        Check(expectedCell is not null, "the split fleet has a free water cell for the new fleet");

        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.fleet_split"), "Split fleet is wired from the menu");
        Check(_mainGame.ActiveOverlay is SplitFleetDialog, "Split fleet opens its dialog");
        var dialog = (SplitFleetDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ModelForCheck.NewFleetId == SplitChildId, "the dialog names the screen's new fleet id");
        dialog.AdjustShipsForCheck(10);
        dialog.AdjustSupplyForCheck(40);
        dialog.AdjustMoneyForCheck(30);
        dialog.OkForCheck();

        Check(_commandsSeen == before + 1, $"Split fleet submits one command ({_commandsSeen - before})");
        var parentAfter = _session.State.FleetById(SplitFleetId)!;
        var child = _session.State.FleetById(SplitChildId);
        Check(child is not null, "Split fleet adds a fleet");
        if (child is null)
        {
            return;
        }

        Check(child.Ships == 10, "the new fleet holds the ten staged ships");
        Check(child.SupplyTons == 40, "the new fleet holds the 40 staged tons");
        Check(child.Money == 30, "the new fleet holds the 30 staged talents");
        Check(
            parentAfter.Ships == parentBefore.Ships - 10
            && parentAfter.SupplyTons == parentBefore.SupplyTons - 40
            && parentAfter.Money == parentBefore.Money - 30,
            "the selected fleet keeps the rest");
        if (expectedCell is not null)
        {
            Check(
                LandingTile.ChebyshevDistance(
                    new GridPoint(child.X, child.Y), new GridPoint(parentBefore.X, parentBefore.Y)) == 1,
                "the new fleet stands one tile from its parent");
        }
    }

    /// <summary>Done-when 3: Join fleets submits one command and leaves one fleet.</summary>
    private void JoinFleets()
    {
        _mainGame.SelectFleetForCheck(JoinAId);
        var before = _commandsSeen;
        var fleetsBefore = _session.State.Fleets.Count;
        var shipsBefore = _session.State.FleetById(JoinAId)!.Ships + _session.State.FleetById(JoinBId)!.Ships;

        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.fleet_join"), "Join fleets is wired from the menu");
        Check(_commandsSeen == before + 1, $"Join fleets submits one command ({_commandsSeen - before})");
        Check(_mainGame.ActiveOverlay is null, "Join fleets opens no dialog");
        Check(_session.State.FleetById(JoinBId) is null, "the absorbed fleet is gone");
        Check(_session.State.Fleets.Count == fleetsBefore - 1, "Join fleets leaves one fleet");
        Check(_session.State.FleetById(JoinAId)!.Ships == shipsBefore, "the survivor holds the combined ships");
    }

    /// <summary>Done-when 3: Scuttle fleet with No submits nothing and leaves the fleet.</summary>
    private void ScuttleNo()
    {
        _mainGame.SelectFleetForCheck(ScuttleFleetId);
        var before = _commandsSeen;
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.fleet_scuttle"), "Scuttle fleet is wired from the menu");
        Check(_mainGame.ActiveOverlay is ConfirmPrompt, "Scuttle fleet asks first");

        PressPromptAnswer("No");

        Check(_commandsSeen == before, $"No submits nothing ({_commandsSeen - before})");
        Check(_session.State.FleetById(ScuttleFleetId) is not null, "the fleet survives No");
        Check(_mainGame.ActiveOverlay is null, "No closes the prompt");
    }

    /// <summary>Done-when 3: Scuttle fleet with Yes removes the fleet and conserves its money and
    /// supplies (to the treasury and the adjacent own city).</summary>
    private void ScuttleYes()
    {
        _mainGame.SelectFleetForCheck(ScuttleFleetId);
        var before = _commandsSeen;
        var fleet = _session.State.FleetById(ScuttleFleetId)!;
        var treasuryBefore = _session.State.NationById(Rome)!.Treasury;
        var citySupplyBefore = _session.State.CityById(ScuttleCityId)!.SupplyTons;

        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.fleet_scuttle"), "Scuttle fleet asks again");
        Check(_mainGame.ActiveOverlay is ConfirmPrompt, "Scuttle fleet asks first on the Yes path");

        PressPromptAnswer("Yes");

        Check(_commandsSeen == before + 1, $"Yes submits one command ({_commandsSeen - before})");
        Check(_session.State.FleetById(ScuttleFleetId) is null, "the fleet is gone after Yes");
        Check(
            _session.State.NationById(Rome)!.Treasury == treasuryBefore + fleet.Money,
            "the fleet's money went to the treasury");
        Check(
            _session.State.CityById(ScuttleCityId)!.SupplyTons == citySupplyBefore + fleet.SupplyTons,
            "the fleet's supplies went to the city");
    }

    /// <summary>Done-when 3: Fortify city commits through the engine and the city's word moves.</summary>
    private void FortifyCity()
    {
        _mainGame.SelectCityForCheck(CityId);
        var before = _commandsSeen;
        var codeBefore = _session.State.CityById(CityId)!.FortificationCode;

        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.city_fortify"), "Fortify city is wired from the menu");
        Check(_mainGame.ActiveOverlay is FortifyDialog, "Fortify city opens its dialog");
        var dialog = (FortifyDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ModelForCheck.CanFortify, "the fortify dialog offers the order");
        dialog.SetPointsForCheck(10);
        dialog.OkForCheck();

        Check(_commandsSeen == before + 1, $"Fortify city submits one command ({_commandsSeen - before})");
        Check(
            _session.State.CityById(CityId)!.FortificationCode != codeBefore,
            "the city's fortification word changed");
    }

    /// <summary>
    /// Done-when 4: the strip's Repair button and the menu's Repair entry each run the same table
    /// handler — <see cref="GameCommandTable.IssuedCount"/> moves once on each, and each opens the
    /// repair dialog.
    /// </summary>
    private void StripSharesTheRepairHandler()
    {
        _mainGame.SelectFleetForCheck(SupplyFleetId);
        Check(_mainGame.CommandStrip.Buttons.Count == 7, "an own fleet shows six fleet buttons and Cancel");
        Check(
            _mainGame.CommandStrip.ButtonFor("unit_map.fleet_repair") is not null,
            "the strip shows the Repair fleet button");

        var before = _mainGame.CommandTable.IssuedCount;
        Check(
            _mainGame.CommandStrip.PressForCheck("unit_map.fleet_repair"),
            "the strip's Repair fleet button is pressed");
        Check(
            _mainGame.CommandTable.IssuedCount == before + 1,
            "the strip's button runs the table handler once");
        Check(_mainGame.ActiveOverlay is RepairDialog, "the strip's button opens the repair dialog");
        ((RepairDialog)_mainGame.ActiveOverlay!).CancelForCheck();

        before = _mainGame.CommandTable.IssuedCount;
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.fleet_repair"), "the menu's Repair entry is wired");
        Check(
            _mainGame.CommandTable.IssuedCount == before + 1,
            "the menu's entry runs the same table handler once");
        Check(_mainGame.ActiveOverlay is RepairDialog, "the menu's entry opens the repair dialog");
        ((RepairDialog)_mainGame.ActiveOverlay!).CancelForCheck();
    }

    /// <summary>Done-when 5: no Button under the context panel issues a command, for a fleet, a city or
    /// an army selection.</summary>
    private void NoContextPanelCommandButton()
    {
        _mainGame.SelectFleetForCheck(SupplyFleetId);
        Check(
            ButtonsUnder(_mainGame.ContextPanel).Count() == 0,
            "the fleet panel renders no Button");

        _mainGame.SelectCityForCheck(CityId);
        Check(
            ButtonsUnder(_mainGame.ContextPanel).Count() == 0,
            "the city panel renders no Button");

        var armyId = _session.State.Armies.First(
            army => string.Equals(army.Nation, Rome, StringComparison.Ordinal)).Id;
        _mainGame.SelectArmyForCheck(armyId);
        Check(
            ButtonsUnder(_mainGame.ContextPanel).Count() == 0,
            "the army panel renders no Button");
    }

    private void Finish()
    {
        GD.Print($"FleetCityOrdersCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    private void PressPromptAnswer(string answer)
    {
        var button = ButtonsUnder(_mainGame.ActiveOverlay!)
            .FirstOrDefault(candidate => string.Equals(candidate.Text, answer, StringComparison.Ordinal));
        Check(button is not null, $"the confirmation prompt has a '{answer}' button");
        button?.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private static IEnumerable<Button> ButtonsUnder(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button)
            {
                yield return button;
            }

            foreach (var nested in ButtonsUnder(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// The shipped classical world with Rome as the human seat and the check's own fleets, a Roman city
    /// and a second scuttle city added on scripted coastal tiles. The tiles are found by scanning the
    /// shipped terrain, so the fixture never depends on a hand-copied coordinate.
    /// </summary>
    private GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var world = resolved.World;
        var terrain = world.Terrain.Decode(world.Width, world.Height);

        var taken = new HashSet<(int X, int Y)>();
        foreach (var army in world.StartingArmies)
        {
            taken.Add((army.X, army.Y));
        }

        foreach (var city in world.Cities)
        {
            taken.Add((city.X, city.Y));
        }

        foreach (var fleet in world.StartingFleets)
        {
            taken.Add((fleet.X, fleet.Y));
        }

        bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < world.Width && y < world.Height;
        bool IsWater(int x, int y) =>
            InBounds(x, y) && world.TileTypeByCode(terrain[(y * world.Width) + x])?.PassableByFleets == true;
        bool IsLand(int x, int y) => LandingTile.IsPassableForArmy(new GridPoint(x, y), world);
        bool Free(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return false;
            }

            foreach (var (tx, ty) in taken)
            {
                if (Math.Max(Math.Abs(tx - x), Math.Abs(ty - y)) <= 4)
                {
                    return false;
                }
            }

            return true;
        }

        // A coastal water tile W with a land neighbour L and a water neighbour W2.
        (int Wx, int Wy, int Lx, int Ly, int W2x, int W2y)? FindCoastal()
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var x = 0; x < world.Width; x++)
                {
                    if (!Free(x, y) || !IsWater(x, y))
                    {
                        continue;
                    }

                    (int X, int Y)? land = null;
                    (int X, int Y)? water = null;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }

                            var nx = x + dx;
                            var ny = y + dy;
                            if (land is null && IsLand(nx, ny) && Free(nx, ny))
                            {
                                land = (nx, ny);
                            }
                            else if (water is null && IsWater(nx, ny) && Free(nx, ny))
                            {
                                water = (nx, ny);
                            }
                        }
                    }

                    if (land is { } l && water is { } w)
                    {
                        return (x, y, l.X, l.Y, w.X, w.Y);
                    }
                }
            }

            return null;
        }

        // A water tile with a free water neighbour (for the split).
        (int X, int Y)? FindSplit()
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var x = 0; x < world.Width; x++)
                {
                    if (!Free(x, y) || !IsWater(x, y))
                    {
                        continue;
                    }

                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if ((dx != 0 || dy != 0) && IsWater(x + dx, y + dy) && Free(x + dx, y + dy))
                            {
                                return (x, y);
                            }
                        }
                    }
                }
            }

            return null;
        }

        // A pair of adjacent water tiles (for the join).
        ((int X, int Y) A, (int X, int Y) B)? FindWaterPair()
        {
            for (var y = 0; y < world.Height; y++)
            {
                for (var x = 0; x < world.Width; x++)
                {
                    if (!Free(x, y) || !IsWater(x, y))
                    {
                        continue;
                    }

                    foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
                    {
                        if (IsWater(x + dx, y + dy) && Free(x + dx, y + dy))
                        {
                            return ((x, y), (x + dx, y + dy));
                        }
                    }
                }
            }

            return null;
        }

        var coastal = FindCoastal() ?? throw new InvalidOperationException("No coastal water tile on the classical map.");
        taken.Add((coastal.Wx, coastal.Wy));
        taken.Add((coastal.Lx, coastal.Ly));
        taken.Add((coastal.W2x, coastal.W2y));

        var scuttleCoastal = FindCoastal()
            ?? throw new InvalidOperationException("No second coastal water tile on the classical map.");
        taken.Add((scuttleCoastal.Wx, scuttleCoastal.Wy));
        taken.Add((scuttleCoastal.Lx, scuttleCoastal.Ly));

        // T112 R1: the carrying pair — carrier at W, empty partner at the neighbour W2, and the army that
        // boards it on the land tile L.
        var carryingCoastal = FindCoastal()
            ?? throw new InvalidOperationException("No third coastal water tile on the classical map.");
        taken.Add((carryingCoastal.Wx, carryingCoastal.Wy));
        taken.Add((carryingCoastal.Lx, carryingCoastal.Ly));
        taken.Add((carryingCoastal.W2x, carryingCoastal.W2y));

        var split = FindSplit() ?? throw new InvalidOperationException("No water tile fits a split fleet.");
        taken.Add(split);

        var join = FindWaterPair() ?? throw new InvalidOperationException("No water pair fits a join.");
        taken.Add(join.A);
        taken.Add(join.B);

        var cities = world.Cities.ToList();
        cities.Add(City(CityId, "T112 Port", coastal.Lx, coastal.Ly));
        cities.Add(City(ScuttleCityId, "T112 Scuttle Port", scuttleCoastal.Lx, scuttleCoastal.Ly));

        var armies = world.StartingArmies.ToList();
        armies.Add(CarryingArmy(CarryingArmyId, carryingCoastal.Lx, carryingCoastal.Ly));

        var fleets = world.StartingFleets.ToList();
        fleets.Add(Fleet(SupplyFleetId, coastal.Wx, coastal.Wy, ships: 30, condition: 70, supply: 100, money: 100));
        fleets.Add(Fleet(PartnerFleetId, coastal.W2x, coastal.W2y, ships: 20, condition: 100, supply: 200, money: 50));
        fleets.Add(Fleet(
            CarryingFleetId, carryingCoastal.Wx, carryingCoastal.Wy,
            ships: 30, condition: 100, supply: 100, money: 0));
        fleets.Add(Fleet(
            CarryingPartnerId, carryingCoastal.W2x, carryingCoastal.W2y,
            ships: 20, condition: 100, supply: 100, money: 0));
        fleets.Add(Fleet(SplitFleetId, split.X, split.Y, ships: 30, condition: 100, supply: 200, money: 100));
        fleets.Add(Fleet(JoinAId, join.A.X, join.A.Y, ships: 10, condition: 100, supply: 50, money: 0));
        fleets.Add(Fleet(JoinBId, join.B.X, join.B.Y, ships: 10, condition: 100, supply: 50, money: 0));
        fleets.Add(Fleet(
            ScuttleFleetId, scuttleCoastal.Wx, scuttleCoastal.Wy,
            ships: 10, condition: 100, supply: 50, money: 20));

        var customWorld = world with
        {
            Cities = ValueList.From(cities),
            StartingArmies = ValueList.From(armies),
            StartingFleets = ValueList.From(fleets),
        };

        return new GameSession(customWorld, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: Rome);
    }

    private static CityDefinition City(string id, string name, int x, int y) => new(
        id, name, x, y, Owner: Rome, Allegiance: Rome, Loyalty: 90, SupplyTons: 500,
        FortificationCode: 0, PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0,
        Garrison: ValueList<UnitSlot>.Empty);

    private static StartingFleet Fleet(
        string id, int x, int y, int ships, int condition, int supply, int money) => new(
        id, Rome, x, y, Ships: ships, ConditionPercent: condition, Money: money, SupplyTons: supply, Moves: 8);

    /// <summary>The scripted army that boards the carrier — 1,000 troops, under the carrier's 15,000 capacity.</summary>
    private static StartingArmy CarryingArmy(string id, int x, int y) => new(
        id, Rome, x, y, Morale: 70, Money: 0, SupplyTons: 0, Moves: 8,
        Units: ValueList.Of(new[]
        {
            new UnitSlot(0, "heavy_infantry", 1_000, 7, "T112 boarded battalion"),
        }));

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }
}
