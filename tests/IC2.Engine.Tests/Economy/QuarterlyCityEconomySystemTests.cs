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
    /// <see cref="NationState.Eliminated"/> (review round 2, B7 -- corrected by T87, <c>#421</c> N8: the
    /// round-1 fixture this finding was about had unity 0 <em>with the <see cref="NationState.Eliminated"/>
    /// flag left at its own default, <see langword="false"/></em> — not, as an earlier revision of this
    /// remark said, "a live-unity fixture"; the fault was the mismatch between the two, not either value
    /// alone. A stale pointer into a city another nation now owns outright is a state the engine only ever
    /// produced through <c>NationElimination.ApplyIfLastCityLost</c> before this task, which always sets
    /// <c>Eliminated</c> alongside unity 0 — so unity 0 with <c>Eliminated</c> still <see langword="false"/>
    /// was not a state the engine could reach, and it let a capital gate that filtered on <c>!Eliminated</c>
    /// alone pass unnoticed. <strong>T87 adds a second, narrower source of a stale capital pointer: rebirth
    /// (<c>Rebellion.cs</c>'s own first branch), through <c>CityCaptureResolver.Defect</c>, can move a
    /// still-<em>live</em> nation's own capital city away to the reborn nation without ever touching that
    /// live nation's own <see cref="NationState.CapitalCityId"/> field — <c>Defect</c> only ever writes a
    /// city's <c>Owner</c>/<c>Allegiance</c>, never any nation's capital pointer, so the live nation's own
    /// field is left stale, still naming a city it no longer owns, mid-quarter, without eliminating anyone
    /// (review round 1, N3: an earlier revision of this remark wrongly said rebirth itself "sets" that
    /// capital away, as if it wrote the field; nothing writes it, which is exactly why it goes stale) — see
    /// <c>QuarterlyCityEconomySystem</c>'s own remarks on why its capital test now reads
    /// <see cref="NationState.CapitalCityId"/> live rather than from a set built once before the loop.</strong>
    /// "old-cap" still names "old-cap", a city "strong" now owns outright. "old-cap"'s owner
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
    /// <summary>
    /// T87 rework round 1, B10: the "grow everything first" remark's own named, unconfirmed edge (review
    /// round 1) is now fixed, not merely pinned — <see cref="QuarterlyCityEconomySystem"/>'s own class
    /// remarks explain why growth and the loyalty draws are now interleaved per city, in list order,
    /// rather than split into two passes. "loser" owns exactly one city and defects it away to "receiver"
    /// (branch (b): owner ≠ allegiance, allegiance alive), which leaves "loser" with zero cities and
    /// eliminates it, disposing of its own army (<c>EliminationForces</c>). That army starts adjacent to
    /// "watcher-owner"'s own city, at war with it, so <see cref="HostileArmyAdjacent.IsThreatened"/> would
    /// read it as a real threat if "watcher-owner"'s city were grown before "loser-city"'s own loyalty
    /// draw ran its rebellion. "loser-city" is earlier in <see cref="GameState.Cities"/>' own list order
    /// than "watched-city", so with growth and draws interleaved per city, "loser"'s elimination (and its
    /// army's disposal) has already happened by the time "watched-city" is reached, and its growth
    /// correctly sees no threat — matching the original's own per-city loop
    /// (<c>decompiled-quarterly-rebellion.md</c> §2's own last paragraph, §5 item 5). Before this rework
    /// round's restructuring, the old two-pass split grew every city first, so "watched-city" was grown
    /// while "loser"'s army was still on the map, suppressing its growth by a threat that would not exist
    /// once the quarter ended; this test used to pin that divergence (population staying at 220) and now
    /// proves the fix instead (proved by mutation: reverting <see cref="QuarterlyCityEconomySystem"/>'s
    /// own interleaved loop to the old two-pass split — growing every city, then running every city's own
    /// loyalty draws in a second pass — makes this test fail with 220 again, verified locally and
    /// reverted).
    /// </summary>
    [Fact]
    public void AnOwnerRebellionEliminatesMidQuarter_ALaterCitysGrowthSeesItsArmyAlreadyGone()
    {
        var loserCity = CaptureTestbed.City(
            "loser-city", "LoserCity", 10, 10, "loser", "receiver", loyalty: 20, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var watchedCity = CaptureTestbed.City(
            "watched-city", "WatchedCity", 10, 11, "watcher-owner", "watcher-owner", loyalty: 90,
            fortificationCode: 0, populationThousands: 220, maxPopulationThousands: 300, tribute: 5);

        var loser = CaptureTestbed.Nation("loser", unity: 600);
        var receiver = CaptureTestbed.Nation("receiver", unity: 600);
        var watcherOwner = CaptureTestbed.Nation("watcher-owner", unity: 600) with { MobilizedPercent = 20 };

        var loserArmy = CaptureTestbed.Army(
            "loser-army", "loser", x: 10, y: 11, morale: 60, CaptureTestbed.Unit("light_infantry", 5000));

        var state = EliminationForcesTestbed.StateWith(
            new[] { loser, receiver, watcherOwner },
            new[] { loserCity, watchedCity },
            armies: new[] { loserArmy });
        state = state with { Relations = state.Relations.WithRelation("loser", "watcher-owner", EconomyTestbed.Ruleset.Diplomacy.StateCodes.War) };

        // Both cities' own tax rates (15, CaptureTestbed.Nation's default) skip the rise draw; both fall
        // rolls miss, so neither city's own loyalty moves from what this test set it to -- loser-city
        // stays under the rebellion threshold, watched-city stays well above it.
        var rng = new ScriptedRng(nextChanceDraws: new[] { false, false });
        var sink = new RecordingEventSink();

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(Context(state, rng, sink));

        Assert.True(result.NationById("loser")!.Eliminated); // the rebellion-elimination actually happened.
        Assert.Equal("receiver", result.CityById("loser-city")!.Owner);
        Assert.Empty(result.Armies); // loser's own army, disposed of by EliminationForces.

        // The fix itself: watched-city DOES grow, because by the time this later city (in list order)
        // is reached, loser's elimination and its army's disposal (earlier in the same per-city loop)
        // have already happened, so HostileArmyAdjacent.IsThreatened no longer sees a threat.
        Assert.Equal(238, result.CityById("watched-city")!.PopulationThousands);
    }

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

    /// <summary>
    /// T87 (#421, N9): a rebirth mid-quarter sets a new capital, and that capital must not be treated as
    /// a rebel candidate for the rest of the same quarter's own loyalty pass —
    /// <c>decompiled-quarterly-rebellion.md</c> §5, item 5; this system's own remarks on why its capital
    /// test now reads <see cref="NationState.CapitalCityId"/> live rather than from a set built once
    /// before the loop. "dead" starts eliminated (unity 0, no cities); <c>q0..q7</c> are all "owner"'s
    /// own cities, allegiant to "dead" and under the rebirth threshold. <c>q0</c>'s own draws leave it
    /// under <see cref="EconomyRules.RebellionLoyaltyThreshold"/>, which triggers <see cref="Rebellion.Run"/>
    /// → <see cref="Rebirth.Run"/> mid-loop: all eight defect to "dead" (using a lowered
    /// <see cref="LoyaltyRules.DefectionFloor"/> override so every moved city's own post-defection
    /// loyalty — not just <c>q0</c>'s pre-rebirth one — is also under the rebellion threshold; without
    /// this override, <c>CityCaptureResolver.Defect</c>'s own formula floor keeps every moved city
    /// comfortably above it, and this test could never observe a difference either way), and <c>q7</c>
    /// (the strongest of the eight — far higher fortification and population) is chosen as the new
    /// capital. "receiver" is "dead"'s own live neighbour, so a non-capital candidate under the
    /// threshold has somewhere real to go (branch (d)'s own gate) — without a real receiver, the
    /// snapshot bug and this fix would look identical (neither has anywhere to send <c>q7</c>).
    /// </summary>
    /// <remarks>
    /// Proved by mutation: reverting <see cref="QuarterlyCityEconomySystem"/>'s own capital test to a set
    /// built once before the loyalty pass (this system's own pre-T87 shape) makes this test fail — <c>q7</c>
    /// is then read as "not a capital" against the stale, pre-rebirth snapshot (which held no capital for
    /// "dead" at all, since it was eliminated), so its own loyalty draw sees <c>isCapital = false</c>, and
    /// (with its own loyalty also under the threshold from the lowered <c>DefectionFloor</c>) it defects
    /// again, this time to "receiver" — verified locally, then reverted.
    /// </remarks>
    [Fact]
    public void ARebirthMidQuarter_SetsANewCapital_AndThatCapitalIsNotTreatedAsARebelCandidate()
    {
        var ruleset = EconomyTestbed.Ruleset with
        {
            // Every candidate's own allegiance already equals "dead" (that is what made it a rebirth
            // candidate), so CityCaptureResolver.Defect's own allegiant branch applies
            // (min(AllegiantRecaptureTarget, AllegiantRecaptureBase - preLoyalty)), not the non-allegiant
            // formula -- lowering the target is what actually needs overriding here.
            Loyalty = EconomyTestbed.Ruleset.Loyalty with { AllegiantRecaptureTarget = 10 },
        };

        var dead = CaptureTestbed.Nation("dead", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("owner", unity: 600) with { TaxRatePercent = 50 };
        var receiverCap = CaptureTestbed.City(
            "receiver-cap", "ReceiverCap", 90, 90, "receiver", "receiver", loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var receiver = CaptureTestbed.Nation("receiver", unity: 600, capitalCityId: "receiver-cap") with { TaxRatePercent = 50 };

        var candidates = new List<CityState>();
        for (var i = 0; i < 7; i++)
        {
            candidates.Add(CaptureTestbed.City(
                $"q{i}", $"Q{i}", 0, 0, "owner", "dead", loyalty: 20, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 500, tribute: 5));
        }

        // q7: strongest of the eight, so Rebirth.Run's own capital pick lands on it.
        candidates.Add(CaptureTestbed.City(
            "q7", "Q7", 0, 0, "owner", "dead", loyalty: 20, fortificationCode: 90,
            populationThousands: 400, maxPopulationThousands: 500, tribute: 5));

        var cities = new List<CityState> { receiverCap };
        cities.AddRange(candidates);

        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner, receiver }, cities);
        state = state with
        {
            Neighbours = ValueList.From(new[] { new NationNeighbours("dead", ValueList.From(new[] { "receiver" })) }),
        };

        // Nine cities total (receiver-cap + q0..q7), each drawing exactly one fall roll (tax >= 11%
        // everywhere skips every rise draw) -- all scripted to miss, so nothing here moves loyalty on
        // its own; every move below comes from a rebellion/rebirth decision, never a loyalty roll.
        // Review round 1, N1: q0's own rebellion triggers Rebirth.Run (dead's unity is 0), which now
        // makes and discards one leader-name draw of its own -- a separate, independent scripted queue.
        var rng = new ScriptedRng(
            nextChanceDraws: Enumerable.Repeat(false, 9).ToArray(),
            nextIntDraws: new[] { 0 });
        var sink = new RecordingEventSink();
        var context = new QuarterBoundaryContext(state, ruleset, EconomyTestbed.Toy.World, EndingSeasonIndex: 0, rng, sink);

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(context);

        var reborn = result.NationById("dead")!;
        Assert.Equal("q7", reborn.CapitalCityId);
        Assert.Equal("dead", result.CityById("q7")!.Owner); // stays put: the live capital read excludes it.
    }

    /// <summary>
    /// Review round 2, R6 (the "growth under the reborn owner" half — the wealth/tax-base half is a named,
    /// still-open divergence instead; see <see cref="QuarterlyCityEconomySystem"/>'s own remarks on why a
    /// per-city, growth-time credit was tried and reverted). <c>q0</c>'s own rebellion triggers
    /// <see cref="Rebirth.Run"/> mid-loop (eight qualifying cities, over
    /// <see cref="EconomyRules.RebirthMinimumQualifyingCityCount"/>'s 7): all eight (<c>q0..q7</c>) defect
    /// to "dead" inside that same call, before <c>q1..q7</c> have had their own turn in this system's own
    /// per-city loop at all. "owner"'s own tax rate (50) and mobilization (0) differ from "dead"'s own
    /// reset values (<see cref="EconomyRules.RebirthTaxRatePercent"/> 20,
    /// <see cref="EconomyRules.RebirthMobilizedPercent"/> 50), so growth under the wrong owner's rates is
    /// numerically distinguishable, not merely a different id.
    /// </summary>
    /// <remarks>
    /// <c>q7</c> is reached by this system's own per-city loop <em>after</em> <c>q0</c>'s own rebirth has
    /// already defected it to "dead" -- its own growth must therefore read "dead"'s own (reborn) tax rate
    /// and mobilization, not "owner"'s pre-rebirth ones, or "owner"'s stale rates it never actually had
    /// once this quarter's rebirth ran. Proved by mutation: reading each city's own owner from a snapshot
    /// taken before this loop runs (<c>context.State.CityById(cityId)!.Owner</c>, rather than the live
    /// <c>state.CityById(cityId)!.Owner</c> this system already uses) makes <c>q7</c>'s own expected
    /// population assertion below fail (it would still grow under "owner"'s own rates instead), verified
    /// locally and reverted.
    /// </remarks>
    [Fact]
    public void ARebirthMidQuarter_GrowsALaterCityUnderTheRebornOwnersOwnRates()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var economy = ruleset.Economy;

        var dead = CaptureTestbed.Nation("dead", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("owner", unity: 600) with { TaxRatePercent = 50, MobilizedPercent = 0 };

        var candidates = new List<CityState>();
        for (var i = 0; i < 8; i++)
        {
            candidates.Add(CaptureTestbed.City(
                $"q{i}", $"Q{i}", 0, 0, "owner", "dead", loyalty: 20, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 500, tribute: 5));
        }

        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner }, candidates);

        // Eight cities total (q0..q7), each drawing exactly one fall roll ("owner"'s own tax rate, 50, is
        // >= lowTaxLoyaltyThresholdPercent, so the rise draw is always skipped) -- all scripted to miss,
        // so nothing here moves loyalty except the rebellion/rebirth decision itself.
        var rng = new ScriptedRng(
            nextChanceDraws: Enumerable.Repeat(false, 8).ToArray(),
            nextIntDraws: new[] { 0 });
        var sink = new RecordingEventSink();
        var context = new QuarterBoundaryContext(state, ruleset, EconomyTestbed.Toy.World, EndingSeasonIndex: 0, rng, sink);

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(context);

        Assert.Equal("dead", result.CityById("q7")!.Owner);
        Assert.False(result.NationById("dead")!.Eliminated);

        // q7's own growth, computed here under "dead"'s own reborn rates -- the value this system's own
        // live owner-fetch must match.
        var expectedQ7Growth = CityPopulationGrowth.Grow(
            populationThousands: 10, maxPopulationThousands: 500,
            ownerTaxRatePercent: economy.RebirthTaxRatePercent, ownerMobilizedPercent: economy.RebirthMobilizedPercent,
            threatened: false, economy);
        Assert.Equal(expectedQ7Growth, result.CityById("q7")!.PopulationThousands);

        // The same growth, under "owner"'s own (wrong, pre-rebirth) rates, is a different number --
        // proving this assertion actually distinguishes the two, not merely restating whatever the code
        // happens to produce.
        var growthUnderOwnersOwnRates = CityPopulationGrowth.Grow(
            populationThousands: 10, maxPopulationThousands: 500,
            ownerTaxRatePercent: owner.TaxRatePercent, ownerMobilizedPercent: owner.MobilizedPercent,
            threatened: false, economy);
        Assert.NotEqual(growthUnderOwnersOwnRates, expectedQ7Growth);
    }

    /// <summary>
    /// Review round 2, F1 (DoD 8: "the loyalty draws under the reborn owner ... are tested") — the
    /// sibling test above pins <em>growth</em> under the reborn owner's own rates; this one pins the
    /// <em>loyalty draw</em>'s own tax-rate bound the same way. <c>q7</c> (the 8th and last city, reached
    /// only after <c>q0</c>'s own rebirth has already defected it to "dead") is scripted to hit its own
    /// fall roll, whose magnitude draw is bounded by <c>ownerTaxRatePercent</c>
    /// (<see cref="CityLoyaltyDraws.Apply"/>: <c>rng.NextInt(ownerTaxRatePercent) / LoyaltyFallTaxDivisor</c>)
    /// — this must be "dead"'s own reborn rate (<see cref="EconomyRules.RebirthTaxRatePercent"/>, 20), not
    /// "owner"'s stale pre-rebirth one (50). <see cref="ScriptedRng"/>'s own <c>expectedNextIntBounds</c>
    /// enforces the bound directly, rather than relying on a drawn value of 0 producing the same loss
    /// (<c>0 / LoyaltyFallTaxDivisor</c>) either way, which is exactly why the sibling growth test's own
    /// technique (comparing the two rates' own numeric results) cannot catch this half: reading the wrong
    /// tax rate here changes only the bound a draw is legal within, not the loyalty outcome of a drawn 0.
    /// </summary>
    /// <remarks>
    /// Proved by mutation: changing this system's own <c>CityLoyaltyDraws.Apply</c> call to pass a
    /// pre-quarter owner's tax rate instead of the live one makes this test fail with "ScriptedRng expected
    /// NextInt(20) but the caller drew NextInt(50)", verified locally and reverted.
    /// </remarks>
    [Fact]
    public void ARebirthMidQuarter_DrawsALaterCitysLoyaltyFallUnderTheRebornOwnersOwnTaxRate()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var economy = ruleset.Economy;

        var dead = CaptureTestbed.Nation("dead", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("owner", unity: 600) with { TaxRatePercent = 50, MobilizedPercent = 0 };

        var candidates = new List<CityState>();
        for (var i = 0; i < 8; i++)
        {
            candidates.Add(CaptureTestbed.City(
                $"q{i}", $"Q{i}", 0, 0, "owner", "dead", loyalty: 20, fortificationCode: 0,
                populationThousands: 10, maxPopulationThousands: 500, tribute: 5));
        }

        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner }, candidates);

        // Eight cities (q0..q7): the first seven's own fall rolls miss, q7's (the last) hits. Two NextInt
        // draws follow, in order: q0's own rebirth leader-name draw (bound 12), then q7's own fall
        // magnitude draw, whose expected bound (RebirthTaxRatePercent, 20) is what actually pins this --
        // ScriptedRng throws if the caller's own bound ever differs.
        var rng = new ScriptedRng(
            nextChanceDraws: Enumerable.Repeat(false, 7).Append(true).ToArray(),
            nextIntDraws: new[] { 0, 0 },
            expectedNextIntBounds: new[] { 12, economy.RebirthTaxRatePercent });
        var sink = new RecordingEventSink();
        var context = new QuarterBoundaryContext(state, ruleset, EconomyTestbed.Toy.World, EndingSeasonIndex: 0, rng, sink);

        var result = new QuarterlyCityEconomySystem().OnQuarterBoundary(context);

        Assert.Equal("dead", result.CityById("q7")!.Owner);
        Assert.False(result.NationById("dead")!.Eliminated);
    }
}
