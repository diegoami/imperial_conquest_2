using IC2.Engine.Ai;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Cities.Capture;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// T87 (<c>#389</c>), DoD 4: <see cref="Rebirth.Run"/> (<c>FUN_0044C360</c>)
/// <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-quarterly-rebellion.md">
/// decompiled-quarterly-rebellion.md</see> §4, at the decision level — hand-built fixtures, the same
/// convention <c>RebellionTests</c> already uses (<see cref="EliminationForcesTestbed.StateWith"/>, never
/// <see cref="CaptureTestbed.StateWith"/>, so an arbitrary nation set still passes
/// <see cref="IC2.Engine.Serialization.GameDataValidation"/>).
/// </summary>
public sealed class RebirthTests
{
    private static Ruleset Ruleset => CaptureTestbed.Ruleset;

    /// <summary>One qualifying city: allegiance names <paramref name="deadNationId"/>, loyalty under the rebirth threshold.</summary>
    private static CityState QualifyingCity(
        string id, string ownerId, string deadNationId, int fortificationCode = 0, int populationThousands = 10) =>
        CaptureTestbed.City(
            id, id, x: 0, y: 0, ownerId, deadNationId, loyalty: 20, fortificationCode,
            populationThousands, maxPopulationThousands: 500, tribute: 5);

    // ---------------------------------------------------------------------------------------------
    // The 7/8-city boundary.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Exactly <see cref="EconomyRules.RebirthMinimumQualifyingCityCount"/> (7) qualifying cities: the
    /// report's own "proceeds only if the count is &gt; 7" is a strict inequality, so exactly 7 does not
    /// qualify. <see cref="Assert.Same"/> proves <paramref name="state"/> comes back untouched, not merely
    /// unchanged in value.
    /// </summary>
    [Fact]
    public void SevenQualifyingCities_DoesNotRebirth()
    {
        var dead = CaptureTestbed.Nation("dead", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var cities = Enumerable.Range(0, 7).Select(i => QualifyingCity($"q{i}", "owner", "dead")).ToList();
        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner }, cities);

        // No draw: the qualifying-city check fails before the leader-name draw is reached.
        var result = Rebirth.Run(state, Ruleset, dead, NullEventSink.Instance, new ScriptedRng());

        Assert.Same(state, result);
    }

    /// <summary>Eight qualifying cities clears the strict <c>&gt; 7</c> gate and rebirth proceeds.</summary>
    [Fact]
    public void EightQualifyingCities_Rebirths()
    {
        var dead = CaptureTestbed.Nation("dead", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var cities = Enumerable.Range(0, 8).Select(i => QualifyingCity($"q{i}", "owner", "dead")).ToList();
        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner }, cities);

        // Review round 1, N1: rebirth qualifies, so the one leader-name draw is made and discarded --
        // AssertAllDrawsConsumed below proves the draw actually happened, not merely that a scripted one
        // would have been accepted if made (proved by mutation: dropping Rebirth.Run's own `rng.NextInt`
        // call makes this assertion fail with one unconsumed NextInt draw, verified locally and reverted).
        var rng = new ScriptedRng(nextIntDraws: new[] { 0 });
        var result = Rebirth.Run(state, Ruleset, dead, NullEventSink.Instance, rng);

        Assert.True(result.NationById("dead")!.Unity > 0);
        Assert.NotSame(state, result);
        rng.AssertAllDrawsConsumed();
    }

    /// <summary>
    /// T146 Done-when 6: with a pool, rebirth's one existing draw selects and writes that entry (no
    /// retry, no second draw); without one the same fixture's draw is still made and the leader is
    /// unchanged.
    /// </summary>
    [Fact]
    public void With_a_pool_the_one_draw_writes_its_entry_and_the_draw_count_is_unchanged()
    {
        var dead = CaptureTestbed.Nation("dead", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var cities = Enumerable.Range(0, 8).Select(i => QualifyingCity($"q{i}", "owner", "dead")).ToList();
        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner }, cities);
        var pool = ValueList.Of(Enumerable.Range(0, 12).Select(i => $"leader-{i}").ToArray());

        var rng = new ScriptedRng(nextIntDraws: new[] { 5 }, expectedNextIntBounds: new[] { 12 });
        var result = Rebirth.Run(state, Ruleset, dead, NullEventSink.Instance, rng, pool);

        rng.AssertAllDrawsConsumed();
        Assert.Equal(pool[5], result.NationById("dead")!.LeaderName);

        // Without a pool the same fixture still makes exactly one draw, and the leader is unchanged.
        var noPoolRng = new ScriptedRng(nextIntDraws: new[] { 5 }, expectedNextIntBounds: new[] { 12 });
        var noPool = Rebirth.Run(state, Ruleset, dead, NullEventSink.Instance, noPoolRng);
        noPoolRng.AssertAllDrawsConsumed();
        Assert.Equal(dead.LeaderName, noPool.NationById("dead")!.LeaderName);
    }

    // ---------------------------------------------------------------------------------------------
    // Every reset field, the defections and the capital choice, in one fixture.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Eight qualifying cities across two different owners ("ownerA" three, "ownerB" five), one
    /// non-qualifying control city (allegiance "dead" but loyalty at the threshold, owned by "ownerA")
    /// left untouched, and one city with no connection to "dead" at all (owned and allegiant to
    /// "ownerA") also left untouched. "q7" is given a far higher fortification and population than every
    /// other qualifying city, so it is unambiguously the strongest after its own defection (all eight
    /// start at the same pre-defection loyalty, so <see cref="CityCaptureResolver.Defect"/>'s own
    /// loyalty-after-transfer formula gives them all the same post-defection loyalty — fortification and
    /// population are what this fixture actually varies). Two of "dead"'s own 40 recruitment slots start
    /// with non-zero troops, one targeting a city outside this fixture entirely (so
    /// <see cref="CityCaptureResolver.Defect"/>'s own per-city slot removal never reaches it) — proving
    /// the slot reset is rebirth's own field write, not a side effect of any single defection.
    /// </summary>
    [Fact]
    public void EightQualifyingCities_ResetsEveryField_DefectsEachOne_AndPicksTheStrongestAsCapital()
    {
        var dead = CaptureTestbed.Nation(
            "dead", treasury: -500, unity: 0, wealth: 999, taxBase: 777, capitalCityId: "old-cap", eliminated: true,
            recruitmentSlots: ValueList.Of(
                new RecruitmentSlot("some-other-city", "archers", Troops: 4000, StateCode: 6),
                new RecruitmentSlot("q0", "light_infantry", Troops: 2500, StateCode: 10))) with
        {
            // T87 rework round 1 (review B6, mutation M3): a non-null starting ConqueredBy, so a
            // mutation that drops `ConqueredBy = null` from Rebirth.Run's own reset is not trivially
            // true against CaptureTestbed.Nation's own default (null).
            ConqueredBy = "somebody",
        };
        var ownerA = CaptureTestbed.Nation("ownerA", unity: 600);
        var ownerB = CaptureTestbed.Nation("ownerB", unity: 600);
        // T87 rework round 1 (review B6, mutation M4): a third nation with its own cooldown toward
        // "dead" that is not one of the moved cities' own owners -- ownerA's and ownerB's own relations
        // get overwritten by their own -8 defection penalty regardless of whether the row is actually
        // zeroed first, so neither proves the row reset on its own. "outsider" has no city connection to
        // this rebirth at all, so its own relation can only change if the row-zero step itself runs.
        var outsider = CaptureTestbed.Nation("outsider", unity: 600);

        var qualifying = new List<CityState>();
        for (var i = 0; i < 3; i++)
        {
            qualifying.Add(QualifyingCity($"q{i}", "ownerA", "dead"));
        }

        for (var i = 3; i < 7; i++)
        {
            qualifying.Add(QualifyingCity($"q{i}", "ownerB", "dead"));
        }

        // The strongest of the eight: far higher fortification and population than the rest.
        qualifying.Add(QualifyingCity("q7", "ownerB", "dead", fortificationCode: 90, populationThousands: 400));

        var oldCap = CaptureTestbed.City(
            "old-cap", "OldCap", 1, 1, "dead", "dead", loyalty: 50, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        // Control: allegiance names "dead" but loyalty is AT the threshold (not under it) -- never a candidate.
        var atThreshold = CaptureTestbed.City(
            "at-threshold", "AtThreshold", 2, 2, "ownerA", "dead",
            loyalty: Ruleset.Economy.RebirthCandidateLoyaltyThreshold, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        // Control: no connection to "dead" at all -- proves the sweep does not touch every city in the game.
        var unrelated = CaptureTestbed.City(
            "unrelated", "Unrelated", 3, 3, "ownerA", "ownerA", loyalty: 10, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        // ownerB owns only candidate cities otherwise -- a spare, non-candidate city of its own keeps it
        // from being eliminated once all five of its own candidates defect away (which would trigger
        // RelationTransitions.ResetAllOnElimination against it, an effect this test does not mean to
        // exercise and that would confound the ownerA<->ownerB relation assertion below).
        var ownerBSpare = CaptureTestbed.City(
            "ownerb-spare", "OwnerBSpare", 4, 4, "ownerB", "ownerB", loyalty: 50, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);

        var cities = new List<CityState> { oldCap, atThreshold, unrelated, ownerBSpare };
        cities.AddRange(qualifying);

        var state = EliminationForcesTestbed.StateWith(new[] { dead, ownerA, ownerB, outsider }, cities);
        state = state with
        {
            Relations = state.Relations
                .WithRelation("dead", "ownerA", -2)
                .WithRelation("dead", "ownerB", -3)
                .WithRelation("dead", "outsider", -5)
                .WithRelation("ownerA", "ownerB", -1),
        };

        var sink = new RecordingEventSink();
        // Review round 1, N1: rebirth qualifies (eight cities), so the leader-name draw runs once, discarded.
        var result = Rebirth.Run(state, Ruleset, dead, sink, new ScriptedRng(nextIntDraws: new[] { 0 }));

        var reborn = result.NationById("dead")!;

        // ---- Reset fields ----
        // Unity resets to RebirthUnity, then each of the eight defections' own CityCaptureResolver.Defect
        // call credits the receiver (the reborn nation itself) DefectionUnityGain, capped at UnityCap --
        // Rebellion.cs's own report citation ("Who calls what": FUN_0044C360's own caller list names
        // FUN_0044BED8, the same defection routine, for each moved city) confirms rebirth reuses the full
        // defection effect, not a trimmed version that skips the receiver's own unity credit.
        var expectedUnity = Ruleset.Economy.RebirthUnity;
        for (var i = 0; i < 8; i++)
        {
            expectedUnity = Math.Min(Ruleset.Economy.UnityCap, expectedUnity + Ruleset.Capture.DefectionUnityGain);
        }

        Assert.Equal(expectedUnity, reborn.Unity);
        // T87 rework round 1 (review B6, M3): "dead" started with ConqueredBy = "somebody" -- proves the
        // reset writes null, not merely that CaptureTestbed.Nation's own default already was null.
        Assert.Null(reborn.ConqueredBy);
        // Treasury and tax base reset to 0, then the eight defections' own credits accumulate on top
        // (CityCaptureResolver.Defect's own treasury/tax-base transfer) -- so both end up positive, not 0.
        // T87 rework round 1 (review B6, M2): the exact expected tax base, computed through the same
        // CityTaxContribution.Compute formula CityOwnershipTaxTransfer.Transfer itself uses, against
        // each moved city's own (pre-defection, population/tribute unaffected by a defection)
        // record -- proves the reset to 0 before these credits, not merely that the total is
        // non-negative (which "dead"'s own starting taxBase: 777, left untouched by a dropped reset,
        // would also satisfy).
        Assert.True(reborn.Treasury > 0);
        var expectedTaxBase = qualifying.Sum(
            city => CityTaxContribution.Compute(city) * Ruleset.Economy.TaxBaseContributionMultiplier);
        Assert.Equal(expectedTaxBase, reborn.TaxBase);
        Assert.Equal(Ruleset.Economy.RebirthTaxRatePercent, reborn.TaxRatePercent);
        Assert.Equal(Ruleset.Economy.RebirthMobilizedPercent, reborn.MobilizedPercent);
        Assert.False(reborn.Eliminated);
        Assert.All(reborn.RecruitmentSlots, slot => Assert.Equal(0, slot.Troops));
        // The slots themselves (state, type, target) are kept, not removed.
        Assert.Equal(2, reborn.RecruitmentSlots.Count);
        Assert.Contains(reborn.RecruitmentSlots, s => s.TargetCityId == "some-other-city" && s.UnitTypeId == "archers" && s.StateCode == 6);
        // The LeaderName step stays [open] -- see Rebirth.cs's own remarks. Not renamed.
        Assert.Equal(dead.LeaderName, reborn.LeaderName);

        // ---- Relations: the reborn nation's own row is zeroed, then each moved city's own -8 penalty
        // is written against its own former owner (both owners here) ----
        Assert.Equal(Ruleset.Economy.RebirthDefectionRelationPenalty, result.Relations.Get("dead", "ownerA"));
        Assert.Equal(Ruleset.Economy.RebirthDefectionRelationPenalty, result.Relations.Get("dead", "ownerB"));
        // T87 rework round 1 (review B6, M4): "outsider" holds no city connection to this rebirth at
        // all, so its own -5 cooldown toward "dead" can only reach 0 through the row-zero step itself --
        // ownerA's and ownerB's own assertions above would stay green even if that step were replaced
        // with a no-op, since their own -8 defection penalty overwrites whatever the row reset left.
        Assert.Equal(0, result.Relations.Get("dead", "outsider"));
        // Third parties' own cooldowns are untouched by rebirth's own row reset (this engine's row is
        // symmetric by construction, but nothing here writes ownerA<->ownerB directly).
        Assert.Equal(-1, result.Relations.Get("ownerA", "ownerB"));

        // ---- The defections: all eight moved, the two controls did not ----
        foreach (var id in Enumerable.Range(0, 8).Select(i => $"q{i}"))
        {
            Assert.Equal("dead", result.CityById(id)!.Owner);
        }

        Assert.Equal("ownerA", result.CityById("at-threshold")!.Owner); // untouched: at the threshold, not under it.
        Assert.Equal("ownerA", result.CityById("unrelated")!.Owner); // untouched: no connection to "dead".
        Assert.Equal(8, sink.Events.OfType<CityDefectsToNation>().Count());

        // ---- The capital choice: q7, unambiguously the strongest ----
        Assert.Equal("q7", reborn.CapitalCityId);
        var newCapital = result.CityById("q7")!;
        var capture = Ruleset.Capture;
        Assert.Equal(
            Math.Min(capture.CapitalMoveNewCapitalStatCap, newCapital.Loyalty - capture.CapitalMoveNewCapitalLoyaltyGain + capture.CapitalMoveNewCapitalLoyaltyGain),
            newCapital.Loyalty);
        Assert.Equal(Math.Min(capture.CapitalMoveNewCapitalStatCap, 90 + capture.CapitalMoveNewCapitalFortificationGain), newCapital.FortificationCode);
        Assert.Equal(400 + capture.CapitalMoveNewCapitalPopulationGain, newCapital.PopulationThousands);
        Assert.Equal(500 + capture.CapitalMoveNewCapitalMaxPopulationGain, newCapital.MaxPopulationThousands);
        Assert.Equal(5 + capture.CapitalMoveNewCapitalTributeGain, newCapital.Tribute);

        // The old capital is left exactly as it was -- rebirth's own capital pick only ever touches the
        // new capital (this type's own remarks; the old-capital marker repaint is presentation, unmodelled
        // here exactly as ConquestTrigger's own remarks already note for the capital-move case).
        Assert.Equal("dead", result.CityById("old-cap")!.Owner);
        Assert.Equal(50, result.CityById("old-cap")!.Loyalty);
    }

    // ---------------------------------------------------------------------------------------------
    // Hazard: probe two dead nations, one reborn and one not.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// "dead-a" has eight qualifying cities (rebirths); "dead-b" has only three (does not). Run through
    /// <see cref="Rebellion.Run"/> itself, not <see cref="Rebirth.Run"/> directly, so this also proves
    /// <see cref="Rebellion.cs"/>'s own branch (a) reaches the right nation for the right city.
    /// </summary>
    [Fact]
    public void TwoDeadNations_OnlyTheOneWithEnoughQualifyingCitiesReturns()
    {
        var deadA = CaptureTestbed.Nation("dead-a", unity: 0, eliminated: true);
        var deadB = CaptureTestbed.Nation("dead-b", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("owner", unity: 600);

        var citiesA = Enumerable.Range(0, 8).Select(i => QualifyingCity($"a{i}", "owner", "dead-a")).ToList();
        var citiesB = Enumerable.Range(0, 3).Select(i => QualifyingCity($"b{i}", "owner", "dead-b")).ToList();
        var allCities = new List<CityState>();
        allCities.AddRange(citiesA);
        allCities.AddRange(citiesB);

        var state = EliminationForcesTestbed.StateWith(new[] { deadA, deadB, owner }, allCities);

        // Rebellion.Run is invoked once per rebelling city, exactly as QuarterlyCityEconomySystem's own
        // loop would for each of "dead-a"/"dead-b"'s own allegiant cities under the rebellion threshold --
        // any one qualifying city triggers the same nation-wide count. Review round 1, N1: only "dead-a"
        // (eight qualifying cities) actually rebirths and draws a leader name; "dead-b" (three) does not,
        // so one shared, once-scripted rng covers both calls.
        var rng = new ScriptedRng(nextIntDraws: new[] { 0 });
        var afterA = Rebellion.Run(state, EconomyTestbed.Toy.World, Ruleset, allCities[0], NullEventSink.Instance, rng);
        var afterB = Rebellion.Run(afterA, EconomyTestbed.Toy.World, Ruleset, citiesB[0], NullEventSink.Instance, rng);

        Assert.False(afterB.NationById("dead-a")!.Eliminated);
        Assert.True(afterB.NationById("dead-a")!.Unity > 0);
        Assert.True(afterB.NationById("dead-b")!.Eliminated); // untouched: only 3 qualifying cities.
        Assert.Equal(0, afterB.NationById("dead-b")!.Unity);
    }

    /// <summary>
    /// Review R5: <see cref="Rebellion.Run"/>'s own branch (a) is the hand-off that passes the reborn
    /// nation's pool to <see cref="Rebirth.Run"/>; the direct <see cref="Rebirth.Run"/> tests above never
    /// exercise it (passing <see langword="null"/> in its place left the suite green). A pool in the
    /// world makes the reborn nation's leader the entry this one draw selects.
    /// </summary>
    [Fact]
    public void Rebellion_passes_the_reborn_nations_pool_so_its_one_draw_writes_that_entry()
    {
        var dead = CaptureTestbed.Nation("dead-a", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var cities = Enumerable.Range(0, 8).Select(i => QualifyingCity($"a{i}", "owner", "dead-a")).ToList();
        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner }, cities);

        var pool = ValueList.Of(Enumerable.Range(0, 12).Select(i => $"leader-{i}").ToArray());
        // Rebellion reads the dead nation's pool from the world, so the world must carry "dead-a" (the
        // toy world has only north/south) -- appended with the pool and no capital so its own geography
        // fallback is never reached in branch (a).
        var world = EconomyTestbed.Toy.World with
        {
            Nations = ValueList.From(EconomyTestbed.Toy.World.Nations.Append(
                EconomyTestbed.Toy.World.Nations[0] with
                {
                    Id = "dead-a", Name = "Dead A", LeaderName = pool[0], LeaderNames = pool, CapitalCityId = null,
                })),
        };

        var rng = new ScriptedRng(nextIntDraws: new[] { 5 }, expectedNextIntBounds: new[] { 12 });
        var after = Rebellion.Run(state, world, Ruleset, cities[0], NullEventSink.Instance, rng);

        rng.AssertAllDrawsConsumed();
        Assert.Equal(pool[5], after.NationById("dead-a")!.LeaderName);
    }

    // ---------------------------------------------------------------------------------------------
    // No armies or fleets added; T84's own deletions stay deleted.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Rebirth_AddsNoArmiesOrFleets()
    {
        var dead = CaptureTestbed.Nation("dead", unity: 0, eliminated: true); // T84 already disposed of its forces.
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var ownerArmy = CaptureTestbed.Army("owner-army", "owner", x: 5, y: 5, morale: 50);
        // "owner" keeps an extra, non-candidate city of its own so the eight defections below do not
        // themselves eliminate it (which would dispose of ownerArmy too, through EliminationForces --
        // a real effect of losing a nation's own last city, not something this test means to exercise).
        var ownerSpare = CaptureTestbed.City(
            "owner-spare", "OwnerSpare", 9, 9, "owner", "owner", loyalty: 50, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var cities = Enumerable.Range(0, 8).Select(i => QualifyingCity($"q{i}", "owner", "dead")).ToList();
        cities.Add(ownerSpare);
        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner }, cities, new[] { ownerArmy });

        // Review round 1, N1: qualifies, so the leader-name draw runs once, discarded.
        var result = Rebirth.Run(state, Ruleset, dead, NullEventSink.Instance, new ScriptedRng(nextIntDraws: new[] { 0 }));

        Assert.True(result.NationById("dead")!.Unity > 0); // rebirth did happen.
        Assert.False(result.NationById("owner")!.Eliminated); // owner-spare kept it alive.
        Assert.DoesNotContain(result.Armies, a => a.Nation == "dead");
        Assert.DoesNotContain(result.Fleets, f => f.Nation == "dead");
        Assert.Single(result.Armies); // "owner-army" -- untouched, and nothing new added anywhere.
    }

    // ---------------------------------------------------------------------------------------------
    // Hazards: a once-human reborn nation is still AI-played; it takes turns and is a legal
    // diplomatic target again.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <see cref="AiTurn.Run"/>'s own gate (<c>src/IC2.Engine/Ai/AiTurn.cs:104</c>) skips a seat only when
    /// it is <see cref="NationState.Eliminated"/>, unknown, or not <see cref="SeatControl.Ai"/> --
    /// unlike the original, it does not re-test unity directly. Rebirth clears <c>Eliminated</c> and
    /// never touches <see cref="NationState.Control"/>, so a nation that was human before it fell (its own
    /// <c>Control</c> already flipped to <see cref="SeatControl.Ai"/> by the fall itself, not by rebirth)
    /// comes back exactly as the report's own "it comes back as a computer nation" says -- still AI, and
    /// no longer skipped.
    /// </summary>
    [Fact]
    public void ARebornNation_IsNoLongerSkippedByAiTurnsOwnGate_AndAOnceHumanSeatStaysAiPlayed()
    {
        // T87 rework round 1 (review B8, bug #441): "dead" starts Human-controlled, owning exactly one
        // city -- its last -- which is defected away for real through CityCaptureResolver.Defect, the
        // same path any ordinary elimination takes. NationElimination.ApplyIfLastCityLost's own fix
        // (this round) hands a human seat to the AI on elimination; the old version of this test set
        // Control = SeatControl.Ai by hand, which was tautological (Rebirth.Run itself never touches
        // Control either way, so the assertion below proved nothing about the engine's own elimination
        // path).
        var deadLastCity = CaptureTestbed.City(
            "dead-last-city", "DeadLastCity", 0, 0, "dead", "dead", loyalty: 20, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var dead = CaptureTestbed.Nation("dead", unity: 600) with { Control = SeatControl.Human };
        var receiver = CaptureTestbed.Nation("receiver", unity: 600);
        var owner = CaptureTestbed.Nation("owner", unity: 600);

        var qualifying = Enumerable.Range(0, 8).Select(i => QualifyingCity($"q{i}", "owner", "dead")).ToList();
        var cities = new List<CityState> { deadLastCity };
        cities.AddRange(qualifying);
        var state = EliminationForcesTestbed.StateWith(new[] { dead, receiver, owner }, cities);

        var afterElimination = CityCaptureResolver.Defect(state, "dead-last-city", "receiver", Ruleset, NullEventSink.Instance);
        var eliminatedDead = afterElimination.NationById("dead")!;
        Assert.True(eliminatedDead.Eliminated);
        Assert.Equal(SeatControl.Ai, eliminatedDead.Control); // bug #441's own fix, proven through the real path.

        // Review round 1, N1: qualifies, so the leader-name draw runs once, discarded.
        var result = Rebirth.Run(afterElimination, Ruleset, eliminatedDead, NullEventSink.Instance, new ScriptedRng(nextIntDraws: new[] { 0 }));
        var reborn = result.NationById("dead")!;

        Assert.False(reborn.Eliminated); // AiTurn.Run's own gate no longer skips it.
        Assert.Equal(SeatControl.Ai, reborn.Control); // still AI-played, even though it fell as a human seat.
    }

    /// <summary>
    /// T87 rework round 2 (review R1, bug #441's conquest half): the same shape as the sibling test
    /// above, but eliminating "dead" through <see cref="ConquestCascade.Apply"/> -- the conquest path --
    /// rather than <see cref="CityCaptureResolver.Defect"/>, proving this round's own fix in
    /// <c>ConquestCascade.cs</c>, not only the defection path's <c>NationElimination.cs</c> one. "dead"
    /// owns only its own capital; <c>ConquestCascade.Apply</c> transfers whatever a loser currently owns
    /// regardless of city count, so it need not actually clear the conquest threshold here -- this test's
    /// own subject is the seat hand-over that method's elimination step now makes, not the trigger that
    /// decides to call it (<c>ConquestCascadeTests</c>' own job).
    /// </summary>
    [Fact]
    public void ARebornNation_ThatFellByConquest_AlsoStaysAiPlayed()
    {
        var deadCap = CaptureTestbed.City(
            "dead-cap", "DeadCap", 0, 0, "dead", "dead", loyalty: 20, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var dead = CaptureTestbed.Nation("dead", unity: 600, capitalCityId: "dead-cap") with { Control = SeatControl.Human };
        var winner = CaptureTestbed.Nation("winner", unity: 600);
        var owner = CaptureTestbed.Nation("owner", unity: 600);

        var qualifying = Enumerable.Range(0, 8).Select(i => QualifyingCity($"q{i}", "owner", "dead")).ToList();
        var cities = new List<CityState> { deadCap };
        cities.AddRange(qualifying);
        var state = EliminationForcesTestbed.StateWith(new[] { dead, winner, owner }, cities);

        var afterConquest = ConquestCascade.Apply(state, Ruleset, "dead", "winner", NullEventSink.Instance);
        var eliminatedDead = afterConquest.NationById("dead")!;
        Assert.True(eliminatedDead.Eliminated);
        Assert.Equal(SeatControl.Ai, eliminatedDead.Control); // this round's own fix, proven through the real path.

        var result = Rebirth.Run(afterConquest, Ruleset, eliminatedDead, NullEventSink.Instance, new ScriptedRng(nextIntDraws: new[] { 0 }));
        var reborn = result.NationById("dead")!;

        Assert.False(reborn.Eliminated);
        Assert.Equal(SeatControl.Ai, reborn.Control); // still AI-played, even though it fell as a human seat.
    }

    /// <summary>
    /// A real <see cref="DeclareWarCommand"/> dispatch against the reborn nation, through the same
    /// <see cref="CommandDispatcher"/>/<see cref="SystemRegistry"/> the CLI itself uses -- proving
    /// <see cref="DiplomacyRejections.CounterpartyEliminated"/> no longer fires, not merely that the flag
    /// reads <see langword="false"/>.
    /// </summary>
    [Fact]
    public void ARebornNation_CanBeDeclaredWarOnAgain_NoLongerRejectedAsEliminated()
    {
        var dead = CaptureTestbed.Nation("dead", unity: 0, eliminated: true);
        var owner = CaptureTestbed.Nation("owner", unity: 600);
        var ownerSpare = CaptureTestbed.City(
            "owner-spare", "OwnerSpare", 9, 9, "owner", "owner", loyalty: 50, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var cities = Enumerable.Range(0, 8).Select(i => QualifyingCity($"q{i}", "owner", "dead")).ToList();
        cities.Add(ownerSpare);
        var state = EliminationForcesTestbed.StateWith(new[] { dead, owner }, cities);

        // Review round 1, N1: qualifies, so the leader-name draw runs once, discarded.
        var reborn = Rebirth.Run(state, Ruleset, dead, NullEventSink.Instance, new ScriptedRng(nextIntDraws: new[] { 0 }));
        // Relations start at Peace (EliminationForcesTestbed.StateWith's own uniform matrix), so a fresh
        // declare-war is legal on every other ground once the eliminated-counterparty gate is cleared.
        // Dispatch as "owner" -- CommandDispatcher's own issuer-is-the-active-seat gate needs the active
        // seat to actually be the issuer, so this points ActiveSeatIndex at "owner" rather than "dead"
        // (StateWith's own default, index 0 of the nation list passed above).
        reborn = reborn with { ActiveSeatIndex = reborn.TurnOrder.IndexOfId(id => id, "owner") };

        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Ruleset, EconomyTestbed.Toy.World, NullEventSink.Instance);
        var result = dispatcher.Dispatch(reborn, new DeclareWarCommand("owner", "dead"));

        Assert.False(result.IsRejected);
    }
}
