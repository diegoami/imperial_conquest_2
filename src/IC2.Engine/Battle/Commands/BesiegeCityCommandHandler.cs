using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;

namespace IC2.Engine.Battle.Commands;

/// <summary>
/// Dispatches a <see cref="BesiegeCityCommand"/> to <see cref="InstantBattleResolver.ResolveSiege"/> and
/// routes its result — win or loss — through <see cref="CityCaptureResolver.ResolveOutcome"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two merged calls, in the order the two tasks defined.</strong>
/// <see cref="InstantBattleResolver.ResolveSiege"/> decides the attempt and applies the besieger's own
/// per-attempt attrition (T17 Done-when 5: every attempt, win or lose);
/// <see cref="CityCaptureResolver.ResolveOutcome"/> takes that already-resolved
/// <see cref="BattleResult"/> and runs everything downstream of it — the forced capture, the loyalty
/// floor, the tax-base and wealth transfer, the treasury credit, the unity swing, the cascading
/// defections, nation elimination and every news line — or, when the defender held, the confirmed
/// <em>"fails to capture"</em> line and nothing else. This handler passes the result through untouched:
/// it never reads <see cref="BattleResult.Winner"/> itself, never branches on the outcome, and never
/// recomputes a single term of either resolver.
/// </para>
/// <para>
/// <strong>This is the first production path that reaches elimination and the defection cascade</strong>
/// (T17's tests were, until now, the only callers), so the delete-then-dangle rule is live here:
/// <c>DoD02_CaptureThroughTheCommandLeavesNothingDangling</c> asserts a capture that eliminates one
/// nation leaves a second, uninvolved nation and its city untouched and leaves no reference to anything
/// the capture removed.
/// </para>
/// <para>
/// <strong>The two resolver ids.</strong> <see cref="BattleCommandRuleset"/> resolves them from the
/// loaded ruleset, and <see cref="AttackLegality"/> has already refused the command if either is missing,
/// which is why the null-forgiving reads below are safe.
/// </para>
/// </remarks>
[CommandHandler]
public sealed class BesiegeCityCommandHandler : ICommandHandler<BesiegeCityCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(BesiegeCityCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var ruleset = context.Ruleset;
        if (AttackLegality.Check(context.State, ruleset, command) is { } rejection)
        {
            return CommandOutcome.Reject(rejection.Code, rejection.Message);
        }

        // Non-null: AttackLegality refuses the command when either id is missing from the ruleset.
        var archerUnitTypeId = BattleCommandRuleset.ArcherUnitTypeIdIn(ruleset)!;
        var fortifyOrderId = BattleCommandRuleset.FortificationOrderIdIn(ruleset)!;

        var siege = InstantBattleResolver.ResolveSiege(
            context.State,
            command.AttackerArmyId,
            command.TargetCityId,
            ruleset,
            context.Rng,
            archerUnitTypeId,
            fortifyOrderId,
            context.Events);

        var resolved = CityCaptureResolver.ResolveOutcome(
            siege.State, siege.Result, ruleset, archerUnitTypeId, fortifyOrderId, context.Events);

        return CommandOutcome.Accept(resolved);
    }
}
