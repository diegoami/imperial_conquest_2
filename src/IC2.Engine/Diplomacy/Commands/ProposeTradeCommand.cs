using IC2.Engine.Core;
using IC2.Engine.Diplomacy;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// A nation proposes trade with another — <c>TPolitics_MakeTrade</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>: refused during a
/// cooldown or while allied or at war (DoD 3), otherwise establishes trade, dropping either side's poorest
/// existing partner first if the new trade would push it past
/// <see cref="Model.DiplomacyRules.MaxTradePartners"/> (<see cref="TradePartnerCap"/>).
/// </summary>
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

        state = TradePartnerCap.MakeRoomForOneMorePartner(
            state, ruleset, command.IssuingNationId, command.TargetNationId);
        state = TradePartnerCap.MakeRoomForOneMorePartner(
            state, ruleset, command.TargetNationId, command.IssuingNationId);

        state = state with
        {
            Relations = state.Relations.WithRelation(command.IssuingNationId, command.TargetNationId, codes.Trade),
        };

        return CommandOutcome.Accept(state);
    }
}
