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
    private static readonly Color GridColor = new(0.16f, 0.22f, 0.28f);
    private static readonly Color CityRingColor = new(1.0f, 0.97f, 0.86f);

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

        foreach (var city in _session.State.Cities)
        {
            var center = new Vector2(
                (city.X + 0.5f) * TileSize,
                (city.Y + 0.5f) * TileSize);

            var owner = _session.World.NationById(city.Owner);
            var fillColor = owner is not null && ColorFromHex(owner.ColorHex, out var parsed)
                ? parsed
                : CityRingColor;

            DrawCircle(center, TileSize / 3f, fillColor);
            DrawArc(center, TileSize / 3f, 0f, Mathf.Tau, 32, CityRingColor, 2f);
        }
    }

    private static bool ColorFromHex(string? hex, out Color color)
    {
        color = CityRingColor;
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
