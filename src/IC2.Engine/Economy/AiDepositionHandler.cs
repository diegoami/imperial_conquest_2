using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The AI half of debt deposition — <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays,
/// mercenary desertion, and deposition for debt", Done-when 6.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: upkeep-payment-and-desertion.md]</strong>: in <c>FUN_00451b40</c>'s nation loop,
/// after the income credit and the unity update -- registered at order 300, after
/// <see cref="QuarterlyNationEconomySystem"/> (order 200), so it reads this quarter's already-credited
/// treasury and already-updated unity, exactly as the debt test requires. Own file, own registration,
/// own random stream (<c>economy.ai-deposition</c>): T35's already-merged
/// <see cref="QuarterlyNationEconomySystem"/> is untouched by this task.
/// </para>
/// <para>
/// Gated the same way the rest of that nation loop is (<c>city-population-growth.md</c>: "for every
/// nation with unity &gt; 0"): a nation already at unity 0 is skipped, matching
/// <see cref="QuarterlyNationEconomySystem"/>'s own guard. Only <see cref="Model.SeatControl.Ai"/>
/// nations are considered here -- the human path is <see cref="HumanDepositionSystem"/>'s.
/// </para>
/// <para>
/// The roll is drawn <em>only</em> for a nation already in debt, not unconditionally every quarter for
/// every nation: the report frames the whole rule as "an AI nation below the debt line has a 1 in 9
/// chance", never a chance drawn for every nation regardless of standing, and no report gives an
/// instruction-level trace of the two tests' evaluation order that would settle a draw-always reading
/// against this one. This also keeps every nation not at risk from perturbing this handler's random
/// stream at all, which is what lets <c>QuarterlyEconomyStepOrderTests</c> and other step-order fixtures
/// run this handler through the real, seeded engine RNG with no script and no risk of an unscripted-draw
/// failure.
/// </para>
/// </remarks>
[QuarterBoundaryHandler("economy.ai-deposition", Order = 300)]
public sealed class AiDepositionHandler : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ruleset = context.Ruleset;
        var state = context.State;
        var relations = state.Relations;

        var updatedNations = new List<NationState>(state.Nations.Count);
        foreach (var nation in state.Nations)
        {
            if (nation.Unity <= 0 || nation.Control != SeatControl.Ai || !Deposition.InDebt(nation, ruleset))
            {
                updatedNations.Add(nation);
                continue;
            }

            if (!context.Rng.NextChance(1, ruleset.Economy.DepositionRandomDivisor))
            {
                updatedNations.Add(nation);
                continue;
            }

            var deposed = Deposition.ApplyEffects(nation, ruleset);
            updatedNations.Add(deposed);
            relations = Deposition.ResetRelations(relations, nation.Id, ruleset);

            context.Events.Publish(new AiLeaderDeposed(deposed.Name, deposed.LeaderName));
        }

        return state with { Nations = ValueList.From(updatedNations), Relations = relations };
    }
}
