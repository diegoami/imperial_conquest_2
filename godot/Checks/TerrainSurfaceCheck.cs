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
/// <item>the drawn material's shader source declares <c>repeat_enable</c> with a mipmapped linear
/// filter on the six surface samplers, <c>repeat_disable</c> on the two splat samplers, and names the
/// bicubic splat sampling function;</item>
/// <item><see cref="GameMapView.TerrainMemoryBytesForCheck"/>, counting every texture the draw keeps
/// plus the splat map's baked byte arrays, stays under 24 MB.</item>
/// </list>
/// The scene also captures the Done-when 4 visual-review screenshots when <c>IC2_SCREENSHOT_DIR</c> is
/// set: a windowed run at 2560 × 1351 (headless screenshots do not work, issue #156) saves the
/// Mediterranean at the default fit, Italy and Greece at 32 px a cell, a Nile river course at 32 px
/// a cell, and <c>05-rim-probe.png</c> — a scripted 40 × 20 grid, plain in columns 0–19 and sea in
/// columns 20–39, drawn through the same shader at exactly 32 px a cell with the grid's top-left cell
/// at the screen's top-left, which <c>scripts/measure-rim.py</c> reads.
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

        CheckSamplerHints();
    }

    /// <summary>
    /// T148 Done-when 3, read from the shader source the drawn material actually uses (the Opus review
    /// of the splatting round, R1): the six surface samplers must declare <c>repeat_enable</c> and a
    /// mipmapped linear filter, the two splat samplers <c>repeat_disable</c>. Without the former every
    /// terrain past the first repeat is its texture's clamped edge texel — the blur; with the latter the
    /// splat's outer half-sample blends the opposite map edge into it.
    /// </summary>
    private void CheckSamplerHints()
    {
        var code = _map.TerrainSurfaceShaderCodeForCheck;
        Check(code.Length > 0, "the drawn material carries a shader to read its sampler hints from");
        if (code.Length == 0)
        {
            return;
        }

        foreach (var sampler in new[] { "tex_plain", "tex_desert", "tex_forest", "tex_mountain", "tex_shallow", "tex_deep" })
        {
            var declaration = SamplerDeclaration(code, sampler);
            Check(declaration is not null
                    && declaration.Contains("repeat_enable", StringComparison.Ordinal)
                    && declaration.Contains("filter_linear_mipmap", StringComparison.Ordinal),
                $"the surface sampler {sampler} declares repeat_enable and filter_linear_mipmap "
                + $"(got {(declaration ?? "no declaration")})");
        }

        foreach (var sampler in new[] { "splat_a", "splat_b" })
        {
            var declaration = SamplerDeclaration(code, sampler);
            Check(declaration is not null
                    && declaration.Contains("repeat_disable", StringComparison.Ordinal),
                $"the splat sampler {sampler} declares repeat_disable "
                + $"(got {(declaration ?? "no declaration")})");
        }

        // T148 Done-when 3 (the Opus review of round 4's R2): the splat sampling must be the named
        // Catmull-Rom bicubic function, not the bilinear texture() call whose lattice zig-zag the round-3
        // review measured.
        Check(code.Contains("sample_splat_bicubic", StringComparison.Ordinal),
            "the shader source names the bicubic splat sampling function");
        Check(code.Contains("sample_splat_bicubic(splat_a", StringComparison.Ordinal)
                && code.Contains("sample_splat_bicubic(splat_b", StringComparison.Ordinal),
            "the shader samples both splat textures through the bicubic function");
    }

    private static string? SamplerDeclaration(string code, string samplerName)
    {
        foreach (var rawLine in code.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("uniform sampler2D ", StringComparison.Ordinal)
                && line.Contains(samplerName, StringComparison.Ordinal))
            {
                return line;
            }
        }

        return null;
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
                BuildRimProbe();
                _frame = 0;
                _step = 24;
                break;

            case 24 when _frame >= SettleFrames:
                Capture("05-rim-probe.png");
                GD.Print("TerrainSurfaceCheck: screenshot tour done.");
                GetTree().Quit(0);
                break;
        }
    }

    /// <summary>
    /// T148 Done-when 4: the rim probe — a scripted 40 × 20 grid, plain (code 2) in columns 0–19 and sea
    /// (code 0) in columns 20–39, drawn through the surface shader at exactly 32 px a cell with the
    /// grid's top-left at the screen's top-left. It is built here, on the check's own CanvasItem, rather
    /// than through <see cref="GameMapView"/> (whose draw is tied to a real world), but it uses the same
    /// <c>res://UI/TerrainSurface.gdshader</c> and binds the same uniforms from
    /// <see cref="TerrainSplatMap"/>'s constants, so the render is the drawn rim.
    /// </summary>
    private void BuildRimProbe()
    {
        const int gridWidth = 40;
        const int gridHeight = 20;
        const float probeCell = 32f;
        const float surfaceCellsPerRepeat = 2.5f; // GameMapView.SurfaceCellsPerRepeat

        // The windowed run renders under the project's canvas_items stretch (base 1500 × 850), so a
        // logical pixel is not a physical one. Done-when 4 measures the screenshot's pixel columns as
        // 32 px a cell, so size the probe's cell in logical units such that it lands on exactly 32
        // screen pixels: logicalCell = 32 / (window height / base viewport height), the uniform
        // canvas_items scale (window/size/stretch/aspect = expand keeps the origin at the top-left).
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var windowSize = DisplayServer.WindowGetSize();
        var physicalScale = viewportSize.Y > 0f ? windowSize.Y / viewportSize.Y : 1f;
        var logicalCell = probeCell / physicalScale;
        GD.Print($"TerrainSurfaceCheck: rim probe cell {probeCell} px, canvas scale {physicalScale:F4}, "
            + $"logical cell {logicalCell:F4}, probe {gridWidth * logicalCell:F1} × {gridHeight * logicalCell:F1} logical.");

        var cells = new int[gridWidth * gridHeight];
        for (var y = 0; y < gridHeight; y++)
        {
            for (var x = 0; x < gridWidth; x++)
            {
                cells[(y * gridWidth) + x] = x < 20 ? 2 : 0;
            }
        }

        var splat = TerrainSplatMap.Bake(gridWidth, gridHeight, cells);
        var shader = GD.Load<Shader>("res://UI/TerrainSurface.gdshader");
        var material = new ShaderMaterial { Shader = shader };
        var imageA = Image.CreateFromData(
            splat.LatticeWidth, splat.LatticeHeight, false, Image.Format.Rgba8, splat.SplatA);
        var imageB = Image.CreateFromData(
            splat.LatticeWidth, splat.LatticeHeight, false, Image.Format.Rgba8, splat.SplatB);
        material.SetShaderParameter("splat_a", ImageTexture.CreateFromImage(imageA));
        material.SetShaderParameter("splat_b", ImageTexture.CreateFromImage(imageB));

        var loader = AssetPackTextureLoader.TryLoadPack(_repositoryRoot, null, message => GD.PrintErr(message));
        var samplerNames = new[] { "tex_plain", "tex_desert", "tex_forest", "tex_mountain", "tex_shallow", "tex_deep" };
        for (var i = 0; i < TerrainSplatMap.SurfaceKeysByClass.Count; i++)
        {
            material.SetShaderParameter(
                samplerNames[i], loader?.TryGetSurfaceTexture(TerrainSplatMap.SurfaceKeysByClass[i]));
        }

        material.SetShaderParameter("world_size", new Vector2(gridWidth, gridHeight));
        material.SetShaderParameter("surface_scale", surfaceCellsPerRepeat);
        material.SetShaderParameter(
            "shallow_tint",
            new Vector3(
                TerrainSplatMap.ShallowTintR,
                TerrainSplatMap.ShallowTintG,
                TerrainSplatMap.ShallowTintB));
        material.SetShaderParameter("shallow_tone_down", TerrainSplatMap.ShallowToneDown);
        material.SetShaderParameter("surf_strength", TerrainSplatMap.SurfStrength);

        // The map view is hidden so the probe fills the top-left corner; the screenshot captures the
        // whole viewport, and measure-rim.py reads only the probe's region.
        _map.Visible = false;
        AddChild(new ColorRect
        {
            Name = "RimProbe",
            Position = Vector2.Zero,
            Size = new Vector2(gridWidth * logicalCell, gridHeight * logicalCell),
            Color = new Color(1f, 1f, 1f, 1f),
            MouseFilter = MouseFilterEnum.Ignore,
            Material = material,
        });
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
