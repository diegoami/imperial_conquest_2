using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T134: the Army menu's <strong>Supply army</strong> dialog drives the real engine over a scripted
/// classical state — a Roman army beside a Roman city, a Roman fleet and a foreign city at peace, with a
/// second Roman army past its room, a Roman army with no provider and a Carthaginian army. Opened from
/// the menu (<see cref="GameMenuBar.PressItemForCheck"/>), every press is read back from
/// <see cref="GameSession.State"/> and counted once at <see cref="MainGameScreen.CommandIssued"/>. Run
/// headless via:
/// <code>
/// godot --headless --path godot res://Checks/SupplyArmyCheck.tscn --quit-after 600
/// </code>
/// </summary>
/// <remarks>
/// The scripted world is delivered through the ordinary constructor (the world's own starting armies,
/// fleets and cities), so no save seam and no map click is needed — <see
/// cref="MainGameScreen.SelectArmyForCheck"/> sets the selection a map click would. A windowed run is the
/// human visual review's vehicle for the T134 hazard's screenshots.
/// </remarks>
public partial class SupplyArmyCheck : Control
{
    private const int SettleFrames = 6;

    private const string ArmyId = "rome-supply-army";
    private const string OverfullArmyId = "rome-overfull-army";
    private const string LonelyArmyId = "rome-lonely-army";
    private const string ForeignArmyId = "army-2"; // Carthage's, repositioned.
    private const string OwnCityId = "t134-own-city";
    private const string ForeignCityId = "t134-foreign-city";
    private const string OwnFleetId = "rome-supply-fleet";
    private const string BuyersNationId = "rome";
    private const string SellersNationId = "carthage";

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;
    private SupplyDialog _dialog = null!;
    private readonly List<Action> _steps = new();
    private int _stepIndex;
    private int _frame;
    private bool _ok = true;

    private int _commandsSeen;
    private string? _lastCommandLine;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        _session = BuildSession();
        _mainGame = new MainGameScreen { Session = _session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        _mainGame.CommandIssued += lines =>
        {
            _commandsSeen++;

            // The session echoes the submitted line first ("> buy ..."); the reply is the line after it.
            _lastCommandLine = lines.Skip(1).FirstOrDefault(text => text.Length > 0);
        };

        _steps.Add(OpenAtOwnProvider);
        _steps.Add(PressOwnCity);
        _steps.Add(PressFleet);
        _steps.Add(StageAndTransferForeignCity);
        _steps.Add(PressMoney);
        _steps.Add(CloseFirstDialog);
        _steps.Add(OpenAtOverfullArmy);
        _steps.Add(PressPastRoom);
        _steps.Add(CloseSecondDialog);
        _steps.Add(SelectForeignArmy);
        _steps.Add(SelectLonelyArmy);
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
            GD.PrintErr($"SupplyArmyCheck: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// The shipped classical world with one Roman army beside a Roman city, a Roman fleet and a peaceful
    /// Carthaginian city; a second Roman army over its room; a third with no provider; and Carthage's
    /// army moved away from everything.
    /// </summary>
    private static GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");

        var cities = resolved.World.Cities.ToList();
        cities.Add(new CityDefinition(
            OwnCityId, "Antium supply check", X: 101, Y: 100, Owner: BuyersNationId, Allegiance: BuyersNationId,
            Loyalty: 95, SupplyTons: 990, FortificationCode: 60, PopulationThousands: 100,
            MaxPopulationThousands: 100, Tribute: 0, Garrison: ValueList<UnitSlot>.Empty));
        cities.Add(new CityDefinition(
            ForeignCityId, "Capua supply check", X: 100, Y: 101, Owner: SellersNationId, Allegiance: SellersNationId,
            Loyalty: 80, SupplyTons: 330, FortificationCode: 50, PopulationThousands: 100,
            MaxPopulationThousands: 100, Tribute: 0, Garrison: ValueList<UnitSlot>.Empty));

        var armies = resolved.World.StartingArmies
            .Select(a => string.Equals(a.Id, ForeignArmyId, StringComparison.Ordinal) ? a with { X = 120, Y = 120 } : a)
            .ToList();
        armies.Add(RomanArmy(ArmyId, X: 100, Y: 100, supplyTons: 200, money: 100));
        armies.Add(RomanArmy(OverfullArmyId, X: 100, Y: 99, supplyTons: 500, money: 0));
        armies.Add(RomanArmy(LonelyArmyId, X: 110, Y: 110, supplyTons: 100, money: 0));

        var fleets = resolved.World.StartingFleets.ToList();
        fleets.Add(new StartingFleet(
            OwnFleetId, BuyersNationId, X: 101, Y: 101, Ships: 10, ConditionPercent: 100, Money: 0,
            SupplyTons: 500, Moves: 4));

        var world = resolved.World with
        {
            Cities = ValueList.From(cities),
            StartingArmies = ValueList.From(armies),
            StartingFleets = ValueList.From(fleets),
        };

        return new GameSession(world, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: BuyersNationId);
    }

    private static StartingArmy RomanArmy(string id, int X, int Y, int supplyTons, int money) => new(
        id, BuyersNationId, X, Y, Morale: 70, Money: money, SupplyTons: supplyTons, Moves: 8,
        Units: ValueList.Of(new[]
        {
            new UnitSlot(0, "heavy_infantry", 20_000, 7, "T134 1st Guards Battalion"),
            new UnitSlot(0, "light_cavalry", 20_000, 7, "T134 1st Lancers Battalion"),
        }));

    private void OpenDialog(string armyId)
    {
        _mainGame.SelectArmyForCheck(armyId);
        _mainGame.MenuBar.PressItemForCheck("unit_map.army_supply");
        Check(_mainGame.ActiveOverlay is SupplyDialog, $"Supply army opens from the menu for {armyId}");
        _dialog = (SupplyDialog)_mainGame.ActiveOverlay!;
    }

    private void OpenAtOwnProvider()
    {
        Check(_session.State.NationById(BuyersNationId)!.Control == SeatControl.Human, "Rome is the human seat");
        Check(_session.State.Relations.Get(BuyersNationId, SellersNationId)
            != _session.Ruleset.Diplomacy.StateCodes.War, "Carthage is at peace with Rome");

        _mainGame.SelectArmyForCheck(ArmyId);
        _mainGame.MenuBar.PressItemForCheck("unit_map.army_supply");

        Check(_mainGame.ActiveOverlay is SupplyDialog, "Supply army opens from the menu");
        _dialog = (SupplyDialog)_mainGame.ActiveOverlay!;
        Check(_commandsSeen == 0, $"opening the dialog issues no command ({_commandsSeen})");

        var ids = _dialog.ModelForCheck.Providers.Select(p => p.Id).ToList();
        Check(ids.Contains(OwnCityId), "the own city is a provider");
        Check(ids.Contains(ForeignCityId), "the peaceful foreign city is a provider");
        Check(ids.Contains(OwnFleetId), "the own fleet is a provider");
    }

    private void PressOwnCity()
    {
        var before = TakeSnapshot(ArmyId);
        _dialog.SelectProviderForCheck(OwnCityId);
        _dialog.PressSupplyForCheck(10);
        var after = TakeSnapshot(ArmyId);

        Check(after.Commands == before.Commands + 1, $"the +10 press issues exactly one command ({after.Commands - before.Commands})");
        Check(after.ArmySupply == before.ArmySupply + 10, $"the army gains 10 t ({before.ArmySupply} -> {after.ArmySupply})");
        Check(after.OwnCitySupply == before.OwnCitySupply - 10, $"the city loses 10 t ({before.OwnCitySupply} -> {after.OwnCitySupply})");
        Check(after.ArmyMoney == before.ArmyMoney, "the army's money is unchanged");
        Check(after.Treasury == before.Treasury, "the treasury is unchanged");
        Check(
            _dialog.ReplyForCheck.Contains("bought", StringComparison.Ordinal),
            $"the session's own reply line is shown in the dialog ('{_dialog.ReplyForCheck}')");
    }

    private void PressFleet()
    {
        var before = TakeSnapshot(ArmyId);
        _dialog.SelectProviderForCheck(OwnFleetId);
        _dialog.PressSupplyForCheck(100);
        var after = TakeSnapshot(ArmyId);

        Check(after.Commands == before.Commands + 1, "the +100 fleet press issues exactly one command");
        Check(after.ArmySupply == before.ArmySupply + 100, $"the army gains 100 t ({before.ArmySupply} -> {after.ArmySupply})");
        Check(after.FleetSupply == before.FleetSupply - 100, $"the fleet loses 100 t ({before.FleetSupply} -> {after.FleetSupply})");
        Check(after.ArmyMoney == before.ArmyMoney && after.Treasury == before.Treasury, "a fleet purchase moves no money");
    }

    private void StageAndTransferForeignCity()
    {
        _dialog.SelectProviderForCheck(ForeignCityId);
        var beforeStage = _commandsSeen;
        _dialog.AdjustStagedForCheck(50);
        Check(_dialog.ModelForCheck.StagedTons == 50, $"the foreign city stages 50 t ({_dialog.ModelForCheck.StagedTons})");
        Check(_commandsSeen == beforeStage, $"staging submits nothing ({_commandsSeen})");

        var before = TakeSnapshot(ArmyId);
        _dialog.TransferForCheck();
        var after = TakeSnapshot(ArmyId);

        Check(after.Commands == before.Commands + 1, "Transfer issues exactly one command");
        Check(after.ArmySupply == before.ArmySupply + 50, $"the army gains 50 t ({before.ArmySupply} -> {after.ArmySupply})");
        Check(after.ForeignCitySupply == before.ForeignCitySupply - 50, "the foreign city loses 50 t");
        Check(after.ArmyMoney == before.ArmyMoney - 10, $"the army's purse pays 10 talents ({before.ArmyMoney} -> {after.ArmyMoney})");
        Check(after.SellerTreasury == before.SellerTreasury + 10, $"the seller's treasury gains 10 talents ({before.SellerTreasury} -> {after.SellerTreasury})");
    }

    private void PressMoney()
    {
        var before = TakeSnapshot(ArmyId);
        _dialog.PressMoneyForCheck(100, viaFleetId: null);
        var after = TakeSnapshot(ArmyId);

        Check(after.Commands == before.Commands + 1, "the +100 money press issues exactly one command");
        Check(after.ArmyMoney == before.ArmyMoney + 100, $"the purse gains 100 talents ({before.ArmyMoney} -> {after.ArmyMoney})");
        Check(after.Treasury == before.Treasury - 100, $"the treasury loses 100 talents ({before.Treasury} -> {after.Treasury})");
    }

    private void CloseFirstDialog()
    {
        _dialog.Close();
        Check(_mainGame.ActiveOverlay is null, "Close closes the dialog");
    }

    private void OpenAtOverfullArmy()
    {
        OpenDialog(OverfullArmyId);
        var army = _session.State.ArmyById(OverfullArmyId)!;
        var capacity = IC2.Engine.Economy.SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, _session.Ruleset);
        Check(army.SupplyTons > capacity, $"the army is past its room ({army.SupplyTons} > {capacity})");
    }

    private void PressPastRoom()
    {
        _lastCommandLine = null;
        var before = TakeSnapshot(OverfullArmyId);
        _dialog.SelectProviderForCheck(OwnCityId);
        _dialog.PressSupplyForCheck(10);
        var after = TakeSnapshot(OverfullArmyId);

        Check(after.Commands == before.Commands + 1, "the past-room press issues exactly one command");
        Check(_lastCommandLine is not null, "the session answered the past-room press");
        Check(_dialog.ReplyForCheck == _lastCommandLine, $"the dialog shows the session's own reply line ('{_dialog.ReplyForCheck}' vs '{_lastCommandLine}')");
        Check(after.ArmySupply < before.ArmySupply, $"the over-room army gives supply back ({before.ArmySupply} -> {after.ArmySupply})");
    }

    private void CloseSecondDialog()
    {
        _dialog.Close();
        Check(_mainGame.ActiveOverlay is null, "the second dialog closes");
    }

    private void SelectForeignArmy()
    {
        var before = _commandsSeen;
        _mainGame.SelectArmyForCheck(ForeignArmyId);
        _mainGame.MenuBar.PressItemForCheck("unit_map.army_supply");

        Check(_mainGame.ActiveOverlay is null, "no dialog opens for a foreign army");
        Check(
            _mainGame.LastMessageForCheck == "Select one of your armies first.",
            $"the select-an-army message is shown ('{_mainGame.LastMessageForCheck}')");
        Check(_commandsSeen == before, "no command is issued for a foreign army");
    }

    private void SelectLonelyArmy()
    {
        var before = _commandsSeen;
        _mainGame.SelectArmyForCheck(LonelyArmyId);
        _mainGame.MenuBar.PressItemForCheck("unit_map.army_supply");

        Check(_mainGame.ActiveOverlay is null, "no dialog opens with no provider within one tile");
        Check(
            _mainGame.LastMessageForCheck == "No city or fleet of yours, or of a nation at peace with you, within one tile.",
            $"the no-provider message is shown ('{_mainGame.LastMessageForCheck}')");
        Check(_commandsSeen == before, "no command is issued with no provider");
    }

    private void Finish()
    {
        GD.Print($"SupplyArmyCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    private Snapshot TakeSnapshot(string armyId)
    {
        var state = _session.State;
        var army = state.ArmyById(armyId)!;
        return new Snapshot(
            Commands: _commandsSeen,
            ArmySupply: army.SupplyTons,
            ArmyMoney: army.Money,
            Treasury: state.NationById(BuyersNationId)!.Treasury,
            SellerTreasury: state.NationById(SellersNationId)!.Treasury,
            OwnCitySupply: state.CityById(OwnCityId)!.SupplyTons,
            ForeignCitySupply: state.CityById(ForeignCityId)!.SupplyTons,
            FleetSupply: state.FleetById(OwnFleetId)!.SupplyTons);
    }

    private sealed record Snapshot(
        int Commands,
        int ArmySupply,
        int ArmyMoney,
        int Treasury,
        int SellerTreasury,
        int OwnCitySupply,
        int ForeignCitySupply,
        int FleetSupply);

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }
}
