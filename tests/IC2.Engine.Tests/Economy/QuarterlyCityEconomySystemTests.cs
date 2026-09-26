using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Cities.Capture;
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
    /// T89 (<c>#397</c>): the old, unconsumed <c>RebellionRiskDetected</c> publication is gone — a
    /// non-capital city under the threshold now actually rebels, in code order, end to end through the
    /// real toy world. Portus's owner (north) is also its allegiance, so this is branch (c)/(d): no army
    /// is at war on the map, so (c) finds nothing; north and south <em>are</em> geometric neighbours in
    /// this world (<c>NeighbourGeography</c>'s own border-tile derivation, <c>toy-3city.json</c> carries
    /// no <c>startingNeighbours</c> of its own), south's unity is 520 (alive), and south is the only
    /// candidate, so (d) picks it regardless of score. The exact receiver-and-tie-break arithmetic is
    /// <c>RebellionTests</c>' own job, at the decision level, with more than one candidate on the board.
    /// </summary>
    [Fact]
    public void ANonCapitalCityUnderTheThreshold_RunsTheRebellionStep()
    {
        var state = EconomyTestbed.InitialState();

        // Arx is north's capital and Meridia is south's (both toy nations' CapitalCityId); Portus is the
        // toy world's one non-capital city, so it is the one this bullet exercises.
        Assert.Equal("arx", state.NationById("north")!.CapitalCityId);
        Assert.Equal("meridia", state.NationById("south")!.CapitalCityId);

        // north's tax rate (15) skips the rise draw; the fall roll misses, so Portus's loyalty going into
        // the rebellion decision is exactly the 29 this test sets, unaffected by the draws themselves.
        var cities = state.Cities.Select(c => c.Id == "portus" ? c with { Loyalty = 29 } : c);
        state = state with { Cities = ValueList.From(cities) };

        var rng = new ScriptedRng(nextChanceDraws: new[] { false, false, false });
        var sink = new RecordingEventSink();

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        // Portus's allegiance stays "north" (CityCaptureResolver.Defect never writes it), so the transfer
        // to south is the non-allegiant loyalty formula: min(DefectionFloor, max(DefectionFormulaFloor,
        // NonAllegiantTransferBase - 29)) -- read from the ruleset, never a bare literal here.
        var loyalty = EconomyTestbed.Ruleset.Loyalty;
        var expectedLoyalty = Math.Min(
            loyalty.DefectionFloor, Math.Max(loyalty.DefectionFormulaFloor, loyalty.NonAllegiantTransferBase - 29));

        var portus = result.CityById("portus")!;
        Assert.Equal("south", portus.Owner);
        Assert.Equal("north", portus.Allegiance);
        Assert.Equal(expectedLoyalty, portus.Loyalty);

        var news = Assert.Single(sink.Events.OfType<CityDefectsToNation>());
        Assert.Equal("Portus", news.CityName);
        Assert.Equal("Northern League", news.OldOwner);
        Assert.Equal("Southern League", news.NewOwner);
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

    /// <summary>
    /// T89 Done-when 5's second half: a real, seeded <see cref="IRng"/> reproduces a whole quarter,
    /// including an actual rebellion, exactly -- at a tax rate of 0, so
    /// <see cref="CityLoyaltyDraws"/>'s own tax-0 stream fix is exercised for real, not just scripted.
    /// "Rebel" (owner == allegiance == "north", tax 0, starting loyalty 5) can only ever end its own draws
    /// at loyalty 2-8 (a rise of at most <c>LoyaltyRiseRollBound - 1</c>, a fall that only ever subtracts),
    /// so it always rebels; "south" is its only neighbour, so branch (d) always picks it regardless of the
    /// exact roll. The non-allegiant defection formula then saturates at its own cap for any loyalty this
    /// low (<c>min(65, max(50, 100 - L))</c> is 65 for every <c>L &lt;= 35</c>), so the exact final owner
    /// and loyalty are pinned by construction, and <see cref="GameStateHash"/> shows the same seed gives
    /// the same whole state twice.
    /// </summary>
    [Fact]
    public void ATaxZeroNation_ReproducesAQuarterWithARebellion_Exactly()
    {
        GameState RunOnce()
        {
            var sleepy = CaptureTestbed.City(
                "sleepy", "Sleepy", 0, 0, "north", "north", loyalty: 80, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
            var rebel = CaptureTestbed.City(
                "rebel", "Rebel", 0, 0, "north", "north", loyalty: 5, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
            var southCapital = CaptureTestbed.City(
                "south-cap", "SouthCap", 5, 5, "south", "south", loyalty: 90, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 20, tribute: 5);

            var north = CaptureTestbed.Nation("north", unity: 600) with { TaxRatePercent = 0 };
            var south = CaptureTestbed.Nation("south", unity: 600, capitalCityId: "south-cap");

            var state = EliminationForcesTestbed.StateWith(
                new[] { north, south }, new[] { sleepy, rebel, southCapital });
            state = state with
            {
                Neighbours = ValueList.From(new[] { new NationNeighbours("north", ValueList.From(new[] { "south" })) }),
            };

            return new QuarterlyCityEconomySystem().OnQuarterBoundary(
                Context(state, new SplitMix64Rng(20260926UL), new RecordingEventSink()));
        }

        var first = RunOnce();
        var second = RunOnce();

        Assert.Equal(GameStateHash.Compute(first), GameStateHash.Compute(second));
        Assert.Equal("south", first.CityById("rebel")!.Owner);
        Assert.Equal(65, first.CityById("rebel")!.Loyalty);
        Assert.Equal(80, first.CityById("sleepy")!.Loyalty); // tax 0: the fall roll's loss is always 0.
    }
}
