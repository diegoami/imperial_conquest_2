using Godot;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The main menu's "Load" screen (<c>docs/game-design.md</c> §"User interface" item 1). Lists native
/// save files (<see cref="SaveManager.PeekSummaryFile"/>) under <c>user://saves</c>
/// <strong>[designed]</strong>: no earlier task established a save-file location convention for a
/// Godot front end, so this picks Godot's own per-user data directory, the same place every other
/// Godot project keeps its own saves, rather than writing into the repository checkout.
/// </summary>
/// <remarks>
/// <para>
/// <strong>T95 (#467): resuming a chosen save.</strong> <see cref="GameSession"/> now has a resume
/// constructor (<c>src/IC2.Engine/Presentation/GameSession.cs</c>) that takes an already-loaded
/// <see cref="SaveManager.LoadFile"/> result instead of always calling
/// <c>GameStateFactory.CreateInitial</c> — the gap this screen's own remarks used to describe.
/// <see cref="OnContinuePressed"/> reads the chosen save's own recorded scenario/world/ruleset ids
/// (<see cref="SaveManager.PeekSummaryFile"/>), resolves them through <see cref="GameDataContext.Repository"/>
/// (the same shared repository <see cref="NewGameFlow"/> uses), and builds the resumed session.
/// </para>
/// <para>
/// <strong>This screen does not itself navigate to <see cref="MainGameScreen"/>.</strong>
/// <c>godot/UI/AppRoot.cs</c> is the only place that owns swapping the displayed screen and keeping its
/// own <c>CurrentScreen</c> in step (its own remarks: "Owns every screen swap itself ... so
/// <c>ScreenshotTour.cs</c> can navigate and inspect the real scene tree"). Swapping the scene tree from
/// here directly, bypassing <c>AppRoot</c>, would leave its own <c>CurrentScreen</c> pointing at this
/// (freed) screen — exactly the invariant its own remarks say <c>ScreenshotTour.cs</c> relies on — so
/// this screen instead builds the resumed session fully and raises <see cref="GameResumed"/> with it,
/// the same <c>event Action&lt;GameSession&gt;?</c> shape <see cref="NewGameFlow.GameStarted"/> already
/// uses for <c>AppRoot.ShowNewGameFlow</c> to subscribe to. <strong>Added 2026-09-28, after PR #481's
/// STOP</strong>: T95's Owns list was widened to include exactly this one wiring line in
/// <c>AppRoot.ShowLoadGame</c> (mirroring its own <c>ShowNewGameFlow</c>), so a "Continue" press now
/// really does reach <see cref="MainGameScreen"/>.
/// </para>
/// </remarks>
public partial class LoadGameScreen : Control
{
    public event Action? BackRequested;

    /// <summary>
    /// Raised once <see cref="OnContinuePressed"/> has successfully built a resumed
    /// <see cref="GameSession"/> from the chosen save — see this class's own remarks for why this screen
    /// does not itself navigate to <see cref="MainGameScreen"/>.
    /// </summary>
    public event Action<GameSession>? GameResumed;

    private const string SavesDirectory = "user://saves";

    private Label _messageLabel = null!;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        column.AddChild(header);

        var backButton = UiKit.MakeButton("< Back", () => BackRequested?.Invoke(), 14);
        backButton.CustomMinimumSize = new Vector2(90, 34);
        header.AddChild(backButton);
        header.AddChild(UiKit.MakeLabel("Load Game", 26, UiKit.AccentColor));

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        column.AddChild(list);

        _messageLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.MutedTextColor);
        column.AddChild(_messageLabel);

        PopulateSaves(list);
    }

    private void PopulateSaves(VBoxContainer list)
    {
        var directory = ProjectSettings.GlobalizePath(SavesDirectory);
        if (!Directory.Exists(directory))
        {
            _messageLabel.Text = "No saved games found.";
            return;
        }

        var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            _messageLabel.Text = "No saved games found.";
            return;
        }

        foreach (var path in files)
        {
            AddSaveRow(list, path);
        }
    }

    private void AddSaveRow(VBoxContainer list, string path)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        list.AddChild(row);

        string label;
        try
        {
            var summary = SaveManager.PeekSummaryFile(path);
            label = $"{summary.Label} — {summary.ScenarioId}, turn {summary.TurnIndex}";
        }
        catch (Exception ex)
        {
            // T94 owns malformed-save handling; this screen reports the file could not be read rather
            // than throwing out of _Ready or silently skipping it.
            label = $"{Path.GetFileName(path)} — could not be read ({ex.Message})";
        }

        row.AddChild(UiKit.MakeLabel(label, 14, UiKit.TextColor));
        row.AddChild(UiKit.MakeButton("Continue", () => OnContinuePressed(path), 14));
    }

    /// <summary>
    /// Resumes <paramref name="path"/> into a playable <see cref="GameSession"/> and raises
    /// <see cref="GameResumed"/> — <c>docs/tasks/T95.md</c> Done-when 2 and Done-when 5 (a missing file, a
    /// malformed save, or a save for a world/ruleset/scenario this build does not have is reported in
    /// <see cref="_messageLabel"/> rather than thrown out of a button handler).
    /// </summary>
    private void OnContinuePressed(string path)
    {
        SaveSummary summary;
        try
        {
            summary = SaveManager.PeekSummaryFile(path);
        }
        catch (Exception ex)
        {
            _messageLabel.Text = $"Could not read '{Path.GetFileName(path)}': {ex.Message}";
            return;
        }

        var repository = GameDataContext.Repository;
        var world = repository.WorldById(summary.WorldId);
        var ruleset = repository.RulesetById(summary.RulesetId);
        var scenario = repository.ScenarioById(summary.ScenarioId);
        if (world is null || ruleset is null || scenario is null)
        {
            _messageLabel.Text =
                $"'{Path.GetFileName(path)}' names a world/ruleset/scenario this build does not have "
                + $"(world '{summary.WorldId}', ruleset '{summary.RulesetId}', scenario '{summary.ScenarioId}').";
            return;
        }

        Engine.Model.SaveGame save;
        try
        {
            save = SaveManager.LoadFile(path, world, ruleset);
        }
        catch (Exception ex)
        {
            _messageLabel.Text = $"Could not load '{Path.GetFileName(path)}': {ex.Message}";
            return;
        }

        var session = new GameSession(world, ruleset, scenario, save);

        // Done-when 2/4: the resumed session's own active seat and human-seat bookkeeping already come
        // from the save's own state (GameSession.ResumeFrom). AppRoot.ShowLoadGame's own GameResumed
        // subscriber swaps to MainGameScreen right after this line, so this message is only ever seen for
        // the instant before that swap (or not at all, in the normal click-through case) -- kept anyway
        // for a caller that does not swap screens, such as this task's own headless check.
        _messageLabel.Text =
            $"Resumed '{summary.Label}' — scenario '{scenario.Id}', turn {summary.TurnIndex}. "
            + $"Active seat: {session.State.NationById(session.State.ActiveNationId)?.Name ?? session.State.ActiveNationId}.";

        GameResumed?.Invoke(session);
    }

    /// <summary>
    /// Resumes <paramref name="path"/> exactly as a real "Continue" click would (<see cref="OnContinuePressed"/>)
    /// — exposed so <c>godot/Checks/SaveResumeCheck.cs</c> and <c>godot/Checks/SaveResumeScreenshotTour.cs</c>
    /// can drive this screen without simulating mouse coordinates, the same reason
    /// <see cref="MainGameScreen.SubmitForCheck"/> and <see cref="ScenarioSeatScreen.ConfirmSeatAndStart"/>
    /// are public.
    /// </summary>
    public void ContinueForCheck(string path) => OnContinuePressed(path);
}
