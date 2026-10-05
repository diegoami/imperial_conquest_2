using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>
/// The original's post-battle "Offer of peace" box — <c>docs/tasks/T139.md</c>: titled, the engine's
/// offer lines one per line, and two buttons, Yes and No. Modal, built with <see cref="ModalOverlay"/> as
/// <see cref="BattleResultScreen"/> is. It has no Close button and Esc does not dismiss it: the offer is
/// answered, as in the original.
/// </summary>
/// <remarks>
/// Reads only <see cref="PeaceOfferViewModel"/>. The screen does not submit anything itself: it raises
/// <see cref="Answered"/> with the button's command, and <see cref="MainGameScreen"/> submits it through
/// its own path. The layout is <c>docs/tasks/T139.md</c>'s <em>[designed]</em> default.
/// </remarks>
public partial class PeaceOfferScreen : Control
{
    public required PeaceOfferViewModel Model { get; init; }

    /// <summary>Raised with the answer's command line (<c>peace-yes</c> or <c>peace-no</c>).</summary>
    public event Action<string>? Answered;

    /// <summary>Exposed for <c>godot/Checks/PeaceOfferCheck.cs</c> to assert the rendered text.</summary>
    public Label TitleLabel { get; private set; } = null!;

    /// <summary>The body's labels, one per engine line, in order.</summary>
    public IReadOnlyList<Label> BodyLabels => _bodyLabels;

    /// <summary>The buttons, in order (Yes, No).</summary>
    public IReadOnlyList<Button> Buttons => _buttons;

    private readonly List<Label> _bodyLabels = new();
    private readonly List<Button> _buttons = new();

    public override void _Ready()
    {
        ModalOverlay.Build(out var root, out var content, new Color(0f, 0f, 0f, 0.65f), 520f);
        AddChild(root);

        TitleLabel = UiKit.MakeLabel(Model.Title, 22, UiKit.AccentColor);
        content.AddChild(TitleLabel);

        foreach (var line in Model.Body)
        {
            var label = UiKit.MakeLabel(line, 16, UiKit.TextColor);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _bodyLabels.Add(label);
            content.AddChild(label);
        }

        content.AddChild(new HSeparator());
        foreach (var spec in Model.Buttons)
        {
            var command = spec.Command;
            var button = UiKit.MakeButton(spec.Label, () => Answered?.Invoke(command));
            _buttons.Add(button);
            content.AddChild(button);
        }
    }

    /// <summary>Presses the button with <paramref name="label"/> as a click would; for the check.</summary>
    public void PressForCheck(string label)
    {
        var index = _buttons.FindIndex(b => b.Text == label);
        if (index < 0)
        {
            throw new ArgumentException($"No button '{label}'.", nameof(label));
        }

        Answered?.Invoke(Model.Buttons[index].Command);
    }
}
