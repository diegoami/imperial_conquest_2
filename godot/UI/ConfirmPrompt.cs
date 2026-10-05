using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// T99: the original's own attack confirmation as a modal Yes/No prompt —
/// <c>docs/investigations/original-ui-command-audit.md</c> §2.1, confirmed by
/// <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c>: before an attack on a nation the
/// active seat is not at war with, the game asks <em>"Are you sure you want to attack this city ?"</em>
/// (or army, or fleet — the original's own spacing included). <strong>Yes</strong> raises
/// <see cref="Confirmed"/>, <strong>No</strong> raises <see cref="Refused"/>; the screen that owns the
/// session (<see cref="MainGameScreen"/>) wires them to <see cref="GameMapView.AnswerAttackConfirmation"/>
/// — Yes submits the order (the engine composes the declaration of war in front of it), No drops the
/// selection.
/// </summary>
/// <remarks>
/// <para>
/// A generic Yes/No modal, not an attack prompt: the question is set by the caller
/// (<see cref="Question"/>) and the two answers are the only outcomes. Later tasks reuse it for the
/// original's other confirmations (<c>Disband army</c>, <c>Scuttle fleet</c>, Close without saving —
/// the audit's §1 rows that ask one).
/// </para>
/// <para>
/// Built in code, not a <c>.tscn</c>, exactly like <see cref="GameMapView"/> and
/// <see cref="ContextPanel"/> (the whole main game screen is); it is shown by
/// <see cref="MainGameScreen.ShowOverlay"/> as a full-rect modal over the map, and it takes the
/// keyboard focus on its <strong>Yes</strong> button so the player can answer with Enter, the way the
/// original's dialogs do. The question label wraps, so the original's full sentence never widens the
/// panel — the same reason <see cref="ContextPanel"/>'s own <see cref="Label"/>s wrap.
/// </para>
/// </remarks>
public partial class ConfirmPrompt : Control
{
    /// <summary>Raised when the player answers <strong>Yes</strong>.</summary>
    public event Action? Confirmed;

    /// <summary>Raised when the player answers <strong>No</strong>.</summary>
    public event Action? Refused;

    /// <summary>The question the prompt asks — the caller's own exact text (for the attack prompt,
    /// <see cref="MapClickOutcome.ConfirmationText"/>, the original's own wording and spacing).</summary>
    public required string Question { get; init; }

    private Label _questionLabel = null!;

    public override void _Ready()
    {
        // A dim full-rect backdrop: the map stays visible behind the prompt but is not clickable
        // while it is open (the overlay covers it), the same modal treatment MainGameScreen's other
        // overlays get.
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var box = new CenterContainer();
        box.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(box);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(420, 0);
        box.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        _questionLabel = UiKit.MakeLabel(Question, 16, UiKit.TextColor);
        _questionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_questionLabel);

        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);

        var no = UiKit.MakeButton("No", () => Answer(yes: false));
        var yes = UiKit.MakeButton("Yes", () => Answer(yes: true));
        buttons.AddChild(no);
        buttons.AddChild(yes);

        // The keyboard can answer: Enter confirms (the focused button), Esc refuses — the same two
        // answers the buttons give, never a third outcome.
        yes.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Esc answers No — the same answer the button gives, never a third outcome. Unhandled rather
        // than _GuiInput because the Yes button holds the keyboard focus, so the key reaches this
        // node through the tree's unhandled path, not this control's own focus path.
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Answer(yes: false);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Answer(bool yes)
    {
        if (yes)
        {
            Confirmed?.Invoke();
        }
        else
        {
            Refused?.Invoke();
        }
    }
}
