using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen's news log — <c>docs/game-design.md</c> §"User interface" item 2: "persistent
/// and dismissible, not modal, browsed often but never blocking." Toggled by the bottom toolbar's own
/// "News" button (<see cref="MainGameScreen"/>); when hidden it neither draws nor accepts input, so it
/// never sits on top of the map the way the original's own news popups did.
/// </summary>
public partial class NewsLogPanel : Control
{
    public required GameSession Session { get; init; }

    private RichTextLabel _text = null!;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;

        var panel = UiKit.MakePanel(new Color(UiKit.PanelColor, 0.92f));
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        column.AddChild(header);
        header.AddChild(UiKit.MakeLabel("News", 18, UiKit.AccentColor));
        header.AddChild(UiKit.MakeButton("Dismiss", () => Visible = false, 12));

        _text = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(0, 160),
            ScrollFollowing = false,
            BbcodeEnabled = false,
        };
        column.AddChild(_text);
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            Refresh();
        }
    }

    /// <summary>Re-reads the news log through the command layer (<c>Submit("news")</c>) — never a direct
    /// read of <c>GameState.NewsLog</c> that bypasses <see cref="GameSession"/>.</summary>
    public void Refresh()
    {
        var output = Session.Submit("news");
        _text.Text = string.Join('\n', output.Lines);
    }
}
