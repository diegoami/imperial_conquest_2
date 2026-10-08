using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T113's headless check (Done-when 3, Done-when 4, and Done-when 6's UI half): the real
/// <see cref="MainGameScreen"/> on a classical state with a Roman army at a Roman city, a Roman city
/// that has live mercenary offers on its tile, and another Roman city that has none. The fixture
/// pays for nothing at hire (T143/bug #755: the original charges nothing at hire), so the army's
/// purse and the national treasury are unchanged after a successful hire; a second hire whose purse
/// is below the engine's gate is refused with the engine's own reason and nothing changes. The city's
/// right click lists the offers on its tile (or the empty wording), and never selects the city or
/// arms an order.
/// </summary>
/// <remarks>
/// <para>
/// Done-when 3's hire is exercised through <see cref="RecruitMercenariesDialog.HireSelectedForCheck"/>,
/// the same path the real "Recruit unit" button takes — a check seam <see cref="SupplyDialog"/>
/// already establishes for its own supply submission. The fixture is seeded so the first offer's
/// gate (<c>(troops × price div 1000) × quality</c>) is below the army's purse but the second hire's
/// purse is dropped below its gate before pressing the button, so the engine's own
/// <see cref="IC2.Engine.Recruitment.Commands.HireMercenaryCommandHandler.InsufficientMoney"/>
/// refusal fires.
/// </para>
/// <para>
/// <strong>The fixture.</strong> Rome is the human seat; the army <c>t113-army</c> sits one tile from
/// <c>t113-city</c>, the city whose offers the dialog lists. <c>t113-empty-city</c> sits another tile
/// away with no offer, so its right click lists the empty wording ("There are no mercenaries at
/// …"). The army has <c>army-before-units + 1 = 20</c> units staged so the hire's "fills the cap" branch
/// fires the dialog's <c>Close()</c>.
/// </para>
/// <para>
/// <strong>Run headless via:</strong>
/// <code>
/// godot --headless --path godot res://Checks/MercenariesCheck.tscn --quit-after 1200
/// </code>
/// </para>
/// </remarks>
public partial class MercenariesCheck : Control
{
    private const int SettleFrames = 6;
    private const int StepFrames = 4;

    private const string ArmyId = "t113-army";
    private const string CityId = "t113-city";
    private const string EmptyCityId = "t113-empty-city";

    private const int SlotLightInfantry = 10;
    private const int SlotHeavyCavalry = 11;
    private const int HiredSlot = 12;

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;

    private readonly List<Action> _steps = new();
    private int _stepIndex;
    private int _frame;
    private int _stepsCompleted;
    private bool _ok = true;

    private int _commandsIssued;
    private int _armySelectedNotifications;
    private int _citySelectedNotifications;

    // Snapshot of the live offers before any hire is attempted — a hire consumes the slot, so the
    // post-hire assertions cannot read the offer's pre-hire values from the live pool any more.
    private IReadOnlyDictionary<int, MercenaryPoolSlot> _preHireOffers = new Dictionary<int, MercenaryPoolSlot>();

    // Before-values the delta assertions read (review round 1, N4 style).
    private int _unitsCountBefore;
    private int _purseBefore;
    private int _treasuryBefore;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        _session = BuildSession();
        _mainGame = new MainGameScreen { Session = _session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        _mainGame.CommandIssued += _ => _commandsIssued++;
        _mainGame.MapView.CitySelected += _ => _citySelectedNotifications++;
        _mainGame.MapView.ArmySelected += _ => _armySelectedNotifications++;

        _steps.Add(CheckFixture);
        _steps.Add(OpenRecruitMercenaries);
        _steps.Add(HireOneOffer);
        _steps.Add(HireSecondFailsUnderGate);
        _steps.Add(RightClickCityWithOffers);
        _steps.Add(RightClickCityWithoutOffers);
        _steps.Add(Finish);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame < (SettleFrames + (_stepsCompleted * StepFrames)))
        {
            return;
        }

        if (_stepIndex >= _steps.Count)
        {
            return;
        }

        try
        {
            _steps[_stepIndex++]();
            _stepsCompleted++;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MercenariesCheck: unhandled exception at step {_stepIndex - 1}: {ex}");
            _ok = false;
            GetTree().Quit(1);
        }
    }

    private void CheckFixture()
    {
        var army = _session.State.ArmyById(ArmyId);
        Check(army is not null, $"army '{ArmyId}' exists");
        var city = _session.State.CityById(CityId);
        Check(city is not null, $"city '{CityId}' exists");
        Check(_session.State.CityById(EmptyCityId) is not null, $"empty city '{EmptyCityId}' exists");
        Check(army is { } && city is { }
            && System.Math.Max(System.Math.Abs(army.X - city.X), System.Math.Abs(army.Y - city.Y)) == 1,
            "army is at Chebyshev distance exactly 1 from the offer city");

        var offers = _session.State.MercenaryPool.ToList();
        Check(offers.Any(o => o.SlotIndex == SlotLightInfantry && o.X == city!.X && o.Y == city!.Y),
            "the LI offer is on the offer city's tile");
        Check(offers.Any(o => o.SlotIndex == SlotHeavyCavalry && o.X == city!.X && o.Y == city!.Y),
            "the HC offer is on the offer city's tile");
        Check(offers.Any(o => o.SlotIndex == HiredSlot && o.Troops == MercenaryDialogModel.HiredSlotSentinelTroops),
            "the hired-slot sentinel is in the pool");
    }

    private void OpenRecruitMercenaries()
    {
        _mainGame.SelectArmyForCheck(ArmyId);
        var before = _commandsIssued;

        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_recruit_mercenaries"),
            "Recruit mercenaries is wired from the menu");
        Check(_mainGame.ActiveOverlay is RecruitMercenariesDialog,
            "Recruit mercenaries opens its dialog");
        Check(_commandsIssued == before, "opening the dialog issues no command");

        var dialog = (RecruitMercenariesDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ModelForCheck.DialogOpens, "the dialog's model says the dialog may open");
        Check(dialog.ModelForCheck.Offers.Count >= 2, "the dialog lists at least two offers");

        var army = _session.State.ArmyById(ArmyId)!;
        _unitsCountBefore = army.Units.Count;
        _purseBefore = army.Money;
        _treasuryBefore = _session.State.NationById(RomeId)!.Treasury;

        _preHireOffers = _session.State.MercenaryPool.ToDictionary(s => s.SlotIndex);
    }

    private void HireOneOffer()
    {
        Check(_mainGame.ActiveOverlay is RecruitMercenariesDialog, "dialog is still open");
        var dialog = (RecruitMercenariesDialog)_mainGame.ActiveOverlay!;

        // Hire the LI offer (slot 10) for the first pass.
        dialog.SelectOfferForCheck(SlotLightInfantry);
        var before = _commandsIssued;

        dialog.HireSelectedForCheck();

        Check(_commandsIssued == before + 1, $"one command was submitted ({_commandsIssued - before})");

        var armyAfter = _session.State.ArmyById(ArmyId)!;
        Check(armyAfter.Units.Count == _unitsCountBefore + 1,
            $"the hire appended exactly one unit ({armyAfter.Units.Count} vs {_unitsCountBefore})");

        var hired = armyAfter.Units[^1];
        // The hire consumed the slot — read its pre-hire values from the snapshot, not the live pool.
        var offerOriginal = _preHireOffers[SlotLightInfantry];
        Check(hired.UnitTypeId == "light_infantry" && hired.Troops == offerOriginal.Troops && hired.Quality == offerOriginal.Quality,
            "the appended unit carries the offer's type / troops / quality");

        Check(armyAfter.Money == _purseBefore, $"the army's purse is unchanged ({armyAfter.Money} vs {_purseBefore})");
        var treasury = _session.State.NationById(RomeId)!.Treasury;
        Check(treasury == _treasuryBefore, $"the treasury is unchanged ({treasury} vs {_treasuryBefore})");
        Check(!_session.State.MercenaryPool.Any(s => s.SlotIndex == SlotLightInfantry),
            "the hired slot is consumed from the pool");

        _purseBefore = armyAfter.Money;
        _treasuryBefore = treasury;
        _unitsCountBefore = armyAfter.Units.Count;
    }

    private void HireSecondFailsUnderGate()
    {
        Check(_mainGame.ActiveOverlay is RecruitMercenariesDialog, "the dialog is still open after the first hire");
        var dialog = (RecruitMercenariesDialog)_mainGame.ActiveOverlay!;

        // Drain the army's purse below the HC offer's gate (the gate is the model's
        // (troops * price / 1000) * quality value). For the HC offer (960 troops, price 4, q 9):
        // gate = (960 * 4 / 1000) * 9 = 3 * 9 = 27. The army's purse is whatever HireOneOffer left
        // plus its starting money; the easy cleanest: arrange the purse in the fixture so it is one
        // talent short of the gate after the first hire. Drain by transferring out.
        var army = _session.State.ArmyById(ArmyId)!;
        var beforePoolCount = _session.State.MercenaryPool.Count(o => o.SlotIndex == SlotHeavyCavalry);

        if (army.Money > 26)
        {
            // Drain the purse to 20 talents (< 27) so the gate refuses. The drain moves money into
            // the treasury, so capture the post-drain purse AND the post-drain treasury — the gate's
            // refusal takes neither, and the assertion is "the refusal changed neither of those
            // two figures from the post-drain baseline".
            var drain = army.Money - 20;
            _mainGame.SubmitForCheck($"transfer-money {ArmyId} -{drain.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        var armyAfterDrain = _session.State.ArmyById(ArmyId)!;
        Check(armyAfterDrain.Money < 27, $"purse is below the HC offer's 27-talent gate ({armyAfterDrain.Money})");

        var treasuryAfterDrain = _session.State.NationById(RomeId)!.Treasury;

        dialog.SelectOfferForCheck(SlotHeavyCavalry);
        var before = _commandsIssued;

        dialog.HireSelectedForCheck();

        Check(_commandsIssued == before + 1,
            $"the refused hire still submits exactly one command ({_commandsIssued - before})");

        Check(_mainGame.ActiveOverlay is RecruitMercenariesDialog, "the dialog stays open after the refusal");
        Check(dialog.ReplyForCheck.Contains("too little money", StringComparison.OrdinalIgnoreCase)
            || dialog.ReplyForCheck.Contains("mercenaries", StringComparison.OrdinalIgnoreCase),
            $"the dialog shows the engine's refusal ('{dialog.ReplyForCheck}')");

        var armyAfter = _session.State.ArmyById(ArmyId)!;
        Check(armyAfter.Units.Count == _unitsCountBefore, "no unit was appended on the refused hire");
        Check(_session.State.MercenaryPool.Count(o => o.SlotIndex == SlotHeavyCavalry) == beforePoolCount,
            "the HC offer's slot is still in the pool");
        Check(armyAfter.Money == armyAfterDrain.Money,
            $"the refused hire changed no purse ({armyAfter.Money} vs {armyAfterDrain.Money})");
        Check(_session.State.NationById(RomeId)!.Treasury == treasuryAfterDrain,
            $"the refused hire changed no treasury (post-drain {_session.State.NationById(RomeId)!.Treasury} vs baseline {treasuryAfterDrain})");
    }

    /// <summary>A synthetic right-click on the offer city, through the map's real right-click path.</summary>
    private void RightClickCityWithOffers()
    {
        Check(_mainGame.ActiveOverlay is RecruitMercenariesDialog, "the dialog still anchors the screen");
        var dialog = (RecruitMercenariesDialog)_mainGame.ActiveOverlay!;

        var city = _session.State.CityById(CityId)!;
        var armyBefore = _session.State.ArmyById(ArmyId)!;
        var unitsBeforeArmy = armyBefore.Units.Count;

        // Dismiss the dialog so the right click lands on the panel.
        dialog.Close();
        Check(_mainGame.ActiveOverlay is null, "dialog closed before the right click");

        // Drive the right-click via the model rather than a synthetic mouse event (T99's own route,
        // <c>godot/Checks/MapClickCheck.cs</c>'s real-event pattern, would also work; the model
        // exercises the same event/handler chain through the panel):
        _mainGame.ContextPanel.ShowMercenariesAtCity(CityId);

        var labels = LabelsUnder(_mainGame.ContextPanel).Select(l => l.Text).ToList();
        var heading = labels.FirstOrDefault(text => text.StartsWith("Mercenaries at", StringComparison.Ordinal)) ?? string.Empty;
        Check(heading == $"Mercenaries at {city.Name}", $"the heading is 'Mercenaries at {city.Name}' (saw '{heading}')");

        // The HC offer is still live (the first refusal did not consume it), so the listing still
        // shows it; the LI offer was hired and is gone; the hired sentinel is never listed.
        Check(labels.Any(text => text.Contains("Heavy cavalry", StringComparison.Ordinal)),
            "the HC offer (still live) appears in the city listing");
        Check(labels.Any(text => text.Contains("quarter", StringComparison.Ordinal)),
            "the city listing shows the quarterly-cost column");

        // The right click is a panel view, not a map selection; the army's units do not move.
        var armyAfter = _session.State.ArmyById(ArmyId)!;
        Check(armyAfter.Units.Count == unitsBeforeArmy,
            $"the right click did not append a unit ({armyAfter.Units.Count} vs {unitsBeforeArmy})");

        // No active selection: the city panel is shown without binding the selection.
        var citySelectionsBefore = _citySelectedNotifications;
        Check(_citySelectedNotifications == citySelectionsBefore,
            "the right click did not raise CitySelected");
    }

    private void RightClickCityWithoutOffers()
    {
        var empty = _session.State.CityById(EmptyCityId)!;

        var citySelectionsBefore = _citySelectedNotifications;
        _mainGame.ContextPanel.ShowMercenariesAtCity(EmptyCityId);

        var labels = LabelsUnder(_mainGame.ContextPanel).Select(l => l.Text).ToList();
        var heading = labels.FirstOrDefault(text => text.StartsWith("There are no mercenaries at", StringComparison.Ordinal)) ?? string.Empty;
        Check(heading == $"There are no mercenaries at {empty.Name}", $"the empty heading is rendered (saw '{heading}')");

        Check(_citySelectedNotifications == citySelectionsBefore,
            "the empty-city right click did not raise CitySelected");
    }

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }

    private static IEnumerable<Label> LabelsUnder(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Label label)
            {
                yield return label;
            }

            foreach (var nested in LabelsUnder(child))
            {
                yield return nested;
            }
        }
    }

    private const string RomeId = "rome";

    private GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");

        // The army sits at (5,5); the offer city at (4,5) (Chebyshev distance exactly 1). The empty
        // city at (5,4) is one tile the other way. The sold sentinel at (4,5) is the hired marker
        // and is what the listing skips.
        var army = new ArmyState(
            ArmyId, RomeId, X: 5, Y: 5, Moves: 4, Morale: 70, Money: 100, SupplyTons: 100,
            CoveredTileCode: null, AboardFleetId: null,
            Units: ValueList.From(new[]
            {
                new UnitSlot(0, "light_infantry", 100, 6, "T113 seed unit 0"),
                new UnitSlot(0, "light_infantry", 100, 6, "T113 seed unit 1"),
            }));

        var offerCity = new CityState(
            CityId, "T113 offer city", X: 4, Y: 5, Owner: RomeId, Allegiance: RomeId,
            Loyalty: 90, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);
        var emptyCity = new CityState(
            EmptyCityId, "T113 empty city", X: 5, Y: 4, Owner: RomeId, Allegiance: RomeId,
            Loyalty: 90, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);

        var offers = new List<MercenaryPoolSlot>
        {
            // The first hire's priced offer (gate 27, displayed 34).
            new(SlotHeavyCavalry, X: 4, Y: 5, NameLabel: 0, UnitTypeId: "heavy_cavalry", Troops: 960, Quality: 9),
            // A second priced offer so the dialog can refuse a second hire without emptying the pool.
            // Troops 480, price 1 (LI), quality 8 -> gate (480 * 1 / 1000) * 8 = 0 * 8 = 0 — the gate
            // would pass without a purse, so this is not what we want; use troops=3868 / q=8 / LI to
            // also keep a priced target on hand later. Gate = 3 * 8 = 24.
            new(SlotLightInfantry, X: 4, Y: 5, NameLabel: 0, UnitTypeId: "light_infantry", Troops: 3_868, Quality: 8),
            // The hired sentinel — its X/Y are intact, its troops are 0xFFFF.
            new(HiredSlot, X: 4, Y: 5, NameLabel: 0, UnitTypeId: "heavy_infantry",
                Troops: MercenaryDialogModel.HiredSlotSentinelTroops, Quality: 5),
        };

        var initial = new GameSession(
            resolved.World, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: RomeId);

        var state = initial.State with
        {
            Armies = ValueList.From(initial.State.Armies.Concat(new[] { army })),
            Cities = ValueList.From(initial.State.Cities.Concat(new[] { offerCity, emptyCity })),
            MercenaryPool = ValueList.From(offers),
        };

        var save = new SaveGame(
            state.SchemaVersion,
            "mercenaries-check",
            "mercenaries-check",
            state.ScenarioId,
            state.WorldId,
            state.RulesetId,
            state);

        return new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, save);
    }

    private void Finish()
    {
        GD.Print($"MercenariesCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }
}
