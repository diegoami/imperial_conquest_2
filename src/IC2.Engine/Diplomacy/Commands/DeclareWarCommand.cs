using IC2.Engine.Core;
using IC2.Engine.Diplomacy;

namespace IC2.Engine.Diplomacy.Commands;

/// <summary>
/// A nation declares war on another — "War: set directly, no check"
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>. This is also the
/// auto-declaration path DoD 5 exercises: attacking sets the relation to war before a battle resolves, by
/// dispatching this command first (<c>TUnitMap_SelectUnit</c>'s own confirmed sequence: "Attacking IS
/// declaring war"). Drags the declaring nation into war with every nation already allied to the target
/// (DoD 4, <see cref="RelationTransitions.DeclareWar"/>).
/// </summary>
public sealed record DeclareWarCommand(string IssuingNationId, string TargetNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "diplomacy.declare-war";
}

/// <summary>Rejection codes <see cref="DeclareWarCommandHandler"/> declares.</summary>
public static class DeclareWarRejections
{
    /// <summary>The command names a nation id the state does not contain.</summary>
    public static readonly RejectionCode UnknownTarget = new("diplomacy.unknown-target");

    /// <summary>A nation cannot declare war on itself.</summary>
    public static readonly RejectionCode SelfTarget = new("diplomacy.self-target");
}

/// <inheritdoc cref="DeclareWarCommand"/>
[CommandHandler]
public sealed class DeclareWarCommandHandler : ICommandHandler<DeclareWarCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(DeclareWarCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;

        if (string.Equals(command.IssuingNationId, command.TargetNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                DeclareWarRejections.SelfTarget, "A nation cannot declare war on itself.");
        }

        if (state.NationById(command.TargetNationId) is null)
        {
            return CommandOutcome.Reject(
                DeclareWarRejections.UnknownTarget, $"'{command.TargetNationId}' is not a known nation.");
        }

        state = RelationTransitions.DeclareWar(state, context.Ruleset, command.IssuingNationId, command.TargetNationId);
        return CommandOutcome.Accept(state);
    }
}
