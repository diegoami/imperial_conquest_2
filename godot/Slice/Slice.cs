using System;
using System.IO;
using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;

namespace IC2.Slice;

/// <summary>
/// T47 "Thin Godot slice: the engine on a screen" -- the risk it exists to retire is that nothing has
/// ever run the reimplementation engine (<c>IC2.Engine</c>) inside Godot; what is beside this scene
/// (<c>MapViewer.tscn</c>) is the research inspector, which reads the original <c>.DAT</c>/<c>.sav</c>
/// through <c>IC2.Data</c> and never touches the engine at all.
/// </summary>
/// <remarks>
/// <para>
/// This scene loads the committed toy world/ruleset/scenario through the engine's own
/// <see cref="GameStateFactory"/>, draws the three toy cities, ends one turn through the same
/// <see cref="IC2.Engine.Core.TurnCoordinator"/> <c>IC2.Cli</c> uses, and prints the resulting status.
/// It does this through <see cref="GameSession"/> -- the exact same presentation class
/// <c>src/IC2.Cli/Program.cs</c> wraps -- rather than re-deriving the wiring, so the values this scene
/// prints are not merely "similar" to what the CLI prints for the same scenario and seed: they come from
/// literally the same code path, run a second time behind a different front end.
/// </para>
/// <para>
/// No menu, no New Game flow, no ruleset chooser -- those are T24's, deliberately not started here
/// (docs/task-catalogue.md T47 hazard 1). No <c>.DAT</c>/<c>.sav</c> reading is ported in either (hazard
/// 2): this scene never references <c>IC2.Data</c> or <c>assets.local.ini</c>, only the committed
/// <c>data/worlds/toy-3city.json</c>, <c>data/rulesets/toy-ruleset.json</c> and the toy scenario.
/// </para>
/// </remarks>
public partial class Slice : Node2D
{
    private const float TileSize = 72f;
    private static readonly Color BackgroundColor = new(0.07f, 0.11f, 0.15f);
    private static readonly Color GridColor = new(0f, 0f, 0f, 0.28f);
    private static readonly Color CityRingColor = new(1.0f, 0.97f, 0.86f);
    private static readonly Color UnknownTerrainColor = new(0.24f, 0.24f, 0.24f);
    private static readonly Color UnknownNationColor = new(0.6f, 0.6f, 0.6f);

    /// <summary>
    /// Terrain colours by the world's own <see cref="TileType.Name"/> (never by a hardcoded numeric
    /// code of this scene's own invention -- the codes and names themselves always come from
    /// <see cref="World.TileTypeByCode"/>, the same lookup <c>GameSessionRendering.RenderMap</c>
    /// already uses for its glyphs). The colours themselves are this scene's own presentation choice:
    /// no report gives terrain a colour, only a name, so there is nothing to source here -- picked for
    /// visual contrast against the city/army markers and each other, nothing more.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, Color> TerrainColorsByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sea"] = new Color(0.06f, 0.16f, 0.40f),
            ["Plain"] = new Color(0.30f, 0.42f, 0.18f),
            ["Desert"] = new Color(0.62f, 0.52f, 0.28f),
            ["Forest"] = new Color(0.10f, 0.28f, 0.14f),
            ["Mountains"] = new Color(0.42f, 0.38f, 0.36f),
            ["River"] = new Color(0.14f, 0.46f, 0.70f),
        };

    private GameSession? _session;
    private int _framesRendered;

    public override void _Ready()
    {
        GD.Print("T47 slice: loading the toy world/ruleset/scenario through IC2.Engine's own GameStateFactory...");

        try
        {
            // Mirrors MapViewer.cs's own convention for finding the repository root from res://
            // (godot/ is one level below the repo root), so this scene needs no assets.local.ini and no
            // knowledge of where Godot's working directory happens to be.
            var repositoryRoot = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
            var dataDirectory = Path.Combine(repositoryRoot, "data");

            var repository = GameDataRepository.Load(dataDirectory);
            var resolved = repository.Resolve("toy-3city");

            // The exact construction IC2.Cli's Program.cs uses (no seed override, so this scenario's own
            // committed random seed drives both front ends identically): GameSession.CreateInitial calls
            // GameStateFactory.CreateInitial (DoD 2) and builds a TurnCoordinator the same way (DoD 3).
            _session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario);

            GD.Print(
                $"Loaded world '{_session.World.Id}' ({_session.World.Cities.Count} cities), "
                + $"ruleset '{_session.Ruleset.Id}', scenario '{_session.Scenario.Id}'.");

            PrintStatusLine("Before ending a turn");

            // Ends one turn -- literally through GameSession's own TurnCoordinator, the same class and
            // the same call IC2.Cli's "end" command drives. Printed verbatim, so a side-by-side diff
            // against the CLI's own output for the same input is a diff of two identical texts.
            var output = _session.Submit("end");
            foreach (var line in output.Lines)
            {
                GD.Print(line);
            }

            PrintStatusLine("After ending a turn");
        }
        catch (GameDataException ex)
        {
            GD.PushError($"T47 slice could not load the toy scenario: {ex.Message}");
        }

        QueueRedraw();
    }

    /// <summary>
    /// Diagnostic-only: saves a PNG of what this scene rendered, purely so a human can see it
    /// (docs/task-catalogue.md T47 DoD 8 -- "for the user's eyes rather than as a merge gate", not a
    /// gate any check asserts on). Opt-in via the <c>IC2_SLICE_SCREENSHOT_PATH</c> environment variable,
    /// so an ordinary run of this scene never writes a file as a side effect.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_framesRendered >= 3)
        {
            return;
        }

        _framesRendered++;
        if (_framesRendered != 3)
        {
            return;
        }

        var path = System.Environment.GetEnvironmentVariable("IC2_SLICE_SCREENSHOT_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        if (error != Error.Ok)
        {
            GD.PushError($"T47 slice: could not save screenshot to '{path}': {error}.");
        }
        else
        {
            GD.Print($"T47 slice: saved screenshot to '{path}'.");
        }
    }

    private void PrintStatusLine(string label)
    {
        if (_session is null)
        {
            return;
        }

        var calendar = _session.State.Calendar;
        GD.Print(
            $"{label}: Week {calendar.Week}, season {calendar.SeasonIndex}, {calendar.YearBc} BC "
            + $"(turn {calendar.TurnIndex}). Active seat: {_session.State.ActiveNationId}.");
    }

    public override void _Draw()
    {
        var viewportSize = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, viewportSize), BackgroundColor);

        if (_session is null)
        {
            return;
        }

        var world = _session.World;
        DrawTerrain(world);
        DrawGrid(world);

        foreach (var city in _session.State.Cities)
        {
            var center = new Vector2(
                (city.X + 0.5f) * TileSize,
                (city.Y + 0.5f) * TileSize);

            var fillColor = NationColor(city.Owner);

            // A dark halo behind the fill keeps the city legible against whichever terrain colour
            // (above) happens to sit under it -- the toy world's Plain and Desert are both light
            // enough that the CityRingColor arc alone was not always enough contrast on its own.
            DrawCircle(center, (TileSize / 3f) + 3f, new Color(0f, 0f, 0f, 0.55f));
            DrawCircle(center, TileSize / 3f, fillColor);
            DrawArc(center, TileSize / 3f, 0f, Mathf.Tau, 32, CityRingColor, 2.5f);
        }

        foreach (var army in _session.State.Armies)
        {
            DrawArmy(army);
        }
    }

    /// <summary>
    /// One tile-coloured rect per cell, from the world's own run-length-encoded terrain and its own
    /// <see cref="TileType"/> table -- the same two calls (<see cref="Terrain.Decode"/>,
    /// <see cref="World.TileTypeByCode"/>) <c>GameSessionRendering.RenderMap</c> already makes for its
    /// text glyphs. An unrecognised code (the terrain type list is open, per T02's scope) falls back
    /// to <see cref="UnknownTerrainColor"/> rather than leaving the tile blank.
    /// </summary>
    private void DrawTerrain(World world)
    {
        var terrainCells = world.Terrain.Decode(world.Width, world.Height);
        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
            {
                var code = terrainCells[(y * world.Width) + x];
                var tileType = world.TileTypeByCode(code);
                var color = tileType is not null
                    && TerrainColorsByName.TryGetValue(tileType.Name, out var known)
                        ? known
                        : UnknownTerrainColor;

                var rect = new Rect2(x * TileSize, y * TileSize, TileSize, TileSize);
                DrawRect(rect, color);
            }
        }
    }

    private void DrawGrid(World world)
    {
        for (var x = 0; x <= world.Width; x++)
        {
            var start = new Vector2(x * TileSize, 0);
            var end = new Vector2(x * TileSize, world.Height * TileSize);
            DrawLine(start, end, GridColor);
        }

        for (var y = 0; y <= world.Height; y++)
        {
            var start = new Vector2(0, y * TileSize);
            var end = new Vector2(world.Width * TileSize, y * TileSize);
            DrawLine(start, end, GridColor);
        }
    }

    /// <summary>
    /// One diamond marker per army, owner-coloured from the same <see cref="World.NationById"/> lookup
    /// (and the same <see cref="NationColor"/> helper) the city markers use above -- deliberately not
    /// a second, independently-invented palette. <c>MapViewer.cs</c>'s own <c>OwnerColor</c> table is
    /// not reused either: the user found it has two byte-identical colour pairs (issue #154), and
    /// copying a known-broken palette into a second place would just give this scene the same defect.
    /// A palette that stays visually distinct across more than the toy world's two nations is a
    /// question for #154 and T24, not this walking skeleton.
    /// </summary>
    private void DrawArmy(ArmyState army)
    {
        var center = new Vector2((army.X + 0.5f) * TileSize, (army.Y + 0.5f) * TileSize);
        var half = TileSize / 4.5f;
        var points = new[]
        {
            center + new Vector2(0, -half),
            center + new Vector2(half, 0),
            center + new Vector2(0, half),
            center + new Vector2(-half, 0),
        };

        var fillColor = NationColor(army.Nation);
        DrawColoredPolygon(points, fillColor);
        for (var i = 0; i < points.Length; i++)
        {
            DrawLine(points[i], points[(i + 1) % points.Length], CityRingColor, 2f);
        }
    }

    /// <summary>
    /// The one source of "what colour is this nation" for both cities and armies: the world's own
    /// <see cref="NationDefinition.ColorHex"/>. Sharing this single lookup (rather than a marker-type-
    /// specific table) is what keeps the city and army palettes from ever silently diverging.
    /// </summary>
    private Color NationColor(string nationId)
    {
        var nation = _session?.World.NationById(nationId);
        return nation is not null && ColorFromHex(nation.ColorHex, out var parsed) ? parsed : UnknownNationColor;
    }

    private static bool ColorFromHex(string? hex, out Color color)
    {
        color = UnknownNationColor;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            color = new Color(hex);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
