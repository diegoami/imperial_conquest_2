using System.Linq;
using IC2.Engine.Battle;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 5: "Per-siege attrition runs on every attempt, win or
/// lose." Already implemented and tested by T16 inside <see cref="InstantBattleResolver.ResolveSiege"/>
/// (<c>src/IC2.Engine/Battle/**</c>, not this task's Owns list); this file proves the DoD line through
/// that resolver's own public entry point, which this task is free to call without owning the file.
/// </summary>
public sealed class SiegeAttritionTests
{
    /// <summary>An attacker overwhelming enough to win still takes casualties from the attempt itself.</summary>
    [Fact]
    public void AttackerThatWinsTheSiege_StillTakesAttrition()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 30, fortificationCode: 0,
            populationThousands: 1, maxPopulationThousands: 10, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 90, CaptureTestbed.Unit("heavy_infantry", 20_000));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(BattleSide.Attacker, resolution.Result.Winner);
        Assert.NotEmpty(resolution.Result.UnitCasualties);
        var attackerAfter = resolution.State.ArmyById("army")!;
        Assert.True(attackerAfter.TotalTroops < attacker.TotalTroops);
    }

    /// <summary>
    /// An attacker too weak to win still takes attrition from the failed attempt. Chosen so the casualty
    /// ratio (<c>loserPower × 40 / winnerPower</c>) truncates to a small but strictly positive value
    /// (attacker power 3,720, defender power 11,500 → ratio 12) rather than to zero: an overwhelming power
    /// gap would make this DoD line vacuously true by giving every slot a zero-troops loss regardless of
    /// whether attrition ran at all, which is exactly the case that must not be mistaken for "no attrition".
    /// </summary>
    [Fact]
    public void AttackerThatLosesTheSiege_StillTakesAttrition()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 50, fortificationCode: 0,
            populationThousands: 20, maxPopulationThousands: 30, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 60, CaptureTestbed.Unit("heavy_infantry", 5000));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(3720, resolution.Result.AttackerPower);
        Assert.Equal(11_500, resolution.Result.DefenderPower);
        Assert.Equal(BattleSide.Defender, resolution.Result.Winner);
        Assert.NotEmpty(resolution.Result.UnitCasualties);
        var attackerAfter = resolution.State.ArmyById("army")!;
        Assert.True(attackerAfter.TotalTroops < attacker.TotalTroops);
    }

    /// <summary>
    /// T63 DoD 2: bug #290's own four-row table, reproduced directly -- <c>ratio =
    /// clamp(defenderStrength x 6 / attackerStrength, 1, 15)</c> at each of the four cases the bug report
    /// gives, against the ORIGINAL's own values (the merged column, 20/39/40/4, was the defect). Each
    /// row's <c>defenderPower</c> is built from a recruitment-slot addend alone (loyalty, fortification
    /// and population all zero), so the ratio is exactly <c>addend x 6 / attackerPower</c> with no
    /// erosion-side rounding to account for.
    /// </summary>
    [Theory]
    [InlineData(100, 50, 3, false)]    // attacker wins, defender = 1/2 attacker -> 3
    [InlineData(100, 99, 5, false)]    // attacker wins narrowly -> ~5 (99 x 6 / 100 = 5.94 -> 5)
    [InlineData(100, 100, 6, true)]    // attacker fails, defender = attacker (a tie fails) -> 6
    [InlineData(10, 100, 15, true)]    // attacker fails, defender = 10x attacker -> clamp(60, 1, 15) = 15
    public void SiegeRatio_ReproducesBug290sFourRowTable(
        int attackerPower, int defenderPower, int expectedRatio, bool expectAttackerFails)
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 0, fortificationCode: 0,
            populationThousands: 0, maxPopulationThousands: 10, tribute: 0);
        // atk = (troops / 80) x morale -- 80 x attackerPower troops at morale 1 gives EXACTLY attackerPower.
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 1, CaptureTestbed.Unit("heavy_cavalry", 80 * attackerPower));
        // def = 0 (loyalty/fort/pop) + recruitment-slot addend (troops / 2) -- 2 x defenderPower troops
        // gives EXACTLY defenderPower.
        var defenderNation = CaptureTestbed.Nation(
            "defender",
            recruitmentSlots: ValueList.Of(new RecruitmentSlot("c1", "heavy_cavalry", 2 * defenderPower, StateCode: 4)));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), defenderNation }, new[] { city }, new[] { attacker });

        // A fixed divisor (105, the span's own floor) rather than a real seed: this test is about the
        // RATIO, and pinning the divisor too makes the expected loss an exact number instead of a range.
        var rng = new FixedDivisorRng();
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(attackerPower, resolution.Result.AttackerPower);
        Assert.Equal(defenderPower, resolution.Result.DefenderPower);
        Assert.Equal(expectAttackerFails, resolution.Result.Winner == BattleSide.Defender);

        // 80 x attackerPower troops is comfortably above heavy_cavalry's 250 deletion threshold even at
        // the ceiling ratio (15), so the deletion pass never interferes with reading the ratio back out
        // of the exact troop loss: (troops / 105) x expectedRatio.
        var attackerTroops = 80 * attackerPower;
        var expectedLoss = (attackerTroops / 105) * expectedRatio;
        var attackerAfter = resolution.State.ArmyById("army")!;
        Assert.Equal(attackerTroops - expectedLoss, attackerAfter.TotalTroops);
    }

    /// <summary>
    /// T63 B3/DoD 5: <c>GalatiaEliminationScenarioTests</c>' own two historical captures, reproduced
    /// through <see cref="InstantBattleResolver.ResolveSiege"/>'s now-confirmed erosion formula -- Laranda
    /// 41/59 &#8594; 30/44 and Gordium 54/23 &#8594; 42/18
    /// (<c>galatia-elimination-and-city-resupply-confirmed.md</c>; <c>docs/game-design.md</c> §Combat:
    /// "Laranda 41/59 &#8594; 30/44 is exactly &#215; 3/4 (needs def/atk &lt; 0.756)… Gordium 54/23
    /// &#8594; 42/18 needs def/atk &#8712; [0.7826, 0.7963)"). Each city's real, historical loyalty,
    /// fortification and population are the fixture; <c>attackerPower</c> is chosen (not historical -- the
    /// original attacking army's own strength was never recorded) to land <c>def/atk</c> inside the cited
    /// bound for that city. <c>GalatiaEliminationScenarioTests</c> itself deliberately calls
    /// <see cref="CityCaptureResolver.Capture"/> directly, bypassing this resolver, to isolate
    /// <c>FUN_0044bb18</c>'s own transfer pseudocode from a real attempt's erosion (see that file's own
    /// class remarks) -- this test is what proves the erosion formula actually reproduces the historical
    /// post-capture figures that isolation leaves unasserted there.
    /// </summary>
    [Theory]
    [InlineData("Laranda", 30, 41, 59, 200, 40_000, 8_000, 400, 30, 44)]
    [InlineData("Gordium", 35, 54, 23, 100, 29_600, 8_000, 296, 42, 18)]
    public void GalatiaHistoricalCaptures_ReproduceThePostSiegeFiguresThroughResolveSiege(
        string cityName, int loyalty, int fortificationBefore, int populationBefore, int maxPopulation,
        int expectedAttackerPower, int attackerTroops, int attackerMorale,
        int expectedFortificationAfter, int expectedPopulationAfter)
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", cityName, 0, 0, "defender", "defender", loyalty, fortificationBefore,
            populationBefore, maxPopulation, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, attackerMorale, CaptureTestbed.Unit("heavy_infantry", attackerTroops));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(expectedAttackerPower, resolution.Result.AttackerPower);

        var eroded = resolution.State.CityById("c1")!;
        Assert.Equal(expectedFortificationAfter, eroded.FortificationCode);
        Assert.Equal(expectedPopulationAfter, eroded.PopulationThousands);

        // N4: Decision 7's six BattleResult.City* fields, populated by a real siege through
        // ResolveSiege -- before/after values match the pre-siege inputs and the post-erosion city
        // state exactly, not merely non-null.
        Assert.Equal(loyalty, resolution.Result.CityLoyaltyBefore);
        Assert.Equal(eroded.Loyalty, resolution.Result.CityLoyaltyAfter);
        Assert.Equal(fortificationBefore, resolution.Result.CityFortificationPercentBefore);
        Assert.Equal(expectedFortificationAfter, resolution.Result.CityFortificationPercentAfter);
        Assert.Equal(populationBefore, resolution.Result.CityPopulationThousandsBefore);
        Assert.Equal(expectedPopulationAfter, resolution.Result.CityPopulationThousandsAfter);
    }

    /// <summary>
    /// N7 (T63 review round 1), enforcing Decision 3 in the <see cref="BattleResult"/> itself: a
    /// besieger that wins the strength comparison (<c>attackerPower(300) &gt; defenderPower(200)</c>)
    /// but is then emptied by THIS SAME attempt's own casualties (its one <c>heavy_cavalry</c> unit,
    /// 255 troops, loses 8 to a ratio-4 attrition pass and falls to 247 -- below the
    /// <c>2,500 / 10 = 250</c> national deletion threshold) must not be reported as a win: an
    /// ownerless city is not representable (Decision 3), so the result must read as the defender
    /// holding, not as an unresolved victory. Chained into
    /// <see cref="CityCaptureResolver.ResolveOutcome"/> to prove the full outcome agrees: no transfer,
    /// the confirmed "fails to capture" event, not a fabricated one.
    /// </summary>
    [Fact]
    public void EmptiedBesieger_ReportsTheDefenderAsWinner_NotACaptureTheCityDidNotYield()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "Emptied", 0, 0, "defender", "defender", loyalty: 0, fortificationCode: 0,
            populationThousands: 1, maxPopulationThousands: 10, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 100, CaptureTestbed.Unit("heavy_cavalry", 255));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new FixedDivisorRng(); // divisor always 105 (the minimum), for an exact, worst-case loss
        var siege = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(300, siege.Result.AttackerPower);
        Assert.Equal(200, siege.Result.DefenderPower);
        Assert.Null(siege.State.ArmyById("army")); // the besieger's only unit was deleted -- the army itself is gone

        // The result must NOT claim the attacker won, even though attackerPower > defenderPower.
        Assert.Equal(BattleSide.Defender, siege.Result.Winner);
        Assert.False(siege.Result.AttackerWon);
        Assert.Equal(0, siege.Result.WinnerCasualties);
        Assert.Equal(8, siege.Result.LoserCasualties); // the besieger's own 8 lost troops, now attributed to the "loser"

        var sink = new RecordingEventSink();
        var outcome = CityCaptureResolver.ResolveOutcome(
            siege.State, siege.Result, ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        Assert.Equal("defender", outcome.CityById("c1")!.Owner); // no capture
        Assert.Single(sink.Events.OfType<CityFailsToBeCaptured>());
        Assert.DoesNotContain(sink.Events, e => e is CityFallsToNation);
    }

    /// <summary>
    /// T63 DoD 5: the five steps, in order, on a SUCCESSFUL attempt -- (1) a pending fortification order
    /// is stripped before anything reads it, (2) def/atk are compared, (3) loyalty, then fortification,
    /// then population erode by the SAME def/atk pair, (4) population is floored, (5) the attacker's own
    /// casualties are applied (#290). The fortification word starts at 250 -- 50% finished with a 2-point
    /// order pending (<c>250 = 50 + 2 x 100</c>) -- so the strip (step 1) and its effect on the erosion
    /// input (step 3) are both exercised in the same fixture, not asserted separately from a guess.
    /// </summary>
    [Fact]
    public void SuccessfulSiege_StripsErodesFloorsPopulationThenAppliesAttrition_InOrder()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 80, fortificationCode: 250,
            populationThousands: 50, maxPopulationThousands: 240, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 100, CaptureTestbed.Unit("heavy_infantry", 40_000));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        // Step 2: atk = (40,000 / 80) x 100 = 50,000. def = 80 x 150 + 50 x 250 + 50 x 200 = 34,500 --
        // note the 50 is the STRIPPED fortification (250 % 100), not the raw 250: step 1 ran first.
        Assert.Equal(50_000, resolution.Result.AttackerPower);
        Assert.Equal(34_500, resolution.Result.DefenderPower);
        Assert.Equal(BattleSide.Attacker, resolution.Result.Winner);

        // Step 3: def/atk = 0.69, below the 3/4 floor threshold, so every field takes the FLOOR branch,
        // field x 3/4: loyalty 80 -> 60, fortification 50 -> 37, population 50 -> 37 (before the floor).
        var eroded = resolution.State.CityById("c1")!;
        Assert.Equal(60, eroded.Loyalty);

        // Step 4: population's eroded 37 is below 240 / 6 + 1 = 41, so the floor overrides it to 41.
        // Fortification has no such floor and keeps its own eroded value, 37.
        Assert.Equal(37, eroded.FortificationCode);
        Assert.Equal(41, eroded.PopulationThousands);

        // Step 5: the attacker still pays attrition even though it won -- ratio = clamp(34,500 x 6 /
        // 50,000, 1, 15) = clamp(4, 1, 15) = 4.
        Assert.NotEmpty(resolution.Result.UnitCasualties);
        var attackerAfter = resolution.State.ArmyById("army")!;
        Assert.True(attackerAfter.TotalTroops < attacker.TotalTroops);
    }

    /// <summary>
    /// T63 DoD 5: "a failed siege leaves fields ≤ 20 unchanged." A defender exactly as strong as the
    /// attacker (a tie, which the siege's own <c>defenderPower &lt; attackerPower</c> test counts as a
    /// failure, same as every other tie in this engine) gives <c>def/atk = 1</c>, so the erosion's ceiling
    /// term is the ONLY branch every field can take here -- the same fixture proves the boundary both
    /// ways: a field at 15 or 10 (≤ 20) is provably unchanged, and a field at 25 (&gt; 20) is provably not.
    /// </summary>
    [Fact]
    public void FailedSiege_FieldsAtOrBelowTwenty_AreUnchanged_ButA_LargerFieldErodes()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 15, fortificationCode: 25,
            populationThousands: 10, maxPopulationThousands: 50, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 100, CaptureTestbed.Unit("heavy_infantry", 8_400));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        // atk = (8,400 / 80) x 100 = 10,500. def = 15 x 150 + 25 x 250 + 10 x 200 = 10,500 -- an exact
        // tie, so the siege fails (ties go to the defender everywhere in this engine).
        Assert.Equal(10_500, resolution.Result.AttackerPower);
        Assert.Equal(10_500, resolution.Result.DefenderPower);
        Assert.Equal(BattleSide.Defender, resolution.Result.Winner);

        var eroded = resolution.State.CityById("c1")!;

        // loyalty 15: ceiling = 15 x 19/20 + 1 = 15 -- exactly the field's own starting value. Unchanged.
        Assert.Equal(15, eroded.Loyalty);

        // fortification 25: ceiling = 25 x 19/20 + 1 = 24 -- strictly below 25. Erosion begins just past
        // the 20 boundary, unlike the two fields at or below it.
        Assert.Equal(24, eroded.FortificationCode);

        // population 10: ceiling = 10 x 19/20 + 1 = 10 -- unchanged, and above the population floor
        // (50 / 6 + 1 = 9), so the floor does not override it either.
        Assert.Equal(10, eroded.PopulationThousands);
    }

    /// <summary>
    /// T63 B1: <c>classical-faithful</c> (and the toy ruleset, set the same way) reproduce the original's
    /// 16-bit clamp comparison -- as a SIGNED comparison (<c>JG</c>/<c>JL</c> at
    /// <c>FUN_00448FD0</c>/<c>FUN_00448FD8</c>, research 3f6ca09), not an unsigned one. Three raw ratios,
    /// chosen so their low 16 bits read very differently signed versus unsigned:
    /// <list type="bullet">
    /// <item><description><c>65,536</c>: low word 0 either way -- signed and unsigned agree (both give ratio 1). Kept as
    /// the simplest possible case, not as proof of the fix on its own.</description></item>
    /// <item><description><c>501,996</c>: the report's own worked example. Low word <c>43,244</c> unsigned, but
    /// <c>43,244 - 65,536 = -22,292</c> signed -- clamps to the floor (1). An unsigned reading (the B1 defect)
    /// would have read <c>43,244</c>, clamped to the ceiling (15) instead.</description></item>
    /// <item><description><c>40,000</c>: already under 65,536, so unsigned reads it unchanged (<c>40,000</c>, clamping
    /// to the ceiling, 15) -- but <c>40,000 - 65,536 = -25,536</c> signed, clamping to the floor (1) instead.
    /// This is the case an implementation could pass by only ever reducing modulo 65,536 without ever
    /// re-reading the low word as signed.</description></item>
    /// </list>
    /// </summary>
    [Theory]
    [InlineData(65_536)]
    [InlineData(501_996)]
    [InlineData(40_000)]
    public void SiegeRatioClamp_ReproducesThe16BitWrap_UnderTheFaithfulPolicy(int rawRatio)
    {
        var ruleset = CaptureTestbed.Ruleset;
        Assert.Equal(SiegeRatioClampPolicy.Reproduce16BitClamp, ruleset.Flags.BugPolicySiegeRatioClamp);

        var (attacker, _) = NearEmptyBesiegerFixture(rawRatio, out var state);
        var rng = new FixedDivisorRng();

        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        // atk = 6 (troops 559, morale 1); def = rawRatio exactly (a plain loyalty/fortification/population
        // sum of 0, plus one recruitment-slot addend of rawRatio). Raw ratio = rawRatio x 6 / 6 = rawRatio
        // exactly -- every one of the three signed low words above clamps to the floor, 1.
        Assert.Equal(6, resolution.Result.AttackerPower);
        Assert.Equal(rawRatio, resolution.Result.DefenderPower);

        var attackerAfter = resolution.State.ArmyById("army")!;
        var lost = attacker.TotalTroops - attackerAfter.TotalTroops;

        // Fixed divisor 105 (FixedDivisorRng): ratio 1 costs exactly (559 / 105) x 1 = 5 troops.
        Assert.Equal(5, lost);
    }

    /// <summary>
    /// T63 B1/Decision 1: <c>improved</c> computes the same three raw ratios in ordinary 32-bit
    /// arithmetic, so every one of them saturates at the 15 ceiling here -- the two tests together are the
    /// "besieger small enough to wrap" case DoD 6 asks for, once per preset, and together prove the
    /// faithful preset's SIGNED reading is what makes it differ from this one (a merely-unsigned reading
    /// would have agreed with this test at 40,000 and 501,996 too).
    /// </summary>
    [Theory]
    [InlineData(65_536)]
    [InlineData(501_996)]
    [InlineData(40_000)]
    public void SiegeRatioClamp_Clamps32Bit_UnderTheImprovedPolicy(int rawRatio)
    {
        var ruleset = CaptureTestbed.Ruleset with
        {
            Flags = CaptureTestbed.Ruleset.Flags with { BugPolicySiegeRatioClamp = SiegeRatioClampPolicy.Clamp32Bit },
        };

        var (attacker, _) = NearEmptyBesiegerFixture(rawRatio, out var state);
        var rng = new FixedDivisorRng();

        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(6, resolution.Result.AttackerPower);
        Assert.Equal(rawRatio, resolution.Result.DefenderPower);

        var attackerAfter = resolution.State.ArmyById("army")!;
        var lost = attacker.TotalTroops - attackerAfter.TotalTroops;

        // Ratio 15 (the un-wrapped ceiling) against the fixed divisor 105: (559 / 105) x 15 = 75.
        Assert.Equal(75, lost);
    }

    /// <summary>
    /// T63 D-A (the user's 2026-09-23 extension of Decision 1): the erosion's OWN <c>field x def / atk</c>
    /// ratio term wraps the same signed-16-bit way the attrition ratio does -- a separate local inside
    /// <c>FUN_0044B230</c>, but read through the same <c>FUN_00448FD0</c>/<c>FUN_00448FD8</c> comparisons
    /// (decompiled-defection-and-siege-attrition.md, research 3f6ca09). <c>attackerStrength</c> = 6,
    /// <c>defenderStrength</c> = 39,120: the ratio term for loyalty (100) is <c>100 x 39,120 / 6 =
    /// 652,000</c>, whose low word is <c>62,176</c> unsigned but <c>62,176 - 65,536 = -3,360</c> signed --
    /// negative, so classical-faithful's erosion takes the FLOOR branch (<c>100 x 3/4 = 75</c>) where
    /// improved's un-wrapped 652,000 takes the CEILING branch (<c>100 x 19/20 + 1 = 96</c>) instead. The
    /// two branches giving different, exact, hand-verifiable numbers is the proof that D-A's wrap reaches
    /// the erosion at all, not just the attrition ratio B1's own tests already cover.
    /// </summary>
    [Theory]
    [InlineData(SiegeRatioClampPolicy.Reproduce16BitClamp, 75)]
    [InlineData(SiegeRatioClampPolicy.Clamp32Bit, 96)]
    public void ErosionRatioTerm_WrapsTheSameSignedWay_PerPreset(SiegeRatioClampPolicy policy, int expectedLoyalty)
    {
        var ruleset = CaptureTestbed.Ruleset with { Flags = CaptureTestbed.Ruleset.Flags with { BugPolicySiegeRatioClamp = policy } };

        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 100, fortificationCode: 0,
            populationThousands: 0, maxPopulationThousands: 10, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 1, CaptureTestbed.Unit("heavy_cavalry", 559));
        // defenderStrength = loyalty x 150 + addend = 100 x 150 + 24,120 = 39,120 -- addend = troops / 2,
        // so 48,240 troops gives the 24,120 addend this needs.
        var defenderNation = CaptureTestbed.Nation(
            "defender",
            recruitmentSlots: ValueList.Of(new RecruitmentSlot("c1", "heavy_infantry", 48_240, StateCode: 4)));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), defenderNation }, new[] { city }, new[] { attacker });

        var rng = new FixedDivisorRng();
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(6, resolution.Result.AttackerPower);
        Assert.Equal(39_120, resolution.Result.DefenderPower);
        Assert.Equal(expectedLoyalty, resolution.State.CityById("c1")!.Loyalty);
    }

    /// <summary>
    /// T63 B9: the SHIPPED presets carry the flag at the value each is supposed to (relocated from
    /// <c>ImprovedPresetTests.cs</c>, which is outside this task's narrow-grant Owns list -- that file's
    /// own edit is limited to the one line its consistency check needs, per the grant).
    /// </summary>
    [Fact]
    public void ShippedPresets_CarryTheSiegeRatioClampFlagAtItsOwnValue()
    {
        var classical = GameDataLoader.LoadFile<Ruleset>(
            System.IO.Path.Combine(TestPaths.DataRoot, "rulesets", "classical-faithful.json"));
        var improved = GameDataLoader.LoadFile<Ruleset>(
            System.IO.Path.Combine(TestPaths.DataRoot, "rulesets", "improved.json"));

        Assert.Equal(SiegeRatioClampPolicy.Reproduce16BitClamp, classical.Flags.BugPolicySiegeRatioClamp);
        Assert.Equal(SiegeRatioClampPolicy.Clamp32Bit, improved.Flags.BugPolicySiegeRatioClamp);
    }

    /// <summary>
    /// T63 Decision 2: the original divides by zero at <c>atk = 0</c>. <see cref="InstantBattleResolver.ResolveSiege"/>
    /// is the defensive backstop -- <c>AttackLegality.Check</c> is the primary, typed-rejection gate a
    /// command handler consults first (<c>tests/IC2.Engine.Tests/Battle/Commands/BesiegeCityCommandTests.cs</c>).
    /// </summary>
    [Fact]
    public void ZeroAttackerStrength_ThrowsBeforeAnyStateChange()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 50, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 30, tribute: 0);
        // An army with no units at all has zero siege strength -- (0 / 80) x morale = 0.
        var attacker = CaptureTestbed.Army("army", "attacker", 0, 0, morale: 60);
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        Assert.Throws<ArgumentException>(() => InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance));
    }

    /// <summary>
    /// A near-empty besieger (siege strength 6) against a defender whose strength is a plain 0 sum of
    /// loyalty/fortification/population plus one recruitment-slot addend of EXACTLY <paramref name="rawRatio"/>
    /// -- the shared scenario every <c>SiegeRatioClamp_*</c> test uses, factored out so their setups
    /// cannot drift apart. <c>defenderStrength x 6 / attackerStrength</c> reduces to exactly
    /// <c>defenderStrength</c> when <c>attackerStrength</c> is 6 (multiplying then dividing by the same 6
    /// loses nothing under truncation), so setting the recruitment addend to <c>rawRatio</c> makes the
    /// siege's own raw ratio exactly <paramref name="rawRatio"/>, for any value.
    /// </summary>
    private static (ArmyState Attacker, NationState DefenderNation) NearEmptyBesiegerFixture(int rawRatio, out GameState state)
    {
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 0, fortificationCode: 0,
            populationThousands: 0, maxPopulationThousands: 10, tribute: 0);
        // heavy_cavalry: the smallest small-unit-deletion threshold (250 national, bug #289) of any unit
        // type, and 559 troops keeps (troops / 80) x morale = 6 x 1 = 6 with the largest troop count that
        // still floors to exactly 6 -- so even a ratio-15 casualty pass (well above what either clamp
        // policy actually produces here) leaves it comfortably above 250 and the deletion pass never
        // empties this army out from under the test.
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 1, CaptureTestbed.Unit("heavy_cavalry", 559));
        // addend = troops / DefenderGarrisonTroopDivisor (2) -- 2 x rawRatio troops gives EXACTLY
        // rawRatio, with no truncation loss (rawRatio x 2 is always even).
        var defenderNation = CaptureTestbed.Nation(
            "defender",
            recruitmentSlots: ValueList.Of(new RecruitmentSlot("c1", "heavy_infantry", 2 * rawRatio, StateCode: 4)));

        state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), defenderNation },
            new[] { city }, new[] { attacker });

        return (attacker, defenderNation);
    }

    /// <summary>
    /// An <see cref="IRng"/> that always draws 0 -- so <see cref="BattleCasualties.Apply"/>'s divisor,
    /// <c>NextInt(CasualtyDivisorRandomSpan) + CasualtyDivisorBase</c>, lands exactly on
    /// <c>CasualtyDivisorBase</c> (105 in the shipped rulesets), making the expected loss an exact number
    /// rather than a range over the drawn span.
    /// </summary>
    private sealed class FixedDivisorRng : IRng
    {
        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64() => throw new NotSupportedException("Not scripted for this test.");

        public int NextInt(int exclusiveUpperBound) => 0;

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) => inclusiveLowerBound;

        public bool NextChance(int numerator, int denominator) => false;

        public IRng ForStream(string streamName) => this;
    }
}
