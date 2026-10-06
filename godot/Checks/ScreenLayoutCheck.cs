using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T147 (bug #781) Done-when 1 and 2, as one headless check: the map fills the game screen's own area,
/// and every window opens centred on it — the overlay's rectangle equals the game screen's, its panel's
/// centre is within 2 px of the game screen's centre, and the panel never overlaps the menu bar. Run
/// headless via:
/// <code>
/// godot --headless --path godot res://Checks/ScreenLayoutCheck.tscn --quit-after 600
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <strong>Done-when 1.</strong> At 1500&#160;×&#160;850, at 2560&#160;×&#160;1351, and after a resize
/// from the first to the second, the default fit covers the map control: the terrain's drawn rectangle
/// contains <see cref="GameMapView"/>'s whole rectangle, so no background shows. A second resize, after a
/// scripted pan, keeps the view's tile centre within one map cell.
/// </para>
/// <para>
/// <strong>Done-when 2.</strong> At both sizes, the Battle Result, Offer of peace, Game End, Hotseat
/// handoff and Diplomacy screens and the Find city, Supply and Save-As dialogs are opened through the
/// app's own openers. Each is read <em>immediately</em> after the opener returns — the frame it opens —
/// which is where a container's deferred layout pass used to leave the panel at (0,0); the geometry is
/// therefore pinned to the same frame the player sees it. This check builds and disposes one real
/// <see cref="MainGameScreen"/> per case, exactly as the other Godot checks do.
/// </para>
/// <para>
/// This check's own root <see cref="Control"/> keeps its default (all-zero, equal) anchors and sets
/// <see cref="Control.Size"/> directly, the same convention
/// <c>godot/Checks/CommandFeedbackCheck.cs</c> and <c>godot/Checks/MapClipCheck.cs</c> use; the main game
/// screen is then a full-rect child, so its own layout runs against a real size. A few settle frames
/// separate the steps that need one (a build, a resize) from the reads, but the overlay reads inside
/// <see cref="OpenAndAssert"/> are deliberately immediate.
/// </para>
/// </remarks>
public partial class ScreenLayoutCheck : Control
{
    private const int SettleFrames = 6;

    private static readonly Vector2 SmallSize = new(1500, 850);
    private static readonly Vector2 LargeSize = new(2560, 1351);

    private readonly List<Action> _steps = new();

    private bool _ok = true;
    private int _frame;
    private int _stepIndex;
    private MainGameScreen? _mainGame;
    private Vector2 _centreBeforeResize;

    public override void _Ready()
    {
        Size = SmallSize;

        // ---- Done-when 1: the map fills its area ----
        _steps.Add(BuildMapGame);
        _steps.Add(() => AssertMapCoversScreen("at 1500x850"));
        _steps.Add(() => Size = LargeSize);
        _steps.Add(() => AssertMapCoversScreen("at 2560x1351"));
        _steps.Add(PanMap);
        _steps.Add(() => Size = SmallSize);
        _steps.Add(AssertViewCentreKept);
        _steps.Add(Teardown);

        // ---- Done-when 2: every window opens centred, at both sizes ----
        foreach (var size in new[] { SmallSize, LargeSize })
        {
            AddOverlayCases(size);
        }

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
            GD.PrintErr($"ScreenLayoutCheck: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    // ---- Done-when 1 ----

    private void BuildMapGame()
    {
        Size = SmallSize;
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        _mainGame = new MainGameScreen
        {
            Session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
    }

    private void AssertMapCoversScreen(string what, bool expectCoverFit = true)
    {
        if (_mainGame is null)
        {
            Check(false, $"a game screen exists (map {what})");
            return;
        }

        var map = _mainGame.MapView;
        var mapRect = new Rect2(Vector2.Zero, map.Size);
        var terrain = map.TerrainDrawRectForCheck;

        Check(
            map.Size.X > 0 && map.Size.Y > 0,
            $"{what}: the map control has a real size ({map.Size})");
        Check(
            terrain.Encloses(mapRect),
            $"{what}: the map's drawn rectangle {terrain} contains the control's whole rect {mapRect} "
            + $"(no background visible)");

        if (!expectCoverFit)
        {
            return;
        }

        // The fit is the cover fit (the larger ratio), not the old letterbox (the smaller one).
        var mapSize = terrain.Size / map.ZoomFactor;
        var coverZoom = Mathf.Max(map.Size.X / mapSize.X, map.Size.Y / mapSize.Y);
        Check(
            Mathf.Abs(map.ZoomFactor - coverZoom) < 0.01f,
            $"{what}: the fit is the cover zoom ({map.ZoomFactor} vs {coverZoom})");
    }

    private void PanMap()
    {
        if (_mainGame is null)
        {
            return;
        }

        var map = _mainGame.MapView;
        var start = map.Size / 2f;
        var end = start + new Vector2(-120, 0);
        map._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = start,
        });
        map._GuiInput(new InputEventMouseMotion { Position = end, Relative = new Vector2(-120, 0) });
        map._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false, Position = end,
        });

        _centreBeforeResize = map.VisibleTileRect.GetCenter();
        Check(
            !string.IsNullOrWhiteSpace($"{_centreBeforeResize}"),
            $"after the scripted pan the view centre is {_centreBeforeResize}");
    }

    private void AssertViewCentreKept()
    {
        if (_mainGame is null)
        {
            return;
        }

        var map = _mainGame.MapView;
        var after = map.VisibleTileRect.GetCenter();
        var delta = (after - _centreBeforeResize).Abs();
        Check(
            delta.X <= 1f && delta.Y <= 1f,
            $"after a resize the panned view centre is kept within one map cell "
            + $"({after} vs {_centreBeforeResize})");
        AssertMapCoversScreen("after the panned resize", expectCoverFit: false);
    }

    // ---- Done-when 2 ----

    private void AddOverlayCases(Vector2 size)
    {
        foreach (var sizeCase in Cases())
        {
            var current = sizeCase;
            _steps.Add(() =>
            {
                Size = size;
                BuildOverlayGame(current.Session());
            });
            _steps.Add(() => OpenAndAssert(current, size));
        }
    }

    private void BuildOverlayGame(GameSession session)
    {
        Teardown();
        _mainGame = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
    }

    private void OpenAndAssert(OverlayCase overlayCase, Vector2 size)
    {
        if (_mainGame is null)
        {
            Check(false, $"{overlayCase.Name} at {size}: a game screen exists");
            return;
        }

        var mainGame = _mainGame;
        overlayCase.Open(mainGame);

        var overlay = mainGame.ActiveOverlay;
        if (overlay is null)
        {
            Check(false, $"{overlayCase.Name} at {size}: the opener opened a window");
            return;
        }

        Check(
            overlayCase.Matches(overlay),
            $"{overlayCase.Name} at {size}: the opener opened the expected window "
            + $"(got {overlay.GetType().Name})");

        var gameRect = mainGame.GetGlobalRect();
        var overlayRect = overlay.GetGlobalRect();
        Check(
            Mathf.Abs(overlayRect.Position.X - gameRect.Position.X) <= 0.5f
            && Mathf.Abs(overlayRect.Position.Y - gameRect.Position.Y) <= 0.5f
            && Mathf.Abs(overlayRect.Size.X - gameRect.Size.X) <= 0.5f
            && Mathf.Abs(overlayRect.Size.Y - gameRect.Size.Y) <= 0.5f,
            $"{overlayCase.Name} at {size}: the overlay's rectangle {overlayRect} equals the game "
            + $"screen's {gameRect}");

        var panel = FindFirst<PanelContainer>(overlay);
        if (panel is null)
        {
            Check(false, $"{overlayCase.Name} at {size}: the window has a panel");
            return;
        }

        var panelRect = panel.GetGlobalRect();
        var panelCentre = panelRect.GetCenter();
        var screenCentre = gameRect.GetCenter();
        Check(
            Mathf.Abs(panelCentre.X - screenCentre.X) <= 2f
            && Mathf.Abs(panelCentre.Y - screenCentre.Y) <= 2f,
            $"{overlayCase.Name} at {size}: the panel centre {panelCentre} is within 2 px of the game "
            + $"screen's centre {screenCentre} (immediate, the frame it opened)");

        var menuRect = mainGame.MenuBar.GetGlobalRect();
        Check(
            !panelRect.Intersects(menuRect),
            $"{overlayCase.Name} at {size}: the panel {panelRect} does not overlap the menu bar "
            + $"{menuRect}");
    }

    private void Teardown()
    {
        if (_mainGame is null)
        {
            return;
        }

        RemoveChild(_mainGame);
        _mainGame.QueueFree();
        _mainGame = null;
    }

    private void Finish()
    {
        GD.Print($"ScreenLayoutCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    private sealed record OverlayCase(
        string Name,
        Func<GameSession> Session,
        Action<MainGameScreen> Open,
        Func<Control, bool> Matches);

    private static IEnumerable<OverlayCase> Cases()
    {
        yield return new OverlayCase(
            "battle result",
            BattleReadySession,
            screen => screen.SubmitForCheck("attack-army north-army-1 south-army-1"),
            overlay => overlay is BattleResultScreen);

        yield return new OverlayCase(
            "offer of peace",
            HumanWinsSession,
            screen =>
            {
                screen.SubmitForCheck("attack-army army-0 army-2");
                (screen.ActiveOverlay as BattleResultScreen)?.Close();
            },
            overlay => overlay is PeaceOfferScreen);

        yield return new OverlayCase(
            "game end",
            AllCitiesSession,
            screen => screen.SubmitForCheck("end"),
            overlay => overlay is GameEndScreen);

        yield return new OverlayCase(
            "hotseat handoff",
            HotseatSession,
            screen => screen.SubmitForCheck("end"),
            overlay => overlay is HotseatHandoffScreen);

        yield return new OverlayCase(
            "diplomacy",
            ToySession,
            screen => screen.OpenDiplomacyScreen(),
            overlay => overlay is DiplomacyScreen);

        yield return new OverlayCase(
            "find city",
            ToySession,
            screen => screen.OpenFindCityDialog(),
            overlay => overlay is FindCityDialog);

        yield return new OverlayCase(
            "supply",
            ToySession,
            screen =>
            {
                screen.SelectArmyForCheck("north-army-1");
                screen.CommandTable.TryInvoke("unit_map.army_supply");
            },
            overlay => overlay is SupplyDialog);

        yield return new OverlayCase(
            "save as",
            ToySession,
            screen => screen.CommandTable.TryInvoke("file.save_as"),
            overlay => overlay is Control && overlay.Name == "SaveAsPrompt");
    }

    // ---- fixtures ----

    private static GameSession ToySession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        return new GameSession(toy.World, toy.Ruleset, toy.Scenario);
    }

    /// <summary>toy-3city with south-army-1 brought next to north-army-1, so the attack resolves.</summary>
    private static GameSession BattleReadySession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with { X = 4, Y = 2 };
        var world = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a)),
        };
        return new GameSession(world, toy.Ruleset, toy.Scenario);
    }

    /// <summary>toy-3city with south switched to human, so ending north's turn hands off to it.</summary>
    private static GameSession HotseatSession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var scenario = toy.Scenario with
        {
            Seats = ValueList.From(toy.Scenario.Seats.Select(
                seat => seat with { Control = seat.Nation == "south" ? SeatControl.Human : seat.Control })),
        };
        return new GameSession(toy.World, toy.Ruleset, scenario);
    }

    /// <summary>toy-3city with Meridia moved to north, so the human seat holds every city and falls.</summary>
    private static GameSession AllCitiesSession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var world = toy.World with
        {
            Cities = ValueList.From(toy.World.Cities.Select(c => c with { Owner = "north" })),
        };
        return new GameSession(world, toy.Ruleset, toy.Scenario);
    }

    // ---- the offer-of-peace fixture, the same shape PeaceOfferCheck uses ----

    private static (ResolvedScenarioHolder Resolved, Ruleset Ruleset) LoadOfferFixture()
    {
        var resolved = GameDataContext.Repository.Resolve("example-classical-improved");
        var ruleset = resolved.Ruleset with
        {
            Combat = resolved.Ruleset.Combat with
            {
                AutoPeaceChanceNumerator = resolved.Ruleset.Combat.AutoPeaceChanceDenominator,
                AutoPeaceLoserUnityThreshold = -1,
                AutoPeaceLoserCityThreshold = 0,
            },
        };
        return (new ResolvedScenarioHolder(resolved.World, resolved.Scenario), ruleset);
    }

    private static GameSession HumanWinsSession()
    {
        var (resolved, ruleset) = LoadOfferFixture();
        const int pocketX = 67;
        const int pocketY = 4;
        var war = ruleset.Diplomacy.StateCodes.War;
        var scenario = resolved.Scenario with
        {
            Seats = ValueList.From(resolved.Scenario.Seats.Select(
                seat => seat with { Control = seat.Nation == "rome" ? SeatControl.Human : SeatControl.Ai })),
        };
        var armies = resolved.World.StartingArmies
            .Select(a => a.Id switch
            {
                "army-0" => Army(a, pocketX, pocketY, 80, Units(20000, 6)),
                "army-2" => Army(a, pocketX + 1, pocketY, 40, Units(3000, 4)),
                _ => a,
            })
            .Append(Extra("check-carthage-1", "carthage", pocketX + 5, pocketY + 6))
            .Append(Extra("check-carthage-2", "carthage", pocketX + 6, pocketY + 6));
        var world = resolved.World with
        {
            StartingRelations = resolved.World.StartingRelations!.WithRelation("rome", "carthage", war),
            StartingArmies = ValueList.From(armies),
        };
        return new GameSession(world, ruleset, scenario, seedOverride: 20261005UL);
    }

    private static ValueList<UnitSlot> Units(int troops, int quality) =>
        ValueList.Of(new UnitSlot(0, "light_infantry", Troops: troops, Quality: quality, Name: "Check Warband"));

    private static StartingArmy Army(StartingArmy army, int x, int y, int morale, ValueList<UnitSlot> units) =>
        army with { X = x, Y = y, Morale = morale, Units = units };

    private static StartingArmy Extra(string id, string nation, int x, int y) =>
        new(id, nation, X: x, Y: y, Morale: 60, Money: 0, SupplyTons: 0, Moves: 8, Units: Units(150000, 6));

    private sealed record ResolvedScenarioHolder(World World, Scenario Scenario);

    // ---- helpers ----

    private static T? FindFirst<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match)
            {
                return match;
            }

            if (FindFirst<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
