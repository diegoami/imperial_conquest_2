using Godot;
using IC2.Slice.Diagnostics;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen's news log — <c>docs/game-design.md</c> §"User interface" item 2: "persistent
/// and dismissible, not modal, browsed often but never blocking." Toggled by the bottom toolbar's own
/// "News" button (<see cref="MainGameScreen"/>); when hidden it neither draws nor accepts input, so it
/// never sits on top of the map the way the original's own news popups did. T235 reveals its latest
/// reports when each human strategic view is ready, after the existing handoff/result sequence.
/// </summary>
public partial class NewsLogPanel : Control
{
    public required GameSession Session { get; init; }

    private RichTextLabel _text = null!;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;

        var panel = UiKit.MakePanel(AppTheme.NewsPanel);
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", AppTheme.PanelGap);
        panel.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", AppTheme.PanelGap);
        column.AddChild(header);
        header.AddChild(UiKit.MakeLabel("News", AppTheme.Header, UiKit.AccentColor));
        header.AddChild(UiKit.MakeButton("Dismiss", () => Visible = false, AppTheme.Button));

        _text = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(0, 160),
            ScrollFollowing = false,
            BbcodeEnabled = false,
        };
        _text.AddThemeColorOverride("default_color", UiKit.TextColor);
        _text.AddThemeFontSizeOverride("normal_font_size", AppTheme.Body);
        column.AddChild(_text);
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            ShowLatest();
        }
    }

    /// <summary>Owner policy T235: reveal the existing panel at the latest reports.</summary>
    public void ShowLatest()
    {
        Visible = true;
        Refresh();
        Callable.From(() =>
        {
            if (IsInsideTree() && GodotObject.IsInstanceValid(_text))
                _text.ScrollToLine(Math.Max(0, _text.GetLineCount() - 1));
        }).CallDeferred();
    }

    /// <summary>Use the command's read-only formatter without consuming pending session outputs.</summary>
    public void Refresh()
    {
        var lines = AppDiagnostics.Operation(DiagnosticEvent.Command, "news", Session.ReadNewsLines,
            command: "news", turn: Session.State.Calendar.TurnIndex, nation: Session.State.ActiveNationId);
        _text.Text = string.Join('\n', lines);
    }
}
