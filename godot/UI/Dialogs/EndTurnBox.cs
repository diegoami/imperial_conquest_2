using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// T118: the original's modal warning before a human seat ends its turn. The engine query supplies the
/// lines, while this control owns only their presentation and the two possible answers.
/// </summary>
public partial class EndTurnBox : Control
{
    /// <summary>Raised when the player chooses <c>END TURN</c>.</summary>
    public event Action? EndTurnRequested;

    /// <summary>Raised when the player chooses <c>MAKE MORE MOVES</c> or presses Escape.</summary>
    public event Action? MakeMoreMovesRequested;

    /// <summary>The warning lines in the engine query's order.</summary>
    public required IReadOnlyList<string> WarningLines { get; init; }

    public Label CaptionLabel { get; private set; } = null!;
    public IReadOnlyList<Label> WarningLabels => _warningLabels;
    public Button EndTurnButton { get; private set; } = null!;
    public Button MakeMoreMovesButton { get; private set; } = null!;

    private readonly List<Label> _warningLabels = new();
    private bool _answered;

    public override void _Ready()
    {
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(560, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        CaptionLabel = UiKit.MakeLabel("End turn ?", 22, UiKit.AccentColor);
        column.AddChild(CaptionLabel);

        foreach (var line in WarningLines)
        {
            var label = UiKit.MakeLabel(line, 16, UiKit.TextColor);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _warningLabels.Add(label);
            column.AddChild(label);
        }

        var moreMoves = UiKit.MakeLabel(
            "If you have not finished your turn click MAKE MORE MOVES.", 15, UiKit.MutedTextColor);
        moreMoves.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(moreMoves);

        var endTurn = UiKit.MakeLabel(
            "If you are finished moving this turn click END TURN.", 15, UiKit.MutedTextColor);
        endTurn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(endTurn);

        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);

        MakeMoreMovesButton = UiKit.MakeButton("MAKE MORE MOVES", () => Answer(makeMoreMoves: true));
        EndTurnButton = UiKit.MakeButton("END TURN", () => Answer(makeMoreMoves: false));
        buttons.AddChild(MakeMoreMovesButton);
        buttons.AddChild(EndTurnButton);
        EndTurnButton.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Answer(makeMoreMoves: true);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Answer(bool makeMoreMoves)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;
        if (makeMoreMoves)
        {
            MakeMoreMovesRequested?.Invoke();
        }
        else
        {
            EndTurnRequested?.Invoke();
        }
    }
}
