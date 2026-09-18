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
/// <strong>No live <see cref="Scenario"/> reaches a running system.</strong> <see cref="SystemContext"/>
/// carries <see cref="SystemContext.Ruleset"/> and <see cref="SystemContext.World"/>, never a
/// <see cref="Scenario"/> — <see cref="GameState"/> itself stores only <see cref="GameState.ScenarioId"/>,
/// a string reference, not the resolved scenario object <see cref="VictoryEvaluator.Evaluate"/>'s
/// <c>scenario</c> parameter exists to read a custom goal or a scenario turn-limit override from.
/// Threading a live <see cref="Scenario"/> through <see cref="SystemContext"/> would be a
/// <c>Core/Pipeline/**</c> change, outside this task's Owns list (<c>docs/task-catalogue.md</c> T43),
/// so this system calls <see cref="VictoryEvaluator.Evaluate"/> with <c>scenario: null</c> — evaluating
/// exactly <see cref="VictoryRules.DefaultCondition"/>/<see cref="VictoryRules.DefaultTurnLimit"/> off
/// the loaded <see cref="Ruleset"/>, per <c>docs/design-audit.md</c> Q5. A caller that needs a scenario's
/// own override live (T23's CLI, most plausibly) still has the same evaluator available to call directly
/// with its resolved <see cref="Scenario"/>, exactly as this task's own dispatch tests already do;
/// widening this seam so a registered system can do the same is future work for whichever task next
/// touches <c>Core/Pipeline/**</c>, not a gap silently patched here.
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

        var outcome = VictoryEvaluator.Evaluate(context.State, context.Ruleset, scenario: null);

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
