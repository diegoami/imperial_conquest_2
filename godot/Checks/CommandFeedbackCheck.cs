using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T96/T289 command-result integration: accepted/refused wording, end-turn footer, whitespace filtering,
/// long text, Save confirmation and actual visible Log geometry. T289's owner-approved 100-result
/// history replaces the old three-line label; full blocks now scroll with the newest result last.
/// </summary>
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
    private CommandHistoryPanel _lastCommandLabel = null!;
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

        // T289 moves the same output label into the right-side Log frame.
        _rootBox = _mainGame.GetChildren().OfType<VBoxContainer>().First();
        _lastCommandLabel = _mainGame.CommandLog;
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
                    if (_mainGame.StrategicTravel.IsActive)
                        Check(!_mainGame.StrategicTravel.CommandsAllowed, "pending strategic travel withholds subsequent commands");
                    _mainGame.StrategicTravel.Skip();
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
                    if (_mainGame.StrategicTravel.IsActive)
                        Check(!_mainGame.StrategicTravel.CommandsAllowed, "pending strategic travel withholds subsequent commands");
                    _mainGame.StrategicTravel.Skip();
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
                    if (_mainGame.StrategicTravel.IsActive)
                        Check(!_mainGame.StrategicTravel.CommandsAllowed, "pending strategic travel withholds subsequent commands");
                    _mainGame.StrategicTravel.Skip();
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
            CheckExit.Quit(this, 1);
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
        Check(_mainGame.OrderNoticeForCheck.Length == 0, "an accepted order shows no refusal notice over the map (#1286)");
        CheckVisibleGeometry("an accepted order");
        CheckLatestBlockInHistory("an accepted order");
        CheckTooltipCarriesTheWholeBlock();
        Check(
            LatestResultTail().SequenceEqual(new[] { ReadableLine }),
            $"an accepted order's visible line is its readable wording (got '{string.Join("|", LatestResultTail())}')");
    }

    private void CheckRejectedOrder()
    {
        Check(
            !_rejectedLines.Contains(
                "diplomacy.declare-war accepted (composed ahead of the attack).", StringComparer.Ordinal),
            "the rejected order's real output carries no composed declaration of war");
        Check(
            _mainGame.LastCommandText.Split('\n').Length == 1,
            $"a rejected order's label shows its refusal alone "
            + $"(got '{_mainGame.LastCommandText}')");

        var visible = LatestResultTail();
        Check(
            visible.Length == 1
            // #1280 F3: the refusal reads as the engine's plain message, without the command kind and code.
            && !visible[0].Contains(" rejected (", StringComparison.Ordinal)
            && char.IsUpper(visible[0].First(char.IsLetter))
            && visible[0].Contains("is not adjacent to 'misurata'", StringComparison.Ordinal),
            $"a rejected order's visible line is its refusal alone (got '{string.Join("|", visible)}')");
        Check(
            _mainGame.OrderNoticeForCheck == visible[0],
            $"a rejected order's refusal is also shown over the map (#1286; got '{_mainGame.OrderNoticeForCheck}')");
        CheckVisibleGeometry("a rejected order");
        CheckLatestBlockInHistory("a rejected order");
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
        Check(_mainGame.OrderNoticeForCheck.Length == 0, "an end turn closes the earlier refusal notice (#1286)");
        CheckVisibleGeometry("an end turn");
        CheckLatestBlockInHistory("an end turn");

        var visible = LatestResultTail();
        var visibleTail = visible.Length > 0 ? visible[^1] : "(none)";
        Check(
            _lastCommandLabel.GetVisibleLineCount() > 0,
            "an end turn leaves readable content in the scrollable Log");
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
        CheckLatestBlockInHistory("the news command");

        var visible = LatestResultTail();
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

        // Fix #537 (T96's review of PR #535, report S1): the confirmation carries the whole user://
        // path, and before the fix that long single line set this label's -- and so the top bar row's
        // and the root's -- minimum width to ~1730px, past the 1500px viewport, cropping the context
        // panel. Record both the label and the root, then hold the root at or below the viewport.
        var viewportWidth = GetViewport().GetVisibleRect().Size.X;
        var labelSize = _mainGame.SaveConfirmationLabelSize;
        var labelMin = _mainGame.SaveConfirmationLabelMinimumSize;
        var rootMin = _rootBox.GetCombinedMinimumSize();
        GD.Print(
            $"INFO: after a Save: labelSize=({labelSize.X}, {labelSize.Y}), "
            + $"labelMin=({labelMin.X}, {labelMin.Y}), rootMin=({rootMin.X}, {rootMin.Y}), "
            + $"viewportWidth={viewportWidth}px");
        Check(
            rootMin.X <= viewportWidth,
            $"a Save's confirmation does not widen the root past the viewport "
            + $"(viewport {viewportWidth}px, rootMin.X {rootMin.X}px)");

        // Fix #537's chosen convention (the same one _lastCommandLabel uses): the label ellipsizes its
        // long text, and the full path stays reachable in the tooltip.
        Check(
            _mainGame.SaveConfirmationTooltip == _mainGame.SaveConfirmationText,
            "the Save confirmation's ellipsized text keeps its whole path in the tooltip");
    }

    private void CheckFinalEndVisual()
    {
        // The windowed run stops here, so the screenshot shows a populated three-line label.
        var visible = LatestResultTail();
        Check(
            _lastCommandLabel.GetVisibleLineCount() > 0,
            "the final end leaves readable Log content for visual review");
        Check(
            visible.Any(line => line.StartsWith("Now: Week ", StringComparison.Ordinal)),
            $"the final end's visible lines include the Now: Week line (got '{string.Join("|", visible)}')");
        CheckVisibleGeometry("the final end");
        CheckLatestBlockInHistory("the final end");
    }

    private void CheckLatestBlockInHistory(string what)
    {
        Check(_lastCommandLabel.Text.EndsWith(_mainGame.LastCommandText, StringComparison.Ordinal),
            what + ": the full latest block ends the actual scrollable history");
    }

    private void CheckVisibleGeometry(string what)
    {
        Check(_lastCommandLabel.IsVisibleInTree() && _lastCommandLabel.Size.Y > 20
            && _lastCommandLabel.GetVisibleLineCount() > 0, what + ": the actual Log has readable visible content");
        var scroll = _lastCommandLabel.GetVScrollBar();
        Check(scroll.Value + scroll.Page >= scroll.MaxValue - 1, what + ": the newest result is at the visible bottom");
    }

    private void CheckTooltipCarriesTheWholeBlock() => Check(
        _lastCommandLabel.TooltipText == _mainGame.LastCommandText, "the tooltip preserves the whole latest result");

    private string[] LatestResultTail() => _mainGame.LastCommandText.Split('\n').TakeLast(3).ToArray();

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
        CheckExit.Quit(this, exitCode);
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
