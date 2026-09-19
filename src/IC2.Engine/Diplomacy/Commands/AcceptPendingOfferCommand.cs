using IC2.Engine.Core;
using IC2.Engine.Diplomacy;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// The human seat accepts the currently pending trade or alliance offer (DoD 10) —
/// <c>news-log-format-and-messages.md</c> Q5: "The offer's only other reader is
/// <c>TPolitics_MakeTrade</c>, where a pending trade offer from that nation waives the three-partner
/// limit." Accepting does <strong>not</strong> clear <see cref="Model.GameState.PendingOffer"/> — only the
/// next human turn start does (<see cref="PendingOfferSystem"/>) — matching "Accepting does not clear it"
/// exactly.
/// </summary>
public sealed record AcceptPendingOfferCommand(string IssuingNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "diplomacy.accept-pending-offer";
}

/// <summary>Rejection codes <see cref="AcceptPendingOfferCommandHandler"/> declares.</summary>
public static class AcceptPendingOfferRejections
{
    /// <summary>There is no pending offer to accept.</summary>
    public static readonly RejectionCode NoPendingOffer = new("diplomacy.no-pending-offer");
}

/// <inheritdoc cref="AcceptPendingOfferCommand"/>
[CommandHandler]
public sealed class AcceptPendingOfferCommandHandler : ICommandHandler<AcceptPendingOfferCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(AcceptPendingOfferCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;

        var pending = state.PendingOffer;
        if (pending is null)
        {
            return CommandOutcome.Reject(
                AcceptPendingOfferRejections.NoPendingOffer, "There is no pending offer to accept.");
        }

        // Alliance acceptance has no waiver to apply -- it is a normal FormAlliance, cascade included.
        // Trade acceptance waives the 3-partner cap: a direct relation write, deliberately skipping
        // TradePartnerCap.MakeRoomForOneMorePartner, per Q5's own words.
        state = pending.ProposedRelationCode == codes.Alliance
            ? RelationTransitions.FormAlliance(state, ruleset, pending.ProposingNationId, command.IssuingNationId)
            : state with
            {
                Relations = state.Relations.WithRelation(
                    pending.ProposingNationId, command.IssuingNationId, codes.Trade),
            };

        return CommandOutcome.Accept(state);
    }
}
