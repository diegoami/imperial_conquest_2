using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The quarterly tick's city loop, wired against real <see cref="GameState"/> —
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 5, 9 and 11; T89 (<c>#397</c>) wires the rebellion itself onto this
/// loop's own loyalty pass.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Runs after T08's upkeep billing</strong> (<see cref="QuarterlyEconomySystem"/>, registered at
/// this hook's default order 0): <c>city-population-growth.md</c>'s step order has upkeep billed first,
/// then wealth and tax base zeroed, then this city loop (growth, the rebuild, the loyalty draws), then
/// the nation loop (<see cref="QuarterlyNationEconomySystem"/>, ordered after this one). Neither this
/// system's growth nor its loyalty draws read anything T08's upkeep billing writes, so the only ordering
/// requirement between the two is that this one not run first — an explicit <see cref="QuarterBoundaryHandlerAttribute.Order"/>
/// past T08's default 0 states that rather than leaving it to registration order.
/// </para>
/// <para>
/// One pass computes every city's grown population (reading each owner's <em>current</em>, pre-decay
/// tax rate and mobilization — <see cref="QuarterlyNationEconomySystem"/> decays mobilization later);
/// <see cref="NationTaxBaseRebuild.Rebuild"/> then reads that grown population. A second pass applies the
/// loyalty draws in city (list) index order over one shared <see cref="QuarterBoundaryContext.Rng"/>
/// stream and, right after each city's own draws — <c>decompiled-quarterly-rebellion.md</c>'s own code
/// order — runs <see cref="Rebellion.Run"/> for any city whose draws leave it under
/// <see cref="EconomyRules.RebellionLoyaltyThreshold"/> and not a capital. T89: this replaces the old,
/// unconsumed <c>RebellionRiskDetected</c> publication. Splitting growth+rebuild from the loyalty pass
/// changes nothing observable: neither draw reads a city's own or any other city's grown population or
/// rebuilt tax base, only its own prior loyalty and its owner's (growth-unaffected) tax rate, so the two
/// passes are independent except for the RNG's own draw order, which this system preserves by processing
/// <see cref="GameState.Cities"/> in its own list order throughout — the loyalty pass below folds each
/// city's own update, and any rebellion it triggers, back into the working <see cref="GameState"/> before
/// moving to the next city in that same list order, exactly the "live reads" the report's own Hazards
/// section asks for (a rebellion's own effects — city counts, unity — must be visible to a later city's
/// own (d) score in the same quarter, not a snapshot taken once at the top of this loop).
/// </para>
/// </remarks>
[QuarterBoundaryHandler("economy.quarterly-city-tick", Order = 100)]
public sealed class QuarterlyCityEconomySystem : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var ruleset = context.Ruleset;
        var economy = ruleset.Economy;

        var grownCities = new List<CityState>(state.Cities.Count);
        foreach (var city in state.Cities)
        {
            var owner = state.NationById(city.Owner);
            if (owner is null)
            {
                grownCities.Add(city);
                continue;
            }

            var threatened = HostileArmyAdjacent.IsThreatened(city, state, ruleset);
            var grownPopulation = CityPopulationGrowth.Grow(
                city.PopulationThousands,
                city.MaxPopulationThousands,
                owner.TaxRatePercent,
                owner.MobilizedPercent,
                threatened,
                economy);

            grownCities.Add(city with { PopulationThousands = grownPopulation });
        }

        state = state with { Cities = ValueList.From(grownCities) };
        state = NationTaxBaseRebuild.Rebuild(state, ruleset);

        var capitalCityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nation in state.Nations)
        {
            if (nation.CapitalCityId is { } capitalCityId)
            {
                capitalCityIds.Add(capitalCityId);
            }
        }

        // Snapshotting the loop's own city order up front, before any city in it can be touched: a
        // rebellion below only ever changes a city's Owner/Loyalty in place (CityCaptureResolver.Defect
        // never adds, removes or reorders GameState.Cities), so re-fetching each id from the working
        // `state` inside the loop always finds it, and this list stays the correct index order for the
        // whole pass.
        var cityIdsInLoyaltyPassOrder = new List<string>(state.Cities.Count);
        foreach (var snapshotCity in state.Cities)
        {
            cityIdsInLoyaltyPassOrder.Add(snapshotCity.Id);
        }

        foreach (var cityId in cityIdsInLoyaltyPassOrder)
        {
            var city = state.CityById(cityId)!;
            var owner = state.NationById(city.Owner);
            if (owner is null)
            {
                continue;
            }

            var isCapital = capitalCityIds.Contains(city.Id);
            var result = CityLoyaltyDraws.Apply(city, owner.TaxRatePercent, isCapital, economy, context.Rng);
            var updatedCity = city with { Loyalty = result.Loyalty };
            state = state with { Cities = CityCaptureResolver.ReplaceCity(state.Cities, updatedCity) };

            if (result.RebellionRisk)
            {
                state = Rebellion.Run(state, context.World, ruleset, updatedCity, context.Events);
            }
        }

        return state;
    }
}
