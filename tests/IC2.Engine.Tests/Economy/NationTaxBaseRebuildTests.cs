using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 5: the quarterly tax-base and wealth rebuild, on a scripted
/// <see cref="GameState"/>.
/// </summary>
public sealed class NationTaxBaseRebuildTests
{
    /// <summary>
    /// The toy world's own cities, un-grown: Arx (north) tribute 40, pop 220, max 300, contribution
    /// <c>40×220/300 = 29</c> (truncated from 29.33); Portus (north) tribute 15, pop 80, max 150,
    /// contribution <c>15×80/150 = 8</c> exactly. North's tax base is <c>(29 + 8) × 4 = 148</c>, wealth
    /// <c>(220 + 80) × 3000 = 900,000</c>. Meridia (south) tribute 25, pop 140, max 200, contribution
    /// <c>25×140/200 = 17</c>; south's tax base <c>17 × 4 = 68</c>, wealth <c>140 × 3000 = 420,000</c>.
    /// Starting from the toy world's own designed (and deliberately different) stored values proves the
    /// rebuild actually zeroes first — it does not simply leave the designed starting figures alone.
    /// </summary>
    [Fact]
    public void RebuildsEveryNationsTaxBaseAndWealthFromItsOwnedCities()
    {
        var state = EconomyTestbed.InitialState();
        var ruleset = EconomyTestbed.Ruleset;

        var rebuilt = NationTaxBaseRebuild.Rebuild(state, ruleset);

        var north = rebuilt.NationById("north")!;
        var south = rebuilt.NationById("south")!;

        Assert.Equal(148, north.TaxBase);
        Assert.Equal(900_000, north.Wealth);
        Assert.Equal(68, south.TaxBase);
        Assert.Equal(420_000, south.Wealth);
    }

    /// <summary>A nation owning no city is rebuilt to a zero tax base and zero wealth, not left stale.</summary>
    [Fact]
    public void ANationWithNoCities_IsRebuiltToZero()
    {
        var state = EconomyTestbed.InitialState();
        var ruleset = EconomyTestbed.Ruleset;

        // Every city becomes south's, so north owns none.
        var cities = state.Cities.Select(c => c with { Owner = "south", Allegiance = "south" });
        state = state with { Cities = ValueList.From(cities) };

        var rebuilt = NationTaxBaseRebuild.Rebuild(state, ruleset);
        var north = rebuilt.NationById("north")!;

        Assert.Equal(0, north.TaxBase);
        Assert.Equal(0, north.Wealth);
    }

    /// <summary>The rebuild reads whatever population is on the city record at the moment it runs — the
    /// grown population, when a caller (<see cref="QuarterlyCityEconomySystem"/>) has already applied
    /// this quarter's growth before calling it.</summary>
    [Fact]
    public void ReadsWhicheverPopulationIsOnTheCityRecordAtRebuildTime()
    {
        var state = EconomyTestbed.InitialState();
        var ruleset = EconomyTestbed.Ruleset;

        var grownArx = state.Cities.Select(c => c.Id == "arx" ? c with { PopulationThousands = 230 } : c);
        state = state with { Cities = ValueList.From(grownArx) };

        var rebuilt = NationTaxBaseRebuild.Rebuild(state, ruleset);
        var north = rebuilt.NationById("north")!;

        // Arx: 40 x 230 / 300 = 30 (truncated from 30.67); Portus unchanged at 8. (30 + 8) x 4 = 152.
        Assert.Equal(152, north.TaxBase);
        Assert.Equal((230 + 80) * 3000, north.Wealth);
    }
}
