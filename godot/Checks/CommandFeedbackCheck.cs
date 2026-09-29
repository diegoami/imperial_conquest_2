using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Fix #484: the main game screen's shared last-command label must echo a real command's own outcome —
/// an accepted order's acceptance line, a rejected order's declaration of war and reason text, an
/// <c>end</c>'s closing summary up to (but never including) the news section — instead of the blank
/// separator line <c>GameSession.Submit</c> always ends its output with (the pre-fix <c>lines[^1]</c>);
/// and T95's separate Save confirmation label must keep working. Run headless:
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
/// <strong>Review round 1 rework.</strong> B1: the <c>end</c> assertions now pin the label's last line
/// to the exact <c>"Now: Week …"</c> line <c>SubmitForCheck</c> returns from the real output, and pin
/// that no line of the news section after <c>News:</c> (the week banner included) appears in the label.
/// N3: the rejected order's assertion now expects the composed <c>diplomacy.declare-war</c> line as
/// well as the refusal. N2: a real <c>news</c> command's whitespace-only spacer line must not reach the
/// label. N1 is pinned by <see cref="MainGameScreen"/>'s own label construction (wrapping, a three-line
/// ceiling with an ellipsis and the full text in the tooltip), which a headless run cannot measure.
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

        // ---- a rejected order from a non-adjacent tile (the bug's second half). Rome starts at peace
        //      with Carthage, so Submit composes the declaration of war ahead of the refusal: the label
        //      shows the whole own outcome, both lines (review N3). ----
        mainGame.SubmitForCheck("besiege-city army-0 misurata");
        var siegeLabelLines = mainGame.LastCommandText.Split('\n');
        Check(
            siegeLabelLines.Length == 2
            && siegeLabelLines[0] == "diplomacy.declare-war accepted (composed ahead of the attack)."
            && siegeLabelLines[1].StartsWith("battle.besiege-city rejected (battle.siege-not-adjacent): ", StringComparison.Ordinal)
            && siegeLabelLines[1].Contains("is not adjacent to 'misurata'", StringComparison.Ordinal),
            $"a rejected order's label shows its whole own outcome, declaration and reason (got '{mainGame.LastCommandText}')");

        // ---- end (the bug's third half): the round's own closing summary, never the news banner
        //      (review B1). The exact "Now: Week ..." line from the real Submit output, and no line of
        //      the news section after News: anywhere in the label. ----
        var endLines = mainGame.SubmitForCheck("end");
        var nowLine = endLines.FirstOrDefault(line => line.StartsWith("Now: Week ", StringComparison.Ordinal));
        Check(
            nowLine is not null
            && nowLine.StartsWith($"Now: Week {session.State.Calendar.Week},", StringComparison.Ordinal),
            $"the end's real output carries the round footer's Now: line with the new week (got '{nowLine}')");

        var endLabelLines = mainGame.LastCommandText.Split('\n');
        Check(
            nowLine is not null && endLabelLines[^1] == nowLine,
            $"an end turn's label ends on the exact Now: Week line, not the news banner (its last line: '{endLabelLines[^1]}')");
        Check(
            !endLabelLines.Any(string.IsNullOrWhiteSpace)
            && !endLabelLines.Any(line => !string.Equals(line, line.Trim(), StringComparison.Ordinal)),
            "an end turn's label carries no blank or padded lines");

        var newsHeaderIndex = -1;
        for (var i = 0; i < endLines.Count; i++)
        {
            if (string.Equals(endLines[i].Trim(), "News:", StringComparison.Ordinal))
            {
                newsHeaderIndex = i;
                break;
            }
        }

        var newsLines = newsHeaderIndex >= 0
            ? endLines.Skip(newsHeaderIndex + 1)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList()
            : new List<string>();
        Check(
            newsHeaderIndex >= 0 && newsLines.Count > 0,
            "the end's real output really carries a News: section to exclude");
        Check(
            !endLabelLines.Any(newsLines.Contains),
            "no news line (the week banner included) appears in the label");

        // ---- review N2: after an end has produced news, a real news command's output really carries
        //      the news log's whitespace-only spacer line, and it must never reach the label ----
        var newsOutput = mainGame.SubmitForCheck("news");
        Check(
            newsOutput.Any(line => line.Length > 0 && string.IsNullOrWhiteSpace(line)),
            "the news command's real output carries a whitespace-only spacer line");
        Check(
            !mainGame.LastCommandText.Split('\n').Any(string.IsNullOrWhiteSpace),
            "no whitespace-only spacer reaches the label");

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
