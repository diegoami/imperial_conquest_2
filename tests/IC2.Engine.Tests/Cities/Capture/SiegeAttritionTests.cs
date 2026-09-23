using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
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
    /// T63 Decision 1: <c>classical-faithful</c> (and the toy ruleset, set the same way) reproduce the
    /// original's 16-bit clamp comparison. A near-empty besieger drives <c>defenderStrength x 6 /
    /// attackerStrength</c> to exactly 65,536 -- a clean multiple of 2^16 -- which the original's own
    /// <c>unsigned short</c> comparison reads as 0 before the clamp runs, wrapping the ratio down to the
    /// floor (1) instead of saturating at the ceiling (15) a 32-bit comparison would give.
    /// </summary>
    [Fact]
    public void SiegeRatioClamp_ReproducesThe16BitWrap_UnderTheFaithfulPolicy()
    {
        var ruleset = CaptureTestbed.Ruleset;
        Assert.Equal(SiegeRatioClampPolicy.Reproduce16BitClamp, ruleset.Flags.BugPolicySiegeRatioClamp);

        var (attacker, defenderNation) = ZeroStrengthWrapFixture(out var state);
        var rng = new SplitMix64Rng(0x517UL);

        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        // atk = 6, def = 65,536 (a plain loyalty/fortification/population sum of 0, plus one 131,072-troop
        // recruitment slot addend of 65,536). Raw ratio = 65,536 x 6 / 6 = 65,536 exactly, which wraps to
        // (ushort) 65,536 = 0 -- clamp(0, 1, 15) = 1, the FLOOR, not the 15 a 32-bit reading would give.
        Assert.Equal(6, resolution.Result.AttackerPower);
        Assert.Equal(65_536, resolution.Result.DefenderPower);

        var attackerAfter = resolution.State.ArmyById("army")!;
        var lost = attacker.TotalTroops - attackerAfter.TotalTroops;

        // Ratio 1 against divisor span [105, 120): 559 / divisor x 1 is at most a handful of troops.
        Assert.True(lost < 50, $"Expected a ratio-1 (wrapped) loss under 50 troops, was {lost}.");
        Assert.True(defenderNation.Id == "defender");
    }

    /// <summary>
    /// T63 Decision 1: <c>improved</c> clamps the same raw ratio in ordinary 32-bit arithmetic, so the
    /// same near-empty besieger that wraps to 1 under the faithful policy instead saturates at the 15
    /// ceiling here -- the two tests together are the "besieger small enough to wrap" case DoD 6 asks for,
    /// once per preset.
    /// </summary>
    [Fact]
    public void SiegeRatioClamp_Clamps32Bit_UnderTheImprovedPolicy()
    {
        var ruleset = CaptureTestbed.Ruleset with
        {
            Flags = CaptureTestbed.Ruleset.Flags with { BugPolicySiegeRatioClamp = SiegeRatioClampPolicy.Clamp32Bit },
        };

        var (attacker, _) = ZeroStrengthWrapFixture(out var state);
        var rng = new SplitMix64Rng(0x517UL);

        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(6, resolution.Result.AttackerPower);
        Assert.Equal(65_536, resolution.Result.DefenderPower);

        var attackerAfter = resolution.State.ArmyById("army")!;
        var lost = attacker.TotalTroops - attackerAfter.TotalTroops;

        // Ratio 15 (the un-wrapped ceiling) against the same divisor span: 559 / divisor x 15, well
        // above the ratio-1 loss the faithful policy's own test bounds.
        Assert.True(lost >= 50, $"Expected a ratio-15 (unwrapped) loss of at least 50 troops, was {lost}.");
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
    /// loyalty/fortification/population plus one 65,536-addend recruitment slot -- the exact scenario the
    /// two <c>SiegeRatioClamp_*</c> tests share, factored out so their setups cannot drift apart.
    /// </summary>
    private static (ArmyState Attacker, NationState DefenderNation) ZeroStrengthWrapFixture(out GameState state)
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
        var defenderNation = CaptureTestbed.Nation(
            "defender",
            recruitmentSlots: ValueList.Of(new RecruitmentSlot("c1", "heavy_infantry", 131_072, StateCode: 4)));

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
