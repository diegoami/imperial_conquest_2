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
    /// Review round 1, B1: Done-when 2's own "including a dead nation's capital" was never covered --
    /// <see cref="ACapitalCityUnderTheThreshold_NeverRebels"/> above only exercises a <em>live</em>
    /// nation's capital. The capital set <see cref="QuarterlyCityEconomySystem"/> builds is every
    /// <see cref="NationState.CapitalCityId"/> with no liveness filter (<c>FUN_0044B8D0</c>'s own "any of
    /// the sixteen nations, dead ones included"): "dead" is unity 0 <em>and</em>
    /// <see cref="NationState.Eliminated"/> (review round 2, B7 -- a stale pointer into a city another
    /// nation now owns outright is a state the engine only ever produces through
    /// <c>NationElimination.ApplyIfLastCityLost</c>, which always sets <c>Eliminated</c>; a live-unity
    /// fixture with the flag left at its default, <see langword="false"/>, is not a state the engine can
    /// reach, and it let a capital gate that filtered on <c>!Eliminated</c> alone pass unnoticed). Its own
    /// <c>CapitalCityId</c> still names "old-cap", a city "strong" now owns outright. "old-cap"'s owner
    /// equals its own allegiance ("strong"), so without the capital gate this would fall straight to (d)
    /// and "third" (strong's only neighbour, alive, with its own capital) would win it -- proving the gate
    /// actually fires, not merely that nothing else does. Review round 2, N7: "strong" is deliberately
    /// built with no <see cref="NationState.CapitalCityId"/> of its own -- branch (d) only ever reads a
    /// <em>candidate</em>'s capital ("third"'s here), never the rebelling city's own owner's.
    /// </summary>
    [Fact]
    public void ADeadNationsStaleCapital_NeverRebels_EvenThoughALiveNeighbourWouldOtherwiseWinIt()
    {
        var oldCap = CaptureTestbed.City(
            "old-cap", "OldCap", 0, 0, "strong", "strong", loyalty: 20, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var thirdCapital = CaptureTestbed.City(
            "third-cap", "ThirdCap", 3, 0, "third", "third", loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);

        var dead = CaptureTestbed.Nation("dead", unity: 0, capitalCityId: "old-cap", eliminated: true);
        var strong = CaptureTestbed.Nation("strong", unity: 600) with { TaxRatePercent = 50 };
        var third = CaptureTestbed.Nation("third", unity: 600, capitalCityId: "third-cap");

        var state = EliminationForcesTestbed.StateWith(
            new[] { dead, strong, third }, new[] { oldCap, thirdCapital });
        state = state with
        {
            Neighbours = ValueList.From(new[] { new NationNeighbours("strong", ValueList.From(new[] { "third" })) }),
        };

        // Tax 50 (>= 11) skips the rise gate outright for "old-cap"; "third-cap" (tax 15 default, loyalty
        // 90) also draws no rise. Both cities' fall rolls miss, so neither city's loyalty moves.
        var rng = new ScriptedRng(nextChanceDraws: new[] { false, false });
        var sink = new RecordingEventSink();

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        Assert.Equal("strong", result.CityById("old-cap")!.Owner);
        Assert.Equal(20, result.CityById("old-cap")!.Loyalty);
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
    /// low (<c>min(65, max(50, 100 - L))</c> is 65 for every <c>L &lt;= 35</c>), so "rebel"'s own final
    /// owner and loyalty are pinned by construction, independent of the stream -- which is exactly why
    /// they alone cannot prove the tax-0 fix (review round 1, B4): removing it changes nothing "rebel" or
    /// "sleepy" (also draw-independent at tax 0, since the loss is 0 either way) end up showing.
    /// <see cref="GameStateHash"/> across two runs proves only that the RNG is itself deterministic, not
    /// that the fix's own draw happened.
    /// <para>
    /// "watcher" (south's own capital, tax 5, starting loyalty 50) is what actually pins the fix: its own
    /// rise/fall draws are read from the shared stream right after "sleepy"'s and "rebel"'s, so its exact
    /// resulting loyalty depends on whether "sleepy" drew the tax-0 fix's extra <see cref="IRng.NextUInt64"/>
    /// call. The seed below (<c>1895</c>) was chosen, by a throwaway brute-force search over
    /// <see cref="SplitMix64Rng"/> seeds 1..2000, for exactly this: "sleepy"'s own <c>Random(3)</c> roll
    /// hits (so the fix's extra draw actually fires), which shifts every later draw by one raw
    /// <see cref="IRng.NextUInt64"/> call and changes "watcher"'s own final loyalty from 50 to 51 (review
    /// round 1, B4's own suggestion: "a later draw"). Verified directly: reverting the tax-0 fix to its
    /// pre-T89 shape (<c>ownerTaxRatePercent &gt; 0 ? rng.NextInt(...) : 0</c>) turns "watcher"'s loyalty
    /// into 51 at this exact seed, confirmed by a mutation run before this assertion was written.
    /// </para>
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
            var watcher = CaptureTestbed.City(
                "south-cap", "Watcher", 5, 5, "south", "south", loyalty: 50, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 20, tribute: 5);

            var north = CaptureTestbed.Nation("north", unity: 600) with { TaxRatePercent = 0 };
            // "watcher" is south's own capital -- irrelevant to (d)'s neighbour lookup beyond its
            // position -- and, at tax 5 (< 11) with starting loyalty 50 (< 80), it draws a real rise
            // every run. Its own draws come from the shared stream right after "sleepy" and "rebel", so
            // its exact resulting loyalty is sensitive to whether "sleepy" actually drew the tax-0 fix's
            // extra IRng.NextUInt64 -- unlike "rebel", whose own loyalty the defection formula saturates
            // regardless (see this test's own remarks below).
            var south = CaptureTestbed.Nation("south", unity: 600, capitalCityId: "south-cap") with { TaxRatePercent = 5 };

            var state = EliminationForcesTestbed.StateWith(
                new[] { north, south }, new[] { sleepy, rebel, watcher });
            state = state with
            {
                Neighbours = ValueList.From(new[] { new NationNeighbours("north", ValueList.From(new[] { "south" })) }),
            };

            return new QuarterlyCityEconomySystem().OnQuarterBoundary(
                Context(state, new SplitMix64Rng(1895UL), new RecordingEventSink()));
        }

        var first = RunOnce();
        var second = RunOnce();

        Assert.Equal(GameStateHash.Compute(first), GameStateHash.Compute(second));
        Assert.Equal("south", first.CityById("rebel")!.Owner);
        Assert.Equal(65, first.CityById("rebel")!.Loyalty);
        Assert.Equal(80, first.CityById("sleepy")!.Loyalty); // tax 0: the fall roll's loss is always 0.

        // The actual pin (review round 1, B4): 50 is the exact drawn value at seed 1895, sensitive to
        // the tax-0 fix's own stream position -- see this test's own remarks.
        Assert.Equal(50, first.CityById("south-cap")!.Loyalty);
    }

    /// <summary>
    /// Review round 1, B5: the "live reads" remarks in <see cref="Rebellion"/> and this class describe an
    /// edge no test visited -- two rebellions in the same quarter, where the second one's own (d) score
    /// must see the first one's effect on a candidate's city count, not a snapshot taken at the top of
    /// the loop. "owner" has two rebels, r1 (10,10) then r2 (10,9), in that list order; its neighbours are
    /// n0 (2 cities, capital at (15,10)) and n1 (4 cities, capital at (10,15)).
    /// <list type="bullet">
    /// <item>r1: cheb to n0's capital is 5 (score 2 - 2*5 = -8); cheb to n1's capital is 5 (score
    /// 4 - 2*5 = -6). n1 wins and now owns 5 cities.</item>
    /// <item>r2: cheb to n0's capital is still 5 (score unchanged, -8). cheb to n1's capital is 6.
    /// Read live, n1 now has 5 cities: score 5 - 2*6 = -7, which beats n0's -8, so r2 also goes to n1.
    /// Read from a quarter-start snapshot, n1 would still show 4 cities: score 4 - 2*6 = -8, a tie with
    /// n0, so the lower index (n0) would win instead.</item>
    /// </list>
    /// Review round 2, N7: "owner" is deliberately built with no <see cref="NationState.CapitalCityId"/>
    /// of its own -- a live nation this hand-built fixture doesn't otherwise need one for, since branch
    /// (d) only ever reads a <em>candidate</em>'s capital (n0's, n1's), never the rebelling city's own
    /// owner's.
    /// </summary>
    [Fact]
    public void ASecondRebellionInTheSameQuarter_ScoresItsNeighboursLive_NotFromAQuarterStartSnapshot()
    {
        var r1 = CaptureTestbed.City(
            "r1", "R1", 10, 10, "owner", "owner", loyalty: 20, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var r2 = CaptureTestbed.City(
            "r2", "R2", 10, 9, "owner", "owner", loyalty: 20, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var ownerExtra = CaptureTestbed.City(
            "owner-extra", "OwnerExtra", 90, 90, "owner", "owner", loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var n0Capital = CaptureTestbed.City(
            "n0-cap", "N0Cap", 15, 10, "n0", "n0", loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var n1Capital = CaptureTestbed.City(
            "n1-cap", "N1Cap", 10, 15, "n1", "n1", loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var n0Extra = CaptureTestbed.FillerCities("n0", 1, startX: 50, y: 50).ToList(); // n0: 2 cities total.
        var n1Extra = CaptureTestbed.FillerCities("n1", 3, startX: 60, y: 60).ToList(); // n1: 4 cities total.

        var owner = CaptureTestbed.Nation("owner", unity: 600) with { TaxRatePercent = 50 };
        var n0 = CaptureTestbed.Nation("n0", unity: 600, capitalCityId: "n0-cap") with { TaxRatePercent = 50 };
        var n1 = CaptureTestbed.Nation("n1", unity: 600, capitalCityId: "n1-cap") with { TaxRatePercent = 50 };

        var cities = new List<CityState> { r1, r2, n0Capital, n1Capital, ownerExtra };
        cities.AddRange(n0Extra);
        cities.AddRange(n1Extra);

        var state = EliminationForcesTestbed.StateWith(new[] { owner, n0, n1 }, cities);
        state = state with
        {
            Neighbours = ValueList.From(new[] { new NationNeighbours("owner", ValueList.From(new[] { "n0", "n1" })) }),
        };

        // Tax 50 (>= 11) skips every city's rise gate; the fall roll misses for all nine cities.
        var rng = new ScriptedRng(nextChanceDraws: Enumerable.Repeat(false, 9).ToArray());
        var sink = new RecordingEventSink();

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        Assert.Equal("n1", result.CityById("r1")!.Owner);
        Assert.Equal("n1", result.CityById("r2")!.Owner); // live: n1 already has 5 cities when r2 is scored.
    }
}
