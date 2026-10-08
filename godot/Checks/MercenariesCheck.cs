using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T113's headless check: the real <see cref="MainGameScreen"/> on a classical state with a Roman army
/// one tile from a Roman offer-city (live LI and HC offers on its tile), a Roman empty city one tile
/// the other way, and an army whose purse is drained below the second offer's gate. Each step waits
/// its own frames, then runs once. The plan covers Done-when 3 (hire + refused hire), Done-when 6
/// (city right click with and without offers, through the real input path and the screen's own
/// <see cref="GameMapView.UnitListRequested"/> event), R2 (the screen's event wiring must drive the
/// listing, not <see cref="ContextPanel.ShowMercenariesAtCity"/> directly), R5 (a 19-unit army's hire
/// closes the dialog; an already-full army's UI is covered), and R1 (the mini-map paints the
/// per-type symbol).
/// </summary>
/// <remarks>
/// <para>
/// All clicks are real <see cref="InputEventMouseButton"/> events at the map's own
/// <see cref="GameMapView.TileCenterForCheck"/> pixels, delivered through
/// <see cref="GameMapView._GuiInput"/> — the same seam <c>godot/Checks/MapClickCheck.cs</c> uses. The
/// city right click asserts against <see cref="GameMapView.CitySelected"/> and the
/// <see cref="ContextPanel"/>'s rendered labels, the way a player sees them, not a snapshot taken
/// after the click (R2's "tautological" finding).
/// </para>
/// <para>
/// The hire passes through <see cref="RecruitMercenariesDialog.HireSelectedForCheck"/> (the same
/// path the real "Recruit unit" button takes), so the dialog's overlay-close branch fires from the
/// real handler, never from a helper that skips it. The 19-unit fixture's first hire brings the army
/// to 20, which <see cref="MercenaryDialogModel.HireFillsCap"/> flags and the dialog
/// <see cref="RecruitMercenariesDialog.Close"/>s; the assertion reads <see cref="MainGameScreen.ActiveOverlay"/>
/// after the close has settled. The 20-unit fixture re-runs the menu entry and asserts the dialog
/// does not open at all (Done-when 2's cap rule).
/// </para>
/// <para>
/// <strong>Run headless via:</strong>
/// <code>
/// godot --headless --path godot res://Checks/MercenariesCheck.tscn --quit-after 1800
/// </code>
/// </para>
/// </remarks>
public partial class MercenariesCheck : Control
{
    private const int InitialSettleFrames = 6;
    private const int BetweenStepsFrames = 4;

    private const string ArmyId = "t113-army";
    private const string FullArmyId = "t113-full-army";
    private const string CityId = "t113-city";
    private const string EmptyCityId = "t113-empty-city";

    private const int SlotLightInfantry = 10;
    private const int SlotHeavyCavalry = 11;
    private const int HiredSlot = 12;

    // The hire-step's offered city sits at (4,5), one tile from the army at (5,5); the empty city
    // sits at (5,4), one tile the other way. Both are Rome-owned, so the engine's pre-open refusals
    // (city at war, supplies, fleet, 100k troops) are silent and the menu entry's only gate is the
    // chosen-city rule and the 20-unit cap.
    private const int ArmyX = 5;
    private const int ArmyY = 5;
    private const int OfferCityX = 4;
    private const int OfferCityY = 5;
    private const int EmptyCityX = 5;
    private const int EmptyCityY = 4;

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;
    private GameMapView _map = null!;

    private bool _ok = true;
    private int _frame;
    private int _planIndex;
    private readonly List<(int WaitFrames, Action Run)> _plan = new();

    private int _commandsSubmitted;
    private int _citySelectedNotifications;
    private int _armySelectedNotifications;

    // Snapshot of the live offers before any hire is attempted — a hire consumes the slot, so the
    // post-hire assertions cannot read the offer's pre-hire values from the live pool any more.
    private IReadOnlyDictionary<int, MercenaryPoolSlot> _preHireOffers = new Dictionary<int, MercenaryPoolSlot>();

    // Before-values the delta assertions read (review round 1, N4).
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

        _map = _mainGame.MapView;
        _mainGame.CommandIssued += lines =>
        {
            _commandsSubmitted++;
            // Keep the lines accessible for the "the hire composed the engine verb" assertion.
            _lastCommandLines = lines;
        };
        _map.CitySelected += _ => _citySelectedNotifications++;
        _map.ArmySelected += _ => _armySelectedNotifications++;

        BuildPlan();
    }

    private IReadOnlyList<string> _lastCommandLines = Array.Empty<string>();

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
            GD.PrintErr($"MercenariesCheck: unhandled exception: {ex}");
            GD.Print("MercenariesCheck: exiting with code 1.");
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// The plan, in Done-when order: each interaction is followed by its own assertion step, so every
    /// panel read happens after the layout pass that follows the click (N3).
    /// </summary>
    private void BuildPlan()
    {
        _plan.AddRange(new (int WaitFrames, Action Run)[]
        {
            // --- Setup checks ---
            (InitialSettleFrames, CheckFixture),

            // --- Done-when 3: hire through the dialog, then a refused hire under the gate ---
            (BetweenStepsFrames, OpenRecruitMercenaries),
            (BetweenStepsFrames, AssertDialogOpenedAndArmyUntouched),
            (BetweenStepsFrames, HireOneOffer),
            (BetweenStepsFrames, AssertHireAppendedUnitAndChangedNothing),
            (BetweenStepsFrames, HireSecondFailsUnderGate),
            (BetweenStepsFrames, AssertRefusedHireChangedNothing),

            // --- R5: a 19-unit army's hire closes the dialog ---
            (BetweenStepsFrames, SetupNineteenUnitHireClosesDialog),
            (BetweenStepsFrames, AssertNineteenUnitHireClosedDialog),

            // --- R5 (already-full UI): 20-unit army, menu entry shows refusal, dialog stays closed ---
            (BetweenStepsFrames, OpenRecruitMercenariesForFullArmy),
            (BetweenStepsFrames, AssertFullArmyDialogStayedClosedAndMessageShown),

            // --- Done-when 6 (R2): real right-click input path drives the city listing ---
            (BetweenStepsFrames, RightClickCityWithOffersViaRealInput),
            (BetweenStepsFrames, AssertCityWithOffersShowsHeadingAndDoesNotSelect),
            (BetweenStepsFrames, RightClickCityWithoutOffersViaRealInput),
            (BetweenStepsFrames, AssertCityWithoutOffersShowsEmptyHeadingAndDoesNotSelect),

            // --- R1: the mini-map distinguishes mercenary types by their unit icons ---
            (BetweenStepsFrames, ToggleMercenaryLayers),
            (BetweenStepsFrames, AssertMercenaryTypeIconsAreDrawnPerType),
        });
    }

    // ---- setup ----

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

    // ---- Done-when 3 (hire + refused hire) ----

    private void OpenRecruitMercenaries()
    {
        // Anchor: the army is already selected by the game's first state. A left click on the army's
        // tile selects it for the menu entry.
        SelectArmyViaInput(ArmyId);
        var before = _commandsSubmitted;

        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_recruit_mercenaries"),
            "Recruit mercenaries is wired from the menu");
        Check(_mainGame.ActiveOverlay is RecruitMercenariesDialog,
            "Recruit mercenaries opens its dialog");
        Check(_commandsSubmitted == before, "opening the dialog issues no command");
    }

    private void AssertDialogOpenedAndArmyUntouched()
    {
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

        dialog.SelectOfferForCheck(SlotLightInfantry);
        var before = _commandsSubmitted;

        dialog.HireSelectedForCheck();

        Check(_commandsSubmitted == before + 1, $"one command was submitted ({_commandsIssuedDelta(before)})");

        var armyAfter = _session.State.ArmyById(ArmyId)!;
        Check(armyAfter.Units.Count == _unitsCountBefore + 1,
            $"the hire appended exactly one unit ({armyAfter.Units.Count} vs {_unitsCountBefore})");

        var hired = armyAfter.Units[^1];
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

    private string _commandsIssuedDelta(int before) =>
        $"{_commandsSubmitted - before}";

    private void AssertHireAppendedUnitAndChangedNothing()
    {
        // The HireOneOffer step runs the assertion as it goes; this empty step lets the layout
        // pass settle before HireSecondFailsUnderGate issues its own command.
    }

    private void HireSecondFailsUnderGate()
    {
        Check(_mainGame.ActiveOverlay is RecruitMercenariesDialog, "the dialog is still open after the first hire");
        var dialog = (RecruitMercenariesDialog)_mainGame.ActiveOverlay!;

        var army = _session.State.ArmyById(ArmyId)!;
        var beforePoolCount = _session.State.MercenaryPool.Count(o => o.SlotIndex == SlotHeavyCavalry);

        if (army.Money > 26)
        {
            var drain = army.Money - 20;
            _mainGame.SubmitForCheck($"transfer-money {ArmyId} -{drain.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        var armyAfterDrain = _session.State.ArmyById(ArmyId)!;
        Check(armyAfterDrain.Money < 27, $"purse is below the HC offer's 27-talent gate ({armyAfterDrain.Money})");

        var treasuryAfterDrain = _session.State.NationById(RomeId)!.Treasury;

        dialog.SelectOfferForCheck(SlotHeavyCavalry);
        var before = _commandsSubmitted;

        dialog.HireSelectedForCheck();

        Check(_commandsSubmitted == before + 1,
            $"the refused hire still submits exactly one command ({_commandsSubmitted - before})");

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

    private void AssertRefusedHireChangedNothing()
    {
        // Settle step: the previous step ran the assertions, this step just lets the layout pass.
    }

    // ---- R5: a 19-unit army's hire closes the dialog ----

    private void SetupNineteenUnitHireClosesDialog()
    {
        // The full army sits at (3,5), one tile from a different offer city at (4,5) we add on the
        // fly; the army is at the unit cap-1, so its first hire brings it to 20 and the dialog
        // closes. The full army is built outside the fixture (BuildSession) — it is staged below.
        // Close the existing dialog first; the open dialog owns the screen.
        if (_mainGame.ActiveOverlay is RecruitMercenariesDialog dialog)
        {
            dialog.Close();
        }
        Check(_mainGame.ActiveOverlay is null, "the previous dialog closed before the next step");

        var fullArmy = _session.State.ArmyById(FullArmyId)!;
        Check(fullArmy.Units.Count == 19, "the full-army fixture has 19 units");

        // Select the full army via the real input path.
        SelectArmyViaInput(FullArmyId);
    }

    private void AssertNineteenUnitHireClosedDialog()
    {
        Check(_mainGame.ActiveOverlay is null,
            "the full-army selection left the overlay closed (no dialog was open)");

        var beforeCommands = _commandsSubmitted;
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_recruit_mercenaries"),
            "the Recruit mercenaries menu opens the dialog for the 19-unit army");
        Check(_mainGame.ActiveOverlay is RecruitMercenariesDialog,
            "the dialog opens for the 19-unit army (cap-1 + at least one offer in reach)");

        var dialog = (RecruitMercenariesDialog)_mainGame.ActiveOverlay!;
        Check(dialog.ModelForCheck.DialogOpens, "the model says the 19-unit dialog opens");
        Check(dialog.ModelForCheck.Offers.Count >= 1, "the 19-unit dialog has at least one offer");

        var offerSlot = dialog.ModelForCheck.Offers[0].SlotIndex;

        // GLM's re-check R1: a REFUSED hire at 19 units must leave the dialog open. Drain the army's
        // purse below any offer's gate, try the hire, and check the dialog and the unit count; then
        // refill the purse for the accepted hire below.
        var purse = _session.State.ArmyById(FullArmyId)!.Money;
        _mainGame.SubmitForCheck($"transfer-money {FullArmyId} -{purse.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        Check(_session.State.ArmyById(FullArmyId)!.Money == 0, "the 19-unit army's purse is drained to 0");
        dialog.SelectOfferForCheck(offerSlot);
        dialog.HireSelectedForCheck();
        Check(_mainGame.ActiveOverlay is RecruitMercenariesDialog,
            "a refused hire at 19 units leaves the dialog open (GLM R1)");
        Check(_session.State.ArmyById(FullArmyId)!.Units.Count == 19,
            "the refused hire added no unit (still 19)");
        _mainGame.SubmitForCheck($"transfer-money {FullArmyId} {purse.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        Check(_session.State.ArmyById(FullArmyId)!.Money == purse, $"the purse is refilled to {purse}");

        dialog.SelectOfferForCheck(offerSlot);
        dialog.HireSelectedForCheck();

        // The dialog closes because the hire brought the army to 20 (= cap).
        Check(_mainGame.ActiveOverlay is null,
            "the 19-unit army's hire closes the dialog (the cap is reached)");

        var fullArmy = _session.State.ArmyById(FullArmyId)!;
        Check(fullArmy.Units.Count == 20,
            $"the 19-unit army's hire brings it to 20 (got {fullArmy.Units.Count})");
    }

    // ---- R5: 20-unit army's UI path: dialog does not open, message shown ----

    private void OpenRecruitMercenariesForFullArmy()
    {
        // The "twenty-unit" army is staged in BuildSession: 20 units, adjacent to the offer city,
        // but the offer-city adjacency rule still picks the city; the menu's pre-open gate then
        // refuses with the cap message.
        SelectArmyViaInput(TwentyUnitArmyId);
    }

    private const string TwentyUnitArmyId = "t113-twenty-army";

    private void AssertFullArmyDialogStayedClosedAndMessageShown()
    {
        var before = _commandsSubmitted;
        Check(_mainGame.MenuBar.PressItemForCheck("unit_map.army_recruit_mercenaries"),
            "the menu entry is enabled for the 20-unit army");
        Check(_mainGame.ActiveOverlay is null,
            "the dialog does not open for the 20-unit army (Done-when 2's cap rule)");
        Check(_commandsSubmitted == before,
            "the refused menu entry submits no engine command");
        // The screen's message line shows the cap refusal — read it via the screen's own API.
        Check(_mainGame.LastMessageForCheck?.Contains("20 units", StringComparison.Ordinal) == true,
            $"the screen's message reports the 20-unit refusal (got '{_mainGame.LastMessageForCheck}')");
    }

    // ---- Done-when 6 (R2): real right-click input path drives the city listing ----

    private void RightClickCityWithOffersViaRealInput()
    {
        var city = _session.State.CityById(CityId)!;
        var armyBefore = _session.State.ArmyById(ArmyId)!;
        var unitsBeforeArmy = armyBefore.Units.Count;
        // R2: capture _citySelectedNotifications *before* the click so the assertion compares with a
        // real before-value, not a snapshot taken after.
        var citySelectionsBefore = _citySelectedNotifications;
        var commandsBefore = _commandsSubmitted;

        RightClickOnTile(city.X, city.Y);

        // Save the before-values on the instance for the assertion step.
        _selectionBeforeLastRightClick = citySelectionsBefore;
        _commandsBeforeLastRightClick = commandsBefore;
        _unitsBeforeLastRightClick = unitsBeforeArmy;
    }

    private void AssertCityWithOffersShowsHeadingAndDoesNotSelect()
    {
        var city = _session.State.CityById(CityId)!;
        var labels = LabelsUnder(_mainGame.ContextPanel).Select(l => l.Text).ToList();
        var heading = labels.FirstOrDefault(text => text.StartsWith("Mercenaries at", StringComparison.Ordinal)) ?? string.Empty;
        Check(heading == $"Mercenaries at {city.Name}",
            $"the heading is 'Mercenaries at {city.Name}' (saw '{heading}')");

        // The HC offer is still live (the refusal did not consume it), so the listing still shows
        // it; the LI offer was hired and is gone; the hired sentinel is never listed.
        Check(labels.Any(text => text.Contains("Heavy cavalry", StringComparison.Ordinal)),
            "the HC offer (still live) appears in the city listing");
        Check(labels.Any(text => text.Contains("quarter", StringComparison.Ordinal)),
            "the city listing shows the quarterly-cost column");

        // R2: the city-selected count did not move on the real right-click path.
        Check(_citySelectedNotifications == _selectionBeforeLastRightClick,
            $"a right click on a city did not raise CitySelected (was {_selectionBeforeLastRightClick}, now {_citySelectedNotifications})");
        Check(_commandsSubmitted == _commandsBeforeLastRightClick,
            "a right click on a city submitted no command");
        var armyAfter = _session.State.ArmyById(ArmyId)!;
        Check(armyAfter.Units.Count == _unitsBeforeLastRightClick,
            $"the right click did not append a unit ({armyAfter.Units.Count} vs {_unitsBeforeLastRightClick})");
    }

    private int _selectionBeforeLastRightClick;
    private int _commandsBeforeLastRightClick;
    private int _unitsBeforeLastRightClick;

    private void RightClickCityWithoutOffersViaRealInput()
    {
        var citySelectionsBefore = _citySelectedNotifications;
        var commandsBefore = _commandsSubmitted;
        var city = _session.State.CityById(EmptyCityId)!;
        RightClickOnTile(city.X, city.Y);
        _selectionBeforeLastRightClick = citySelectionsBefore;
        _commandsBeforeLastRightClick = commandsBefore;
    }

    private void AssertCityWithoutOffersShowsEmptyHeadingAndDoesNotSelect()
    {
        var empty = _session.State.CityById(EmptyCityId)!;
        var labels = LabelsUnder(_mainGame.ContextPanel).Select(l => l.Text).ToList();
        var heading = labels.FirstOrDefault(text => text.StartsWith("There are no mercenaries at", StringComparison.Ordinal)) ?? string.Empty;
        Check(heading == $"There are no mercenaries at {empty.Name}",
            $"the empty heading is rendered (saw '{heading}')");

        Check(_citySelectedNotifications == _selectionBeforeLastRightClick,
            $"the empty-city right click did not raise CitySelected (was {_selectionBeforeLastRightClick}, now {_citySelectedNotifications})");
        Check(_commandsSubmitted == _commandsBeforeLastRightClick,
            "the empty-city right click submitted no command");
    }

    // ---- R1: the mini-map draws the per-type mercenary icon ----

    private void ToggleMercenaryLayers()
    {
        _mainGame.AreaMapView.SetHighlight(AreaMapHighlightKind.MercenariesLightInfantry, true);
        _mainGame.AreaMapView.SetHighlight(AreaMapHighlightKind.MercenariesHeavyCavalry, true);
    }

    private void AssertMercenaryTypeIconsAreDrawnPerType()
    {
        // Two paint cycles: the layer toggle above queued a redraw, so read after a couple of frames.
        var drawn = _mainGame.AreaMapView.LastDrawnMercenaryIconsForCheck;
        var city = _session.State.CityById(CityId)!;

        Check(
            drawn.TryGetValue((city.X, city.Y), out var iconKey),
            $"the offer city's tile has a drawn mercenary icon (got '{iconKey ?? "<none>"}')");

        // The HC layer is on; the LI slot was hired so its offer is gone, only the HC one is live.
        Check(string.Equals(iconKey, IC2.Engine.Assets.AssetKeys.UnitHeavyCavalryIcon, StringComparison.Ordinal),
            $"the offer-city tile's drawn icon is the heavy-cavalry icon (got '{iconKey}')");
    }

    // ---- input helpers ----

    /// <summary>
    /// Drives the map's real input path with real mouse events at real positions, exactly as
    /// <c>godot/Checks/MapClickCheck.cs</c>: a left press arms the drag state, the release is the
    /// click. The right button has no press state — its release alone is the click.
    /// </summary>
    private void LeftClick(int x, int y) => Click(x, y, MouseButton.Left);

    private void RightClick(int x, int y) => Click(x, y, MouseButton.Right);

    private void Click(int x, int y, MouseButton button)
    {
        var position = _map.TileCenterForCheck(x, y);
        if (button == MouseButton.Left)
        {
            _map._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = button,
                Pressed = true,
                Position = position,
            });
        }

        _map._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = false,
            Position = position,
        });
    }

    private void RightClickOnTile(int x, int y) => RightClick(x, y);

    private void SelectArmyViaInput(string armyId)
    {
        var army = _session.State.ArmyById(armyId)
            ?? throw new InvalidOperationException($"Army '{armyId}' is not in the fixture.");
        LeftClick(army.X, army.Y);
    }

    // ---- panel helpers ----

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

    private void Check(bool condition, string description)
    {
        _ok &= condition;
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
    }

    // ---- finish ----

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"MercenariesCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private const string RomeId = "rome";

    private GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");

        var army = new ArmyState(
            ArmyId, RomeId, X: ArmyX, Y: ArmyY, Moves: 4, Morale: 70, Money: 100, SupplyTons: 100,
            CoveredTileCode: null, AboardFleetId: null,
            Units: ValueList.From(new[]
            {
                new UnitSlot(0, "light_infantry", 100, 6, "T113 seed unit 0"),
                new UnitSlot(0, "light_infantry", 100, 6, "T113 seed unit 1"),
            }));

        // R5's 19-unit army: positioned one tile from a separate offer city at (3,5). Its first
        // hire brings it to 20, the dialog closes.
        var fullArmy = new ArmyState(
            FullArmyId, RomeId, X: 3, Y: 5, Moves: 4, Morale: 70, Money: 100, SupplyTons: 100,
            CoveredTileCode: null, AboardFleetId: null,
            Units: ValueList.From(Enumerable.Range(0, 19).Select(i =>
                new UnitSlot(0, "light_infantry", 100, 6, $"T113 full unit {i}"))));

        var twentyUnitArmy = new ArmyState(
            TwentyUnitArmyId, RomeId, X: 4, Y: 4, Moves: 4, Morale: 70, Money: 100, SupplyTons: 100,
            CoveredTileCode: null, AboardFleetId: null,
            Units: ValueList.From(Enumerable.Range(0, 20).Select(i =>
                new UnitSlot(0, "light_infantry", 100, 6, $"T113 twenty unit {i}"))));

        var offerCity = new CityState(
            CityId, "T113 offer city", X: OfferCityX, Y: OfferCityY, Owner: RomeId, Allegiance: RomeId,
            Loyalty: 90, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);
        var emptyCity = new CityState(
            EmptyCityId, "T113 empty city", X: EmptyCityX, Y: EmptyCityY, Owner: RomeId, Allegiance: RomeId,
            Loyalty: 90, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);

        // R5: a second offer city for the 19-unit army, also Rome-owned, with one HC offer.
        var fullArmyOfferCity = new CityState(
            "t113-full-offer-city", "T113 full offer city", X: 4, Y: 5, Owner: RomeId, Allegiance: RomeId,
            Loyalty: 90, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);

        var offers = new List<MercenaryPoolSlot>
        {
            // The first hire's priced offer (gate 27, displayed 34).
            new(SlotHeavyCavalry, X: OfferCityX, Y: OfferCityY, NameLabel: 0, UnitTypeId: "heavy_cavalry", Troops: 960, Quality: 9),
            // A second priced offer so the dialog can refuse a second hire without emptying the pool.
            new(SlotLightInfantry, X: OfferCityX, Y: OfferCityY, NameLabel: 0, UnitTypeId: "light_infantry", Troops: 3_868, Quality: 8),
            // The hired sentinel — its X/Y are intact, its troops are 0xFFFF.
            new(HiredSlot, X: OfferCityX, Y: OfferCityY, NameLabel: 0, UnitTypeId: "heavy_infantry",
                Troops: MercenaryDialogModel.HiredSlotSentinelTroops, Quality: 5),
            // R5: an offer for the 19-unit army on its own offer city at (4,5); troops small enough
            // to fit, gate (1 * 4 / 1000) * 9 = 0 * 9 = 0 so it passes the purse gate too.
            new(20, X: 4, Y: 5, NameLabel: 0, UnitTypeId: "heavy_cavalry", Troops: 1, Quality: 9),
        };

        var initial = new GameSession(
            resolved.World, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: RomeId);

        var state = initial.State with
        {
            Armies = ValueList.From(initial.State.Armies.Concat(new[] { army, fullArmy, twentyUnitArmy })),
            Cities = ValueList.From(initial.State.Cities.Concat(new[] { offerCity, emptyCity, fullArmyOfferCity })),
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
}