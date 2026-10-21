using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Bug #1282 (D1 of the v0.6.2 play-through #1280): Esc on an open dialog closes it, and the owner's
/// close handler removes the dialog from the tree before the key handler goes on, so a
/// <c>GetViewport()</c> call after the close is null and throws. This opens each overlay the menu bar
/// reaches without a selection on the real <see cref="MainGameScreen"/>, pushes a real Esc key event
/// through the viewport, and asserts the overlay closed and no error was logged.
/// </summary>
/// <remarks>
/// <strong>Run headless via:</strong>
/// <code>
/// godot --headless --path godot res://Checks/DialogEscCheck.tscn --quit-after 900
/// </code>
/// </remarks>
public partial class DialogEscCheck : Control
{
    private const int SettleFrames = 4;

    private static readonly string[] MenuItems =
    {
        "strategy.balance_sheet",
        "strategy.taxation",
        "strategy.recruit_unit",
        "strategy.build_fleet",
        "area_map.find_city",
        "help.topics",
        "help.about",
    };

    private readonly EscErrorLogger _logger = new();
    private MainGameScreen _mainGame = null!;
    private bool _ok = true;
    private int _frame;
    private int _step;

    private partial class EscErrorLogger : Logger
    {
        private int _errors;
        public int Errors => Volatile.Read(ref _errors);

        public override void _LogError(string function, string file, int line, string code, string rationale,
            bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            Interlocked.Increment(ref _errors);
        }
    }

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;
        var data = GameDataContext.Repository.Resolve("classical-mediterranean");
        var session = new GameSession(data.World, data.Ruleset, data.Scenario);
        _mainGame = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
        OS.AddLogger(_logger);
    }

    public override void _Process(double delta)
    {
        if (++_frame < SettleFrames)
        {
            return;
        }

        _frame = 0;
        try
        {
            // Even steps open the next dialog; odd steps press Esc on it.
            var item = MenuItems[_step / 2];
            if (_step % 2 == 0)
            {
                _mainGame.MenuBar.PressItemForCheck(item);
                Check(_mainGame.ActiveOverlay is not null, $"{item} opens an overlay");
            }
            else
            {
                var overlay = _mainGame.ActiveOverlay?.GetType().Name ?? "nothing";
                var errorsBefore = _logger.Errors;
                var esc = new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true };
                GetViewport().PushInput(esc);
                Check(_mainGame.ActiveOverlay is null, $"Esc closes {overlay} ({item})");
                Check(_logger.Errors == errorsBefore, $"Esc on {overlay} logs no error (got {_logger.Errors - errorsBefore})");
            }

            if (++_step >= MenuItems.Length * 2)
            {
                Finish();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"DialogEscCheck: unhandled exception: {ex}");
            _ok = false;
            Finish();
        }
    }

    private void Finish()
    {
        OS.RemoveLogger(_logger);
        SetProcess(false);
        GD.Print($"DialogEscCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }
}
