using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Victory;

/// <summary>
/// Wires <see cref="VictoryEvaluator"/> into the live turn pipeline — the gap #105 identified: T12
/// delivered the evaluator as a pure function, but nothing registered it as a <see cref="TurnPhase.RoundEnd"/>
/// system, so it was never called during an actual run.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Registers from inside <c>Victory/**</c>, like T08's systems register from inside
/// <c>Economy/**</c>.</strong> #105's own finding was that T12's stated reason for not registering one
/// ("the Owns list excludes <c>Core/Pipeline/**</c>") does not hold: the attribute-based mechanism
/// <see cref="GameSystemAttribute"/> declares needs no edit to <c>Core/Pipeline/**</c> at all. This file
/// is the fix.
/// </para>
/// <para>
/// <strong>The scenario's own condition (T199, #121).</strong> Until T199, <see cref="SystemContext"/> carried no
/// <see cref="Scenario"/>, so this system evaluated <c>scenario: null</c> and every game played its ruleset's
/// default. It now evaluates <see cref="SystemContext.Scenario"/>: the scenario's <see cref="Scenario.Victory"/> and
/// <see cref="Scenario.TurnLimit"/>, falling back to <see cref="VictoryRules.DefaultCondition"/> and
/// <see cref="VictoryRules.DefaultTurnLimit"/> only when the coordinator was built without a scenario.
/// </para>
/// <para>
/// <strong>Runs last in <see cref="TurnPhase.RoundEnd"/>, after T42's news writer</strong>
/// (<c>IC2.Engine.News.NewsLogWriterRoundEnd</c>, also declared at <c>Order = int.MaxValue</c>; the
/// ordinal tie-break on id puts <c>"news.writer.round"</c> before <c>"victory.round-end-check"</c>), so
/// a win or expiry is evaluated against the state the round actually ended in — including whatever that
/// round's own news flush and header append did to <see cref="GameState.NewsLog"/>. Pinned by a test in
/// this task's own test project.
/// </para>
/// <para>
/// <strong>Idempotent within a run.</strong> If a <see cref="GameWon"/> or <see cref="GameExpired"/> was
/// already published earlier in <em>this</em> run — visible through <see cref="SystemContext.PublishedEvents"/>,
/// which never carries over from one turn to the next — this system publishes nothing more. Evaluating
/// the same, already-decided state twice within one round therefore produces exactly one event, not two.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.RoundEnd, "victory.round-end-check", Order = int.MaxValue)]
public sealed class VictoryCheckSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var published in context.PublishedEvents)
        {
            if (published.Event is GameWon or GameExpired)
            {
                return context.State;
            }
        }

        var outcome = VictoryEvaluator.Evaluate(context.State, context.Ruleset, context.Scenario);

        switch (outcome.Status)
        {
            case VictoryStatus.Won:
                context.Events.Publish(new GameWon(outcome.ConditionType, outcome.WinningNationId!));
                break;
            case VictoryStatus.Expired:
                context.Events.Publish(new GameExpired(outcome.ConditionType));
                break;
            case VictoryStatus.Undecided:
            default:
                break;
        }

        return context.State;
    }
}
