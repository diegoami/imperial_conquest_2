using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// #1286 (v0.6.2 play-through F3): a refused order's plain message, shown over the top of the map where the
/// player is looking, instead of only in the faint status lines under it. It closes by itself after
/// <see cref="ShownSeconds"/> or when clicked; the status lines keep the message as history.
/// </summary>
/// <remarks>
/// [designed]. The original shows each refusal in an OK box (research
/// <c>2026-10-05-refusal-texts-and-conditions.md</c>); a notice that needs no click keeps the same words
/// in view without stopping play. Orders given from a dialog are not shown here: the dialog shows its own
/// reply.
/// </remarks>
public partial class OrderNotice : PanelContainer
{
    /// <summary>How long a notice stays before it closes by itself.</summary>
    public const double ShownSeconds = 4.0;

    private const float TopMargin = 12f;
    private const float MaxWidth = 560f;

    private readonly Label _label;
    private double _remaining;

    public OrderNotice()
    {
        var style = new StyleBoxFlat
        {
            BgColor = UiKit.PanelColorRaised,
            BorderColor = UiKit.AccentColor,
            CornerRadiusTopLeft = AppTheme.CornerRadius,
            CornerRadiusTopRight = AppTheme.CornerRadius,
            CornerRadiusBottomLeft = AppTheme.CornerRadius,
            CornerRadiusBottomRight = AppTheme.CornerRadius,
            ContentMarginLeft = AppTheme.PanelMarginSide,
            ContentMarginRight = AppTheme.PanelMarginSide,
            ContentMarginTop = AppTheme.PanelMarginVertical,
            ContentMarginBottom = AppTheme.PanelMarginVertical,
        };
        style.SetBorderWidthAll(2);
        AddThemeStyleboxOverride("panel", style);

        _label = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_label);

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "Click to close";
    }

    /// <summary>The message shown, or an empty string when the notice is closed.</summary>
    public string Message => Visible ? _label.Text : string.Empty;

    /// <summary>Shows <paramref name="message"/> at the top centre of the parent, replacing any notice already shown.</summary>
    public void ShowMessage(string message)
    {
        _label.Text = message;
        _remaining = ShownSeconds;
        Visible = true;
        Place();
    }

    /// <summary>Closes the notice at once.</summary>
    public void Close()
    {
        Visible = false;
        _remaining = 0;
    }

    public override void _Ready()
    {
        if (GetParent() is Control parent)
        {
            parent.Resized += Place;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _remaining -= delta;
        if (_remaining <= 0)
        {
            Close();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true })
        {
            Close();
            AcceptEvent();
        }
    }

    private void Place()
    {
        if (GetParent() is not Control parent)
        {
            return;
        }

        var width = Mathf.Min(MaxWidth, parent.Size.X - (2 * TopMargin));
        _label.CustomMinimumSize = new Vector2(width - (2 * AppTheme.PanelMarginSide), 0);
        ResetSize();
        Position = new Vector2((parent.Size.X - Size.X) / 2, TopMargin);
    }
}
