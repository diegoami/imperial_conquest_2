using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 9 and 11, wired against real <see cref="GameState"/> through
/// <see cref="QuarterlyCityEconomySystem"/> directly (no calendar or full coordinator needed — the same
/// entry point T08's own quarterly tests use).
/// </summary>
public sealed class QuarterlyCityEconomySystemTests
{
    private static QuarterBoundaryContext Context(GameState state, IRng rng, IEventSink sink) =>
        new(state, EconomyTestbed.Ruleset, EconomyTestbed.Toy.World, EndingSeasonIndex: 0, rng, sink);

    [Fact]
    public void RebellionRiskDetected_PublishesForANonCapitalCityUnderTheThreshold()
    {
        var state = EconomyTestbed.InitialState();

        // Arx is north's capital and Meridia is south's (both toy nations' CapitalCityId); Portus is the
        // toy world's one non-capital city, so it is the one this bullet exercises.
        Assert.Equal("arx", state.NationById("north")!.CapitalCityId);
        Assert.Equal("meridia", state.NationById("south")!.CapitalCityId);

        // north's tax rate (15) skips the rise draw; the fall roll misses, so Portus's loyalty is
        // whatever this test sets it to going in, unaffected by the draws themselves.
        var cities = state.Cities.Select(c => c.Id == "portus" ? c with { Loyalty = 29 } : c);
        state = state with { Cities = ValueList.From(cities) };

        var rng = new ScriptedRng(nextChanceDraws: new[] { false, false, false });
        var sink = new RecordingEventSink();

        new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        var risk = Assert.Single(sink.Events.OfType<RebellionRiskDetected>());
        Assert.Equal("portus", risk.CityId);
        Assert.Equal(29, risk.Loyalty);
    }

    [Fact]
    public void RebellionRiskDetected_NeverPublishesForACapital()
    {
        var state = EconomyTestbed.InitialState();
        var cities = state.Cities.Select(c => c.Id == "arx" ? c with { Loyalty = 5 } : c); // arx is north's capital.
        state = state with { Cities = ValueList.From(cities) };

        var rng = new ScriptedRng(nextChanceDraws: new[] { false, false, false });
        var sink = new RecordingEventSink();

        new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        Assert.Empty(sink.Events.OfType<RebellionRiskDetected>());
    }

    /// <summary>
    /// The loyalty draws happen in city (list) index order, over one shared stream, so a fixed script
    /// (standing in for a fixed seed) reproduces exactly which city gets which draw.
    /// </summary>
    [Fact]
    public void LoyaltyDraws_ConsumeTheSharedStream_InCityListOrder()
    {
        var state = EconomyTestbed.InitialState();

        // north (tax 15 -- not below the 11% threshold) skips its cities' rise draws; give it a tax rate
        // under 11 instead so both its cities (arx, then portus, in that list order) draw a rise. south
        // stays at 20%, well above the threshold, so meridia draws no rise at all.
        var nations = state.Nations.Select(n => n.Id == "north" ? n with { TaxRatePercent = 5 } : n);
        state = state with { Nations = ValueList.From(nations) };

        var cities = state.Cities.Select(c => c with { Loyalty = 50 }); // below 80 everywhere, for uniformity.
        state = state with { Cities = ValueList.From(cities) };

        // Cities list order is [arx, portus, meridia]; arx and portus each draw one NextInt (their rise),
        // meridia draws none. Every city draws exactly one NextChance, regardless.
        var rng = new ScriptedRng(
            nextIntDraws: new[] { 1, 3 },
            nextChanceDraws: new[] { false, false, false });
        var sink = new RecordingEventSink();

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        Assert.Equal(51, result.CityById("arx")!.Loyalty); // first draw (1) went to the first city.
        Assert.Equal(53, result.CityById("portus")!.Loyalty); // second draw (3) went to the second city.
        Assert.Equal(50, result.CityById("meridia")!.Loyalty); // unchanged: no rise, no fall.
    }

    /// <summary>The same seed gives an identical result twice, run through the real, seeded <see cref="IRng"/>.</summary>
    [Fact]
    public void FixedSeed_IsExactlyReproducibleAcrossTwoRuns()
    {
        GameState RunOnce()
        {
            var coordinator = EconomyTestbed.CoordinatorOnly(null, typeof(QuarterlyCityEconomySystem));
            return coordinator.FireQuarterBoundary(EconomyTestbed.InitialState(), endingSeasonIndex: 0);
        }

        var first = RunOnce();
        var second = RunOnce();

        Assert.Equal(GameStateHash.Compute(first), GameStateHash.Compute(second));
    }
}
