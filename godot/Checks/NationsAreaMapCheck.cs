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
            (BetweenStepsFrames, ChooseAllNations),
            (BetweenStepsFrames, AssertAllNationsNoPanel),
            (BetweenStepsFrames, ResetToRomeAndShowArmies),
            (BetweenStepsFrames, AssertRomeHighlightSet),
            (BetweenStepsFrames, ChooseAllNationsForHighlights),
            (BetweenStepsFrames, AssertAllNationsHighlightSet),
            (BetweenStepsFrames, ResetToRomeForFindCity),
            (BetweenStepsFrames, OpenFindCityAndChooseCapital),
            (BetweenStepsFrames, AssertFindCityCentredAndHighlighted),
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
    }

    private void ResetToRomeFromMenu()
    {
        _mainGame.MenuBar.PressItemForCheck("nations.rome");
        Check(
            string.Equals(_mainGame.ViewedNationId, RomeId, StringComparison.Ordinal),
            "the Nations menu can set the viewed nation back to Rome");
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

        Check(
            distance <= 1,
            $"Find a city centres the order map on Carthage's capital within one tile "
            + $"(centre ({centreX},{centreY}), capital ({_carthageCapital.X},{_carthageCapital.Y}), "
            + $"Chebyshev {distance})");
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
            _commandsSeen == 0,
            $"the whole check issued no session command ({_commandsSeen})");
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
