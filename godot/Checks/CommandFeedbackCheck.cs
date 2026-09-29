using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Fix #484: the main game screen's shared last-command label must echo a real command's own outcome —
/// an accepted order's acceptance line, a rejected order's reason text, an <c>end</c>'s closing summary —
/// instead of the blank separator line <c>GameSession.Submit</c> always ends its output with (the
/// pre-fix <c>lines[^1]</c>); and T95's separate Save confirmation label must keep working. Run headless:
/// <code>
/// godot --headless --path godot res://Checks/CommandFeedbackCheck.tscn
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Drives the real <see cref="MainGameScreen"/> over the shipped classical pair as Rome (the CLI's
/// <c>--seat rome</c> shape) through <see cref="MainGameScreen.SubmitForCheck"/> and
/// <see cref="MainGameScreen.PressSaveForCheck"/> — the same public hooks <c>godot/Screens/Checks/
/// ScreensCheck.cs</c> and <c>godot/Checks/SaveResumeCheck.cs</c> already establish — then reads the
/// labels' real text through <see cref="MainGameScreen.LastCommandText"/> and
/// <see cref="MainGameScreen.SaveConfirmationText"/>. It never rebuilds the rule itself: the xunit half
/// (<c>tests/IC2.Engine.Tests/Ui/CommandOutcomeTextTests.cs</c>) pins the rule against real
/// <c>Submit</c> output, this check pins that the screen really assigns it to the labels.
/// </para>
/// <para>
/// The Save half writes under <c>user://saves</c> (the real Save action's own convention, which no
/// Godot flag can redirect — see <c>SaveResumeCheck</c>'s remarks), under this check's own fixed file-name
/// suffix, and deletes only that file (and the directory, if it left it empty) in a <c>finally</c> block:
/// a real player's own, differently named save is never touched.
/// </para>
/// </remarks>
public partial class CommandFeedbackCheck : Node
{
    private bool _ok = true;
    private string? _savedPathToCleanUp;

    public override void _Ready()
    {
        try
        {
            Run();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"CommandFeedbackCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            CleanUpSavedFile();
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"CommandFeedbackCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void Run()
    {
        var classical = GameDataContext.Repository.Resolve("classical-mediterranean");
        var session = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: 1, humanSeatNationId: "rome");
        var mainGame = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        AddChild(mainGame);

        // ---- an accepted order (the bug's first half) ----
        mainGame.SubmitForCheck("recruit-standing rome light_infantry 15000");
        Check(
            mainGame.LastCommandText == "recruitment.recruit-standing-unit accepted.",
            $"an accepted order's label shows its acceptance line (got '{mainGame.LastCommandText}')");

        // ---- a rejected order from a non-adjacent tile (the bug's second half) ----
        mainGame.SubmitForCheck("besiege-city army-0 misurata");
        Check(
            mainGame.LastCommandText.Contains("battle.siege-not-adjacent", StringComparison.Ordinal)
            && mainGame.LastCommandText.Contains("is not adjacent to 'misurata'", StringComparison.Ordinal),
            $"a rejected order's label shows the rejection's reason text (got '{mainGame.LastCommandText}')");

        // ---- end (the bug's third half: the label must not be blank) ----
        mainGame.SubmitForCheck("end");
        Check(
            !string.IsNullOrWhiteSpace(mainGame.LastCommandText),
            "an end turn's label is not blank");
        Check(
            mainGame.LastCommandText.StartsWith("Now: Week ", StringComparison.Ordinal)
            || mainGame.LastCommandText.StartsWith("  ", StringComparison.Ordinal),
            $"an end turn's label shows the round footer or the newest news entry (got '{mainGame.LastCommandText}')");

        // ---- T95's own Save confirmation label, kept by fix #484 ----
        mainGame.PressSaveForCheck("fix484-commandfeedbackcheck");
        _savedPathToCleanUp = mainGame.LastSavedPath;
        Check(
            mainGame.SaveConfirmationText.Contains("Saved to", StringComparison.Ordinal),
            $"T95's Save confirmation label still shows the save's own outcome (got '{mainGame.SaveConfirmationText}')");

        RemoveChild(mainGame);
        mainGame.QueueFree();
    }

    private void Check(bool condition, string what)
    {
        if (condition)
        {
            GD.Print($"PASS: {what}");
            return;
        }

        GD.PrintErr($"FAIL: {what}");
        _ok = false;
    }

    /// <summary>
    /// Deletes the file <see cref="_savedPathToCleanUp"/> names, and its containing directory if that
    /// leaves it empty — the same best-effort cleanup <c>SaveResumeCheck</c> does, and for the same
    /// reason (no Godot flag can redirect <c>user://</c>).
    /// </summary>
    private void CleanUpSavedFile()
    {
        if (_savedPathToCleanUp is null)
        {
            return;
        }

        try
        {
            if (File.Exists(_savedPathToCleanUp))
            {
                File.Delete(_savedPathToCleanUp);
            }

            var directory = Path.GetDirectoryName(_savedPathToCleanUp);
            if (directory is not null && Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"CommandFeedbackCheck: could not clean up '{_savedPathToCleanUp}': {ex.Message}");
        }
    }
}
