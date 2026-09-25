using IC2.Engine.Core;
using IC2.Engine.Diplomacy;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// A nation proposes trade with another — <c>TPolitics_MakeTrade</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>: refused during a
/// cooldown or while allied or at war (DoD 3), and refused on either side's own 3-partner cap (rework
/// round 2, R2 — see below), otherwise establishes trade.
/// </summary>
/// <remarks>
/// <strong>Rework round 2, R2: both caps were previously ignored outright.</strong> The report's own
/// four refusals for <c>TPolitics_MakeTrade</c> (:55190) are the cooldown, the allied-or-at-war relation,
/// and two distinct cap refusals this handler used to skip entirely by calling
/// <see cref="TradePartnerCap.MakeRoomForOneMorePartner"/> unconditionally on both sides instead of
/// checking either cap first: <em>"The human's working row already has 3 partners"</em> — a plain
/// refusal, never waived, the issuer's own cap — and <em>"the target already has 3 partners and there is
/// no pending trade offer from that target"</em> — refused unless a <see cref="Model.PendingDiplomaticOffer"/>
/// from that exact target is already showing, in which case (and only then) <c>TPolitics_OK</c> (:55335)
/// drops the target's own poorest partner rather than refusing. The round-1 remark on
/// <see cref="AcceptPendingOfferCommand"/> claiming this handler already performs "the same drop... a
/// fresh <see cref="ProposeTradeCommand"/> against a capped target performs" was wrong in exactly this
/// way — this handler never drops the target's own partner unless the pending-offer exception applies,
/// and never drops the issuer's own partner at all.
/// </remarks>
public sealed record ProposeTradeCommand(string IssuingNationId, string TargetNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "diplomacy.propose-trade";
}

/// <summary>Rejection codes <see cref="ProposeTradeCommandHandler"/> declares.</summary>
public static class ProposeTradeRejections
{
    /// <summary>The command names a nation id the state does not contain.</summary>
    public static readonly RejectionCode UnknownTarget = new("diplomacy.unknown-target");

    /// <summary>A nation cannot propose trade with itself.</summary>
    public static readonly RejectionCode SelfTarget = new("diplomacy.self-target");

    /// <summary>
    /// The relation is negative (a cooldown) — the confirmed refusal <em>"X does not want to trade with
    /// you."</em>
    /// </summary>
    public static readonly RejectionCode Cooldown = new("diplomacy.trade-cooldown");

    /// <summary>
    /// The relation is already alliance or war — the confirmed refusal <em>"You cannot trade with X."</em>
    /// </summary>
    public static readonly RejectionCode AlliedOrAtWar = new("diplomacy.allied-or-at-war");

    /// <summary>The two nations are already trading.</summary>
    public static readonly RejectionCode AlreadyTrading = new("diplomacy.already-trading");

    /// <summary>
    /// Rework round 2, R2: the issuer already has <see cref="Model.DiplomacyRules.MaxTradePartners"/>
    /// trade partners — "You can only trade with 3 nations", never waived.
    /// </summary>
    public static readonly RejectionCode IssuerCapReached = new("diplomacy.trade-cap-reached");

    /// <summary>
    /// Rework round 2, R2: the target already has <see cref="Model.DiplomacyRules.MaxTradePartners"/>
    /// trade partners and no pending trade offer from that same target is showing — the one case that
    /// would have waived it.
    /// </summary>
    public static readonly RejectionCode TargetCapReached = new("diplomacy.target-trade-cap-reached");
}

/// <inheritdoc cref="ProposeTradeCommand"/>
[CommandHandler]
public sealed class ProposeTradeCommandHandler : ICommandHandler<ProposeTradeCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(ProposeTradeCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;

        if (string.Equals(command.IssuingNationId, command.TargetNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                ProposeTradeRejections.SelfTarget, "A nation cannot propose trade with itself.");
        }

        var target = state.NationById(command.TargetNationId);
        if (target is null)
        {
            return CommandOutcome.Reject(
                ProposeTradeRejections.UnknownTarget, $"'{command.TargetNationId}' is not a known nation.");
        }

        if (target.Eliminated)
        {
            return CommandOutcome.Reject(
                DiplomacyRejections.CounterpartyEliminated,
                $"'{target.Name}' has been eliminated and can no longer trade.");
        }

        var relation = state.Relations.Get(command.IssuingNationId, command.TargetNationId);
        if (relation < codes.Peace)
        {
            return CommandOutcome.Reject(
                ProposeTradeRejections.Cooldown, $"'{target.Name}' does not want to trade with you.");
        }

        if (relation == codes.Trade)
        {
            return CommandOutcome.Reject(
                ProposeTradeRejections.AlreadyTrading, $"You are already trading with '{target.Name}'.");
        }

        if (relation > codes.Trade)
        {
            return CommandOutcome.Reject(
                ProposeTradeRejections.AlliedOrAtWar, $"You cannot trade with '{target.Name}'.");
        }

        // Rework round 2, R2: the issuer's own cap is a plain refusal, never waived by anything --
        // report §"TPolitics_MakeTrate", "You can only trade with 3 nations."
        if (TradePartnerCap.CurrentPartners(state, ruleset, command.IssuingNationId, excluding: command.TargetNationId).Count
            >= ruleset.Diplomacy.MaxTradePartners)
        {
            return CommandOutcome.Reject(
                ProposeTradeRejections.IssuerCapReached,
                $"You already trade with {ruleset.Diplomacy.MaxTradePartners} nations.");
        }

        // Rework round 2, R2: the target's own cap refuses too, unless a pending trade offer from that
        // exact target is already showing -- the report's own "unless there is a pending trade offer
        // from that target" exception. Only in that one case does TPolitics_OK go on to drop the
        // target's own poorest partner instead of refusing.
        var hasPendingTradeOfferFromTarget = state.PendingOffer is { } offer
            && string.Equals(offer.ProposingNationId, command.TargetNationId, StringComparison.Ordinal)
            && offer.ProposedRelationCode == codes.Trade;

        if (!hasPendingTradeOfferFromTarget
            && TradePartnerCap.CurrentPartners(state, ruleset, command.TargetNationId, excluding: command.IssuingNationId).Count
                >= ruleset.Diplomacy.MaxTradePartners)
        {
            return CommandOutcome.Reject(
                ProposeTradeRejections.TargetCapReached,
                $"'{target.Name}' already trades with {ruleset.Diplomacy.MaxTradePartners} nations.");
        }

        if (hasPendingTradeOfferFromTarget)
        {
            state = TradePartnerCap.MakeRoomForOneMorePartner(
                state, ruleset, command.TargetNationId, command.IssuingNationId);
        }

        state = state with
        {
            Relations = state.Relations.WithRelation(command.IssuingNationId, command.TargetNationId, codes.Trade),
        };

        return CommandOutcome.Accept(state);
    }
}
