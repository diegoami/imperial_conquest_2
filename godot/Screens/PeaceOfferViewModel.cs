using IC2.Engine.Presentation;

namespace IC2.Slice.Screens;

/// <summary>
/// One button of the Offer of peace window and the command it submits.
/// </summary>
/// <param name="Label">The button's caption.</param>
/// <param name="Command">The command line it submits through the screen's submit path.</param>
public sealed record PeaceOfferButton(string Label, string Command);

/// <summary>
/// Everything the Offer of peace window says and offers for one pending offer, Godot-free —
/// <c>docs/tasks/T139.md</c> owns list. The same seam <see cref="BattleResultViewModel"/> and
/// <see cref="GameEndViewModel"/> established, so a plain xunit test pins the title, the body and the
/// buttons without the Godot SDK.
/// </summary>
/// <remarks>
/// <strong>The body is the engine's own lines, never a copy.</strong> <see cref="Body"/> is
/// <see cref="PendingPeaceOffer.Lines"/> taken whole, in order; nothing here is added, dropped or
/// reworded. The title is the original's box caption <strong>[Wine candidate: research report
/// 2026-10-05-battle-peace-offer.md]</strong>; the two buttons are Yes and No, and nothing else.
/// </remarks>
/// <param name="Title">The window's title.</param>
/// <param name="Body">The engine's offer lines, in order.</param>
/// <param name="Buttons">Yes then No.</param>
public sealed record PeaceOfferViewModel(
    string Title,
    IReadOnlyList<string> Body,
    IReadOnlyList<PeaceOfferButton> Buttons)
{
    /// <summary>The original's box caption.</summary>
    public const string OfferOfPeaceTitle = "Offer of peace";

    /// <summary>The command Yes submits: the engine's <c>peace-yes</c>.</summary>
    public const string YesCommand = "peace-yes";

    /// <summary>The command No submits: the engine's <c>peace-no</c>.</summary>
    public const string NoCommand = "peace-no";

    /// <summary>Builds the window's model from the engine's pending offer.</summary>
    /// <param name="offer">The offer <see cref="GameSession.PendingPeaceOfferFor"/> returned.</param>
    public static PeaceOfferViewModel FromOffer(PendingPeaceOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return new PeaceOfferViewModel(
            OfferOfPeaceTitle,
            offer.Lines,
            new[]
            {
                new PeaceOfferButton("Yes", YesCommand),
                new PeaceOfferButton("No", NoCommand),
            });
    }
}
