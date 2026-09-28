using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T95.md</c> (#467) Done-when 6: a headless run that starts a game, saves through the
/// main game screen's own Save action (the real <c>GameSession.Submit("save ...")</c> path, via
/// <see cref="MainGameScreen.PressSaveForCheck"/>), resumes it through the Load screen and
/// <see cref="AppRoot"/> (the same routing a real player's "Continue" click reaches, via
/// <see cref="LoadGameScreen.ContinueForCheck"/> and this task's own <c>AppRoot.ShowLoadGame</c> wiring),
/// ends a turn, and exits 0 — or 1, with a <c>PASS</c>/<c>FAIL</c> line naming whichever check failed. Run
/// headless via:
/// <code>
/// Godot_..._console.exe --headless --path godot res://Checks/SaveResumeCheck.tscn --quit-after 5
/// </code>
/// <strong>No <c>--user-data-dir</c> flag exists in this Godot build</strong> (checked against this
/// build's own <c>--help</c>: no such option, and none of the "data-dir"/"profile"/"config" flags it does
/// list override <c>user://</c> per invocation) to redirect <c>user://</c> (where the real Save action
/// always writes, <c>MainGameScreen</c>'s own <c>user://saves</c> convention) to a temporary directory for
/// this one process. So this check does two things instead (rework round 1, N2): it saves under a fixed,
/// check-specific file-name suffix (<see cref="MainGameScreen.PressSaveForCheck"/>'s own
/// <c>checkUniqueSuffix</c> parameter — <c>"t95-saveresumecheck"</c> here — never the bare
/// <c>&lt;scenario&gt;-turn-&lt;N&gt;.json</c> name a real player's own click produces), so this check's
/// own save file can never be mistaken for, collide with, or overwrite a real save of the same scenario
/// and turn; and it deletes only that uniquely-named file (and the <c>saves</c> directory, if it left it
/// empty) once it is done with it, in a <c>finally</c> block (<see cref="_Ready"/>) that runs whether the
/// checks above passed, failed, or threw — a real player's own, differently-named save is never touched
/// by either the write or the cleanup.
/// </summary>
/// <remarks>
/// Drives the real <see cref="AppRoot"/>/<see cref="NewGameFlow"/>/<see cref="LoadGameScreen"/>/
/// <see cref="MainGameScreen"/> classes directly, through the same public hooks
/// <c>godot/Checks/RulesetFlowCheck.cs</c> and <c>godot/Checks/ScreenshotTour.cs</c> already establish
/// (<c>ConfirmSelection</c>, <c>ConfirmSeatAndStart</c>, <c>SubmitForCheck</c>) plus this task's own two
/// additions (<see cref="MainGameScreen.PressSaveForCheck"/>, <see cref="LoadGameScreen.ContinueForCheck"/>)
/// — never a hand-built mirror of the UI, and never simulated mouse clicks at hardcoded coordinates.
/// </remarks>
public partial class SaveResumeCheck : Node
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
            GD.PrintErr($"SaveResumeCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            CleanUpSavedFile();
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"SaveResumeCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    /// <summary>
    /// Deletes the file <see cref="_savedPathToCleanUp"/> names, and its containing directory if that
    /// leaves it empty — see this class's own summary for why this check does its own cleanup rather than
    /// redirecting <c>user://</c>. Best effort: a leftover file is a nuisance the next run overwrites
    /// anyway (the save's own filename is deterministic, <c>&lt;scenario&gt;-turn-&lt;N&gt;</c>), not a
    /// reason to change this check's own exit code.
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
            GD.PrintErr($"SaveResumeCheck: could not clean up '{_savedPathToCleanUp}': {ex.Message}");
        }
    }

    private void Run()
    {
        var appRoot = new AppRoot();
        AddChild(appRoot);

        // ---- start a game (the default ruleset card, seat 0 -- the same defaults RulesetFlowCheck's own
        // "picking the default" case exercises) ----
        appRoot.ShowNewGameFlow();
        var flow = appRoot.CurrentScreen as NewGameFlow;
        if (!Check(flow?.Chooser is not null, "New Game reaches the ruleset chooser"))
        {
            return;
        }

        flow!.Chooser!.ConfirmSelection();
        if (!Check(flow.SeatScreen is not null, "the seat step is reached after the ruleset chooser"))
        {
            return;
        }

        flow.SeatScreen!.ConfirmSeatAndStart();

        if (!Check(appRoot.CurrentScreen is MainGameScreen, "Start Game reaches the main game screen"))
        {
            return;
        }

        var originalScreen = (MainGameScreen)appRoot.CurrentScreen!;
        var originalState = originalScreen.Session.State;
        var originalActiveSeat = originalState.ActiveNationId;
        var originalTurn = originalState.Calendar.TurnIndex;

        // ---- save through the main game screen's own Save action (Done-when 1, the Godot half) ----
        // N2: a check-unique suffix, never the bare filename a real player's own click would produce.
        originalScreen.PressSaveForCheck("t95-saveresumecheck");
        var savedPath = originalScreen.LastSavedPath;
        _savedPathToCleanUp = savedPath;
        if (!Check(
            savedPath is not null && File.Exists(savedPath),
            $"the Save action wrote a file (path: {savedPath ?? "<none>"})"))
        {
            return;
        }

        // ---- go through the Load screen and AppRoot to resume it (Done-when 2, the Godot half) ----
        appRoot.ShowLoadGame();
        if (!Check(appRoot.CurrentScreen is LoadGameScreen, "AppRoot shows the Load screen"))
        {
            return;
        }

        var loadScreen = (LoadGameScreen)appRoot.CurrentScreen!;
        loadScreen.ContinueForCheck(savedPath!);

        if (!Check(
            appRoot.CurrentScreen is MainGameScreen,
            "Continue routes back to the main game screen through AppRoot.ShowLoadGame's own GameResumed wiring"))
        {
            return;
        }

        var resumedScreen = (MainGameScreen)appRoot.CurrentScreen!;
        Check(
            !ReferenceEquals(resumedScreen, originalScreen),
            "the resumed screen is a new MainGameScreen instance, not the original one still on screen");

        var resumedState = resumedScreen.Session.State;
        Check(
            resumedState.ActiveNationId == originalActiveSeat,
            $"the resumed active seat matches the saved one ('{resumedState.ActiveNationId}' vs '{originalActiveSeat}')");
        Check(
            resumedState.Calendar.TurnIndex == originalTurn,
            $"the resumed turn matches the saved one ({resumedState.Calendar.TurnIndex} vs {originalTurn})");
        Check(
            Equals(resumedState, originalState),
            "the resumed GameState equals the saved one in full (GameState is a value-equal record tree)");

        // ---- end a turn (proves the resumed session is actually playable, not just constructed) ----
        var beforeEndTurn = resumedState.Calendar.TurnIndex;
        var beforeEndSeat = resumedState.ActiveNationId;
        resumedScreen.SubmitForCheck("end");
        var afterEnd = resumedScreen.Session.State;
        Check(
            afterEnd.Calendar.TurnIndex != beforeEndTurn || afterEnd.ActiveNationId != beforeEndSeat,
            "ending a turn on the resumed session actually advances it (turn or active seat changed)");
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
