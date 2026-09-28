using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>
/// <c>docs/game-design.md</c> §"User interface" item 5: "a blocking 'Pass to [Nation]' screen between
/// human turns, with the optional blind-info-hiding mode." Always an opaque, full-rect backdrop — "needs
/// to be unmissable" (item 5) means the previous seat's own screen state is fully hidden the instant this
/// shows, blind mode or not; <see cref="HotseatHandoffInfo.Blind"/> only decides whether this screen also
/// withholds the outgoing seat's own turn/date recap it would otherwise show.
/// </summary>
public partial class HotseatHandoffScreen : Control
{
    public required HotseatHandoffInfo Info { get; init; }

    /// <summary>
    /// The current calendar line to show when <see cref="HotseatHandoffInfo.Blind"/> is
    /// <see langword="false"/> — the one piece of session state this screen can show without it being
    /// "the previous seat's own information" in any meaningful sense (every seat sees the same calendar).
    /// <see langword="null"/> omits the line entirely (blind mode, or when the caller has none to give).
    /// </summary>
    public string? CalendarLine { get; init; }

    /// <summary>Raised once the incoming seat confirms they are ready.</summary>
    public event Action? Continued;

    /// <summary>Exposed for <c>godot/Screens/Checks/**</c> to assert on the rendered text without re-deriving it.</summary>
    public Label MessageLabel { get; private set; } = null!;

    /// <summary>Exposed the same way — present only when <see cref="HotseatHandoffInfo.Blind"/> is <see langword="true"/>.</summary>
    public Label? BlindNoticeLabel { get; private set; }

    public override void _Ready()
    {
        ModalOverlay.Build(out var root, out var content, UiKit.Background);
        AddChild(root);

        content.AddChild(UiKit.MakeLabel("Pass the Device", 22, UiKit.AccentColor));

        MessageLabel = UiKit.MakeLabel($"Pass to {Info.NextNationName}.", 18, UiKit.TextColor);
        content.AddChild(MessageLabel);

        if (Info.Blind)
        {
            BlindNoticeLabel = UiKit.MakeLabel(
                "Blind hotseat: the previous seat's own information stays hidden.", 13, UiKit.MutedTextColor);
            content.AddChild(BlindNoticeLabel);
        }
        else if (!string.IsNullOrEmpty(CalendarLine))
        {
            content.AddChild(UiKit.MakeLabel(CalendarLine, 13, UiKit.MutedTextColor));
        }

        content.AddChild(new HSeparator());
        content.AddChild(UiKit.MakeButton($"I am {Info.NextNationName} — Continue", Continue));
    }

    /// <summary>Confirms the incoming seat is ready — what the "Continue" button does, exposed under its
    /// own name for <c>godot/Screens/Checks/**</c>, the same convention <see cref="BattleResultScreen.Close"/>
    /// uses.</summary>
    public void Continue() => Continued?.Invoke();
}
