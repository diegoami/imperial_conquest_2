using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 10's step-order assertion, fired through the <em>real</em> engine
/// assembly's registered quarter-boundary handlers (T08's upkeep at its default order 0, then this
/// task's city tick at 100, then its nation tick at 200) — proving the registration order, not just the
/// three handlers called in a hand-picked sequence.
/// </summary>
/// <remarks>
/// One scripted nation and city, with numbers chosen so that using the <em>wrong</em> value at any of the
/// four checkpoints <c>city-population-growth.md</c>'s step order describes would change the observed
/// result:
/// <list type="bullet">
/// <item><description>Growth reads mobilization <em>before</em> its decay (50, not 47): with the gap and
/// tax rate chosen here, growing at mobilization 50 gives population 66; at the wrong, already-decayed
/// 47 it would give 67.</description></item>
/// <item><description>The rebuild reads the <em>grown</em> population (66, not the pre-tick 40): its
/// contribution, and therefore the rebuilt tax base (264), only comes out right from the grown value.</description></item>
/// <item><description>The treasury credit reads the tax base and wealth <em>just rebuilt this quarter</em>
/// (264 / 198,000), not the deliberately absurd stale starting values (99,999 / 99,999,999) that would
/// send the treasury to roughly +20,000 instead of the +50 this test asserts.</description></item>
/// <item><description>The unity update reads the <em>decayed</em> mobilization (47, not 50): at 47 the
/// result is 516; at the wrong, pre-decay 50 it would be 515.</description></item>
/// </list>
/// </remarks>
public sealed class QuarterlyEconomyStepOrderTests
{
    [Fact]
    public void UpkeepThenGrowthAndRebuildThenTheNationLoop_RunsInTheConfirmedOrder()
    {
        var toy = EconomyTestbed.Toy;
        var state = EconomyTestbed.InitialState();

        // Arx becomes north's only city (Portus moves to south, out of the way); its numbers are chosen
        // so the four checkpoints above are each individually discriminating.
        var cities = state.Cities.Select(city => city.Id switch
        {
            "arx" => city with { PopulationThousands = 40, MaxPopulationThousands = 160, Tribute = 160 },
            "portus" => city with { Owner = "south", Allegiance = "south" },
            _ => city,
        });
        state = state with { Cities = ValueList.From(cities) };

        // No armies or fleets at all: T08's upkeep billing (order 0, still expected to run first) then
        // charges nothing, so the treasury figure this test asserts is exactly this task's own credit,
        // not entangled with an upkeep debit.
        state = state with { Armies = ValueList<ArmyState>.Empty, Fleets = ValueList<FleetState>.Empty };

        var nations = state.Nations.Select(nation => nation.Id == "north"
            ? nation with
            {
                TaxRatePercent = 0,
                MobilizedPercent = 50,
                Unity = 500,
                Treasury = 1000,
                // Deliberately absurd stale values: if the treasury credit read these instead of this
                // quarter's rebuild, the treasury would land far from this test's expected +50.
                TaxBase = 99_999,
                Wealth = 99_999_999,
            }
            : nation);
        state = state with { Nations = ValueList.From(nations) };

        var coordinator = EconomyTestbed.RealEngineCoordinator();
        var after = coordinator.FireQuarterBoundary(state, endingSeasonIndex: toy.Ruleset.Calendar.StartSeasonIndex);

        var arxAfter = after.CityById("arx")!;
        var northAfter = after.NationById("north")!;

        Assert.Equal(66, arxAfter.PopulationThousands); // growth read mobilization 50, not the decayed 47.
        Assert.Equal(264, northAfter.TaxBase); // the rebuild read the grown population, 66.
        Assert.Equal(1050, northAfter.Treasury); // the credit read this quarter's rebuilt 264 / 198,000.
        Assert.Equal(47, northAfter.MobilizedPercent); // decayed by 3.
        Assert.Equal(516, northAfter.Unity); // the unity update read the decayed 47, not the pre-decay 50.
    }
}
