using Godot;
using IC2.Engine.Persistence;

namespace IC2.Slice.UI;

/// <summary>
/// The main menu's "Load" screen (<c>docs/game-design.md</c> §"User interface" item 1). Lists native
/// save files (<see cref="SaveManager.PeekSummaryFile"/>) under <c>user://saves</c>
/// <strong>[designed]</strong>: no earlier task established a save-file location convention for a
/// Godot front end, so this picks Godot's own per-user data directory, the same place every other
/// Godot project keeps its own saves, rather than writing into the repository checkout.
/// </summary>
/// <remarks>
/// <strong>Known gap, reported rather than worked around (docs/tasks/T24.md's own instruction: "If a
/// screen needs an engine or view-model capability that GameSession doesn't expose, do NOT change src/.
/// STOP and report what's missing").</strong> <c>IC2.Engine.Presentation.GameSession</c>'s only
/// constructor always calls <c>GameStateFactory.CreateInitial</c> — there is no way to hand it an
/// already-restored <c>GameState</c> (what <see cref="SaveManager.LoadFile"/> actually produces). This
/// screen can therefore list saves and read their summary metadata, but cannot yet resume one into a
/// playable session; selecting a save says so plainly instead of silently doing nothing or crashing.
/// <c>docs/tasks/T94.md</c> already owns malformed-save handling and the load-seat hazard for whatever
/// task adds that constructor/factory seam.
/// </remarks>
public partial class LoadGameScreen : Control
{
    public event Action? BackRequested;

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

    private void OnContinuePressed(string path)
    {
        _messageLabel.Text =
            "Resuming a saved game is not available yet: GameSession has no constructor that accepts a "
            + "restored GameState (only GameStateFactory.CreateInitial). See this task's PR body.";
    }
}
