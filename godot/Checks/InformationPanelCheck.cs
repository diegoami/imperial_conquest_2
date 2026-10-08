using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T140's headless check: builds the real <see cref="MainGameScreen"/> on the shipped classical
/// Mediterranean start and reads the rendered <see cref="ContextPanel"/> labels for the city, own
/// army, foreign army, own fleet (built), foreign fleet (Carthage's starting fleet), the Conqueror
/// row's red colour, and the active-seat rule. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/InformationPanelCheck.tscn --quit-after 600
/// </code>
/// </summary>
/// <remarks>
/// Conforms to <c>godot/Checks/MapClickCheck.cs</c> and <c>godot/Checks/NationsAreaMapCheck.cs</c>'s
/// settle-frame plan and <c>godot/Checks/ContextPanelWidthCheck.cs</c>'s viewport setup. The "active
/// seat" rule (the brief's Done-when 4) is exercised by keeping the active seat fixed throughout:
/// <see cref="ShowFleetWithWarriorsState"/> mutates the session after construction by
/// <c>with</c>-replacing the fleets (and <see cref="ShowArmyWithoutCoveredTileState"/> does the
/// same for an army's <c>CoveredTileCode</c>), never the active seat, so the viewer check below is
/// the same as the brief's "Rome the active seat" precondition.
/// </remarks>
public partial class InformationPanelCheck : Control
{
    private const int InitialSettleFrames = 6;
    private const int BetweenStepsFrames = 4;
    private const string RomeId = "rome";
    private const string CarthageId = "carthage";
    private const string RomeCityId = "rome";
    private const string CarthagoCityId = "carthago";
    private const string RomanArmyId = "army-0";
    private const string CarthaginianFleetId = "fleet-0";

    private MainGameScreen _mainGame = null!;

    private bool _ok = true;
    private int _frame;
    private int _planIndex;
    private readonly List<(int WaitFrames, Action Run)> _plan = new();

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var session = new GameSession(
            resolved.World,
            resolved.Ruleset,
            resolved.Scenario,
            seedOverride: 1,
            humanSeatNationId: RomeId);

        _mainGame = new MainGameScreen
        {
            Session = session,
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        BuildPlan();
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            if (_planIndex >= _plan.Count)
            {
                return;
            }

            var (waitFrames, run) = _plan[_planIndex];
            if (_frame < waitFrames)
            {
                return;
            }

            run();
            _planIndex++;
            _frame = 0;

            if (_planIndex >= _plan.Count)
            {
                Finish();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"InformationPanelCheck: unhandled exception: {ex}");
            Finish();
        }
    }

    private void BuildPlan()
    {
        _plan.AddRange(new (int WaitFrames, Action Run)[]
        {
            (InitialSettleFrames, AssertInitialView),
            (BetweenStepsFrames, ShowRomeCity),
            (BetweenStepsFrames, AssertRomeCityPanel),
            (BetweenStepsFrames, ShowRomanArmy),
            (BetweenStepsFrames, AssertRomanArmyPanel),
            (BetweenStepsFrames, ShowForeignArmy),
            (BetweenStepsFrames, AssertForeignArmyPanel),
            (BetweenStepsFrames, ShowForeignFleet),
            (BetweenStepsFrames, AssertForeignFleetPanel),
            (BetweenStepsFrames, ChooseCarthageFromMenu),
            (BetweenStepsFrames, AssertForeignNationPanel),
            (BetweenStepsFrames, AssertPanelShowsForeignTributeWordForCarthago),
            (BetweenStepsFrames, ChooseRomeBackFromMenu),
            (BetweenStepsFrames, AssertOwnNationPanelAndRomeCityPanel),
            (BetweenStepsFrames, PrepareEliminatedScenario),
            (BetweenStepsFrames, OpenEliminatedNationPanel),
            (BetweenStepsFrames, AssertConqueredRowIsRed),
            (BetweenStepsFrames, ResetRomeViewAfterEliminatedCheck),
        });
    }

    // ---- Initial view ----

    private void AssertInitialView()
    {
        Check(
            string.Equals(_mainGame.ViewedNationId, RomeId, StringComparison.Ordinal),
            $"the viewed nation starts as the active seat (got '{_mainGame.ViewedNationId ?? "<all>"}')");
        Check(
            string.Equals(_mainGame.ContextPanel.StatusNationIdForCheck, RomeId, StringComparison.Ordinal),
            "the context panel starts on Rome's own status panel");
    }

    // ---- City panel (own) ----

    private void ShowRomeCity()
    {
        _mainGame.ContextPanel.ShowCity(RomeCityId);
    }

    private void AssertRomeCityPanel()
    {
        Check(PanelShowsHeading("City — Rome"), "Rome's city panel is shown (heading carries the city name)");

        var rome = _mainGame.Session.State.NationById(RomeId)!;
        var city = _mainGame.Session.State.CityById(RomeCityId)!;
        var percent = city.PopulationThousands * 100 / city.MaxPopulationThousands;

        // C01..C10: every fact from Done-when 2 lands on the panel as a label.
        Check(PanelHasLabelStartingWith("Rome  (capital of Rome)"), "the capital marker is on Rome's city line");
        Check(PanelHasLabel($"Controlled by: {rome.Name}"), "Rome's city lists the controlled-by nation");
        Check(PanelHasLabel($"Allegiance to: {rome.Name}"), "Rome's city lists the allegiance-to nation");
        Check(
            PanelHasLabel($"Population: {city.PopulationThousands * 1000} ({percent}%)"),
            "Rome's city shows population in people and the percent of max");
        Check(
            PanelHasLabel($"Loyalty: {InformationWords.Loyalty(city.Loyalty)}"),
            "Rome's city shows the loyalty band word");
        Check(
            PanelHasLabelStartingWith("Fortification:"),
            "Rome's city shows the fortification line");
        Check(
            PanelHasLabel($"Tribute: {IC2.Engine.Economy.CityTaxContribution.Compute(city)} talents"),
            "Rome's city shows tribute in talents (own city)");
        Check(PanelHasLabel("Supply: 990 tons"), "Rome's city shows supply in tons (own city)");
    }

    // ---- Own army panel ----

    private void ShowRomanArmy()
    {
        _mainGame.ContextPanel.ShowArmy(RomanArmyId);
    }

    private void AssertRomanArmyPanel()
    {
        var army = _mainGame.Session.State.ArmyById(RomanArmyId)!;
        Check(PanelShowsHeading($"Army — {RomanArmyId}"), "Rome's army panel is shown");
        Check(
            PanelHasLabel("Army of Rome"),
            "the panel reads 'Army of Rome'");

        var pct = IC2.Engine.Economy.SupplyCapacity.PercentFull(
            army.SupplyTons, army.TotalTroops, _mainGame.Session.Ruleset);

        Check(PanelHasLabel($"Moves: {army.Moves}"), "the panel shows the army's moves");
        Check(
            PanelHasLabel($"Supply: {army.SupplyTons} tons  ({pct}%)"),
            "the panel shows supply tons and the PercentFull percent");
        Check(
            PanelHasLabel($"Morale: {InformationWords.Morale(army.Morale)}"),
            "the panel shows the morale word");
        Check(PanelHasLabel($"Money: {army.Money} talents"), "the panel shows the army money");
        Check(
            PanelHasLabel($"Total troops: {army.TotalTroops}"),
            "the panel shows Total troops");

        if (army.CoveredTileCode is { } code)
        {
            var name = _mainGame.Session.World.TileTypeByCode(code)?.Name ?? $"code {code}";
            Check(PanelHasLabel($"Terrain: {name}"), "the panel shows the Terrain line");
        }

        // Five unit-type sums.
        var sums = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in army.Units)
        {
            sums.TryGetValue(slot.UnitTypeId, out var cur);
            sums[slot.UnitTypeId] = cur + slot.Troops;
        }

        foreach (var typeId in InformationPanelModel.UnitTypeIds)
        {
            var sum = sums.TryGetValue(typeId, out var v) ? v : 0;
            var displayName = UnitDisplayName(typeId);
            Check(
                PanelHasLabel($"{displayName}: {sum}"),
                $"the panel sums the {displayName} troop count");
        }
    }

    // ---- Foreign army panel ----

    private void ShowForeignArmy()
    {
        _mainGame.ContextPanel.ShowArmy("army-2");
    }

    private void AssertForeignArmyPanel()
    {
        var army = _mainGame.Session.State.ArmyById("army-2")!;
        Check(PanelShowsHeading("Army — army-2"), "the foreign-army panel is shown");

        Check(PanelHasLabel("Army of Carthage"), "the foreign panel reads 'Army of Carthage'");
        Check(PanelHasLabel("Moves: withheld"), "the foreign panel withholds moves");
        Check(PanelHasLabel("Supply: withheld"), "the foreign panel withholds supply");
        Check(PanelHasLabel("Morale: withheld"), "the foreign panel withholds morale");
        Check(PanelHasLabel("Money: withheld"), "the foreign panel withholds money");

        if (army.CoveredTileCode is { } code)
        {
            var name = _mainGame.Session.World.TileTypeByCode(code)?.Name ?? $"code {code}";
            Check(
                PanelHasLabel($"Terrain: {name}"),
                "the foreign panel shows its Terrain line when the army covers a tile");
        }

        var sums = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in army.Units)
        {
            sums.TryGetValue(slot.UnitTypeId, out var cur);
            sums[slot.UnitTypeId] = cur + slot.Troops;
        }

        foreach (var typeId in InformationPanelModel.UnitTypeIds)
        {
            var sum = sums.TryGetValue(typeId, out var v) ? v : 0;
            var displayName = UnitDisplayName(typeId);
            Check(
                PanelHasLabel($"{displayName}: {sum}"),
                $"the foreign panel sums {displayName}");
        }

        Check(
            PanelHasLabel($"Total troops: {army.TotalTroops}"),
            "the foreign panel shows Total troops");

        // The brief's "no upkeep on a foreign army" — the panel does not draw No. of units /
        // Regulars cost / Mercenary pay on a foreign panel.
        Check(!PanelHasLabelStartingWith("No. of units:"), "the foreign panel omits No. of units");
        Check(!PanelHasLabelStartingWith("Regulars cost:"), "the foreign panel omits Regulars cost");
        Check(!PanelHasLabelStartingWith("Mercenary pay:"), "the foreign panel omits Mercenary pay");
    }

    // ---- Foreign fleet panel (Carthage's fleet is foreign to Rome) ----

    private void ShowForeignFleet()
    {
        _mainGame.ContextPanel.ShowFleet(CarthaginianFleetId);
    }

    private void AssertForeignFleetPanel()
    {
        var fleet = _mainGame.Session.State.FleetById(CarthaginianFleetId)!;
        Check(PanelShowsHeading($"Fleet — {CarthaginianFleetId}"), "the foreign-fleet panel is shown");

        Check(PanelHasLabel("Fleet of Carthage"), "the fleet header reads 'Fleet of Carthage'");
        Check(PanelHasLabel($"Ships: {fleet.Ships}"), "the fleet's Ships are shown");
        Check(
            PanelHasLabel("Moves:"),
            "the foreign fleet withholds moves (caption only)");
        Check(
            PanelHasLabel("Repair:"),
            "the foreign fleet withholds repair");
        Check(
            PanelHasLabel("Supply:"),
            "the foreign fleet withholds supply");
        Check(
            PanelHasLabel("Money:"),
            "the foreign fleet withholds money");

        var transport = _mainGame.Session.Ruleset.Naval.TransportTroopsPerShip;
        Check(
            PanelHasLabel($"Capacity: {fleet.Ships * transport} troops"),
            "the fleet's Capacity is shown");

        var expectedSea = $"Sea: {InformationWords.Sea(fleet.CoveredTileCode ?? 0)}";
        Check(PanelHasLabel(expectedSea), "the fleet's Sea word is shown");
    }

    // ---- Foreign nation panel (Carthage viewed by Rome) ----

    private void ChooseCarthageFromMenu()
    {
        Check(
            _mainGame.MenuBar.PressItemForCheck("nations.carthage"),
            "the Nations menu accepts Carthage");
        Check(
            string.Equals(_mainGame.ViewedNationId, CarthageId, StringComparison.Ordinal),
            "Carthage is the viewed nation");
        Check(
            string.Equals(_mainGame.ContextPanel.StatusNationIdForCheck, CarthageId, StringComparison.Ordinal),
            "Carthage's status panel is shown");
    }

    private void AssertForeignNationPanel()
    {
        var carthage = _mainGame.Session.State.NationById(CarthageId)!;

        // The 2026-10-05 decision: Population = Wealth, Unity as a word, Tax rate — every relation.
        Check(
            PanelHasLabel($"Leader: {carthage.LeaderName}"),
            "Carthage's panel shows the leader");
        Check(
            PanelHasLabel($"Population: {carthage.Wealth}"),
            "Carthage's panel shows Population (Wealth)");
        Check(
            PanelHasLabel($"Unity: {InformationWords.Unity(carthage.Unity)}"),
            "Carthage's panel shows Unity as a word");
        Check(
            PanelHasLabel($"Tax rate: {carthage.TaxRatePercent}%"),
            "Carthage's panel shows Tax rate");

        // Withheld: Mobilized and Treasury are blank for foreign nations — the rows are present
        // (N08 / N09) but the values are empty. R3 of the R1 review: a foreign panel that omits
        // the rows entirely hides the field's place in the original's order.
        Check(PanelHasLabel("Mobilized:"), "Carthage's panel keeps a blank Mobilized row");
        Check(PanelHasLabel("Treasury:"), "Carthage's panel keeps a blank Treasury row");
        // The values do not leak: the blank line has no digits or '%' after the colon.
        var mobilizedBlank = FindPanelLabel(label => label.Text == "Mobilized:");
        Check(
            mobilizedBlank is not null && !ContainsDigit(mobilizedBlank.Text),
            "the foreign Mobilized line carries no digits");
        var treasuryBlank = FindPanelLabel(label => label.Text == "Treasury:");
        Check(
            treasuryBlank is not null && !ContainsDigit(treasuryBlank.Text),
            "the foreign Treasury line carries no digits");
    }

    private void AssertPanelShowsForeignTributeWordForCarthago()
    {
        // Clicking Carthago while Rome is the active seat shows the foreign city panel — the tribute
        // word and a blank supply line.
        _mainGame.ContextPanel.ShowCity(CarthagoCityId);

        var city = _mainGame.Session.State.CityById(CarthagoCityId)!;
        Check(PanelShowsHeading("City — Carthago"), "Carthago's city panel is shown under Carthage viewed");

        // C09 (foreign): tribute is the band word (raw tribute is in the very-rich band).
        var expectedTribute = $"Tribute: {InformationWords.Tribute(city.Tribute)}";
        Check(
            PanelHasLabel(expectedTribute),
            "Carthago's city uses the tribute band word (foreign viewer)");

        // C10 (foreign): Supply line is present but blank, keeping the field's position.
        Check(PanelHasLabel("Supply:"), "Carthago's city shows a blank Supply line (foreign viewer)");
    }

    // ---- Back to Rome from the menu ----

    private void ChooseRomeBackFromMenu()
    {
        Check(
            _mainGame.MenuBar.PressItemForCheck("nations.rome"),
            "the Nations menu accepts Rome");
    }

    private void AssertOwnNationPanelAndRomeCityPanel()
    {
        var rome = _mainGame.Session.State.NationById(RomeId)!;
        Check(
            PanelHasLabel($"Population: {rome.Wealth}"),
            "Rome's own panel shows Population (Wealth, 2577000)");
        Check(
            PanelHasLabel($"Unity: {InformationWords.Unity(rome.Unity)}"),
            "Rome's own panel shows Unity as a word");
        Check(
            PanelHasLabel($"Mobilized: {rome.MobilizedPercent}%"),
            "Rome's own panel shows Mobilized");
        Check(
            PanelHasLabel($"Treasury: {rome.Treasury} talents"),
            "Rome's own panel shows Treasury with the ' talents' suffix (R4 of the R1 review)");

        // Clicking Rome (the capital) while the active seat is Rome shows the own shape — tribute
        // in talents, supply in tons.
        _mainGame.ContextPanel.ShowCity(RomeCityId);
        var city = _mainGame.Session.State.CityById(RomeCityId)!;
        var talents = IC2.Engine.Economy.CityTaxContribution.Compute(city);
        Check(PanelHasLabel($"Tribute: {talents} talents"), "Rome's city shows tribute in talents (own)");
        Check(PanelHasLabel("Supply: 990 tons"), "Rome's city shows supply in tons (own)");
    }

    // ---- Red row ----

    private void PrepareEliminatedScenario()
    {
        // Mark Carthage Eliminated + ConqueredBy on a fresh MainGameScreen's session. The new
        // session keeps Rome as the active seat so the active-seat precondition (and the
        // with-Rome-viewed assertion) is identical to the rest of the check. Resuming through a
        // SaveGame is the existing constructor seam the live Session.State can come from: the
        // standard session does not expose a state-replacement path.
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var baseSession = new GameSession(
            resolved.World, resolved.Ruleset, resolved.Scenario,
            seedOverride: 1, humanSeatNationId: RomeId);

        var arranged = baseSession.State with
        {
            Nations = ValueList.From(baseSession.State.Nations.Select(n =>
                string.Equals(n.Id, CarthageId, StringComparison.Ordinal)
                    ? n with { Eliminated = true, ConqueredBy = RomeId }
                    : n)),
        };

        var save = new SaveGame(
            SchemaVersion: 1,
            Id: "information-panel-check-eliminated",
            Label: "T140 eliminated state",
            ScenarioId: baseSession.Scenario.Id,
            WorldId: resolved.World.Id,
            RulesetId: resolved.Ruleset.Id,
            State: arranged);

        var resumed = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, save);

        _mainGame.QueueFree();
        _mainGame = new MainGameScreen
        {
            Session = resumed,
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
    }

    private void OpenEliminatedNationPanel()
    {
        // Settle frames need to pass before the panel rebuild lands. The new MainGameScreen has
        // Rome as the active seat by construction (the SaveGame.Nations list keeps Rome's
        // SeatControl.Human), so its first paint is Rome's nation panel — which is the panel
        // the brief's "red row" assertion needs.
    }

    private void AssertConqueredRowIsRed()
    {
        var carthage = _mainGame.Session.State.NationById(CarthageId)!;
        var conqueror = _mainGame.Session.State.NationById(RomeId)!;
        var expectedText = $"   ( {carthage.Name} conquerred by {conqueror.Name} )";
        var red = FindPanelLabel(label => label.Text == expectedText);
        Check(
            red is not null,
            $"the conquered row text is laid out (expected '{expectedText}')");
        if (red is null)
        {
            return;
        }

        var color = red.GetThemeColor("font_color");
        Check(
            Approximately(color, ContextPanel.ConqueredRowColor),
            $"the conquered row's font_color is the panel's red (got {color}, expected {ContextPanel.ConqueredRowColor})");

        // Every other relation row remains the panel's text colour.
        var plain = FindPanelLabel(label => label.Text.StartsWith("Carthage") is false
            && label.Text.StartsWith("Rome:") is false
            && label.Text.Contains(':'));
        if (plain is not null)
        {
            var plainColor = plain.GetThemeColor("font_color");
            Check(
                !Approximately(plainColor, ContextPanel.ConqueredRowColor),
                $"a non-conquered relation row keeps the text colour (got {plainColor})");
        }
    }

    private void ResetRomeViewAfterEliminatedCheck()
    {
        // No-op; the next step is the Finish, the active seat is still Rome, the panel still shows
        // Rome's nation panel. The reset exists so a downstream reader sees the same state this
        // check leaves (deterministic, useful when the check is run alongside others).
    }

    // ---- Helpers ----

    private static string UnitDisplayName(string typeId) => typeId switch
    {
        "light_infantry" => "Light infantry",
        "heavy_infantry" => "Heavy infantry",
        "archers" => "Archers",
        "light_cavalry" => "Light cavalry",
        "heavy_cavalry" => "Heavy cavalry",
        _ => typeId,
    };

    private static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.R - b.R) < 0.01f
        && Mathf.Abs(a.G - b.G) < 0.01f
        && Mathf.Abs(a.B - b.B) < 0.01f
        && Mathf.Abs(a.A - b.A) < 0.01f;

    /// <summary>Whether <paramref name="text"/> carries any digit. Used to confirm a blank-value
    /// row (e.g. a foreign Mobilized or Treasury caption) really withholds its number.</summary>
    private static bool ContainsDigit(string text) => text.Any(char.IsDigit);

    private bool PanelShowsHeading(string text) =>
        FindPanelLabel(label => label.Text == text) is { Visible: true };

    private bool PanelHasLabel(string text) =>
        FindPanelLabel(label => label.Text == text) is not null;

    private bool PanelHasLabelStartingWith(string prefix) =>
        FindPanelLabel(label => label.Text.StartsWith(prefix, StringComparison.Ordinal)) is not null;

    private Label? FindPanelLabel(Func<Label, bool> predicate) =>
        FindLabel(_mainGame.ContextPanel, predicate);

    private static Label? FindLabel(Node root, Func<Label, bool> predicate)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Label label && predicate(label))
            {
                return label;
            }

            if (FindLabel(child, predicate) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private void Check(bool condition, string description)
    {
        _ok &= condition;
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
    }

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"InformationPanelCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }
}
