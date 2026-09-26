using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Cities.Capture;
using IC2.Engine.Tests.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// T89 (<c>#397</c>): <see cref="Rebellion.Run"/> (<c>FUN_0044C204</c>), at the decision level, one branch
/// at a time — <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-quarterly-rebellion.md">
/// decompiled-quarterly-rebellion.md</see> (research <c>235af11</c>). The wired-through-the-quarterly-loop
/// case (a real toy-world rebellion, city-index order, the shared RNG stream) is
/// <c>QuarterlyCityEconomySystemTests</c>' own job; this file is deliberately hand-built fixtures with more
/// than one candidate on the board, since the toy world's own two nations cannot exercise a tie or a
/// three-way army pick. States are built through <see cref="EliminationForcesTestbed.StateWith"/> (not
/// <see cref="CaptureTestbed.StateWith"/>), which sizes <see cref="GameState.TurnOrder"/> and
/// <see cref="GameState.Relations"/> to whatever nation ids a test actually uses, so an arbitrary set of
/// invented nations still passes <see cref="GameDataValidation"/>.
/// </summary>
public sealed class RebellionTests
{
    private static Ruleset Ruleset => CaptureTestbed.Ruleset;
    private static World World => PersistenceTestbed.Toy.World; // Naming only: state.Neighbours is always set below, so NeighbourGeography never actually reads World's own data.

    private static GameState WithNeighbours(GameState state, string ownerId, params string[] neighbourIds) =>
        state with { Neighbours = ValueList.From(new[] { new NationNeighbours(ownerId, ValueList.From(neighbourIds)) }) };

    // ---------------------------------------------------------------------------------------------
    // (a): owner != allegiance, allegiance nation dead.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// (a) <c>[confirmed]</c>: a dead allegiance nation (unity &lt;= 0) runs the rebirth check
    /// (T87's own call, not here) and nothing else -- the city stays exactly where it is, with no
    /// fallback to (c) or (d). Review round 1, B2: the original fixture had no army and no neighbour, so
    /// (c) and (d) would have found nothing anyway -- proving nothing about the fallback itself, since a
    /// mutant that falls through to (c)/(d) on a dead allegiance would still leave this test green. "gaul"
    /// is both at war with "rome" (an army 2 tiles away, so (c) would pick it) and "rome"'s only neighbour
    /// (so (d) would pick it too, if (c) somehow missed) -- either fallback would move the city, so
    /// <see cref="Assert.Same"/> genuinely proves neither runs.
    /// </summary>
    [Fact]
    public void A_DeadAllegianceNation_LeavesTheCityInPlace_WithNoFallbackToCOrD()
    {
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "rome", "carthage", loyalty: 20, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var gaulCapital = CaptureTestbed.City(
            "gaul-cap", "GaulCap", 3, 0, "gaul", "gaul", 90, 0, 10, 20, 5);
        var rome = CaptureTestbed.Nation("rome", unity: 600);
        var carthage = CaptureTestbed.Nation("carthage", unity: 0); // <= 0: dead.
        var gaul = CaptureTestbed.Nation("gaul", unity: 600, capitalCityId: "gaul-cap");
        var gaulArmy = CaptureTestbed.Army("gaul-army", "gaul", x: 2, y: 0, morale: 50); // distance 2.

        var state = EliminationForcesTestbed.StateWith(
            new[] { rome, carthage, gaul }, new[] { city, gaulCapital }, new[] { gaulArmy });
        state = state with
        {
            Relations = state.Relations.WithRelation("rome", "gaul", Ruleset.Diplomacy.StateCodes.War),
            Neighbours = ValueList.From(new[] { new NationNeighbours("rome", ValueList.From(new[] { "gaul" })) }),
        };

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Same(state, result);
    }

    // ---------------------------------------------------------------------------------------------
    // (b): owner != allegiance, allegiance nation alive.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// (b) <c>[confirmed]</c>: no war, no distance, no AI check -- a live allegiance nation gets the city
    /// even when it is human and at peace. Also Done-when 4: the transfer is
    /// <see cref="CityCaptureResolver.Defect"/> itself, so this shows the receiver's treasury gain
    /// (contribution × <see cref="CaptureRules.DefectionTreasuryCreditMultiplier"/>) and the old owner's
    /// unity floor (260 - 20 = 240, floored to <see cref="CaptureRules.DefectionUnityLossFloor"/>).
    /// </summary>
    [Fact]
    public void B_ALiveAllegianceNation_ReceivesTheCity_EvenWhenHumanAndAtPeace()
    {
        // tribute 30 * population 100 / maxPopulation 200 = contribution 15.
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "rome", "carthage", loyalty: 29, fortificationCode: 0,
            populationThousands: 100, maxPopulationThousands: 200, tribute: 30);
        // A second city keeps "rome" from being eliminated by this defection (DefectionTests' own
        // pattern), isolating the unity-floor term under test from NationElimination's own unity reset.
        var romeOtherCity = CaptureTestbed.City("c2", "Other", 9, 9, "rome", "rome", 50, 0, 5, 10, 3);
        var rome = CaptureTestbed.Nation("rome", unity: 260, treasury: 500);
        var carthage = CaptureTestbed.Nation("carthage", unity: 600, treasury: 1000) with { Control = SeatControl.Human };
        var state = EliminationForcesTestbed.StateWith(new[] { rome, carthage }, new[] { city, romeOtherCity });

        var sink = new RecordingEventSink();
        var result = Rebellion.Run(state, World, Ruleset, city, sink);

        Assert.Equal("carthage", result.CityById("c1")!.Owner);
        Assert.Equal("carthage", result.CityById("c1")!.Allegiance); // Defect never writes allegiance.

        // Receiver == allegiance: min(AllegiantRecaptureTarget, AllegiantRecaptureBase - 29).
        var loyalty = Ruleset.Loyalty;
        var expectedLoyalty = Math.Min(loyalty.AllegiantRecaptureTarget, loyalty.AllegiantRecaptureBase - 29);
        Assert.Equal(expectedLoyalty, result.CityById("c1")!.Loyalty);

        Assert.Equal(1000 + (15 * Ruleset.Capture.DefectionTreasuryCreditMultiplier), result.NationById("carthage")!.Treasury);
        Assert.Equal(Ruleset.Capture.DefectionUnityLossFloor, result.NationById("rome")!.Unity); // 240 floored to 250.

        var news = Assert.Single(sink.Events.OfType<CityDefectsToNation>());
        Assert.Equal("rome", news.OldOwner);
        Assert.Equal("carthage", news.NewOwner);
    }

    // ---------------------------------------------------------------------------------------------
    // (c): owner == allegiance, an enemy army nearby.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// (c) <c>[confirmed]</c>: the test is Chebyshev &lt; 10, so distance 9 qualifies and distance 10 does
    /// not. The qualifying army (raider2, distance 9) is listed <em>before</em> the disqualified one
    /// (raider1, distance 10): if the boundary were wrong (accepting 10), raider1 would also qualify and,
    /// being later in the army list, would wrongly become the pick instead of raider2.
    /// </summary>
    [Fact]
    public void C_AnArmy_QualifiesAtDistanceNine_ButNotAtDistanceTen()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var raider1 = CaptureTestbed.Nation("raider1", unity: 600);
        var raider2 = CaptureTestbed.Nation("raider2", unity: 600);
        var armies = new[]
        {
            CaptureTestbed.Army("a-raider2", "raider2", x: 9, y: 0, morale: 50), // distance 9: qualifies.
            CaptureTestbed.Army("a-raider1", "raider1", x: 10, y: 0, morale: 50), // distance 10: excluded.
        };
        var state = EliminationForcesTestbed.StateWith(new[] { owner, raider1, raider2 }, new[] { city }, armies);
        state = state with
        {
            Relations = state.Relations
                .WithRelation("owner", "raider1", Ruleset.Diplomacy.StateCodes.War)
                .WithRelation("owner", "raider2", Ruleset.Diplomacy.StateCodes.War),
        };

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Equal("raider2", result.CityById("c1")!.Owner);
    }

    /// <summary>(c) <c>[confirmed]</c>: a live army within range but not at war with the owner does not qualify.</summary>
    [Fact]
    public void C_ALiveArmy_NotAtWar_DoesNotQualify()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var neutral = CaptureTestbed.Nation("neutral", unity: 600);
        var armies = new[] { CaptureTestbed.Army("a1", "neutral", x: 3, y: 0, morale: 50) }; // well within range.
        // EliminationForcesTestbed.StateWith's own Relations are already peace-uniform: never at war.
        var state = EliminationForcesTestbed.StateWith(new[] { owner, neutral }, new[] { city }, armies);
        state = WithNeighbours(state, "owner"); // no candidate for (d) either, so nothing happens overall.

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Same(state, result);
    }

    /// <summary>
    /// (c) <c>[confirmed]</c>: two matching armies -- the scan never breaks, so the later match in
    /// <see cref="GameState.Armies"/>' own list order overwrites the earlier one.
    /// </summary>
    [Fact]
    public void C_TwoMatchingArmies_TheLastOneInListOrderWins()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var raider1 = CaptureTestbed.Nation("raider1", unity: 600);
        var raider2 = CaptureTestbed.Nation("raider2", unity: 600);
        var armies = new[]
        {
            CaptureTestbed.Army("a1", "raider1", x: 1, y: 0, morale: 50), // matches first.
            CaptureTestbed.Army("a2", "raider2", x: 2, y: 0, morale: 50), // matches last -- this one wins.
        };
        var state = EliminationForcesTestbed.StateWith(new[] { owner, raider1, raider2 }, new[] { city }, armies);
        state = state with
        {
            Relations = state.Relations
                .WithRelation("owner", "raider1", Ruleset.Diplomacy.StateCodes.War)
                .WithRelation("owner", "raider2", Ruleset.Diplomacy.StateCodes.War),
        };

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Equal("raider2", result.CityById("c1")!.Owner);
    }

    // ---------------------------------------------------------------------------------------------
    // (d): owner == allegiance, no matching army -- the best-placed neighbour.
    // ---------------------------------------------------------------------------------------------

    /// <summary>(d) <c>[confirmed]</c>: a neighbour at unity 0 (dead) is never a candidate.</summary>
    [Fact]
    public void D_ANeighbourAtUnityZero_DoesNotQualify()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var dead = CaptureTestbed.Nation("dead", unity: 0);
        var state = EliminationForcesTestbed.StateWith(new[] { owner, dead }, new[] { city });
        state = WithNeighbours(state, "owner", "dead");

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Same(state, result);
    }

    /// <summary>(d) <c>[confirmed]</c>: there is no relation test -- an ally can receive the city.</summary>
    [Fact]
    public void D_AnAllyOfTheOwner_CanReceiveTheCity()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var allyCapital = CaptureTestbed.City("ally-cap", "AllyCap", 3, 0, "ally", "ally", 90, 0, 50, 100, 10);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var ally = CaptureTestbed.Nation("ally", unity: 600, capitalCityId: "ally-cap");
        var state = EliminationForcesTestbed.StateWith(new[] { owner, ally }, new[] { city, allyCapital });
        state = WithNeighbours(state, "owner", "ally") with
        {
            Relations = state.Relations.WithRelation("owner", "ally", Ruleset.Diplomacy.StateCodes.Alliance),
        };

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Equal("ally", result.CityById("c1")!.Owner);
    }

    /// <summary>
    /// (d) <c>[confirmed]</c>: the comparison is strict (<c>&gt;</c>), so an equal score keeps the
    /// earlier, lower-indexed nation -- n0 and n1 both own exactly one city (their own capital), each the
    /// same Chebyshev distance from the rebelling city, so both score identically; n0 is listed first.
    /// </summary>
    [Fact]
    public void D_ATiedScore_TheLowerIndexedNeighbourWins()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var n0Capital = CaptureTestbed.City("n0-cap", "N0Cap", 5, 0, "n0", "n0", 90, 0, 10, 20, 5); // distance 5.
        var n1Capital = CaptureTestbed.City("n1-cap", "N1Cap", 0, 5, "n1", "n1", 90, 0, 10, 20, 5); // distance 5.
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var n0 = CaptureTestbed.Nation("n0", unity: 600, capitalCityId: "n0-cap");
        var n1 = CaptureTestbed.Nation("n1", unity: 600, capitalCityId: "n1-cap");
        var state = EliminationForcesTestbed.StateWith(
            new[] { owner, n0, n1 }, new[] { city, n0Capital, n1Capital });
        state = WithNeighbours(state, "owner", "n0", "n1"); // ascending index order: n0 first.

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Equal("n0", result.CityById("c1")!.Owner);
    }

    /// <summary>
    /// Review round 1, B3: every (d) test above has either one candidate or a tie of equal cities and
    /// equal distance, so <c>cities(n) - weight * cheb</c> could lose either term and still pass. This
    /// pins the distance term: n0 has 5 cities (4 fillers plus its own capital) at distance 20 (score
    /// -35), n1 has 1 city at distance 3 (score -5); n1 must win. Dropping the distance term entirely
    /// (score = cities(n) alone) would give n0 5 and n1 1, flipping the winner to n0.
    /// </summary>
    [Fact]
    public void D_ScoreFormula_TheDistanceTermDecidesTheWinner()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var n0Capital = CaptureTestbed.City("n0-cap", "N0Cap", 20, 0, "n0", "n0", 90, 0, 10, 20, 5); // distance 20.
        var n1Capital = CaptureTestbed.City("n1-cap", "N1Cap", 3, 0, "n1", "n1", 90, 0, 10, 20, 5); // distance 3.
        var n0Fillers = CaptureTestbed.FillerCities("n0", 4, startX: 50, y: 50);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var n0 = CaptureTestbed.Nation("n0", unity: 600, capitalCityId: "n0-cap");
        var n1 = CaptureTestbed.Nation("n1", unity: 600, capitalCityId: "n1-cap");
        var cities = new List<CityState> { city, n0Capital, n1Capital };
        cities.AddRange(n0Fillers);
        var state = EliminationForcesTestbed.StateWith(new[] { owner, n0, n1 }, cities);
        state = WithNeighbours(state, "owner", "n0", "n1");

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Equal("n1", result.CityById("c1")!.Owner);
    }

    /// <summary>
    /// Review round 1, B3: pins the <c>cities(n)</c> term. n0 and n1 sit at the same distance (5); n0 owns
    /// only its own capital (1 city), n1 owns its capital plus one filler (2 cities). n1 must win (score
    /// -8 against -9). Dropping the cities term entirely (score = -weight * cheb alone) would tie both at
    /// -10 and flip the winner to n0, the lower index.
    /// </summary>
    [Fact]
    public void D_ScoreFormula_TheCitiesTermDecidesTheWinner()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var n0Capital = CaptureTestbed.City("n0-cap", "N0Cap", 5, 0, "n0", "n0", 90, 0, 10, 20, 5); // distance 5.
        var n1Capital = CaptureTestbed.City("n1-cap", "N1Cap", 0, 5, "n1", "n1", 90, 0, 10, 20, 5); // distance 5.
        var n1Filler = CaptureTestbed.FillerCities("n1", 1, startX: 50, y: 50);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var n0 = CaptureTestbed.Nation("n0", unity: 600, capitalCityId: "n0-cap");
        var n1 = CaptureTestbed.Nation("n1", unity: 600, capitalCityId: "n1-cap");
        var cities = new List<CityState> { city, n0Capital, n1Capital };
        cities.AddRange(n1Filler);
        var state = EliminationForcesTestbed.StateWith(new[] { owner, n0, n1 }, cities);
        state = WithNeighbours(state, "owner", "n0", "n1");

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Equal("n1", result.CityById("c1")!.Owner);
    }

    /// <summary>
    /// Review round 1, B3: sensitive to the weight itself being 2 (not 3). n0: 1 city at distance 2
    /// (score -3 at weight 2). n1: 4 cities (3 fillers plus its capital) at distance 3 (score -2 at
    /// weight 2): n1 wins. At weight 3, both score -5 -- a tie, so n0 (the lower index) would win instead.
    /// </summary>
    [Fact]
    public void D_ScoreFormula_IsSensitiveToTheWeight_NotThree()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var n0Capital = CaptureTestbed.City("n0-cap", "N0Cap", 2, 0, "n0", "n0", 90, 0, 10, 20, 5); // distance 2.
        var n1Capital = CaptureTestbed.City("n1-cap", "N1Cap", 3, 0, "n1", "n1", 90, 0, 10, 20, 5); // distance 3.
        var n1Fillers = CaptureTestbed.FillerCities("n1", 3, startX: 50, y: 50);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var n0 = CaptureTestbed.Nation("n0", unity: 600, capitalCityId: "n0-cap");
        var n1 = CaptureTestbed.Nation("n1", unity: 600, capitalCityId: "n1-cap");
        var cities = new List<CityState> { city, n0Capital, n1Capital };
        cities.AddRange(n1Fillers);
        var state = EliminationForcesTestbed.StateWith(new[] { owner, n0, n1 }, cities);
        state = WithNeighbours(state, "owner", "n0", "n1");

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Equal("n1", result.CityById("c1")!.Owner);
    }

    /// <summary>
    /// Review round 1, B3: sensitive to the weight itself being 2 (not 1). n0: 2 cities (1 filler plus its
    /// capital) at distance 3 (score -4 at weight 2). n1: 1 city at distance 2 (score -3 at weight 2): n1
    /// wins. At weight 1, both score -1 -- a tie, so n0 (the lower index) would win instead.
    /// </summary>
    [Fact]
    public void D_ScoreFormula_IsSensitiveToTheWeight_NotOne()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var n0Capital = CaptureTestbed.City("n0-cap", "N0Cap", 3, 0, "n0", "n0", 90, 0, 10, 20, 5); // distance 3.
        var n1Capital = CaptureTestbed.City("n1-cap", "N1Cap", 2, 0, "n1", "n1", 90, 0, 10, 20, 5); // distance 2.
        var n0Filler = CaptureTestbed.FillerCities("n0", 1, startX: 50, y: 50);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var n0 = CaptureTestbed.Nation("n0", unity: 600, capitalCityId: "n0-cap");
        var n1 = CaptureTestbed.Nation("n1", unity: 600, capitalCityId: "n1-cap");
        var cities = new List<CityState> { city, n0Capital, n1Capital };
        cities.AddRange(n0Filler);
        var state = EliminationForcesTestbed.StateWith(new[] { owner, n0, n1 }, cities);
        state = WithNeighbours(state, "owner", "n0", "n1");

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Equal("n1", result.CityById("c1")!.Owner);
    }

    /// <summary>(d) <c>[confirmed]</c>: the owner has no neighbour at all -- nothing happens.</summary>
    [Fact]
    public void D_NoCandidate_NothingHappens()
    {
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", 20, 0, 10, 20, 5);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var state = EliminationForcesTestbed.StateWith(new[] { owner }, new[] { city });
        state = WithNeighbours(state, "owner"); // an entry with an empty neighbour list.

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        Assert.Same(state, result);
    }

    // ---------------------------------------------------------------------------------------------
    // Hazard: "a rebellion that eliminates a nation" -- reuse the path, sweep it clean.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// T89 Hazards, "A rebellion that eliminates a nation": a rebellion (here, branch (b)) taking a
    /// nation's last city runs T86's elimination through the very same
    /// <see cref="CityCaptureResolver.Defect"/> call -- no second ownership-change path exists to get this
    /// wrong. Probed with two nations: the eliminated nation's own army is disposed of (no dangling
    /// reference survives), and the resulting state both passes <see cref="GameDataValidation.Validate"/>
    /// and round-trips through <see cref="SaveManager"/> unchanged, exactly the DoD's own two checks.
    /// </summary>
    [Fact]
    public void ARebellionThatEliminatesTheOldOwner_SweepsCleanly_AndRoundTrips()
    {
        // Branch (b): weak's only city is allegiant to strong, and strong is alive.
        var city = CaptureTestbed.City(
            "lonely", "Lonely", 0, 0, "weak", "strong", loyalty: 20, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var weakArmy = CaptureTestbed.Army("weak-army", "weak", x: 9, y: 9, morale: 50);
        var weak = CaptureTestbed.Nation("weak", unity: 600, treasury: 100);
        var strong = CaptureTestbed.Nation("strong", unity: 600, treasury: 500);
        var state = EliminationForcesTestbed.StateWith(
            new[] { weak, strong }, new[] { city }, new[] { weakArmy });

        var result = Rebellion.Run(state, World, Ruleset, city, NullEventSink.Instance);

        var eliminatedWeak = result.NationById("weak")!;
        Assert.True(eliminatedWeak.Eliminated);
        Assert.Equal("strong", eliminatedWeak.ConqueredBy);
        Assert.Equal(Ruleset.Capture.EliminationUnityReset, eliminatedWeak.Unity);
        Assert.Equal("strong", result.CityById("lonely")!.Owner);

        // Nothing may reference a removed entity: weak's own army is disposed of by the same elimination
        // path a forced capture already uses (T84's EliminationForces), not left dangling.
        Assert.DoesNotContain(result.Armies, a => a.Nation == "weak");

        // Resources conserved: the receiver's treasury credit is exactly contribution * multiplier.
        var contribution = CityTaxContribution.Compute(city);
        Assert.Equal(
            500 + (contribution * Ruleset.Capture.DefectionTreasuryCreditMultiplier),
            result.NationById("strong")!.Treasury);

        AssertNoDanglingIdsAndRoundTrips(result, "elimination-sweep");
    }

    /// <summary>
    /// Done-when's own two checks for the elimination sweep: every reference resolves
    /// (<see cref="GameDataValidation.Validate"/>), and the state survives the real
    /// <see cref="SaveManager.Serialize"/>/<see cref="SaveManager.Load"/> round trip byte-for-byte, the
    /// same pattern <c>EliminationForcesTests</c> already established for T84.
    /// </summary>
    private static void AssertNoDanglingIdsAndRoundTrips(GameState result, string label)
    {
        GameDataValidation.Validate($"t89-rebellion-{label}", result);

        var toy = PersistenceTestbed.Toy;
        var save = new SaveGame(
            SchemaVersion: result.SchemaVersion,
            Id: $"t89-rebellion-round-trip-{label}",
            Label: $"T89 rebellion round trip ({label})",
            ScenarioId: result.ScenarioId,
            WorldId: result.WorldId,
            RulesetId: result.RulesetId,
            State: result);

        var text = SaveManager.Serialize(save);
        var reloaded = SaveManager.Load($"t89-rebellion-round-trip-{label}.json", text, toy.World, toy.Ruleset);

        Assert.Equal(result, reloaded.State);
    }
}
