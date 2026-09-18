using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Registers <see cref="CityWeeklySupply.Apply"/> into the round's city-tick phase —
/// <c>docs/task-catalogue.md</c> "T37 City supply production and famine unrest", Done-when 1-6.
/// </summary>
/// <remarks>
/// <para>
/// Runs in <see cref="TurnPhase.CityTick"/>, which sits <em>before</em> <see cref="TurnPhase.CalendarAdvance"/>
/// in T06's declared order: every city this system updates reads <c>context.State.Calendar.SeasonIndex</c>
/// as the season that is ending, exactly as <c>city-population-growth.md</c> requires ("Order at a
/// quarter tick... all use the pre-growth population and the ending season"). Reading the post-advance
/// season here would make every Winter turn's figure wrong, the same hazard
/// <see cref="ArmySupplyAndMoraleSystem"/> already documents for army consumption.
/// </para>
/// <para>
/// <see cref="TurnPhase.CityTick"/> also runs <em>before</em> <see cref="TurnPhase.CalendarAdvance"/>'s
/// quarter-boundary hook, so at a quarter tick this system reads each city's population <em>before</em>
/// T35's <see cref="QuarterlyCityEconomySystem"/> grows it — again matching the report's confirmed
/// ordering, and this task's own Done-when 6.
/// </para>
/// <para>
/// Every city in the game is updated, every round, regardless of owner or seat — the original's tick
/// loops all 334 cities, not just the ending nation's. A city with no resolvable owner is left unchanged
/// and draws nothing: the production formula needs an owner's mobilization, exactly the same convention
/// <see cref="QuarterlyCityEconomySystem"/> already uses for its own per-city loop.
/// </para>
/// <para>
/// Two things this loop's original also does are explicitly not this system's: the fortify-order advance
/// (<c>fortification &gt; 100</c>) is T18's, and armies drawing on or depositing into city stocks between
/// ticks (the untraced AI auto-resupply path at dump lines <c>53090–53156</c>) is untraced and not this
/// task's — see this task's catalogue entry's hazards.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.CityTick, "economy.city-supply-production")]
public sealed class WeeklyCitySupplySystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;
        var seasonIndex = state.Calendar.SeasonIndex;

        var updated = new List<CityState>(state.Cities.Count);
        foreach (var city in state.Cities)
        {
            var owner = state.NationById(city.Owner);
            if (owner is null)
            {
                updated.Add(city);
                continue;
            }

            var threatened = HostileArmyAdjacent.IsThreatened(city, state, ruleset);
            var result = CityWeeklySupply.Apply(
                currentSupplyTons: city.SupplyTons,
                populationThousands: city.PopulationThousands,
                seasonIndex: seasonIndex,
                ownerMobilizedPercent: owner.MobilizedPercent,
                threatened: threatened,
                ruleset: ruleset,
                rng: context.Rng);

            updated.Add(city with
            {
                SupplyTons = result.SupplyTons,
                Loyalty = city.Loyalty + result.LoyaltyDelta,
            });
        }

        return state with { Cities = ValueList.From(updated) };
    }
}
