using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// An AI seat writes trade with another AI seat directly, with no consent step — the trade half of
/// <c>FUN_0044FB7C</c> <strong>[confirmed: decompiled-ai-offers-to-human-seats.md §1a]</strong>: "rel ==
/// 0 and trades(me) &lt; 3 and trades(k) &lt; 3 and k is AI: setRelation(me, k, TRADE)". Distinct from
/// <see cref="ProposeTradeCommand"/> (<c>TPolitics_MakeTrade</c>, the human Politics-screen path): that
/// handler makes room by dropping either side's poorest partner when the cap is already full, which the
/// original's own AI code never does for itself — the AI simply skips a partner already at the cap
/// (<see cref="AiOwnDiplomacyRule.EligibleTradePartners"/>, the caller's own gate). This handler's own
/// gate re-checks both sides' caps at write time (the cap can move between candidates being generated
/// and one landing, in the same proposal pass) and that the partner is still computer-controlled.
/// </summary>
public sealed record AiFormTradeCommand(string IssuingNationId, string PartnerNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "diplomacy.ai-form-trade";
}

/// <summary>Rejection codes <see cref="AiFormTradeCommandHandler"/> declares.</summary>
public static class AiFormTradeRejections
{
    /// <summary>The command names a nation id the state does not contain.</summary>
    public static readonly RejectionCode UnknownTarget = new("diplomacy.unknown-target");

    /// <summary>A nation cannot trade with itself.</summary>
    public static readonly RejectionCode SelfTarget = new("diplomacy.self-target");

    /// <summary>The partner is not computer-controlled.</summary>
    public static readonly RejectionCode PartnerNotAi = new("diplomacy.partner-not-ai");

    /// <summary>The relation is not peace (already trading, allied, at war, or on a cooldown).</summary>
    public static readonly RejectionCode NotAtPeace = new("diplomacy.not-at-peace");

    /// <summary>Either side is already at <see cref="Model.DiplomacyRules.MaxTradePartners"/>.</summary>
    public static readonly RejectionCode PartnerCapReached = new("diplomacy.trade-cap-reached");
}

/// <inheritdoc cref="AiFormTradeCommand"/>
[CommandHandler]
public sealed class AiFormTradeCommandHandler : ICommandHandler<AiFormTradeCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(AiFormTradeCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;

        if (string.Equals(command.IssuingNationId, command.PartnerNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                AiFormTradeRejections.SelfTarget, "A nation cannot propose trade with itself.");
        }

        var partner = state.NationById(command.PartnerNationId);
        if (partner is null)
        {
            return CommandOutcome.Reject(
                AiFormTradeRejections.UnknownTarget, $"'{command.PartnerNationId}' is not a known nation.");
        }

        if (partner.Eliminated)
        {
            return CommandOutcome.Reject(
                DiplomacyRejections.CounterpartyEliminated,
                $"'{partner.Name}' has been eliminated and can no longer trade.");
        }

        if (partner.Control != SeatControl.Ai)
        {
            return CommandOutcome.Reject(
                AiFormTradeRejections.PartnerNotAi,
                $"'{partner.Name}' is not computer-controlled; an AI seat's own diplomacy only writes to another AI.");
        }

        if (state.Relations.Get(command.IssuingNationId, command.PartnerNationId) != codes.Peace)
        {
            return CommandOutcome.Reject(
                AiFormTradeRejections.NotAtPeace, $"'{partner.Name}' is not currently at peace with you.");
        }

        if (AiOwnDiplomacyRule.TradeCount(state, ruleset, command.IssuingNationId) >= ruleset.Diplomacy.MaxTradePartners
            || AiOwnDiplomacyRule.TradeCount(state, ruleset, command.PartnerNationId) >= ruleset.Diplomacy.MaxTradePartners)
        {
            return CommandOutcome.Reject(
                AiFormTradeRejections.PartnerCapReached,
                $"Either you or '{partner.Name}' already trades with {ruleset.Diplomacy.MaxTradePartners} nations.");
        }

        state = state with
        {
            Relations = state.Relations.WithRelation(command.IssuingNationId, command.PartnerNationId, codes.Trade),
        };

        return CommandOutcome.Accept(state);
    }
}
