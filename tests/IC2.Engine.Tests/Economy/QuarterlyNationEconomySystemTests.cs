using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 10, wired against real <see cref="GameState"/> through
/// <see cref="QuarterlyNationEconomySystem"/> directly — the same entry-point style
/// <see cref="QuarterlyCityEconomySystemTests"/> uses for the city loop.
/// </summary>
public sealed class QuarterlyNationEconomySystemTests
{
    private static QuarterBoundaryContext Context(GameState state, IRng rng) =>
        new(state, EconomyTestbed.Ruleset, EconomyTestbed.Toy.World, EndingSeasonIndex: 0, rng, NullEventSink.Instance);

    /// <summary>
    /// <c>city-population-growth.md</c>: "for every nation with unity &gt; 0" gates the entire nation-loop
    /// body. A nation already at unity 0 is left completely untouched this quarter — not just its unity,
    /// but its mobilization and treasury too.
    /// </summary>
    [Fact]
    public void ANationAtUnityZero_IsLeftCompletelyUntouched()
    {
        var state = EconomyTestbed.InitialState();
        var nations = state.Nations.Select(n => n.Id == "north"
            ? n with { Unity = 0, MobilizedPercent = 40, Treasury = 777, TaxBase = 500, Wealth = 900_000 }
            : n);
        state = state with { Nations = ValueList.From(nations) };
        var before = state.NationById("north")!;

        // No RNG draw is scripted at all: this system never should draw one, for any nation.
        var rng = new ScriptedRng();

        var after = new QuarterlyNationEconomySystem().OnQuarterBoundary(Context(state, rng));
        var northAfter = after.NationById("north")!;

        Assert.Equal(before, northAfter);
    }

    /// <summary>The contrasting case: a nation with unity &gt; 0 does have its mobilization, treasury and
    /// unity updated this quarter — proving the zero-unity case above is a real guard, not a system that
    /// happens to do nothing.</summary>
    [Fact]
    public void ANationWithPositiveUnity_IsUpdated()
    {
        var state = EconomyTestbed.InitialState();
        var nations = state.Nations.Select(n => n.Id == "north"
            ? n with { Unity = 1, MobilizedPercent = 40, Treasury = 777, TaxBase = 500, Wealth = 900_000 }
            : n);
        state = state with { Nations = ValueList.From(nations) };
        var before = state.NationById("north")!;

        var rng = new ScriptedRng();

        var after = new QuarterlyNationEconomySystem().OnQuarterBoundary(Context(state, rng));
        var northAfter = after.NationById("north")!;

        Assert.NotEqual(before, northAfter);
        Assert.Equal(37, northAfter.MobilizedPercent); // decayed by 3.
    }
}
