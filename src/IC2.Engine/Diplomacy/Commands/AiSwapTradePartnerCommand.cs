using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// An AI seat drops one of its own trade partners in favour of a richer one, directly, with no consent
/// step — <c>FUN_0044FB7C</c>'s closing loop <strong>[confirmed: decompiled-ai-offers-to-human-seats.md
/// §1a]</strong>: "for each partner s of me, for k with rel[me][k] == 0, taxBase[s] &lt; taxBase[k],
/// trades(k) &lt; 3, k is AI: setRelation(me, s, 0); setRelation(me, k, TRADE)". The setter's own
/// "setRelation(x, y, 0)" against a currently-trading pair writes the broken-trade cooldown, not plain
/// peace (<see cref="RelationTransitions.BreakToPeace"/>, the same mapping every other break-to-peace
/// caller in this codebase uses). The partner search itself
/// (<see cref="AiOwnDiplomacyRule.FindTradeSwap"/>) is the caller's; this handler re-checks both writes
/// are still legal at write time.
/// </summary>
public sealed record AiSwapTradePartnerCommand(
    string IssuingNationId, string PoorerPartnerId, string RicherCandidateId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "diplomacy.ai-swap-trade-partner";
}

/// <summary>Rejection codes <see cref="AiSwapTradePartnerCommandHandler"/> declares.</summary>
public static class AiSwapTradePartnerRejections
{
    /// <summary>One of the two named nations does not resolve.</summary>
    public static readonly RejectionCode UnknownTarget = new("diplomacy.unknown-target");

    /// <summary>The three ids are not all distinct.</summary>
    public static readonly RejectionCode NotDistinct = new("diplomacy.self-target");

    /// <summary>
    /// The issuer is not computer-controlled (rework round 1, B1): this command is the AI's own direct
    /// write, with no consent step, and is not a substitute a human seat may use.
    /// </summary>
    public static readonly RejectionCode IssuerNotAi = new("diplomacy.issuer-not-ai");

    /// <summary>The richer candidate is not computer-controlled.</summary>
    public static readonly RejectionCode PartnerNotAi = new("diplomacy.partner-not-ai");

    /// <summary>The poorer partner is not, in fact, a current trade partner.</summary>
    public static readonly RejectionCode NotCurrentlyTrading = new("diplomacy.not-currently-trading");

    /// <summary>The richer candidate is not currently at peace (already trading, allied, at war, or on a cooldown).</summary>
    public static readonly RejectionCode CandidateNotAtPeace = new("diplomacy.not-at-peace");

    /// <summary>The richer candidate's own tax base is not actually higher than the poorer partner's.</summary>
    public static readonly RejectionCode CandidateNotRicher = new("diplomacy.candidate-not-richer");

    /// <summary>The richer candidate is already at <see cref="Model.DiplomacyRules.MaxTradePartners"/>.</summary>
    public static readonly RejectionCode CandidateCapReached = new("diplomacy.trade-cap-reached");
}

/// <inheritdoc cref="AiSwapTradePartnerCommand"/>
[CommandHandler]
public sealed class AiSwapTradePartnerCommandHandler : ICommandHandler<AiSwapTradePartnerCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(AiSwapTradePartnerCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;

        if (string.Equals(command.IssuingNationId, command.PoorerPartnerId, StringComparison.Ordinal)
            || string.Equals(command.IssuingNationId, command.RicherCandidateId, StringComparison.Ordinal)
            || string.Equals(command.PoorerPartnerId, command.RicherCandidateId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                AiSwapTradePartnerRejections.NotDistinct, "The issuer, the poorer partner and the richer candidate must all be distinct.");
        }

        var issuer = state.NationById(command.IssuingNationId);
        if (issuer is not null && issuer.Control != SeatControl.Ai)
        {
            return CommandOutcome.Reject(
                AiSwapTradePartnerRejections.IssuerNotAi,
                $"'{issuer.Name}' is not computer-controlled; this is the AI's own direct write, not a human proposal.");
        }

        var poorer = state.NationById(command.PoorerPartnerId);
        var richer = state.NationById(command.RicherCandidateId);
        if (poorer is null || richer is null)
        {
            return CommandOutcome.Reject(
                AiSwapTradePartnerRejections.UnknownTarget, "One of the named nations is not known.");
        }

        if (poorer.Eliminated || richer.Eliminated)
        {
            return CommandOutcome.Reject(
                DiplomacyRejections.CounterpartyEliminated, "One of the named nations has been eliminated.");
        }

        if (richer.Control != SeatControl.Ai)
        {
            return CommandOutcome.Reject(
                AiSwapTradePartnerRejections.PartnerNotAi,
                $"'{richer.Name}' is not computer-controlled; an AI seat's own diplomacy only writes to another AI.");
        }

        if (state.Relations.Get(command.IssuingNationId, command.PoorerPartnerId) != codes.Trade)
        {
            return CommandOutcome.Reject(
                AiSwapTradePartnerRejections.NotCurrentlyTrading,
                $"You are not currently trading with '{poorer.Name}'.");
        }

        if (state.Relations.Get(command.IssuingNationId, command.RicherCandidateId) != codes.Peace)
        {
            return CommandOutcome.Reject(
                AiSwapTradePartnerRejections.CandidateNotAtPeace,
                $"You are not at peace with '{richer.Name}'.");
        }

        if (richer.TaxBase <= poorer.TaxBase)
        {
            return CommandOutcome.Reject(
                AiSwapTradePartnerRejections.CandidateNotRicher,
                $"'{richer.Name}' is not richer than '{poorer.Name}'.");
        }

        if (AiOwnDiplomacyRule.TradeCount(state, ruleset, command.RicherCandidateId) >= ruleset.Diplomacy.MaxTradePartners)
        {
            return CommandOutcome.Reject(
                AiSwapTradePartnerRejections.CandidateCapReached,
                $"'{richer.Name}' already trades with {ruleset.Diplomacy.MaxTradePartners} nations.");
        }

        state = RelationTransitions.BreakToPeace(state, ruleset, command.IssuingNationId, command.PoorerPartnerId);
        state = state with
        {
            Relations = state.Relations.WithRelation(command.IssuingNationId, command.RicherCandidateId, codes.Trade),
        };

        return CommandOutcome.Accept(state);
    }
}
