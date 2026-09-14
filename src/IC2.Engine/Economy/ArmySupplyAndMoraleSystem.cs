using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Registers <see cref="SupplyMoraleRule.ApplyTurn"/> into the attrition phase T06 declared for exactly
/// this rule — <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses", Done-when 10
/// and 11, and the phase T06's <c>AttritionPhaseOrderingTests</c> proved was registrable before this task
/// existed.
/// </summary>
/// <remarks>
/// <para>
/// Runs in <see cref="TurnPhase.ArmyTick"/>, which sits <em>before</em> <see cref="TurnPhase.CalendarAdvance"/>
/// in T06's declared order (see that enum's remarks): every army this system updates reads
/// <c>context.State.Calendar.SeasonIndex</c> as the season that is ending, exactly as
/// <c>docs/investigations/thracia-supply-morale.md</c> requires. Reading the post-advance season here
/// would make every Winter turn's consumption figure wrong by 7×.
/// </para>
/// <para>
/// Every army in the game is updated, every round — the original's tick loops all armies regardless of
/// seat, not just the ending nation's (T06's own remarks on <see cref="TurnPhaseScope.Round"/>).
/// </para>
/// </remarks>
[GameSystem(TurnPhase.ArmyTick, "economy.supply-consumption-and-morale")]
public sealed class ArmySupplyAndMoraleSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ruleset = context.Ruleset;
        var seasonIndex = context.State.Calendar.SeasonIndex;

        var updated = new List<ArmyState>(context.State.Armies.Count);
        foreach (var army in context.State.Armies)
        {
            var outcome = SupplyMoraleRule.ApplyTurn(
                troops: army.TotalTroops,
                currentSupplyTons: army.SupplyTons,
                currentMorale: army.Morale,
                isEmbarked: army.IsEmbarked,
                seasonIndex: seasonIndex,
                ruleset: ruleset);

            updated.Add(army with
            {
                SupplyTons = outcome.SupplyTonsAfterConsumption,
                Morale = outcome.Morale,
                Moves = outcome.Moves,
            });
        }

        return context.State with { Armies = ValueList.From(updated) };
    }
}
