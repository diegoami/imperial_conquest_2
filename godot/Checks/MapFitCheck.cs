using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T201 Done-when 2: a portrait (160×200) and a wide (400×120) world fit the area map inside its pixel budget and
/// fill the main map with no background showing, at the opening fit, fully zoomed out and fully zoomed in. The
/// classical world keeps its 320×140, scale-1 area map. Runs the real <see cref="MainGameScreen"/> at the shipped
/// window size.
/// </summary>
public partial class MapFitCheck : Control
{
    private const int SettleFrames = 8;
    private const float CoverageRounding = 0.001f;

    private static readonly (int Width, int Height)[] Worlds = { (160, 200), (400, 120) };

    private readonly List<(int WaitFrames, Action Run)> _plan = new();
    private MainGameScreen? _mainGame;
    private bool _ok = true;
    private int _frame;
    private int _planIndex;

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(
            (int)ProjectSettings.GetSetting("display/window/size/viewport_width"),
            (int)ProjectSettings.GetSetting("display/window/size/viewport_height"));
        Size = GetViewport().GetVisibleRect().Size;

        var classical = AreaMapGeometry.ForWorld(320, 140);
        Check(classical.Scale == 1f && classical.PixelWidth == 320 && classical.PixelHeight == 140,
            "the classical world's area map stays 320×140 at one pixel per tile");

        foreach (var (width, height) in Worlds)
        {
            var label = $"{width}×{height}";
            _plan.Add((0, () => ShowWorld(width, height)));
            _plan.Add((SettleFrames, () => CheckFit(label, "opening fit")));
            _plan.Add((1, () => _mainGame!.MapView.SetDrawnCellPixelsForCheck(0.01f)));
            _plan.Add((SettleFrames, () => CheckFit(label, "zoomed fully out")));
            _plan.Add((1, () => _mainGame!.MapView.ZoomToMaxForCheck()));
            _plan.Add((SettleFrames, () => CheckFit(label, "zoomed fully in")));
        }
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
            Check(false, $"unhandled exception: {ex}");
            _planIndex = _plan.Count;
            Finish();
        }
    }

    /// <summary>The toy scenario on a plain world of <paramref name="width"/>×<paramref name="height"/>.</summary>
    private void ShowWorld(int width, int height)
    {
        _mainGame?.QueueFree();
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var world = toy.World with
        {
            Width = width,
            Height = height,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, width * height)), Data: null),
        };
        _mainGame = new MainGameScreen
        {
            Session = new GameSession(world, toy.Ruleset, toy.Scenario),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
    }

    private void CheckFit(string world, string stage)
    {
        var what = $"{world}, {stage}";
        var map = _mainGame!.MapView;
        var mapRect = new Rect2(Vector2.Zero, map.Size);
        var terrain = map.TerrainDrawRectForCheck;
        Check(map.Size.X > 0 && map.Size.Y > 0, $"{what}: the main map has a real size ({map.Size})");
        // Float centering/scaling can leave offsets around 0.000015px. Allow
        // only a thousandth of a pixel; a visible background gap still fails.
        Check(terrain.Grow(CoverageRounding).Encloses(mapRect), $"{what}: the drawn terrain {terrain} covers the main map {mapRect} (no background)");
        Check(!new Rect2(new Vector2(1, 0), map.Size - new Vector2(1, 0)).Grow(CoverageRounding).Encloses(mapRect),
            "coverage rounding cannot hide a one-pixel background gap");

        var area = _mainGame.AreaMapView;
        var geometry = area.GeometryForCheck;
        Check(
            geometry.PixelWidth <= AreaMapGeometry.DefaultMaxPixelWidth && geometry.PixelHeight <= AreaMapGeometry.DefaultMaxPixelHeight,
            $"{what}: the area map is {geometry.PixelWidth}×{geometry.PixelHeight} px, inside its {AreaMapGeometry.DefaultMaxPixelWidth}×{AreaMapGeometry.DefaultMaxPixelHeight} budget (scale {geometry.Scale})");
        Check(
            area.Size.Y <= AreaMapGeometry.DefaultMaxPixelHeight + 1 && area.Size.X <= _mainGame.ContextPanel.Size.X + 1,
            $"{what}: the area map control ({area.Size}) does not overflow its column ({_mainGame.ContextPanel.Size.X} px wide)");
        var areaRect = area.GetGlobalRect();
        Check(!areaRect.Intersects(map.GetGlobalRect()), $"{what}: the area map {areaRect} does not overlap the main map {map.GetGlobalRect()}");
    }

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"MapFitCheck: exiting with code {exitCode}.");
        CheckExit.Quit(this, exitCode);
    }

    private void Check(bool condition, string what)
    {
        if (condition)
        {
            GD.Print($"PASS: {what}");
            return;
        }

        _ok = false;
        GD.PrintErr($"FAIL: {what}");
    }
}
