using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// A nation proposes an alliance with another — <c>TPolitics_MakeAlliance</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>: "refused
/// against an AI nation if either side is currently at war with anyone, or if the relation is negative.
/// Always accepted from a human seat." Forming it drags the proposer into war with every nation the new
/// ally is already at war with (DoD 4, <see cref="RelationTransitions.FormAlliance"/>).
/// </summary>
public sealed record ProposeAllianceCommand(string IssuingNationId, string TargetNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "diplomacy.propose-alliance";
}

/// <summary>Rejection codes <see cref="ProposeAllianceCommandHandler"/> declares.</summary>
public static class ProposeAllianceRejections
{
    /// <summary>The command names a nation id the state does not contain.</summary>
    public static readonly RejectionCode UnknownTarget = new("diplomacy.unknown-target");

    /// <summary>A nation cannot propose an alliance with itself.</summary>
    public static readonly RejectionCode SelfTarget = new("diplomacy.self-target");

    /// <summary>The relation is negative (a cooldown).</summary>
    public static readonly RejectionCode Cooldown = new("diplomacy.alliance-cooldown");

    /// <summary>Either side is currently at war with anyone (an AI target only).</summary>
    public static readonly RejectionCode SideAtWar = new("diplomacy.side-at-war");

    /// <summary>The two nations are already allied.</summary>
    public static readonly RejectionCode AlreadyAllied = new("diplomacy.already-allied");
}

/// <inheritdoc cref="ProposeAllianceCommand"/>
[CommandHandler]
public sealed class ProposeAllianceCommandHandler : ICommandHandler<ProposeAllianceCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(ProposeAllianceCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;

        if (string.Equals(command.IssuingNationId, command.TargetNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                ProposeAllianceRejections.SelfTarget, "A nation cannot propose an alliance with itself.");
        }

        var target = state.NationById(command.TargetNationId);
        if (target is null)
        {
            return CommandOutcome.Reject(
                ProposeAllianceRejections.UnknownTarget, $"'{command.TargetNationId}' is not a known nation.");
        }

        if (target.Eliminated)
        {
            return CommandOutcome.Reject(
                DiplomacyRejections.CounterpartyEliminated,
                $"'{target.Name}' has been eliminated and can no longer ally.");
        }

        var relation = state.Relations.Get(command.IssuingNationId, command.TargetNationId);
        if (relation == codes.Alliance)
        {
            return CommandOutcome.Reject(
                ProposeAllianceRejections.AlreadyAllied, $"You are already allied with '{target.Name}'.");
        }

        // Always accepted from a human seat: the war/cooldown gates below apply only when the target is
        // AI-controlled.
        if (target.Control == SeatControl.Ai)
        {
            if (relation < codes.Peace)
            {
                return CommandOutcome.Reject(
                    ProposeAllianceRejections.Cooldown, $"'{target.Name}' does not want to ally with you.");
            }

            if (RelationTransitions.IsAtWarWithAnyone(state, ruleset, command.IssuingNationId)
                || RelationTransitions.IsAtWarWithAnyone(state, ruleset, command.TargetNationId))
            {
                return CommandOutcome.Reject(
                    ProposeAllianceRejections.SideAtWar,
                    $"'{target.Name}' will not ally while either side is at war.");
            }
        }

        state = RelationTransitions.FormAlliance(state, ruleset, command.IssuingNationId, command.TargetNationId);
        return CommandOutcome.Accept(state);
    }
}
