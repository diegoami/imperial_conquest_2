using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// A nation proposes an alliance with another — <c>TPolitics_MakeAlliance</c>
/// <strong>[confirmed: decompiled-ai-offers-to-human-seats.md §3]</strong>. Against an AI target, refused
/// when the relation is negative, or when the <em>proposing</em> side is itself at war with anyone, or
/// when any nation the proposer is allied with is itself at war with anyone (<c>FUN_00449CD8</c>) — the
/// AI target's own wars are never checked (T82 rework round 1, B2 corrects an earlier "either side"
/// reading that also checked the target's). Always accepted from a human target. Forming it drags the
/// proposer into war with every nation the new ally is already at war with (DoD 4,
/// <see cref="RelationTransitions.FormAlliance"/>) — which is exactly how allying with an AI already at
/// war reaches the proposer, since that war is never refused up front.
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

    /// <summary>
    /// The proposer is itself at war with anyone, or allied with a nation that is (an AI target only;
    /// T82 rework round 1, B2 — the AI target's own wars are never checked, report §3).
    /// </summary>
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

            // T82 rework round 1 (B2): decompiled-ai-offers-to-human-seats.md §3 is explicit that only the
            // proposer's own side is checked here -- "The AI target's own wars are not checked" -- so
            // allying with an AI already at war drags the proposer into that war through FormAlliance's
            // own cascade below, exactly as the original does.
            if (RelationTransitions.IsAtWarWithAnyone(state, ruleset, command.IssuingNationId)
                || RelationTransitions.HasAnAllyAtWarWithAnyone(state, ruleset, command.IssuingNationId))
            {
                return CommandOutcome.Reject(
                    ProposeAllianceRejections.SideAtWar,
                    $"'{target.Name}' will not ally while you, or one of your allies, is at war.");
            }
        }

        state = RelationTransitions.FormAlliance(state, ruleset, command.IssuingNationId, command.TargetNationId);
        return CommandOutcome.Accept(state);
    }
}
