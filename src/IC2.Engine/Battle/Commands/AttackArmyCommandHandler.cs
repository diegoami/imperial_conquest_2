using IC2.Engine.Core;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// Dispatches an <see cref="AttackArmyCommand"/> to <see cref="InstantBattleResolver.ResolveField"/>
/// once every gate in <see cref="AttackLegality"/> has passed.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Everything this handler does is in these few lines, on purpose.</strong> The battle's every
/// formula, threshold, draw and outcome is T16's, twice-reviewed and merged; this type contributes the
/// legality gates, the typed refusals, the resolver's arguments and the resulting state. It does not
/// inspect the <see cref="BattleResult"/>, adjust it, or re-derive any part of it.
/// </para>
/// <para>
/// <strong>Why the war declaration is the caller's command and not a line in this method.</strong> The
/// original does both in one click, with the declaration first
/// (<strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>, see
/// <see cref="AttackLegality"/>), and the natural port would be for this handler to call
/// <c>RelationTransitions.DeclareWar</c> before resolving. It cannot: T16's own merged decoupling guard
/// (<c>FieldBattleTests.AssertNoBattleSourceReferencesDiplomacy</c>) fails the build if <em>any</em> file
/// under <c>src/IC2.Engine/Battle/**</c> — which is where T54's Owns list puts these commands — so much
/// as names the diplomacy namespace, and that test is not this task's to change. The sequence the
/// original guarantees is preserved exactly all the same, because the relation must already be war before
/// this handler will resolve anything: a caller declares war (T19's <c>DeclareWarCommand</c>, whose own
/// remarks name this as the auto-declaration path) and then attacks, and an attack without the
/// declaration is refused with <see cref="AttackArmyRejections.NotAtWar"/> and changes nothing.
/// </para>
/// <para>
/// <strong>Determinism.</strong> Every draw the battle makes comes from
/// <see cref="CommandContext.Rng"/> — the dispatcher's per-command stream, derived from the state's own
/// seed and this command's <see cref="ICommand.Kind"/> — so the same state and the same order resolve the
/// same battle, and no other command's draws are disturbed.
/// </para>
/// </remarks>
[CommandHandler]
public sealed class AttackArmyCommandHandler : ICommandHandler<AttackArmyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(AttackArmyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (AttackLegality.Check(context.State, context.Ruleset, command) is { } rejection)
        {
            return CommandOutcome.Reject(rejection.Code, rejection.Message);
        }

        var resolution = InstantBattleResolver.ResolveField(
            context.State,
            command.AttackerArmyId,
            command.TargetArmyId,
            context.Ruleset,
            context.World,
            context.Rng,
            context.Events);

        return CommandOutcome.Accept(resolution.State);
    }
}
