namespace IC2.Engine.Presentation;

/// <summary>
/// One post-battle offer of peace the engine holds for a human seat, as typed data
/// (<c>docs/tasks/T139.md</c>, "The engine exposes the offer"; T25's pattern for battles, T138's for
/// falls): the offered human nation, the battle's winner and loser, and the offer's lines as the engine
/// builds them (<see cref="GameSession.PeaceTreatyOfferDialogLines"/>), in the order the CLI prints them.
/// </summary>
/// <remarks>
/// A read-only snapshot. Reading it never changes the pending set, and answering the offer
/// (<c>peace-yes</c>, <c>peace-no</c>, or the lapse at the seat's next <c>end</c>) is still the engine's
/// own command path.
/// </remarks>
/// <param name="OfferedHumanNationId">The human seat the offer is addressed to.</param>
/// <param name="WinnerNationId">The battle's winner.</param>
/// <param name="LoserNationId">The battle's loser.</param>
/// <param name="Lines">The offer's lines, in the order the CLI prints them; read-only.</param>
public sealed record PendingPeaceOffer(
    string OfferedHumanNationId,
    string WinnerNationId,
    string LoserNationId,
    IReadOnlyList<string> Lines);
