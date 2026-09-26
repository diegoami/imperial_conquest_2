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
/// <remarks>
/// <strong>T88 (DoD 5): also the human's path to end a trade or an alliance, not war alone.</strong>
/// <c>decompiled-war-cascade-and-peace-paths.md</c> §2.1 (rework round 1, B5: re-cited from
/// <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c>, which has no §2.1 and none of these
/// claims) reads <c>TPolitics_MakePeace</c>'s
/// own refusal precisely: it fires only when the target is AI <em>and</em> the committed relation is
/// war (<c>rel[me][target] == 3</c>). Any other current relation — including trade (1) or alliance (2) —
/// falls straight to "<c>working[target] = 0</c>", the same working-value reset <c>TPolitics_OK</c> later
/// commits through the setter into its cooldown (trade → −8, alliance → −24). So the click this command
/// models is not "end a war": it is "click peace on this nation's row", and the original only special-
/// cases a still-committed war against an AI. Before this task, this handler rejected
/// <see cref="MakePeaceRejections.NotAtWar"/> for anything but a live war, which left no engine path for
/// a human to end a trade or an alliance at all (T82's review flagged this gap; the report's own §2.1
/// closing line confirms it: "the engine's <c>MakePeaceCommandHandler</c> accepts war only... so it has
/// no route for the human to end a trade or an alliance"). The gate below now accepts any of the three
/// positive relations, and refuses only when there is nothing to end (already peace, or already on a
/// cooldown) or when the specific war-against-an-AI case applies.
/// </remarks>
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
    /// The pair is already at peace, or already on a cooldown — there is nothing to end. (T88, DoD 5:
    /// widened from "no war" to "no trade, alliance or war", the same three positive relations
    /// <see cref="RelationTransitions.BreakToPeace"/> maps to a cooldown.)
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

        // T88 (DoD 5): trade, alliance and war are all "something to end" -- see this type's own remarks.
        // Peace and a cooldown are not: there is nothing left to break.
        var relation = state.Relations.Get(command.IssuingNationId, command.TargetNationId);
        if (relation != codes.Trade && relation != codes.Alliance && relation != codes.War)
        {
            return CommandOutcome.Reject(
                MakePeaceRejections.NotAtWar,
                $"You have no trade, alliance or war with '{target.Name}' to end.");
        }

        // "A human-controlled nation always accepts": the refusal applies only to an AI target, and only
        // to the specific committed-war case -- ending a trade or an alliance is never refused, AI or
        // human (report §2.1: the refusal check tests only rel[me][target] == 3).
        if (relation == codes.War && target.Control == SeatControl.Ai)
        {
            return CommandOutcome.Reject(
                MakePeaceRejections.Refused, $"'{target.Name}' does not want to make peace at this time.");
        }

        state = RelationTransitions.BreakToPeace(state, ruleset, command.IssuingNationId, command.TargetNationId);
        return CommandOutcome.Accept(state);
    }
}
