using Godot;

namespace IC2.Slice.UI;

/// <summary>Independent presentation-only panel visibility. Reopen controls live outside the column.</summary>
public partial class StrategicPanels : VBoxContainer
{
    private readonly Dictionary<string, (Control Frame, Button Toggle, Action<bool>? Change)> _panels = new();
    public HBoxContainer Toggles { get; } = new();
    public event Action? Changed;
    public event Action? OpenRequested;
    public bool AnyOpen => _panels.Values.Any(p => p.Frame.Visible);

    public void AddPanel(string id, string title, Control content, float weight, bool scroll = false, Action<bool>? change = null)
    {
        var frame = UiKit.MakePanel(UiKit.PanelColor);
        frame.Name = id + "Panel";
        frame.SizeFlagsVertical = SizeFlags.ExpandFill;
        frame.SizeFlagsStretchRatio = weight;
        // Compact framing leaves the existing content's own padding and scrolling intact.
        var style = (StyleBoxFlat)frame.GetThemeStylebox("panel").Duplicate();
        style.ContentMarginLeft = style.ContentMarginRight = 4;
        style.ContentMarginTop = style.ContentMarginBottom = 4;
        frame.AddThemeStyleboxOverride("panel", style);
        AddChild(frame);
        var column = new VBoxContainer();
        frame.AddChild(column);
        var header = new HBoxContainer();
        column.AddChild(header);
        var caption = UiKit.MakeLabel(title, AppTheme.Body, UiKit.AccentColor);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(caption);
        var close = UiKit.MakeButton("×", () =>
        {
            SetOpen(id, false);
            _panels[id].Toggle.GrabFocus();
        }, AppTheme.Body);
        close.CustomMinimumSize = new Vector2(28, 24);
        close.Name = id + "Close";
        UiKit.SetHint(close, "Hide " + title, true);
        header.AddChild(close);
        content.SizeFlagsVertical = SizeFlags.ExpandFill;
        if (scroll)
        {
            var scroller = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            column.AddChild(scroller);
            content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scroller.AddChild(content);
        }
        else column.AddChild(content);
        var toggle = new Button { Name = id + "Toggle", Text = title, ToggleMode = true,
            ButtonPressed = true, CustomMinimumSize = new Vector2(0, 28) };
        UiKit.SetHint(toggle, "Show or hide " + title, true);
        toggle.Toggled += open => SetOpen(id, open);
        Toggles.AddChild(toggle);
        _panels.Add(id, (frame, toggle, change));
    }

    public Control Frame(string id) => _panels[id].Frame;
    public Button Toggle(string id) => _panels[id].Toggle;
    public bool IsOpen(string id) => _panels[id].Frame.Visible;

    public void SetOpen(string id, bool open)
    {
        if (open) OpenRequested?.Invoke();
        var panel = _panels[id];
        panel.Change?.Invoke(open);
        Synchronize(id, open);
    }

    /// <summary>Reflect existing news dismissal/handoff visibility without changing its policy.</summary>
    public void Synchronize(string id, bool open)
    {
        var panel = _panels[id];
        panel.Frame.Visible = open;
        panel.Toggle.SetPressedNoSignal(open);
        Changed?.Invoke();
    }
}
