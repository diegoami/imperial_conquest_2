using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Fix #484 / T96: the main game screen's shared last-command label must echo a real command's own
/// outcome — an accepted order's acceptance line, a rejected order's declaration of war and reason text,
/// an <c>end</c>'s closing summary up to (but never including) the news section — instead of the blank
/// separator line <c>GameSession.Submit</c> always ends its output with (the pre-fix <c>lines[^1]</c>);
/// and T95's separate Save confirmation label must keep working. Run headless:
/// <code>
/// godot --headless --path godot res://Checks/CommandFeedbackCheck.tscn
/// </code>
/// A <em>windowed</em> run of the same scene is the human visual review's vehicle (T96, plan-review R2):
/// it runs every check and then keeps the window open on the final <c>end</c> state for a screenshot:
/// <code>
/// godot.cmd --path godot res://Checks/CommandFeedbackCheck.tscn
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Drives the real <see cref="MainGameScreen"/> over the shipped classical pair as Rome (the CLI's
/// <c>--seat rome</c> shape) through <see cref="MainGameScreen.SubmitForCheck"/> and
/// <see cref="MainGameScreen.PressSaveForCheck"/> — the same public hooks <c>godot/Screens/Checks/
/// ScreensCheck.cs</c> and <c>godot/Checks/SaveResumeCheck.cs</c> already establish. It never rebuilds
/// the rule itself: the xunit half (<c>tests/IC2.Engine.Tests/Ui/CommandOutcomeTextTests.cs</c>) pins the
/// rule against real <c>Submit</c> output, this check pins that the screen really assigns it to the
/// labels <em>and displays it</em>.
/// </para>
/// <para>
/// <strong>T96: geometry, not just <c>.Text</c>.</strong> PR #521's round 1 left the label 1&#160;px tall
/// with zero visible lines (a Godot <see cref="Label"/> with autowrap plus clipping or an overrun
/// behaviour reports a minimum size of (1, 1)) and its tests read only <c>.Text</c>, so they stayed
/// green. This check builds the real screen as a full-rect child of its own <see cref="Control"/> root
/// against the configured 1500x850 viewport — the same shape <c>ContextPanelWidthCheck</c> and
/// <c>MapClipCheck</c> use — and waits settle frames after every command, so the container layout pass
/// really runs before the label's geometry is read. It asserts, on the real label:
/// <list type="bullet">
/// <item><see cref="Label.GetVisibleLineCount"/> is between 1 and 3 and the laid-out height is at least
/// three lines' height, for an accepted order, a rejected order, an <c>end</c> and a 1,000-character
/// line (T96 DoD 1);</item>
/// <item>after an <c>end</c> as Rome, the lines actually visible (the text past
/// <see cref="Label.LinesSkipped"/>) end on the exact <c>Now: Week …, Active seat: …</c> line read from
/// the real <c>Submit</c> output (T96 DoD 2);</item>
/// <item>the tooltip carries the whole block and a 1,000-character line does not widen the root's
/// minimum width (T96, plan-review R1);</item>
/// <item>the tightened news assertions compare trimmed lines on both sides and require the label to
/// really show something, so neither passes vacuously (T96 DoD 4; PR #521 re-review R1-N5).</item>
/// </list>
/// The check's own root <see cref="Control"/> keeps its default (all-zero, equal) anchors deliberately
/// and sets <see cref="Control.Size"/> directly instead — the same reason <c>ContextPanelWidthCheck</c>'s
/// own remarks give: a scene reached by an explicit <c>--path</c> argument never gets its Size resolved
/// from anchors the way a project's own <c>run/main_scene</c> does.
/// </para>
/// <para>
/// The Save half writes under <c>user://saves</c> (the real Save action's own convention, which no
/// Godot flag can redirect — see <c>SaveResumeCheck</c>'s remarks), under this check's own fixed file-name
/// suffix, and deletes only that file (and the directory, if it left it empty) in a <c>finally</c>-style
/// <see cref="_ExitTree"/> hook: a real player's own, differently named save is never touched.
/// </para>
/// </remarks>
public partial class CommandFeedbackCheck : Control
{
    /// <summary>Frames to let the real <see cref="MainGameScreen"/>'s container layout settle before the
    /// first command, the same settle-frame pattern the other Godot checks use.</summary>
    private const int InitialSettleFrames = 6;

    /// <summary>Frames to wait after every command, so the deferred layout pass sees the new text and
    /// the geometry read below is real. T96 DoD 1 asks for "at least two"; three is the same margin the
    /// other checks use.</summary>
    private const int CommandSettleFrames = 3;

    private bool _ok = true;
    private string? _savedPathToCleanUp;

    private MainGameScreen _mainGame = null!;
    private GameSession _session = null!;
    private VBoxContainer _rootBox = null!;
    private Label _lastCommandLabel = null!;
    private int _frame;
    private int _step;
    private bool _finished;
    private float _baselineRootMinWidth;

    private IReadOnlyList<string> _acceptedLines = null!;
    private IReadOnlyList<string> _rejectedLines = null!;
    private IReadOnlyList<string> _endLines = null!;
    private IReadOnlyList<string> _newsLines = null!;
    private string? _nowLine;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        var classical = GameDataContext.Repository.Resolve("classical-mediterranean");
        _session = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: 1, humanSeatNationId: "rome");
        _mainGame = new MainGameScreen { Session = _session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        // The label under test is the only direct Label child of the screen's root VBoxContainer (the
        // top bar, body and bottom toolbar are containers, the news log is a panel). Found by structure,
        // not through a new accessor, so this same check also compiles and runs against c073ab2 -- the
        // fails-before evidence T96 DoD 1-3 ask for.
        _rootBox = _mainGame.GetChildren().OfType<VBoxContainer>().First();
        _lastCommandLabel = _rootBox.GetChildren().OfType<Label>().Single();
    }

    public override void _Process(double delta)
    {
        if (_finished)
        {
            // A windowed run holds the final state for the visual review (see Finish): stop stepping so
            // the checks do not re-run every frame until the reviewer closes the window.
            return;
        }

        _frame++;

        try
        {
            switch (_step)
            {
                case 0 when _frame >= InitialSettleFrames:
                    _baselineRootMinWidth = _rootBox.GetCombinedMinimumSize().X;
                    GD.Print($"INFO: root minimum width before any command: {_baselineRootMinWidth}px");
                    _acceptedLines = _mainGame.SubmitForCheck("recruit-standing rome light_infantry 15000");
                    NextCommand();
                    break;

                case 1 when _frame >= CommandSettleFrames:
                    CheckAcceptedOrder();
                    _rejectedLines = _mainGame.SubmitForCheck("besiege-city army-0 misurata");
                    NextCommand();
                    break;

                case 2 when _frame >= CommandSettleFrames:
                    CheckRejectedOrder();
                    _endLines = _mainGame.SubmitForCheck("end");
                    NextCommand();
                    break;

                case 3 when _frame >= CommandSettleFrames:
                    CheckEndTurn();
                    _mainGame.SubmitForCheck(new string('x', 1000));
                    NextCommand();
                    break;

                case 4 when _frame >= CommandSettleFrames:
                    CheckLongLine();
                    _newsLines = _mainGame.SubmitForCheck("news");
                    NextCommand();
                    break;

                case 5 when _frame >= CommandSettleFrames:
                    CheckNewsSpacer();
                    _mainGame.PressSaveForCheck("fix484-commandfeedbackcheck");
                    _savedPathToCleanUp = _mainGame.LastSavedPath;
                    NextCommand();
                    break;

                case 6 when _frame >= CommandSettleFrames:
                    CheckSaveConfirmation();
                    _mainGame.SubmitForCheck("end");
                    NextCommand();
                    break;

                case 7 when _frame >= CommandSettleFrames:
                    CheckFinalEndVisual();
                    _finished = true;
                    Finish();
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CommandFeedbackCheck: unhandled exception: {ex}");
            GD.Print("CommandFeedbackCheck: exiting with code 1.");
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// Deletes the file <see cref="_savedPathToCleanUp"/> names, and its containing directory if that
    /// leaves it empty — the same best-effort cleanup <c>SaveResumeCheck</c> does, and for the same
    /// reason (no Godot flag can redirect <c>user://</c>). Runs when the tree is torn down, so both the
    /// headless <see cref="Finish"/> quit and a windowed run's window close clean up.
    /// </summary>
    public override void _ExitTree() => CleanUpSavedFile();

    private void NextCommand()
    {
        _frame = 0;
        _step++;
    }

    private void CheckAcceptedOrder()
    {
        const string AcceptedLine = "recruitment.recruit-standing-unit accepted.";

        // T147 (bug #781 point 4): the engine's own output still carries the raw acceptance line, but the
        // app's label shows the readable wording for the command kind, not the key.
        const string ReadableLine = "Unit recruited.";
        Check(
            _acceptedLines.Contains(AcceptedLine, StringComparer.Ordinal),
            "the accepted order's real output carries its acceptance line");
        Check(
            _mainGame.LastCommandText == ReadableLine,
            $"an accepted order's label shows its readable wording (got '{_mainGame.LastCommandText}')");
        CheckVisibleGeometry("an accepted order");
        CheckLinesMapOneToOne("an accepted order");
        CheckTooltipCarriesTheWholeBlock();
        Check(
            VisibleLines().SequenceEqual(new[] { ReadableLine }),
            $"an accepted order's visible line is its readable wording (got '{string.Join("|", VisibleLines())}')");
    }

    private void CheckRejectedOrder()
    {
        Check(
            _rejectedLines.Contains(
                "diplomacy.declare-war accepted (composed ahead of the attack).", StringComparer.Ordinal),
            "the rejected order's real output carries the composed declaration of war");
        Check(
            _mainGame.LastCommandText.Split('\n').Length == 2,
            $"a rejected order's label shows its whole own outcome, declaration and reason "
            + $"(got '{_mainGame.LastCommandText}')");

        var visible = VisibleLines();
        Check(
            visible.Length == 2
            && visible[0] == "diplomacy.declare-war accepted (composed ahead of the attack)."
            && visible[1].StartsWith("battle.besiege-city rejected (battle.siege-not-adjacent): ", StringComparison.Ordinal)
            && visible[1].Contains("is not adjacent to 'misurata'", StringComparison.Ordinal),
            $"a rejected order's visible lines are the declaration and the reason (got '{string.Join("|", visible)}')");
        CheckVisibleGeometry("a rejected order");
        CheckLinesMapOneToOne("a rejected order");
        CheckTooltipCarriesTheWholeBlock();
    }

    private void CheckEndTurn()
    {
        // T96 DoD 2: the exact "Now: Week ..." line from the real Submit output, and what is *visible*
        // must include it -- the whole point of showing the block's last lines.
        _nowLine = _endLines.FirstOrDefault(line => line.StartsWith("Now: Week ", StringComparison.Ordinal));
        Check(
            _nowLine is not null
            && _nowLine.StartsWith($"Now: Week {_session.State.Calendar.Week},", StringComparison.Ordinal),
            $"the end's real output carries the round footer's Now: line with the new week (got '{_nowLine}')");
        Check(
            _mainGame.LastCommandText.Split('\n').Length > 3,
            "the end's block really is longer than the label's three-line window");
        CheckVisibleGeometry("an end turn");
        CheckLinesMapOneToOne("an end turn");

        var visible = VisibleLines();
        var visibleTail = visible.Length > 0 ? visible[^1] : "(none)";
        Check(
            visible.Length == 3,
            $"an end turn's label shows three visible lines (got {visible.Length})");
        Check(
            _nowLine is not null && visible.Length > 0 && visible[^1] == _nowLine,
            $"an end turn's visible lines end on the exact Now: Week line (their last line: '{visibleTail}')");
        Check(
            visible.All(line => !string.IsNullOrWhiteSpace(line)
                && string.Equals(line, line.Trim(), StringComparison.Ordinal)),
            "an end turn's visible lines carry no blank or padded lines");
        Check(
            _mainGame.LastCommandText.Split('\n')[^1] == _nowLine,
            "the end's own block (the label's text) ends on the Now: Week line, not the news banner");
        CheckTooltipCarriesTheWholeBlock();

        var newsHeaderIndex = -1;
        for (var i = 0; i < _endLines.Count; i++)
        {
            if (string.Equals(_endLines[i].Trim(), "News:", StringComparison.Ordinal))
            {
                newsHeaderIndex = i;
                break;
            }
        }

        var newsLines = newsHeaderIndex >= 0
            ? _endLines.Skip(newsHeaderIndex + 1)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList()
            : new List<string>();
        Check(
            newsHeaderIndex >= 0 && newsLines.Count > 0,
            "the end's real output really carries a News: section to exclude");

        // T96 DoD 4 / R1-N5: trimmed on both sides, so a padded news banner can never slip past the
        // comparison the way it did at c073ab2.
        Check(
            !visible.Any(line => newsLines.Contains(line.Trim())),
            "no news line (the week banner included) appears in the visible label");
    }

    private void CheckLongLine()
    {
        var text = _mainGame.LastCommandText;
        Check(
            text.Length >= 1000 && text.StartsWith("Unknown command '", StringComparison.Ordinal),
            $"the 1,000-character command's label carries its 1,000+ character outcome (got {text.Length} chars)");
        CheckVisibleGeometry("a 1,000-character line");
        CheckTooltipCarriesTheWholeBlock();

        // T96 plan-review R1: the wrap/clip/overrun settings must keep the long line from widening the
        // root -- the same no-widening outcome PR #521's round-1 re-review measured (555px).
        var after = _rootBox.GetCombinedMinimumSize().X;
        Check(
            after <= _baselineRootMinWidth + 0.5f,
            $"a 1,000-character line does not widen the root (before {_baselineRootMinWidth}px, after {after}px)");
    }

    private void CheckNewsSpacer()
    {
        // T96 DoD 4 / R1-N5: the real news output really carries the news log's whitespace-only spacer,
        // and it must not reach the *visible* label -- with a non-vacuity check, so an empty label (as
        // at c073ab2) cannot pass this by showing nothing.
        Check(
            _newsLines.Any(line => line.Length > 0 && string.IsNullOrWhiteSpace(line)),
            "the news command's real output carries a whitespace-only spacer line");
        CheckLinesMapOneToOne("the news command");

        var visible = VisibleLines();
        Check(
            visible.Length >= 1,
            $"the news command's label really shows visible lines (got {visible.Length})");
        Check(
            !visible.Any(string.IsNullOrWhiteSpace),
            "no whitespace-only spacer reaches the visible label");
        Check(
            visible.All(line => string.Equals(line, line.Trim(), StringComparison.Ordinal)),
            "every visible label line is trimmed");
    }

    private void CheckSaveConfirmation()
    {
        Check(
            _mainGame.SaveConfirmationText.Contains("Saved to", StringComparison.Ordinal),
            $"T95's Save confirmation label still shows the save's own outcome (got '{_mainGame.SaveConfirmationText}')");
        Check(
            _mainGame.LastCommandText == _mainGame.SaveConfirmationText,
            "the shared last-command label shows the Save's own outcome too");
    }

    private void CheckFinalEndVisual()
    {
        // The windowed run stops here, so the screenshot shows a populated three-line label.
        var visible = VisibleLines();
        Check(
            visible.Length == 3,
            $"the final end leaves three visible lines for the visual review (got {visible.Length})");
        Check(
            visible.Any(line => line.StartsWith("Now: Week ", StringComparison.Ordinal)),
            $"the final end's visible lines include the Now: Week line (got '{string.Join("|", visible)}')");
        CheckVisibleGeometry("the final end");
        CheckLinesMapOneToOne("the final end");
    }

    /// <summary>
    /// <see cref="VisibleLines"/> maps <see cref="Label.LinesSkipped"/> (a wrapped-line index) onto the
    /// block's newline-separated text lines, which is only exact when no text line wraps. Every block
    /// this check compares visible text for is made of short lines at the 1500&#160;px viewport width;
    /// the 1,000-character block is the one that wraps, and it is asserted on geometry and tooltip only.
    /// </summary>
    private void CheckLinesMapOneToOne(string what)
    {
        var textLines = _lastCommandLabel.Text.Split('\n').Length;
        var wrappedLines = _lastCommandLabel.GetLineCount();
        Check(
            wrappedLines == textLines,
            $"{what}: every outcome line occupies exactly one wrapped line, so LinesSkipped maps to text "
            + $"lines (wrapped {wrappedLines}, text {textLines})");
    }

    /// <summary>
    /// T96 DoD 1: the label is really laid out and visible — <see cref="Label.GetVisibleLineCount"/> is
    /// between 1 and 3, and the laid-out height is at least three lines' height (the floor that keeps
    /// the wrap/clip/overrun label from collapsing to (1, 1)). The INFO line prints the geometry the
    /// assertion is made on.
    /// </summary>
    private void CheckVisibleGeometry(string what)
    {
        var visibleCount = _lastCommandLabel.GetVisibleLineCount();
        var lineHeight = _lastCommandLabel.GetLineHeight();
        var textLineCount = _lastCommandLabel.Text.Split('\n').Length;
        GD.Print(
            $"INFO: {what}: textLines={textLineCount}, LinesSkipped={_lastCommandLabel.LinesSkipped}, "
            + $"GetVisibleLineCount={visibleCount}, lineHeight={lineHeight}, labelSize={_lastCommandLabel.Size}");

        Check(
            visibleCount >= 1 && visibleCount <= 3,
            $"{what}: the label shows between 1 and 3 visible lines (got {visibleCount})");
        Check(
            lineHeight > 0 && _lastCommandLabel.Size.Y + 0.5f >= 3 * lineHeight,
            $"{what}: the label's laid-out height is at least three lines' height "
            + $"({_lastCommandLabel.Size.Y} vs {3 * lineHeight})");
    }

    private void CheckTooltipCarriesTheWholeBlock()
    {
        Check(
            _lastCommandLabel.TooltipText == _lastCommandLabel.Text,
            "the label's tooltip carries the whole outcome block");
    }

    /// <summary>
    /// The lines the player can actually see: the label's text past <see cref="Label.LinesSkipped"/>,
    /// capped by <see cref="Label.GetVisibleLineCount"/> — never <c>.Text</c> alone (T96's first hazard).
    /// Exact when each text line wraps to exactly one visual line; the callers that compare visible text
    /// assert that with <see cref="CheckLinesMapOneToOne"/> first.
    /// </summary>
    private string[] VisibleLines()
    {
        var lines = _lastCommandLabel.Text.Split('\n');
        var skipped = Mathf.Clamp(_lastCommandLabel.LinesSkipped, 0, lines.Length);
        var count = Mathf.Clamp(_lastCommandLabel.GetVisibleLineCount(), 0, lines.Length - skipped);
        return lines.Skip(skipped).Take(count).ToArray();
    }

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        if (DisplayServer.GetName() != "headless")
        {
            // T96 plan-review R2: the human visual review's vehicle is a windowed run. The checks above
            // have run; the window stays on the final end state so the reviewer can screenshot it.
            GD.Print(
                $"CommandFeedbackCheck: windowed run finished with code {exitCode}. The window stays open "
                + "on the final end state for the human visual review; close it to exit.");
            return;
        }

        GD.Print($"CommandFeedbackCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
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
