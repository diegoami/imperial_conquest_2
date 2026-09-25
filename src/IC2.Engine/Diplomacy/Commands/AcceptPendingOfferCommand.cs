using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// The human seat accepts the currently pending trade or alliance offer (DoD 10) —
/// <c>news-log-format-and-messages.md</c> Q5: "The offer's only other reader is
/// <c>TPolitics_MakeTrade</c>, where a pending trade offer from that nation waives the three-partner
/// limit." Accepting does <strong>not</strong> clear <see cref="Model.GameState.PendingOffer"/> — only the
/// next human turn start does (<see cref="PendingOfferSystem"/>) — matching "Accepting does not clear it"
/// exactly.
/// </summary>
/// <remarks>
/// <strong>Rework round 1, B2.</strong> An earlier revision of this handler applied no legality check at
/// all beyond "a pending offer exists" — meaning a stale offer (rolled at this turn's start, before the
/// player did anything else in the same turn) could be accepted after the player had since declared war
/// on the proposer, silently overwriting War with Trade or Alliance. DoD 10's own wording is specific:
/// accepting "only waives the three-partner limit" — every other gate a fresh proposal would apply still
/// applies to an acceptance. This handler now applies <see cref="ProposeTradeCommandHandler"/>'s three
/// trade gates (cooldown, already-trading, allied-or-at-war) unconditionally for a trade offer, skipping
/// only <see cref="TradePartnerCap"/>, and <see cref="ProposeAllianceCommandHandler"/>'s alliance gates
/// (already-allied; and, against an AI proposer, cooldown and either-side-at-war) for an alliance offer —
/// alliance has no partner cap to waive in the first place, so nothing is skipped there.
/// </remarks>
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

    /// <summary>The pending offer's proposing nation no longer resolves (defensive; never expected).</summary>
    public static readonly RejectionCode UnknownProposer = new("diplomacy.unknown-proposer");

    /// <summary>
    /// The relation with the proposer has moved onto a cooldown since the offer was rolled — the same
    /// gate <see cref="ProposeTradeRejections.Cooldown"/> / <see cref="ProposeAllianceRejections.Cooldown"/>
    /// apply to a fresh proposal.
    /// </summary>
    public static readonly RejectionCode Cooldown = new("diplomacy.accept-cooldown");

    /// <summary>The two nations are already trading (trade offer only).</summary>
    public static readonly RejectionCode AlreadyTrading = new("diplomacy.already-trading");

    /// <summary>
    /// The relation has moved to allied or war since the offer was rolled (trade offer only) — the
    /// scenario B2 exists to close: a pending trade offer accepted after declaring war on the proposer,
    /// in the same turn, would otherwise silently overwrite War with Trade.
    /// </summary>
    public static readonly RejectionCode AlliedOrAtWar = new("diplomacy.allied-or-at-war");

    /// <summary>The two nations are already allied (alliance offer only).</summary>
    public static readonly RejectionCode AlreadyAllied = new("diplomacy.already-allied");

    /// <summary>Either side is currently at war with anyone (alliance offer, AI proposer only).</summary>
    public static readonly RejectionCode SideAtWar = new("diplomacy.side-at-war");
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

        var proposer = state.NationById(pending.ProposingNationId);
        if (proposer is null)
        {
            return CommandOutcome.Reject(
                AcceptPendingOfferRejections.UnknownProposer,
                $"'{pending.ProposingNationId}' is not a known nation.");
        }

        if (proposer.Eliminated)
        {
            return CommandOutcome.Reject(
                DiplomacyRejections.CounterpartyEliminated,
                $"'{proposer.Name}' has been eliminated and the offer can no longer be accepted.");
        }

        var relation = state.Relations.Get(pending.ProposingNationId, command.IssuingNationId);

        if (pending.ProposedRelationCode == codes.Alliance)
        {
            // ProposeAllianceCommandHandler's own gates, unconditionally re-applied: nothing is waived
            // for an alliance offer, because alliance has no partner cap to waive in the first place.
            if (relation == codes.Alliance)
            {
                return CommandOutcome.Reject(
                    AcceptPendingOfferRejections.AlreadyAllied, $"You are already allied with '{proposer.Name}'.");
            }

            if (proposer.Control == SeatControl.Ai)
            {
                if (relation < codes.Peace)
                {
                    return CommandOutcome.Reject(
                        AcceptPendingOfferRejections.Cooldown, $"'{proposer.Name}' does not want to ally with you.");
                }

                if (RelationTransitions.IsAtWarWithAnyone(state, ruleset, pending.ProposingNationId)
                    || RelationTransitions.IsAtWarWithAnyone(state, ruleset, command.IssuingNationId))
                {
                    return CommandOutcome.Reject(
                        AcceptPendingOfferRejections.SideAtWar,
                        $"'{proposer.Name}' will not ally while either side is at war.");
                }
            }

            state = RelationTransitions.FormAlliance(state, ruleset, pending.ProposingNationId, command.IssuingNationId);
            return CommandOutcome.Accept(state);
        }

        // ProposeTradeCommandHandler's three trade gates, unconditionally re-applied -- the relation may
        // have moved since the offer was rolled at this turn's start (B2's own scenario: the player
        // declares war on the proposer, then accepts the now-stale trade offer in the same turn).
        if (relation < codes.Peace)
        {
            return CommandOutcome.Reject(
                AcceptPendingOfferRejections.Cooldown, $"'{proposer.Name}' does not want to trade with you.");
        }

        if (relation == codes.Trade)
        {
            return CommandOutcome.Reject(
                AcceptPendingOfferRejections.AlreadyTrading, $"You are already trading with '{proposer.Name}'.");
        }

        if (relation > codes.Trade)
        {
            return CommandOutcome.Reject(
                AcceptPendingOfferRejections.AlliedOrAtWar, $"You cannot trade with '{proposer.Name}'.");
        }

        // Deliberately skips TradePartnerCap.MakeRoomForOneMorePartner: accepting waives ONLY the
        // three-partner limit (DoD 10, news-log-format-and-messages.md Q5), nothing else.
        state = state with
        {
            Relations = state.Relations.WithRelation(pending.ProposingNationId, command.IssuingNationId, codes.Trade),
        };

        return CommandOutcome.Accept(state);
    }
}
