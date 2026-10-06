using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T110's headless check (docs/tasks/T110.md, Done-when 3, 4, 5 and 7): it builds the <em>real</em>
/// <see cref="MainGameScreen"/> and drives the real Nations menu, nation swatches, Area-map Show entries
/// and Find a city dialog. It asserts:
/// <list type="bullet">
/// <item>Done-when 3 — choosing Carthage from the Nations menu shows Carthage's status panel and presses
/// Carthage's swatch; choosing it from a real-input swatch click does the same; All nations shows no
/// status panel;</item>
/// <item>Done-when 4 — with Rome viewed and Show armies on, the mini-map's highlight set equals
/// <see cref="AreaMapHighlights.TilesFor"/> for Rome, and after All nations it equals the all-nations
/// set;</item>
/// <item>Done-when 5 — Find a city, with Carthage and its capital chosen, centres the order map within
/// one tile of that city and puts its tile in the highlight set;</item>
/// <item>Done-when 7 — no Nations or Area map entry submits a session command.</item>
/// </list>
/// Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/NationsAreaMapCheck.tscn --quit-after 900
/// </code>
/// </summary>
/// <remarks>
/// Same conventions as <c>godot/Checks/MenuBarCheck.cs</c> and <c>godot/Checks/AreaMapCheck.cs</c>: the
/// real control tree, the real table handlers and the ordered settle-frame plan, never a hand-built
/// mirror. The real-input swatch click uses <see cref="Viewport.PushInput"/> with
/// <c>in_local_coords: true</c> (without it a headless click lands nowhere) and is paired with a
/// positive control — the same click on Rome's swatch must set Rome viewed — so the Carthage assertion
/// cannot pass vacuously.
/// </remarks>
public partial class NationsAreaMapCheck : Control
{
    private const int InitialSettleFrames = 6;
    private const int BetweenStepsFrames = 4;
    private const string RomeId = "rome";
    private const string CarthageId = "carthage";

    private MainGameScreen _mainGame = null!;

    private bool _ok = true;
    private int _frame;
    private int _planIndex;
    private int _commandsSeen;

    private readonly List<(int WaitFrames, Action Run)> _plan = new();

    private CityState _carthageCapital = null!;

    /// <summary>The turn commands this check itself issues (the two "end"s of B1 and B2); the final
    /// Done-when 7 step asserts that no <em>other</em> command reached the session.</summary>
    private int _expectedSessionCommands;

    /// <summary>The mini-map's draw count when the B1 scenario was arranged, compared after "end".</summary>
    private int _drawCountBeforeEnd;

    /// <summary>The table's handler counter before the strip button click (N5).</summary>
    private int _stripIssuedBefore;

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

        // Done-when 7: every command that reaches the screen's own CommandIssued path counts here. The
        // whole check must leave this at zero.
        _mainGame.CommandIssued += _ => _commandsSeen++;

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
            GD.PrintErr($"NationsAreaMapCheck: unhandled exception: {ex}");
            Finish();
        }
    }

    private void BuildPlan()
    {
        _plan.AddRange(new (int WaitFrames, Action Run)[]
        {
            (InitialSettleFrames, CheckInitialView),
            (BetweenStepsFrames, ChooseCarthageFromMenu),
            (BetweenStepsFrames, ResetToRomeFromMenu),
            (BetweenStepsFrames, ClickRomeSwatchRealInput),
            (BetweenStepsFrames, AssertRomeSwatchClickLanded),
            (BetweenStepsFrames, ClickCarthageSwatchRealInput),
            (BetweenStepsFrames, AssertCarthageFromSwatch),
            (BetweenStepsFrames, ClickAreaMapStripCitiesRealInput),
            (BetweenStepsFrames, AssertAreaMapStripButtonRanItsRow),
            (BetweenStepsFrames, ChooseAllNations),
            (BetweenStepsFrames, AssertAllNationsNoPanel),
            (BetweenStepsFrames, ResetToRomeAndShowArmies),
            (BetweenStepsFrames, AssertRomeHighlightSet),
            (BetweenStepsFrames, ChooseAllNationsForHighlights),
            (BetweenStepsFrames, AssertAllNationsHighlightSet),
            (BetweenStepsFrames, PrepareShowAllCheck),
            (BetweenStepsFrames, PressShowAllMenu),
            (BetweenStepsFrames, AssertShowAllUnion),
            (BetweenStepsFrames, ResetToRomeForFindCity),
            (BetweenStepsFrames, OpenFindCityAndChooseCapital),
            (BetweenStepsFrames, AssertFindCityCentredAndHighlighted),
            (BetweenStepsFrames, AssertFindCityHighlightClearedOnNationChange),
            (8, PrepareHighlightRefreshAfterEnd),
            (12, IssueEndTurnForHighlightRefresh),
            (8, AssertHighlightRefreshAfterEnd),
            (BetweenStepsFrames, ViewCarthageBeforeTurnReset),
            (8, EndTurnAndAssertViewedNationReset),
            (BetweenStepsFrames, AssertNoEntrySubmittedACommand),
        });
    }

    // ---- Done-when 3 ----

    private void CheckInitialView()
    {
        _carthageCapital = _mainGame.Session.State.Cities.Single(
            city => string.Equals(city.Owner, CarthageId, StringComparison.Ordinal)
                && string.Equals(city.Id, _mainGame.Session.State.NationById(CarthageId)!.CapitalCityId, StringComparison.Ordinal));

        Check(
            string.Equals(_mainGame.ViewedNationId, RomeId, StringComparison.Ordinal),
            $"the viewed nation starts as the active seat's own (got '{_mainGame.ViewedNationId ?? "<all>"}')");
        Check(
            string.Equals(_mainGame.ContextPanel.StatusNationIdForCheck, RomeId, StringComparison.Ordinal),
            $"the context panel starts on Rome's status panel (got "
            + $"'{_mainGame.ContextPanel.StatusNationIdForCheck ?? "<none>"}')");
    }

    private void ChooseCarthageFromMenu()
    {
        Check(
            _mainGame.MenuBar.PressItemForCheck("nations.carthage"),
            "the Nations menu's Carthage entry is enabled and takes a press");
        Check(
            string.Equals(_mainGame.ViewedNationId, CarthageId, StringComparison.Ordinal),
            $"choosing Carthage from the menu makes it the viewed nation (got '{_mainGame.ViewedNationId ?? "<all>"}')");
        Check(
            string.Equals(_mainGame.ContextPanel.StatusNationIdForCheck, CarthageId, StringComparison.Ordinal),
            $"Carthage's status panel is shown (got '{_mainGame.ContextPanel.StatusNationIdForCheck ?? "<none>"}')");
        Check(
            _mainGame.Toolbar.ButtonFor("nations.carthage") is { ButtonPressed: true },
            "Carthage's toolbar swatch is pressed after the menu choice");
        Check(
            _mainGame.Toolbar.ButtonFor("nations.rome") is { ButtonPressed: false },
            "Rome's swatch is no longer pressed (the swatches are a radio group)");

        // B3: the menu items must be radio-checkable, or SetItemChecked draws no mark at all.
        var popup = _mainGame.MenuBar.MenuForCheck("Nations")!;
        Check(
            popup.ItemCount == 17,
            $"the Nations menu carries all 17 items, 16 nations plus All nations (got {popup.ItemCount})");
        Check(
            Enumerable.Range(0, popup.ItemCount).All(popup.IsItemRadioCheckable),
            "every Nations item is radio-checkable, so the viewed nation's mark is actually drawn");
        Check(
            popup.IsItemChecked(CaptionIndex(popup, "Carthage")),
            "Carthage's Nations menu mark is checked while Carthage is viewed");
        Check(
            !popup.IsItemChecked(CaptionIndex(popup, "Rome")),
            "Rome's Nations menu mark is unchecked while Carthage is viewed");

        // N4: a pressed swatch must be visibly different from an unpressed one.
        AssertPressedSwatchStyleDiffers();

        // B4: Carthage is foreign to the active seat (Rome), so its panel is public facts only.
        AssertCarthagePanelIsPublicOnly();
    }

    private void ResetToRomeFromMenu()
    {
        Check(
            _mainGame.MenuBar.PressItemForCheck("nations.rome"),
            "the Nations menu can set the viewed nation back to Rome");
        var popup = _mainGame.MenuBar.MenuForCheck("Nations")!;
        Check(
            popup.IsItemChecked(CaptionIndex(popup, "Rome")),
            "Rome's Nations menu mark is checked after choosing it");
        Check(
            !popup.IsItemChecked(CaptionIndex(popup, "Carthage")),
            "Carthage's Nations menu mark cleared");

        // B4: Rome is the active seat's own nation, so its panel carries the full list.
        AssertRomePanelIsFull();
    }

    // ---- B3, N4, B4 helpers ----

    private static int CaptionIndex(PopupMenu popup, string caption)
    {
        for (var i = 0; i < popup.ItemCount; i++)
        {
            if (string.Equals(popup.GetItemText(i), caption, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void AssertPressedSwatchStyleDiffers()
    {
        var swatch = _mainGame.Toolbar.ButtonFor("nations.carthage")!;
        var normal = swatch.GetThemeStylebox("normal");
        var pressed = swatch.GetThemeStylebox("pressed");
        if (normal is StyleBoxFlat normalFlat && pressed is StyleBoxFlat pressedFlat)
        {
            Check(
                normalFlat.BorderWidthTop != pressedFlat.BorderWidthTop
                    || normalFlat.BorderColor != pressedFlat.BorderColor,
                "a pressed nation swatch's border differs from an unpressed one's");
        }
        else
        {
            Check(false, "a nation swatch carries flat normal and pressed styleboxes");
        }
    }

    private void AssertCarthagePanelIsPublicOnly()
    {
        var panel = _mainGame.ContextPanel;
        foreach (var shown in new[]
        {
            NationStatusModel.LeaderKey,
            NationStatusModel.CapitalKey,
            NationStatusModel.CitiesKey,
            NationStatusModel.CityNamesKey,
            NationStatusModel.RelationKeyPrefix + RomeId,
        })
        {
            Check(panel.HasViewedNationLineForCheck(shown), $"Carthage's foreign panel shows '{shown}'");
        }

        foreach (var withheld in new[]
        {
            NationStatusModel.TreasuryKey,
            NationStatusModel.TaxRateKey,
            NationStatusModel.PopulationKey,
            NationStatusModel.UnityKey,
            NationStatusModel.MobilizedKey,
            NationStatusModel.TrainingHeaderKey,
            NationStatusModel.TrainingNoneKey,
        })
        {
            Check(!panel.HasViewedNationLineForCheck(withheld), $"Carthage's foreign panel withholds '{withheld}'");
        }

        Check(
            panel.ViewedNationLinesForCheck?.Any(line =>
                line.Key.StartsWith(NationStatusModel.TrainingKeyPrefix, StringComparison.Ordinal)) != true,
            "Carthage's foreign panel withholds units in training");
    }

    private void AssertRomePanelIsFull()
    {
        var panel = _mainGame.ContextPanel;
        foreach (var shown in new[]
        {
            NationStatusModel.LeaderKey,
            NationStatusModel.CapitalKey,
            NationStatusModel.CitiesKey,
            NationStatusModel.PopulationKey,
            NationStatusModel.UnityKey,
            NationStatusModel.TaxRateKey,
            NationStatusModel.MobilizedKey,
            NationStatusModel.TreasuryKey,
        })
        {
            Check(panel.HasViewedNationLineForCheck(shown), $"Rome's own panel shows '{shown}'");
        }
    }

    // ---- Done-when 3, the real-input swatch click ----

    private void ClickRomeSwatchRealInput()
    {
        // The positive control: a real click on Rome's swatch must set Rome viewed. The previous step
        // already put Rome there, so first move away, or the assertion below would be vacuous.
        _mainGame.MenuBar.PressItemForCheck("nations.carthage");
        ClickControl(_mainGame.Toolbar.ButtonFor("nations.rome")!);
    }

    private void AssertRomeSwatchClickLanded()
    {
        Check(
            string.Equals(_mainGame.ViewedNationId, RomeId, StringComparison.Ordinal),
            $"a real-input click on Rome's swatch sets it viewed (got '{_mainGame.ViewedNationId ?? "<all>"}')");
    }

    private void ClickCarthageSwatchRealInput() =>
        ClickControl(_mainGame.Toolbar.ButtonFor("nations.carthage")!);

    private void AssertCarthageFromSwatch()
    {
        Check(
            string.Equals(_mainGame.ViewedNationId, CarthageId, StringComparison.Ordinal),
            $"a real-input click on Carthage's swatch makes it the viewed nation (got '{_mainGame.ViewedNationId ?? "<all>"}')");
        Check(
            string.Equals(_mainGame.ContextPanel.StatusNationIdForCheck, CarthageId, StringComparison.Ordinal),
            $"the swatch click shows Carthage's status panel (got '{_mainGame.ContextPanel.StatusNationIdForCheck ?? "<none>"}')");
        Check(
            _mainGame.Toolbar.ButtonFor("nations.carthage") is { ButtonPressed: true },
            "Carthage's swatch is pressed after the click");
    }

    // ---- N5: the Area-map strip's real buttons ----

    private void ClickAreaMapStripCitiesRealInput()
    {
        // Start with the layer off, so the click is what turns it on.
        _mainGame.AreaMapView.SetHighlight(AreaMapHighlightKind.Cities, false);
        _stripIssuedBefore = _mainGame.CommandTable.IssuedCount;

        var button = _mainGame.AreaMapView.StripButtonForCheck("area_map.show_cities");
        Check(button is not null, "the Area-map strip carries the Show cities button");
        if (button is not null)
        {
            ClickControl(button);
        }
    }

    private void AssertAreaMapStripButtonRanItsRow()
    {
        Check(
            _mainGame.CommandTable.IssuedCount > _stripIssuedBefore,
            $"the strip button really ran its table row (handlers {_stripIssuedBefore} -> "
            + $"{_mainGame.CommandTable.IssuedCount})");
        Check(
            _mainGame.AreaMapView.ActiveHighlightsForCheck.Contains(AreaMapHighlightKind.Cities),
            "the Show cities strip button turned its highlight on");
        Check(
            _mainGame.AreaMapView.StripButtonForCheck("area_map.show_cities") is { ButtonPressed: true },
            "the Show cities strip button shows as pressed after the click");
    }

    private void ChooseAllNations() => _mainGame.MenuBar.PressItemForCheck("nations.all");

    private void AssertAllNationsNoPanel()
    {
        Check(
            _mainGame.ViewedNationId is null,
            $"All nations makes the viewed nation null (got '{_mainGame.ViewedNationId ?? "<all>"}')");
        Check(
            _mainGame.ContextPanel.ShowsAllNationsForCheck,
            "All nations shows the All nations view, not a status panel");
        Check(
            _mainGame.ContextPanel.StatusNationIdForCheck is null,
            "no nation's status panel is shown under All nations");
        Check(
            _mainGame.Toolbar.ButtonFor("nations.all") is { ButtonPressed: true },
            "the All nations swatch is pressed");
        Check(
            _mainGame.Toolbar.ButtonFor("nations.carthage") is { ButtonPressed: false },
            "no nation swatch stays pressed under All nations");
    }

    // ---- Done-when 4 ----

    private void ResetToRomeAndShowArmies()
    {
        _mainGame.MenuBar.PressItemForCheck("nations.rome");

        // Isolate the one layer this check asserts: turn every Show layer off through the view's own
        // API, then turn Show armies on through the table (the entry's real path).
        foreach (var kind in Enum.GetValues<AreaMapHighlightKind>())
        {
            _mainGame.AreaMapView.SetHighlight(kind, false);
        }

        Check(
            _mainGame.MenuBar.PressItemForCheck("area_map.show_armies"),
            "the Area map's Show armies entry is enabled and takes a press");
    }

    private void AssertRomeHighlightSet()
    {
        var expected = AreaMapHighlights.TilesFor(
            _mainGame.Session.State, AreaMapHighlightKind.Armies, RomeId);
        var actual = _mainGame.AreaMapView.HighlightTilesForCheck;

        Check(
            _mainGame.AreaMapView.ActiveHighlightsForCheck.Contains(AreaMapHighlightKind.Armies),
            "Show armies turned its highlight layer on");
        Check(
            expected.Count > 0,
            $"Rome has armies to mark, so the equality below is not vacuous (got {expected.Count})");
        Check(
            actual.Count == expected.Count && expected.All(actual.Contains),
            $"with Rome viewed and Show armies on, the mini-map's highlight set equals AreaMapHighlights "
            + $"for Rome (mini {actual.Count} tiles, expected {expected.Count})");
    }

    private void ChooseAllNationsForHighlights() => _mainGame.MenuBar.PressItemForCheck("nations.all");

    private void AssertAllNationsHighlightSet()
    {
        var expected = AreaMapHighlights.TilesFor(
            _mainGame.Session.State, AreaMapHighlightKind.Armies, viewedNationId: null);
        var romeOnly = AreaMapHighlights.TilesFor(
            _mainGame.Session.State, AreaMapHighlightKind.Armies, RomeId);
        var actual = _mainGame.AreaMapView.HighlightTilesForCheck;

        Check(
            actual.Count == expected.Count && expected.All(actual.Contains),
            $"after All nations, the highlight set equals the all-nations set "
            + $"(mini {actual.Count} tiles, expected {expected.Count})");
        Check(
            actual.Count > romeOnly.Count,
            $"the all-nations set is wider than Rome's own ({actual.Count} vs {romeOnly.Count})");
    }

    // ---- B7: the UI's Show all is the same union the unit test pins ----

    private void PrepareShowAllCheck()
    {
        _mainGame.MenuBar.PressItemForCheck("nations.rome");
        foreach (var kind in Enum.GetValues<AreaMapHighlightKind>())
        {
            _mainGame.AreaMapView.SetHighlight(kind, false);
        }
    }

    private void PressShowAllMenu() => Check(
        _mainGame.MenuBar.PressItemForCheck("area_map.show_all"),
        "the Area map's Show all entry is enabled and takes a press");

    private void AssertShowAllUnion()
    {
        var expected = AreaMapHighlights.AllTiles(_mainGame.Session.State, RomeId);
        var actual = _mainGame.AreaMapView.HighlightTilesForCheck;

        Check(
            Enum.GetValues<AreaMapHighlightKind>().All(_mainGame.AreaMapView.ActiveHighlightsForCheck.Contains),
            "the UI's Show all turned every one of the four layers on");
        Check(
            actual.Count == expected.Count && expected.All(actual.Contains),
            $"the UI's Show all equals AreaMapHighlights.AllTiles for Rome "
            + $"(mini {actual.Count} tiles, expected {expected.Count})");

        var popup = _mainGame.MenuBar.MenuForCheck("Area map")!;
        foreach (var caption in new[] { "Show cities", "Show capital", "Show armies", "Show fleets", "Show all" })
        {
            Check(
                popup.IsItemCheckable(CaptionIndex(popup, caption)),
                $"the Area map's '{caption}' item is checkable, so its mark is drawn");
        }

        Check(
            popup.IsItemChecked(CaptionIndex(popup, "Show all")),
            "Show all's menu mark follows the toggle");
    }

    // ---- N7: the Find a city highlight does not survive a nation change ----

    private void AssertFindCityHighlightClearedOnNationChange()
    {
        Check(
            _mainGame.AreaMapView.FindCityIdForCheck is not null,
            "the chosen city is highlighted before the viewed nation changes");
        _mainGame.MenuBar.PressItemForCheck("nations.carthage");
        Check(
            _mainGame.AreaMapView.FindCityIdForCheck is null,
            "changing the viewed nation clears the Find a city highlight");
    }

    // ---- B1: the highlights are redrawn from the live state after a command ----

    private void PrepareHighlightRefreshAfterEnd()
    {
        _mainGame.MenuBar.PressItemForCheck("nations.rome");
        foreach (var kind in Enum.GetValues<AreaMapHighlightKind>())
        {
            _mainGame.AreaMapView.SetHighlight(kind, false);
        }

        _mainGame.AreaMapView.SetFindCityHighlight(null);
        _mainGame.AreaMapView.SetHighlight(AreaMapHighlightKind.Armies, true);

        Check(
            string.Equals(_mainGame.ViewedNationId, RomeId, StringComparison.Ordinal),
            "B1's scenario views Rome before the command");
    }

    private void IssueEndTurnForHighlightRefresh()
    {
        // The scenario's own SetHighlight queued a redraw; the plan waits before this step so it has
        // settled, and only then is the draw count read. Otherwise the increase after "end" could be
        // that queued redraw rather than the command's own refresh, and the check would pass vacuously.
        _drawCountBeforeEnd = _mainGame.AreaMapView.DrawCountForCheck;
        _mainGame.SubmitForCheck("end");
        _expectedSessionCommands++;
    }

    private void AssertHighlightRefreshAfterEnd()
    {
        Check(
            _mainGame.AreaMapView.DrawCountForCheck > _drawCountBeforeEnd,
            $"the mini-map redrew after the command (draws {_drawCountBeforeEnd} -> "
            + $"{_mainGame.AreaMapView.DrawCountForCheck})");

        var live = _mainGame.AreaMapView.HighlightTilesForCheck;
        var drawn = _mainGame.AreaMapView.LastDrawnHighlightTilesForCheck;
        Check(
            drawn.Count == live.Count && live.All(drawn.Contains),
            $"the painted highlight set equals the live set after 'end' (drawn {drawn.Count}, live {live.Count})");
        Check(
            _mainGame.AreaMapView.ActiveHighlightsForCheck.Contains(AreaMapHighlightKind.Armies),
            "Show armies stayed on across the turn");
    }

    // ---- B2: every turn start re-views the active seat ----

    private void ViewCarthageBeforeTurnReset()
    {
        Check(
            _mainGame.MenuBar.PressItemForCheck("nations.carthage"),
            "Carthage can be viewed before the turn reset");
        Check(
            string.Equals(_mainGame.ViewedNationId, CarthageId, StringComparison.Ordinal),
            "Carthage is the viewed nation before the turn reset");
    }

    private void EndTurnAndAssertViewedNationReset()
    {
        var turnBefore = _mainGame.Session.State.Calendar.TurnIndex;
        _mainGame.SubmitForCheck("end");
        _expectedSessionCommands++;

        Check(
            _mainGame.Session.State.Calendar.TurnIndex > turnBefore,
            $"B2's command started a new turn ({turnBefore} -> {_mainGame.Session.State.Calendar.TurnIndex})");
        Check(
            string.Equals(_mainGame.ViewedNationId, RomeId, StringComparison.Ordinal),
            $"a turn start resets the viewed nation to the active seat's (got "
            + $"'{_mainGame.ViewedNationId ?? "<all>"}')");
        Check(
            string.Equals(_mainGame.ContextPanel.StatusNationIdForCheck, RomeId, StringComparison.Ordinal),
            $"the panel shows the active seat's status after the turn reset (got "
            + $"'{_mainGame.ContextPanel.StatusNationIdForCheck ?? "<none>"}')");
    }

    // ---- Done-when 5 ----

    private void ResetToRomeForFindCity() => _mainGame.MenuBar.PressItemForCheck("nations.rome");

    private void OpenFindCityAndChooseCapital()
    {
        var dialog = _mainGame.OpenFindCityDialog();
        Check(
            ReferenceEquals(_mainGame.ActiveOverlay, dialog),
            "Find a city opens its dialog as the modal overlay");
        dialog.SelectNationForCheck(CarthageId);
        dialog.SelectCityForCheck(_carthageCapital.Id);
        dialog.ChooseForCheck();
    }

    private void AssertFindCityCentredAndHighlighted()
    {
        Check(
            _mainGame.ActiveOverlay is null,
            "choosing a city closes the Find a city dialog");

        var visible = _mainGame.MapView.VisibleTileRect;
        var centreX = Mathf.FloorToInt(visible.Position.X + (visible.Size.X / 2f));
        var centreY = Mathf.FloorToInt(visible.Position.Y + (visible.Size.Y / 2f));
        var distance = Mathf.Max(Mathf.Abs(centreX - _carthageCapital.X), Mathf.Abs(centreY - _carthageCapital.Y));

        // The user's decision (issue #786): no background ever shows, so Find a city brings the tile as
        // close to the view's centre as the no-background clamp allows. The tile is always inside the
        // visible rect; on an axis with scroll room left the visible centre is within one tile of the
        // tile; on an axis the clamp pins, the view sits at the clamp's limit nearest the tile.
        var world = _mainGame.Session.World;
        var clamped = new Vector2(
            Mathf.Clamp((_carthageCapital.X + 0.5f) - (visible.Size.X / 2f), 0f, Mathf.Max(0f, world.Width - visible.Size.X)),
            Mathf.Clamp((_carthageCapital.Y + 0.5f) - (visible.Size.Y / 2f), 0f, Mathf.Max(0f, world.Height - visible.Size.Y)));
        var tileInsideView = visible.Position.X <= _carthageCapital.X
            && visible.Position.X + visible.Size.X >= _carthageCapital.X + 1f
            && visible.Position.Y <= _carthageCapital.Y
            && visible.Position.Y + visible.Size.Y >= _carthageCapital.Y + 1f;
        var noBackground = visible.Position.X >= -0.01f
            && visible.Position.Y >= -0.01f
            && visible.Position.X + visible.Size.X <= world.Width + 0.01f
            && visible.Position.Y + visible.Size.Y <= world.Height + 0.01f;
        var centred = distance <= 1;
        var pinnedAtClampLimit = Mathf.Abs(visible.Position.X - clamped.X) <= 0.01f
            && Mathf.Abs(visible.Position.Y - clamped.Y) <= 0.01f;

        Check(
            tileInsideView && (centred || (pinnedAtClampLimit && noBackground)),
            $"Find a city centres the order map on Carthage's capital as far as the no-background clamp "
            + $"allows (centre ({centreX},{centreY}), capital ({_carthageCapital.X},{_carthageCapital.Y}), "
            + $"Chebyshev {distance}"
            + (centred ? ", centred" : $", pinned at the clamp {visible.Position}, no background")
            + ")");
        Check(
            _mainGame.AreaMapView.HighlightTilesForCheck.Contains((_carthageCapital.X, _carthageCapital.Y)),
            "the chosen city's tile is in the mini-map's highlight set");
    }

    // ---- Done-when 7 ----

    private void AssertNoEntrySubmittedACommand()
    {
        var issuedBefore = _mainGame.CommandTable.IssuedCount;
        var commandsBefore = _commandsSeen;

        foreach (var row in GameCommandTable.Rows.Where(row =>
            row.Wired
            && (string.Equals(row.Menu, "Nations", StringComparison.Ordinal)
                || string.Equals(row.Menu, "Area map", StringComparison.Ordinal))))
        {
            _mainGame.CommandTable.TryInvoke(row.Id);

            // Find a city opened its dialog: close it without choosing, so the sweep leaves no overlay.
            if (_mainGame.ActiveOverlay is FindCityDialog dialog)
            {
                dialog.SelectNationForCheck(CarthageId);
                dialog.SelectCityForCheck(_carthageCapital.Id);
                dialog.ChooseForCheck();
            }
        }

        Check(
            _mainGame.CommandTable.IssuedCount > issuedBefore,
            "the Nations/Area map entries really ran (the handler counter moved)");
        Check(
            _commandsSeen == commandsBefore,
            $"no Nations or Area map entry submits a session command ({commandsBefore} -> {_commandsSeen})");
        Check(
            _commandsSeen == _expectedSessionCommands,
            $"only the check's own {_expectedSessionCommands} turn commands reached the session "
            + $"({_commandsSeen})");
    }

    /// <summary>
    /// Clicks <paramref name="control"/> with a real mouse press and release at its centre through
    /// <see cref="Viewport.PushInput"/>, in local coordinates — the same helper and reasoning as
    /// <c>MenuBarCheck.ClickControl</c> (without <c>true</c> a headless click lands nowhere).
    /// </summary>
    private void ClickControl(Control control)
    {
        var center = control.GlobalPosition + (control.Size / 2f);
        var viewport = GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = center, GlobalPosition = center }, true);
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = center,
            GlobalPosition = center,
            ButtonMask = MouseButtonMask.Left,
        }, true);
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = center,
            GlobalPosition = center,
        }, true);
    }

    private void Check(bool condition, string description)
    {
        _ok &= condition;
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
    }

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"NationsAreaMapCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }
}
