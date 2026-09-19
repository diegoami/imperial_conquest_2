using IC2.Engine.Core;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// Dispatches an <see cref="AttackFleetCommand"/> to <see cref="InstantBattleResolver.ResolveNaval"/>
/// once every gate in <see cref="AttackLegality"/> has passed (Done-when 7).
/// </summary>
/// <remarks>
/// The same shape, and the same restraint, as <see cref="AttackArmyCommandHandler"/>: the gates and the
/// dispatch are this task's, and everything the battle does — including a sunk carrier taking its army
/// with it — is T16's and T52's, unread and unadjusted here.
/// </remarks>
[CommandHandler]
public sealed class AttackFleetCommandHandler : ICommandHandler<AttackFleetCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(AttackFleetCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var ruleset = context.Ruleset;
        if (AttackLegality.Check(context.State, ruleset, command) is { } rejection)
        {
            return CommandOutcome.Reject(rejection.Code, rejection.Message);
        }

        // Non-null: AttackLegality refuses the command when the ruleset declares no archer unit type.
        var archerUnitTypeId = BattleCommandRuleset.ArcherUnitTypeIdIn(ruleset)!;

        var resolution = InstantBattleResolver.ResolveNaval(
            context.State,
            command.AttackerFleetId,
            command.TargetFleetId,
            ruleset,
            context.World,
            context.Rng,
            archerUnitTypeId,
            context.Events);

        return CommandOutcome.Accept(resolution.State);
    }
}
