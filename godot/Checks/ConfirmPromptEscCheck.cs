using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Bug #598: Esc on an open <see cref="ConfirmPrompt"/> answers No, and the <c>Refused</c> handler
/// removes the prompt from the tree before the key handler goes on, so a <c>GetViewport()</c> call after
/// the answer would be null and log a script error. This pushes a real Esc key event through the viewport
/// and asserts the answer is No, nothing Yes, and no error was logged.
/// </summary>
public partial class ConfirmPromptEscCheck : Node
{
    private bool _ok = true;
    private readonly EscErrorLogger _logger = new();

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
        OS.AddLogger(_logger);
        try
        {
            Run();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"ConfirmPromptEscCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            OS.RemoveLogger(_logger);
        }

        GD.Print($"ConfirmPromptEscCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    private void Run()
    {
        var prompt = new ConfirmPrompt { Question = "Are you sure ?" };
        var confirmed = 0;
        var refused = 0;
        prompt.Confirmed += () => confirmed++;
        // Like the real owners (MainGameScreen.ShowOverlay), drop the prompt from the tree on an answer.
        prompt.Refused += () =>
        {
            refused++;
            prompt.GetParent()?.RemoveChild(prompt);
            prompt.QueueFree();
        };
        AddChild(prompt);

        var esc = new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true };
        GetViewport().PushInput(esc);

        Check(refused == 1, $"Esc answers No (Refused fired {refused} time(s))");
        Check(confirmed == 0, "Esc does not answer Yes");
        Check(_logger.Errors == 0, $"no script error was logged (got {_logger.Errors})");
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
