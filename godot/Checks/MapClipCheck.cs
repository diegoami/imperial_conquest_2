using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Bug #498 part 1's reproduction as a headless check: at the map's maximum zoom, the real
/// <see cref="GameMapView"/> must keep all of its drawing inside its own rect instead of spilling over
/// the sibling main toolbar (<c>Save</c> and <c>End turn</c>, moved there by T100) and the bottom
/// toolbar — which is what left the player unable to end a turn or save while zoomed in (bug #498). The check builds the real
/// <see cref="MainGameScreen"/> exactly as the app does, zooms through the view's own zoom path, and
/// asserts:
/// <list type="bullet">
/// <item>the map really reached <see cref="GameMapView.MaxZoom"/> and its terrain drawing really does
/// extend past its own rect at that zoom (the bug's precondition, so the clipping assertion below is
/// not vacuous);</item>
/// <item>the main toolbar's <c>Save</c> and <c>End turn</c> buttons, and the menu bar itself, stay
/// outside the map's rect, centre and bounds both;</item>
/// <item><see cref="Control.ClipContents"/> is set on the map, the mechanism that keeps every tile and
/// marker inside that rect;</item>
/// <item>Godot resolves <c>display/window/stretch/aspect</c> to <c>expand</c> and
/// <c>display/window/stretch/mode</c> to <c>canvas_items</c> (bug #498 part 2).</item>
/// </list>
/// Run headless via:
/// <code>
/// Godot_..._console.exe --headless --path godot res://Checks/MapClipCheck.tscn --quit-after 600
/// </code>
/// </summary>
/// <remarks>
/// Godot headless still builds a real <see cref="Control"/> tree and runs real layout code (see
/// <c>godot/Checks/ContextPanelWidthCheck.cs</c>'s own remarks) — only pixel readback needs a window, so
/// this check asserts the clipping mechanism and the layout geometry rather than reading a pixel. This
/// check's root <see cref="Control"/> keeps its default anchors and sets <see cref="Control.Size"/>
/// directly, the same reason <c>godot/Checks/ScreenshotTour.cs</c>'s remarks give; the two settle-frame
/// waits before anything is read are the same pattern the other checks use.
/// </remarks>
public partial class MapClipCheck : Control
{
    private const int SettleFrames = 6;
    private const float ZoomTolerance = 0.001f;

    // T100 moved the Save and End Turn buttons out of the top bar and into the main toolbar (the menu
    // bar and toolbar now sit above the map). This is the list of moved controls that must stay outside
    // the map's rect; the caption is "End turn" in the inventory's own casing.
    private static readonly string[] ToolbarButtonLabels = { "Save", "End turn" };

    private MainGameScreen _mainGame = null!;
    private int _frame;
    private int _step;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        // The shipped world the New Game flow's default card starts (RulesetPresets.ClassicalFaithful):
        // 320x140 tiles. At MaxZoom its terrain drawing is 23040x10080 px, far past the map control's own
        // rect, which is exactly the user's play case. The tiny toy world cannot reproduce it -- at 12x
        // its 8x6 tiles still fit inside the control, so the terrain spill the bug is about would not be
        // exercised at all.
        var scenario = GameDataContext.Repository.Resolve("classical-mediterranean");
        _mainGame = new MainGameScreen
        {
            Session = new GameSession(scenario.World, scenario.Ruleset, scenario.Scenario),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            switch (_step)
            {
                case 0 when _frame >= SettleFrames:
                    _mainGame.MapView.ZoomToMaxForCheck();
                    _frame = 0;
                    _step = 1;
                    break;

                case 1 when _frame >= SettleFrames:
                    var ok = RunChecks();
                    var exitCode = ok ? 0 : 1;
                    GD.Print($"MapClipCheck: exiting with code {exitCode}.");
                    GetTree().Quit(exitCode);
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MapClipCheck: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    private bool RunChecks()
    {
        var ok = true;
        var map = _mainGame.MapView;
        var mapRect = map.GetGlobalRect();

        ok &= Check(
            Mathf.Abs(map.ZoomFactor - GameMapView.MaxZoom) < ZoomTolerance,
            $"the map is zoomed to its maximum ({GameMapView.MaxZoom}x; got {map.ZoomFactor})");

        var terrainRect = map.TerrainDrawRectForCheck;
        ok &= Check(
            !mapRect.Encloses(terrainRect),
            $"the maximum-zoom terrain drawing really does extend past the map control's own rect "
            + $"(terrain {terrainRect} vs control {mapRect}) -- the bug's precondition");

        foreach (var label in ToolbarButtonLabels)
        {
            var button = FindButtons(_mainGame).FirstOrDefault(candidate => candidate.Text == label);
            if (!Check(button is not null, $"the main toolbar renders its '{label}' button"))
            {
                continue;
            }

            var rect = button!.GetGlobalRect();
            var centre = rect.GetCenter();
            ok &= Check(
                !mapRect.HasPoint(centre),
                $"the main toolbar's '{label}' button centre {centre} lies outside the map's rect {mapRect}");
            ok &= Check(
                !rect.Intersects(mapRect),
                $"the main toolbar's '{label}' button rect {rect} does not overlap the map's rect {mapRect}");
        }

        // Rework N10: the menu bar sits above the toolbar and the map, and must not overlap the map
        // either, so a menu title can never cover the map's own tiles.
        var menuBarRect = _mainGame.MenuBar.GetGlobalRect();
        ok &= Check(
            !menuBarRect.Intersects(mapRect),
            $"the menu bar's rect {menuBarRect} does not overlap the map's rect {mapRect}");

        ok &= Check(
            map.ClipContents,
            "the map control clips its drawing to its own rect (Control.ClipContents), so no tile or "
            + "marker can cover the top bar or the bottom toolbar");

        ok &= Check(
            ProjectSettings.GetSetting("display/window/stretch/aspect").AsString() == "expand",
            "Godot resolves display/window/stretch/aspect to \"expand\" (bug #498 part 2: no "
            + "letterboxing in a window that is not 1500:850)");

        ok &= Check(
            ProjectSettings.GetSetting("display/window/stretch/mode").AsString() == "canvas_items",
            "Godot resolves display/window/stretch/mode to \"canvas_items\" (MainGameScreen's own "
            + "container layout gives the extra room to the map)");

        return ok;
    }

    private static IEnumerable<Button> FindButtons(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button)
            {
                yield return button;
            }

            foreach (var nested in FindButtons(child))
            {
                yield return nested;
            }
        }
    }

    private static bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        return condition;
    }
}
