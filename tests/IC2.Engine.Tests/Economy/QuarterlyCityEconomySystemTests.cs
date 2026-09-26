using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 9 and 11, wired against real <see cref="GameState"/> through
/// <see cref="QuarterlyCityEconomySystem"/> directly (no calendar or full coordinator needed — the same
/// entry point T08's own quarterly tests use). Also carries T37's DoD 10 fix (bug <c>#132</c>) for T35's
/// first unproving test: <see cref="AThreatenedCity_DoesNotGrow_WithARealAdjacentHostileArmy"/> below.
/// </summary>
public sealed class QuarterlyCityEconomySystemTests
{
    private static QuarterBoundaryContext Context(GameState state, IRng rng, IEventSink sink) =>
        new(state, EconomyTestbed.Ruleset, EconomyTestbed.Toy.World, EndingSeasonIndex: 0, rng, sink);

    private static GameState WithArmyAt(GameState state, string armyId, int x, int y, string nation) =>
        state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a.Id == armyId
                ? a with { X = x, Y = y, Nation = nation, AboardFleetId = null, CoveredTileCode = 2 }
                : a)),
        };

    private static GameState AtWar(GameState state, string a, string b) =>
        state with { Relations = state.Relations.WithRelation(a, b, EconomyTestbed.Ruleset.Diplomacy.StateCodes.War) };

    /// <summary>
    /// <c>docs/task-catalogue.md</c> "T37 City supply production and famine unrest", Done-when 10
    /// (bug <c>#132</c>): T35's own threat-predicate test
    /// (<c>CityPopulationGrowthTests.AThreatenedCity_DoesNotGrow_TheSameCityOneCellFurtherAwayDoes</c>)
    /// passed a <c>threatened: true</c> literal straight to <see cref="CityPopulationGrowth.Grow"/>,
    /// proving only that the pure formula honours the flag -- never that
    /// <see cref="HostileArmyAdjacent.IsThreatened"/> is actually wired to it. This test goes through
    /// the registered <see cref="QuarterlyCityEconomySystem"/> with a real hostile army placed adjacent
    /// to Arx (north's capital, below its maximum population, so it would otherwise grow) and a real War
    /// relation, never a literal. Proved by mutation: forcing <c>threatened</c> to <c>false</c> at
    /// <c>QuarterlyCityEconomySystem.cs:62</c> makes this test fail (verified locally; today, with that
    /// line unmutated, this test and all its siblings are green).
    /// </summary>
    [Fact]
    public void AThreatenedCity_DoesNotGrow_WithARealAdjacentHostileArmy()
    {
        var state = EconomyTestbed.InitialState();
        state = WithArmyAt(state, "south-army-1", x: 2, y: 2, nation: "south"); // one cell from Arx (2,1).
        state = AtWar(state, "north", "south");

        var rng = new ScriptedRng(nextChanceDraws: new[] { false, false, false });
        var sink = new RecordingEventSink();

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        // Arx (pop 220 of 300, tax 15, mob 20) would otherwise grow to 238 this quarter -- see the
        // unthreatened sibling below, which proves it does.
        Assert.Equal(220, result.CityById("arx")!.PopulationThousands);
    }

    /// <summary>The same city, the same army one cell further away: not threatened, grows normally.</summary>
    [Fact]
    public void TheSameCityOneCellFurtherAway_IsNotThreatened_AndGrowsNormally()
    {
        var state = EconomyTestbed.InitialState();
        state = WithArmyAt(state, "south-army-1", x: 2, y: 3, nation: "south"); // two cells from Arx (2,1).
        state = AtWar(state, "north", "south");

        var rng = new ScriptedRng(nextChanceDraws: new[] { false, false, false });
        var sink = new RecordingEventSink();

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        Assert.Equal(238, result.CityById("arx")!.PopulationThousands); // grew, exactly as computed above.
    }

    /// <summary>
    /// T89 (<c>#397</c>): the old, unconsumed <c>RebellionRiskDetected</c> publication is gone —
    /// a non-capital city under the threshold now actually rebels, in code order. Portus's owner (north)
    /// is also its allegiance, so this is branch (c)/(d); with no army and no live neighbour of north in
    /// the toy world's own geometry (<c>startingNeighbours</c> is null for <c>toy-3city.json</c>, and its
    /// 8×6 map does not clear <c>NeighbourGeography</c>'s own border-tile threshold for north/south), (d)
    /// also finds no candidate, so nothing happens here but the loyalty draw itself — proven separately,
    /// at the decision level, by <c>RebellionTests</c>.
    /// </summary>
    [Fact]
    public void ANonCapitalCityUnderTheThreshold_RunsTheRebellionStep()
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

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        // Owner == allegiance (both "north"), no army on the map, and north has no live neighbour in this
        // world -- (c) and (d) both find nothing, so Portus stays north's at its drawn loyalty, and no
        // defection news is published.
        Assert.Equal("north", result.CityById("portus")!.Owner);
        Assert.Equal(29, result.CityById("portus")!.Loyalty);
        Assert.Empty(sink.Events.OfType<CityDefectsToNation>());
    }

    [Fact]
    public void ACapitalCityUnderTheThreshold_NeverRebels()
    {
        var state = EconomyTestbed.InitialState();
        var cities = state.Cities.Select(c => c.Id == "arx" ? c with { Loyalty = 5 } : c); // arx is north's capital.
        state = state with { Cities = ValueList.From(cities) };

        var rng = new ScriptedRng(nextChanceDraws: new[] { false, false, false });
        var sink = new RecordingEventSink();

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        // Arx's own draws leave its loyalty at 5 (rise skipped, fall roll misses) -- Done-when 2: a
        // capital at loyalty under the threshold still never rebels.
        Assert.Equal("north", result.CityById("arx")!.Owner);
        Assert.Equal(5, result.CityById("arx")!.Loyalty);
        Assert.Empty(sink.Events.OfType<CityDefectsToNation>());
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
