using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>The owner's last-100-results log. Plain text, newest last, no save or diagnostics feed.</summary>
public partial class CommandHistoryPanel : RichTextLabel
{
    public CommandResultHistory History { get; } = new();
    public required Func<string> ViewerKey { get; init; }
    public string LatestResult => History.LatestResult;

    public override void _Ready()
    {
        Name = "CommandHistory";
        BbcodeEnabled = false;
        ScrollActive = true;
        ScrollFollowing = true;
        SelectionEnabled = true;
        FocusMode = FocusModeEnum.All;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeColorOverride("default_color", UiKit.TextColor);
        AddThemeFontSizeOverride("normal_font_size", AppTheme.Note);
        UiKit.SetHint(this, "Last 100 command results, newest at the bottom. Cleared when the viewing player changes.", true);
        SynchronizeViewer();
    }

    public override void _Process(double delta) => SynchronizeViewer();

    public void AppendResult(string result)
    {
        SynchronizeViewer();
        History.Append(result);
        RefreshText();
    }

    public void SynchronizeViewer()
    {
        var before = History.LatestResult;
        var count = History.Count;
        History.SetViewer(ViewerKey());
        if (count != History.Count || before != History.LatestResult) RefreshText();
    }

    private void RefreshText()
    {
        Text = string.Join("\n\n", History.Entries);
        TooltipText = History.LatestResult;
    }
}
