using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T148 "The map's surface is painted, not tiled" Done-when 3, run headless via:
/// <code>
/// godot --headless --path godot res://Checks/TerrainSurfaceCheck.tscn --quit-after 600
/// </code>
/// It drives the real <see cref="GameMapView"/> against the shipped <c>classical-mediterranean</c>
/// world and asserts:
/// <list type="bullet">
/// <item>after at least one real <c>_Draw</c> frame, <see cref="GameMapView.TerrainSurfaceForCheck"/>
/// records that the draw used the shader path with the six surface keys bound — the recorded drawn
/// code, never the configured material;</item>
/// <item>with a scratch pack missing one surface key, the draw records the flat-colour fallback
/// instead;</item>
/// <item><see cref="GameMapView.TerrainMemoryBytesForCheck"/> stays under 24 MB.</item>
/// </list>
/// The scene also captures the Done-when 4 visual-review screenshots when <c>IC2_SCREENSHOT_DIR</c> is
/// set: a windowed run at 2560 × 1351 (headless screenshots do not work, issue #156) saves the
/// Mediterranean at the default fit, Italy and Greece at 32 px a cell, and a Nile river course at 32 px
/// a cell.
/// </summary>
public partial class TerrainSurfaceCheck : Control
{
    private const long MemoryLimitBytes = 24L * 1024 * 1024;
    private const int SettleFrames = 6;
    private const float ReviewCell = 32f;

    private readonly List<string> _findings = new();
    private bool _ok = true;

    private GameMapView _map = null!;
    private GameMapView? _fallbackMap;
    private GameSession _session = null!;
    private string _repositoryRoot = string.Empty;
    private string _scratchPackRoot = string.Empty;

    private string _screenshotDirectory = string.Empty;
    private bool _screenshotMode;
    private (int X, int Y) _rome;
    private (int X, int Y) _athens;
    private (int X, int Y) _nile;

    private int _frame;
    private int _step;

    public override void _Ready()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        _session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario);
        _repositoryRoot = GameDataContext.RepositoryRoot;

        var screenshotDirectory = System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            _screenshotMode = true;
            _screenshotDirectory = screenshotDirectory;
            Directory.CreateDirectory(_screenshotDirectory);

            // A short windowed run at the review's size; the canvas_items stretch makes the control
            // exactly this size (headless screenshots do not work, issue #156).
            Size = new Vector2(2560, 1351);
            _map = new GameMapView { Size = Size };
            AddChild(_map);
            _map.Attach(_session, _repositoryRoot);
            _rome = FindCity("Rome");
            _athens = FindCity("Athens");
            _nile = FindNearestRiverCell(FindCity("Memphis"));
            _step = 20;
            return;
        }

        Size = GetViewport().GetVisibleRect().Size;
        _map = new GameMapView { Size = Size };
        AddChild(_map);
        _map.Attach(_session, _repositoryRoot);
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            if (_screenshotMode)
            {
                ScreenshotStep();
                return;
            }

            switch (_step)
            {
                case 0 when _frame >= SettleFrames:
                    CheckShownSurface();
                    _frame = 0;
                    _step = 1;
                    break;

                case 1 when _frame >= SettleFrames:
                    BuildFallbackPack();
                    _fallbackMap = new GameMapView { Size = Size };
                    AddChild(_fallbackMap);
                    _fallbackMap.Attach(_session, _scratchPackRoot);
                    _frame = 0;
                    _step = 2;
                    break;

                case 2 when _frame >= SettleFrames:
                    CheckFallbackSurface();
                    CheckMemory();
                    Finish();
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"TerrainSurfaceCheck: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    // --- Done-when 3 ----------------------------------------------------------------

    private void CheckShownSurface()
    {
        var state = _map.TerrainSurfaceForCheck;
        Check(state.Kind == TerrainSurfaceKind.Shader,
            $"after at least one real _Draw frame the recorded surface is the shader path "
            + $"(got {state.Kind})");
        Check(state.BoundSurfaceKeys.Count == 6,
            $"the shader surface binds exactly six surface keys (got {state.BoundSurfaceKeys.Count})");
        foreach (var key in TerrainSplatMap.SurfaceKeysByClass)
        {
            Check(state.BoundSurfaceKeys.Contains(key),
                $"the shader surface binds {key}");
        }
    }

    private void CheckFallbackSurface()
    {
        var state = _fallbackMap!.TerrainSurfaceForCheck;
        Check(state.Kind == TerrainSurfaceKind.FlatFallback,
            $"with a surface key missing from the pack the recorded surface is the flat fallback "
            + $"(got {state.Kind})");

        var missing = TerrainSplatMap.SurfaceKeysByClass[0];
        Check(!state.BoundSurfaceKeys.Contains(missing),
            $"the missing surface key {missing} is not bound");
        Check(state.BoundSurfaceKeys.Count == 5,
            $"the fallback still names the five resolved surface keys (got {state.BoundSurfaceKeys.Count})");
    }

    private void CheckMemory()
    {
        var bytes = _map.TerrainMemoryBytesForCheck;
        Check(bytes > 0 && bytes < MemoryLimitBytes,
            $"the terrain draw holds {bytes} bytes, under the 24 MB limit ({MemoryLimitBytes})");
    }

    /// <summary>
    /// Builds a scratch pack under the git-ignored <c>rendered/</c> that is the real placeholder pack
    /// with exactly one surface key (plain) removed from the manifest, so the shader path cannot resolve
    /// and the draw falls back. Never touches the committed packs.
    /// </summary>
    private void BuildFallbackPack()
    {
        _scratchPackRoot = Path.Combine(_repositoryRoot, "rendered", "terrain-surface-fallback-pack");
        var packDir = Path.Combine(_scratchPackRoot, "assets", "packs", "placeholder");
        var sourceDir = Path.Combine(_repositoryRoot, "assets", "packs", "placeholder");

        if (Directory.Exists(_scratchPackRoot))
        {
            Directory.Delete(_scratchPackRoot, true);
        }

        CopyDirectory(sourceDir, packDir);

        var manifestPath = Path.Combine(packDir, "manifest.json");
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        root["assets"]!.AsObject().Remove(TerrainSplatMap.SurfaceKeysByClass[0]);
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    // --- Done-when 4: the visual-review screenshots ----------------------------------

    private void ScreenshotStep()
    {
        switch (_step)
        {
            case 20 when _frame >= SettleFrames:
                Capture("01-mediterranean-default-fit.png");
                _map.SetDrawnCellPixelsForCheck(ReviewCell);
                _map.CentreOnTile(_rome.X, _rome.Y);
                _frame = 0;
                _step = 21;
                break;

            case 21 when _frame >= SettleFrames:
                Capture("02-italy-32px.png");
                _map.CentreOnTile(_athens.X, _athens.Y);
                _frame = 0;
                _step = 22;
                break;

            case 22 when _frame >= SettleFrames:
                Capture("03-greece-32px.png");
                _map.CentreOnTile(_nile.X, _nile.Y);
                _frame = 0;
                _step = 23;
                break;

            case 23 when _frame >= SettleFrames:
                Capture("04-nile-river-32px.png");
                GD.Print("TerrainSurfaceCheck: screenshot tour done.");
                GetTree().Quit(0);
                break;
        }
    }

    private void Capture(string fileName)
    {
        var path = Path.Combine(_screenshotDirectory, fileName);
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        GD.Print(error == Error.Ok
            ? $"TerrainSurfaceCheck: saved {path}"
            : $"TerrainSurfaceCheck: could not save {path}: {error}");
    }

    private (int X, int Y) FindCity(string name)
    {
        var city = _session.World.Cities.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        return city is null ? (-1, -1) : (city.X, city.Y);
    }

    /// <summary>The river cell (codes 6–11) nearest <paramref name="near"/>, for the Nile screenshot.</summary>
    private (int X, int Y) FindNearestRiverCell((int X, int Y) near)
    {
        var world = _session.World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        var best = (-1, -1);
        var bestDistance = int.MaxValue;
        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
            {
                var code = cells[(y * world.Width) + x];
                if (code is < 6 or > 11)
                {
                    continue;
                }

                var distance = Math.Abs(x - near.X) + Math.Abs(y - near.Y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (x, y);
                }
            }
        }

        return best;
    }

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"TerrainSurfaceCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        if (!condition)
        {
            _ok = false;
            _findings.Add(description);
        }
    }
}
