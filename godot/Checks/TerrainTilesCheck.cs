using Godot;
using IC2.Engine.Assets;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Assets;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T148 "The map draws terrain from tiles" Done-when 3, run headless via:
/// <code>
/// godot --headless --path godot res://Checks/TerrainTilesCheck.tscn --quit-after 600
/// </code>
/// It drives the real <see cref="GameMapView"/> against the shipped
/// <c>classical-mediterranean</c> world and asserts:
/// <list type="bullet">
/// <item>at 32 px a drawn cell, <see cref="GameMapView.TerrainKeysForCheck(int,int)"/> reports the
/// Done-when 1 keys for a scripted set of cells — a plain, a forest, a deep sea, a river of code 8
/// and a coastal land cell;</item>
/// <item>at exactly 12 px a cell the tile path is used (<c>UsesTiles</c>), at 11.9 px the flat-colour
/// path is, and the tile keys do not change with the path;</item>
/// <item>the drawn terrain rectangle is still exactly <c>W × H × cell</c> (the MapClipCheck
/// contract);</item>
/// <item><see cref="GameMapView.TerrainMemoryBytesForCheck"/> stays under 64 MB after drawing the whole
/// map at 12 px and then at 72 px a cell.</item>
/// </list>
/// The threshold is the single <see cref="TerrainTileKeys.TilePixelThreshold"/> the draw itself reads,
/// so mutating it makes this check fail (the PR's mutation proof).
/// </summary>
/// <remarks>
/// The same scene also captures the Done-when 4 visual-review screenshots when
/// <c>IC2_SCREENSHOT_DIR</c> is set: a windowed run at 2560 × 1351 (headless screenshots do not work,
/// issue #156) saves the Mediterranean at the default fit, Italy and Greece at 32 px a cell, and a
/// Nile river course at 32 px a cell. It is the same real <see cref="GameMapView"/> the headless run
/// asserts on, so the screenshots and the checks cannot drift apart.
/// </remarks>
public partial class TerrainTilesCheck : Control
{
    private const long MemoryLimitBytes = 64L * 1024 * 1024;
    private const int SettleFrames = 6;

    // The cell sizes the check drives, in screen pixels. 12.0 and 11.9 are the two sides of
    // TerrainTileKeys.TilePixelThreshold (the DoD's boundary case).
    private const float WholeMapCell = 12f;
    private const float MaxZoomCell = 72f;
    private const float KeysCell = 32f;
    private const float ThresholdCell = 12f;
    private const float BelowThresholdCell = 11.9f;

    private readonly List<string> _findings = new();
    private bool _ok = true;

    private GameMapView _map = null!;
    private GameSession _session = null!;
    private string _repositoryRoot = string.Empty;

    // The scripted cells, found from the shipped world's own terrain grid.
    private (int X, int Y) _plain;
    private (int X, int Y) _forest;
    private (int X, int Y) _deepSea;
    private (int X, int Y) _riverCode8;
    private (int X, int Y) _coastalLand;

    // Screenshot mode (DoD 4).
    private string _screenshotDirectory = string.Empty;
    private bool _screenshotMode;
    private (int X, int Y) _rome;
    private (int X, int Y) _athens;
    private (int X, int Y) _nile;

    private int _frame;
    private int _step;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");

        // Done-when 3 scripts a deep-sea cell. The shipped classical world's grid uses only code 0
        // for sea (checked below): code 1 is a defined tile type — a second "Sea" with a higher move
        // cost (terrain-move-cost-table-in-dat.md) — that this export never places. So the check
        // scripts a genuine code-1 cell onto an existing sea cell through the engine's own base64
        // grid encoding, and then the deep-sea case is a real cell of the world it draws. A real
        // code-1 cell, if the world ever gains one, is used as it is.
        var world = EnsureADeepSeaCell(resolved.World);

        _session = new GameSession(world, resolved.Ruleset, resolved.Scenario);
        _repositoryRoot = GameDataContext.RepositoryRoot;

        _map = new GameMapView { Size = Size };
        AddChild(_map);

        // Load the session and bake once so the scripted cells can be found from the same decode the
        // draw and the click path use.
        _map.Attach(_session, _repositoryRoot);

        var screenshotDirectory = System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            _screenshotMode = true;
            _screenshotDirectory = screenshotDirectory;
            Directory.CreateDirectory(_screenshotDirectory);
            SetupScreenshotTour();
            return;
        }

        RunHeadlessChecks();
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
                    CheckMemory(WholeMapCell);
                    SetWholeMapCell(MaxZoomCell);
                    _frame = 0;
                    _step = 1;
                    break;

                case 1 when _frame >= SettleFrames:
                    CheckMemory(MaxZoomCell);
                    Finish();
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"TerrainTilesCheck: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    // --- Done-when 3: keys, threshold and memory ---------------------------------------------

    private void RunHeadlessChecks()
    {
        FindScriptedCells();

        // The key mapping and the threshold do not need a rendered frame: TerrainKeysForCheck reads
        // the view's own state. Drive those cases synchronously.
        RunKeysAtCellSize(KeysCell, expectTiles: true);
        RunKeysAtCellSize(ThresholdCell, expectTiles: true);
        RunKeysAtCellSize(BelowThresholdCell, expectTiles: false);

        // The memory cases do: size the control to the whole map at 12 px and let the real _Draw run.
        SetWholeMapCell(WholeMapCell);
    }

    /// <summary>
    /// Sizes the map control to the whole map at <paramref name="cellPixels"/> a cell and attaches,
    /// so the view's own fit gives exactly that drawn cell size and the whole map is inside the
    /// control's rect (the "whole map" the memory bound is measured over).
    /// </summary>
    private void SetWholeMapCell(float cellPixels)
    {
        var world = _session.World;
        _map.Size = new Vector2(world.Width, world.Height) * cellPixels;
        _map.Attach(_session, _repositoryRoot);
        _map.QueueRedraw();
        _frame = 0;
    }

    private void RunKeysAtCellSize(float cellPixels, bool expectTiles)
    {
        _map.SetDrawnCellPixelsForCheck(cellPixels);

        var world = _session.World;
        var rect = _map.TerrainDrawRectForCheck;
        Check(
            rect.Size.IsEqualApprox(new Vector2(world.Width, world.Height) * cellPixels),
            $"at {cellPixels} px a cell the drawn terrain rectangle is still W x H x {cellPixels} "
            + $"(got {rect.Size})");

        var cells = world.Terrain.Decode(world.Width, world.Height);
        var scripted = new (string Name, (int X, int Y) Cell, string Expected)[]
        {
            ("plain", _plain, AssetKeys.TerrainPlainTile),
            ("forest", _forest, AssetKeys.TerrainForestTile),
            ("deep sea", _deepSea, AssetKeys.TerrainSeaDeepTile),
            ("river code 8", _riverCode8, AssetKeys.TerrainRiverEn),
        };

        foreach (var (name, cell, expected) in scripted)
        {
            var keys = _map.TerrainKeysForCheck(cell.X, cell.Y);
            Check(keys.UsesTiles == expectTiles,
                $"at {cellPixels} px a cell the {name} cell {(expectTiles ? "uses" : "does not use")} "
                + $"the tile path (got {keys.UsesTiles})");
            if (expectTiles)
            {
                Check(keys.TileKey == expected,
                    $"at {cellPixels} px a cell the {name} cell reports its Done-when 1 key "
                    + $"{expected} (got {keys.TileKey ?? "<none>"})");
            }
        }

        // The coastal land cell: its shore overlays must be exactly the sides whose in-map neighbour
        // is sea, derived independently here from the raw codes (0 and 1 are the two sea codes).
        var coastal = _map.TerrainKeysForCheck(_coastalLand.X, _coastalLand.Y);
        Check(coastal.UsesTiles == expectTiles,
            $"at {cellPixels} px a cell the coastal land cell {(expectTiles ? "uses" : "does not use")} "
            + $"the tile path (got {coastal.UsesTiles})");

        var expectedShores = ExpectedShoreKeys(cells, world.Width, world.Height, _coastalLand.X, _coastalLand.Y);
        Check(expectedShores.SequenceEqual(coastal.ShoreKeys),
            $"at {cellPixels} px a cell the coastal land cell reports shore overlays "
            + $"[{string.Join(", ", coastal.ShoreKeys)}], expected [{string.Join(", ", expectedShores)}]");
    }

    private void CheckMemory(float cellPixels)
    {
        var bytes = _map.TerrainMemoryBytesForCheck;
        Check(bytes < MemoryLimitBytes,
            $"after drawing the whole map at {cellPixels} px a cell the terrain draw holds "
            + $"{bytes} bytes, under the 64 MB limit ({MemoryLimitBytes})");
    }

    // --- Done-when 4: the visual-review screenshots ------------------------------------------

    private void SetupScreenshotTour()
    {
        _rome = FindCity("Rome");
        _athens = FindCity("Athens");
        _nile = FindNearestRiverCell(FindCity("Memphis"));

        // The map control fills the window, so the default fit is the app's own first fit.
        _map.Size = Size;
        _map.Attach(_session, _repositoryRoot);
        _frame = 0;
        _step = 20;
    }

    private void ScreenshotStep()
    {
        switch (_step)
        {
            case 20 when _frame >= SettleFrames:
                Capture("01-mediterranean-default-fit.png");
                _map.SetDrawnCellPixelsForCheck(KeysCell);
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
                GD.Print("TerrainTilesCheck: screenshot tour done.");
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
            ? $"TerrainTilesCheck: saved {path}"
            : $"TerrainTilesCheck: could not save {path}: {error}");
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
        GD.Print($"TerrainTilesCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    // --- Shared helpers ----------------------------------------------------------------------

    /// <summary>
    /// The Done-when 1 shore rule, re-derived here from the raw codes so the check does not simply
    /// mirror <see cref="TerrainTileKeys"/>: 0 and 1 are the shipped sea codes, everything else land,
    /// and a side only counts when its neighbour is in-map.
    /// </summary>
    private static List<string> ExpectedShoreKeys(int[] cells, int width, int height, int x, int y)
    {
        static bool Sea(int code) => code is 0 or 1;
        var keys = new List<string>();
        if (y - 1 >= 0 && Sea(cells[((y - 1) * width) + x]))
        {
            keys.Add(AssetKeys.TerrainShoreN);
        }

        if (x + 1 < width && Sea(cells[(y * width) + x + 1]))
        {
            keys.Add(AssetKeys.TerrainShoreE);
        }

        if (y + 1 < height && Sea(cells[((y + 1) * width) + x]))
        {
            keys.Add(AssetKeys.TerrainShoreS);
        }

        if (x - 1 >= 0 && Sea(cells[(y * width) + x - 1]))
        {
            keys.Add(AssetKeys.TerrainShoreW);
        }

        return keys;
    }

    private void FindScriptedCells()
    {
        var world = _session.World;
        var cells = world.Terrain.Decode(world.Width, world.Height);

        _plain = Find(cells, world.Width, world.Height, code => code == 2);
        _forest = Find(cells, world.Width, world.Height, code => code == 4);
        _deepSea = Find(cells, world.Width, world.Height, code => code == 1);
        _riverCode8 = Find(cells, world.Width, world.Height, code => code == 8);
        _coastalLand = FindCoastalLand(cells, world.Width, world.Height);

        Check(_plain.X >= 0, "the classical world has a plain cell (code 2)");
        Check(_forest.X >= 0, "the classical world has a forest cell (code 4)");
        Check(_deepSea.X >= 0, "the classical world has a deep-sea cell (code 1)");
        Check(_riverCode8.X >= 0, "the classical world has a river cell of code 8");
        Check(_coastalLand.X >= 0, "the classical world has a land cell with a sea neighbour");
    }

    private static (int X, int Y) Find(int[] cells, int width, int height, Func<int, bool> match)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (match(cells[(y * width) + x]))
                {
                    return (x, y);
                }
            }
        }

        return (-1, -1);
    }

    private static (int X, int Y) FindCoastalLand(int[] cells, int width, int height)
    {
        static bool Sea(int code) => code is 0 or 1;
        static bool Land(int code) => code is >= 2 and <= 11;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!Land(cells[(y * width) + x]))
                {
                    continue;
                }

                if ((y - 1 >= 0 && Sea(cells[((y - 1) * width) + x]))
                    || (x + 1 < width && Sea(cells[(y * width) + x + 1]))
                    || (y + 1 < height && Sea(cells[((y + 1) * width) + x]))
                    || (x - 1 >= 0 && Sea(cells[(y * width) + x - 1])))
                {
                    return (x, y);
                }
            }
        }

        return (-1, -1);
    }

    /// <summary>
    /// Returns a world that has at least one deep-sea cell (code 1). If the shipped grid already has
    /// one it is returned unchanged; otherwise the first sea cell (code 0) is rewritten to code 1 and
    /// the grid is re-encoded as the engine's own little-endian 16-bit base64.
    /// </summary>
    private static World EnsureADeepSeaCell(World world)
    {
        var cells = world.Terrain.Decode(world.Width, world.Height);
        if (Array.IndexOf(cells, 1) >= 0)
        {
            return world;
        }

        var seaIndex = Array.IndexOf(cells, 0);
        if (seaIndex < 0)
        {
            return world;
        }

        cells[seaIndex] = 1;
        var bytes = new byte[cells.Length * 2];
        for (var i = 0; i < cells.Length; i++)
        {
            bytes[i * 2] = (byte)(cells[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)((cells[i] >> 8) & 0xFF);
        }

        var grid = new TerrainGrid(
            TerrainEncoding.Base64,
            Runs: null,
            Data: Convert.ToBase64String(bytes),
            DataFile: null);
        return world with { Terrain = grid };
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
