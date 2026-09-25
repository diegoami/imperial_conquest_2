using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// An AI seat writes an alliance with another AI seat directly, with no consent step — the alliance half
/// of <c>FUN_0044FB7C</c> <strong>[confirmed: decompiled-ai-offers-to-human-seats.md §1a]</strong>: "if
/// not busy and Random(20) == 0 ... setRelation(me, m, ALLIANCE)". Distinct from
/// <see cref="ProposeAllianceCommand"/> (<c>TPolitics_MakeAlliance</c>, the human Politics-screen path,
/// whose gates are wrong for this one: the original's own AI code never calls
/// <c>TPolitics_MakeAlliance</c> at all). The busy gate, the <c>Random(20)</c> roll and the whole partner
/// search (<see cref="AiOwnDiplomacyRule.FindAlliancePartner"/>) are the caller's
/// (<see cref="Ai.AiDiplomacyPhase"/>); this handler's own gate is only "is the target still a legal,
/// computer-controlled counterparty" — every write still requires a computer partner (report §1a's own
/// closing line).
/// </summary>
public sealed record AiFormAllianceCommand(string IssuingNationId, string PartnerNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "diplomacy.ai-form-alliance";
}

/// <summary>Rejection codes <see cref="AiFormAllianceCommandHandler"/> declares.</summary>
public static class AiFormAllianceRejections
{
    /// <summary>The command names a nation id the state does not contain.</summary>
    public static readonly RejectionCode UnknownTarget = new("diplomacy.unknown-target");

    /// <summary>A nation cannot ally with itself.</summary>
    public static readonly RejectionCode SelfTarget = new("diplomacy.self-target");

    /// <summary>The partner is not computer-controlled — every AI-to-AI write requires one (report §1a).</summary>
    public static readonly RejectionCode PartnerNotAi = new("diplomacy.partner-not-ai");
}

/// <inheritdoc cref="AiFormAllianceCommand"/>
[CommandHandler]
public sealed class AiFormAllianceCommandHandler : ICommandHandler<AiFormAllianceCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(AiFormAllianceCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;

        if (string.Equals(command.IssuingNationId, command.PartnerNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                AiFormAllianceRejections.SelfTarget, "A nation cannot propose an alliance with itself.");
        }

        var partner = state.NationById(command.PartnerNationId);
        if (partner is null)
        {
            return CommandOutcome.Reject(
                AiFormAllianceRejections.UnknownTarget, $"'{command.PartnerNationId}' is not a known nation.");
        }

        if (partner.Eliminated)
        {
            return CommandOutcome.Reject(
                DiplomacyRejections.CounterpartyEliminated,
                $"'{partner.Name}' has been eliminated and can no longer ally.");
        }

        if (partner.Control != SeatControl.Ai)
        {
            return CommandOutcome.Reject(
                AiFormAllianceRejections.PartnerNotAi,
                $"'{partner.Name}' is not computer-controlled; an AI seat's own diplomacy only writes to another AI.");
        }

        state = RelationTransitions.FormAlliance(state, context.Ruleset, command.IssuingNationId, command.PartnerNationId);
        return CommandOutcome.Accept(state);
    }
}
