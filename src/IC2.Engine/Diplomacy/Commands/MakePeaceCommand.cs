using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// A nation proposes peace with another — <c>TPolitics_MakePeace</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>: "refused with
/// 'X does not want to make peace at this time.' if the target is computer-controlled and currently at
/// war. A human-controlled nation always accepts." Completes DoD 1's round-trip back to peace outside the
/// post-battle treaty (<see cref="RelationTransitions.BreakToPeace"/>'s cooldown mapping).
/// </summary>
public sealed record MakePeaceCommand(string IssuingNationId, string TargetNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "diplomacy.make-peace";
}

/// <summary>Rejection codes <see cref="MakePeaceCommandHandler"/> declares.</summary>
public static class MakePeaceRejections
{
    /// <summary>The command names a nation id the state does not contain.</summary>
    public static readonly RejectionCode UnknownTarget = new("diplomacy.unknown-target");

    /// <summary>A nation cannot propose peace with itself.</summary>
    public static readonly RejectionCode SelfTarget = new("diplomacy.self-target");

    /// <summary>
    /// The pair is already at peace (or on a cooldown) — there is no war to end.
    /// </summary>
    public static readonly RejectionCode NotAtWar = new("diplomacy.not-at-war");

    /// <summary>
    /// The target is AI-controlled and currently at war — the confirmed refusal <em>"X does not want to
    /// make peace at this time."</em>
    /// </summary>
    public static readonly RejectionCode Refused = new("diplomacy.peace-refused");
}

/// <inheritdoc cref="MakePeaceCommand"/>
[CommandHandler]
public sealed class MakePeaceCommandHandler : ICommandHandler<MakePeaceCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(MakePeaceCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;

        if (string.Equals(command.IssuingNationId, command.TargetNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                MakePeaceRejections.SelfTarget, "A nation cannot propose peace with itself.");
        }

        var target = state.NationById(command.TargetNationId);
        if (target is null)
        {
            return CommandOutcome.Reject(
                MakePeaceRejections.UnknownTarget, $"'{command.TargetNationId}' is not a known nation.");
        }

        if (target.Eliminated)
        {
            return CommandOutcome.Reject(
                DiplomacyRejections.CounterpartyEliminated,
                $"'{target.Name}' has been eliminated and cannot be offered peace.");
        }

        if (state.Relations.Get(command.IssuingNationId, command.TargetNationId) != codes.War)
        {
            return CommandOutcome.Reject(
                MakePeaceRejections.NotAtWar, $"You are not at war with '{target.Name}'.");
        }

        // "A human-controlled nation always accepts": the refusal applies only to an AI target.
        if (target.Control == SeatControl.Ai)
        {
            return CommandOutcome.Reject(
                MakePeaceRejections.Refused, $"'{target.Name}' does not want to make peace at this time.");
        }

        state = RelationTransitions.BreakToPeace(state, ruleset, command.IssuingNationId, command.TargetNationId);
        return CommandOutcome.Accept(state);
    }
}
