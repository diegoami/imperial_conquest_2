using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Strength;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// The siege variant of the instant resolver — the strength comparison at the head of
/// <c>FUN_0044B27C</c>. Done-when line 12 of <c>docs/task-catalogue.md</c> T16, plus the arithmetic the
/// comparison rests on.
/// </summary>
/// <remarks>
/// What this variant deliberately leaves alone is as much the point as what it does: there is no
/// transfer, no garrison change, no defection cascade, no unity move, no news line — all of that is T17,
/// which routes its siege resolution through this method. T63 (bug #293) changed one part of that
/// boundary: the city's own loyalty, fortification and population now erode here, on every attempt, win
/// or lose, because the original does that inside the same <c>FUN_0044B27C</c> this method ports — it is
/// not part of the capture/defection transfer T17 owns. <see cref="CityCaptureResolver.ResolveOutcome"/>
/// still decides ownership from this method's own <see cref="BattleResult.Winner"/>.
/// </remarks>
public class SiegeBattleTests
{
    private const string Besieger = "north-besieger";
    private const string Archers = "archers";
    private const string Fortify = "fortify";

    /// <summary>
    /// Done-when 12: <c>combat.onDefeat</c> has no effect on siege resolution under either ruleset. The
    /// same siege resolved under both settings produces the same result and the same state, byte for
    /// byte, and the result records no defeat outcome at all.
    /// </summary>
    [Fact]
    public void DoD12_CombatOnDefeatHasNoEffectOnSiegeResolution()
    {
        var state = Fixture();

        var (destroyedState, destroyed) = Resolve(state, BattleTestbed.Destroyed);
        var (scatterState, scatter) = Resolve(state, BattleTestbed.Scatter);

        Assert.Equal(DefeatOutcome.Destroyed, BattleTestbed.Destroyed.Flags.CombatOnDefeat);
        Assert.Equal(DefeatOutcome.Scatter, BattleTestbed.Scatter.Flags.CombatOnDefeat);

        Assert.Equal(destroyed, scatter);
        Assert.Equal(destroyedState, scatterState);

        // The flag never reaches this path, so the result reports no setting and no fate.
        Assert.Null(destroyed.AppliedDefeatOutcome);
        Assert.Null(scatter.AppliedDefeatOutcome);
        Assert.Equal(LoserFate.Unaffected, destroyed.LoserFate);
        Assert.Equal(LoserFate.Unaffected, scatter.LoserFate);
        Assert.Null(destroyed.Scatter);
        Assert.Null(scatter.Scatter);
    }

    /// <summary>
    /// Done-when 12, stated the way T17 will depend on it: the <em>defender's</em> outcome is identical
    /// under both flags. The besieged city erodes the same way under both resolutions (T63 bug #293 --
    /// erosion is confirmed core arithmetic, and <c>combat.onDefeat</c> never reaches the siege path), and
    /// so does which side won.
    /// </summary>
    [Fact]
    public void DoD12_TheSiegeDefendersOutcomeIsUnchangedByTheFlag()
    {
        var state = Fixture();
        var before = state.CityById("meridia")!;

        var (destroyedState, destroyed) = Resolve(state, BattleTestbed.Destroyed);
        var (scatterState, scatter) = Resolve(state, BattleTestbed.Scatter);

        Assert.Equal(destroyed.Winner, scatter.Winner);
        Assert.Equal(destroyed.DefenderPower, scatter.DefenderPower);

        // Both presets erode Meridia identically -- and differently from `before`, per bug #293: attacker
        // fails (104,583 defenderPower >= 18,700 attackerPower), so every field takes the ceiling branch,
        // field x 19/20 + 1: loyalty 65 -> 62, fortification 100 -> 96, population 140 -> 134 (the 200/6 +
        // 1 = 34 population floor does not bind).
        var erodedMeridia = destroyedState.CityById("meridia")!;
        Assert.Equal(erodedMeridia, scatterState.CityById("meridia"));
        Assert.NotEqual(before, erodedMeridia);
        Assert.Equal(62, erodedMeridia.Loyalty);
        Assert.Equal(96, erodedMeridia.FortificationCode);
        Assert.Equal(134, erodedMeridia.PopulationThousands);
        Assert.Equal(before.Owner, erodedMeridia.Owner);

        // And nothing outside the besieging army and the besieged city moved: no unity, no money, no
        // supplies.
        Assert.Equal(state.Nations, destroyedState.Nations);
        Assert.Equal(0, destroyed.WinnerUnityDelta);
        Assert.Equal(0, destroyed.LoserUnityDelta);
        Assert.Equal(0, destroyed.AbsorbedMoney);
        Assert.Equal(0, destroyed.AbsorbedSupplyTons);
        Assert.False(destroyed.PeaceTreatyFired);
    }

    /// <summary>
    /// The comparison itself: archers tripled on the attacker's side, the loyalty/fortification/population
    /// sum with the capital branch on the defender's, and a tie to the defender.
    /// </summary>
    [Fact]
    public void TheSiegeComparisonUsesBothConfirmedStrengthFormulas()
    {
        var state = Fixture();
        var (_, result) = Resolve(state, BattleTestbed.Destroyed);

        // (12,000 + 4,000 + 2,000×3) / 80 = 275; × 68 = 18,700.
        var attacker = SiegeStrength.Attacker(
            state.ArmyById(Besieger)!.Units, 68, BattleTestbed.Destroyed, Archers);
        Assert.Equal(18700, attacker);
        Assert.Equal(attacker, result.AttackerPower);

        // Meridia is South's capital at loyalty 65 > 59, so the ×5/3 branch applies:
        // (65×150 + 100×250 + 140×200) = 62,750; ×5/3 = 104,583.
        Assert.Equal(104583, result.DefenderPower);
        Assert.Equal(BattleSide.Defender, result.Winner);
        Assert.Equal(BattleKind.Siege, result.Kind);
    }

    /// <summary>
    /// The attacking army takes attrition on every attempt, win or lose
    /// (<c>tests/fixtures/corpus.json</c> <c>siege.attritionEveryAttempt</c>), and its move is spent.
    /// </summary>
    /// <remarks>
    /// T63 (bug #290 part 1): the pinned ratios and per-unit losses below are the OLD, field-battle-shaped
    /// arithmetic's evidence, not correct behaviour, and are replaced. The siege ratio is
    /// <c>clamp(defenderStrength x 6 / attackerStrength, 1, 15)</c> -- raw defender/attacker strengths,
    /// never a "loser/winner" framing -- not <c>loserPower x 40 / winnerPower</c>. The casualty-divisor
    /// draws themselves are unaffected (they are drawn before the ratio is even used), so the same seeded
    /// divisors 118, 110, 116 still apply.
    /// </remarks>
    [Fact]
    public void TheBesiegingArmyTakesAttritionWhetherItWinsOrLoses()
    {
        var siege = BattleTestbed.Destroyed.Siege;

        // The same FUN_0044AE20 the field variant calls, with the same seeded divisors 118, 110, 116.
        var divisors = new[] { 118, 110, 116 };
        var troops = new[] { 12000, 4000, 2000 };

        var (repulsedState, repulsed) = Resolve(Fixture(), BattleTestbed.Destroyed);
        Assert.Equal(BattleSide.Defender, repulsed.Winner);
        // defenderStrength x 6 / attackerStrength = 104,583 x 6 / 18,700 = 33 -> clamped to the 15 ceiling.
        var repulsedRatio = Math.Max(
            siege.AttritionRatioFloor,
            Math.Min(siege.AttritionRatioCeiling, (repulsed.DefenderPower * siege.AttritionRatioMultiplier) / repulsed.AttackerPower));
        Assert.Equal(15, repulsedRatio);
        Assert.Equal(
            troops.Select((t, i) => (t / divisors[i]) * repulsedRatio).ToArray(),
            repulsed.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(new[] { 1515, 540, 255 }, repulsed.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(2310, repulsed.LoserCasualties);
        Assert.Equal(0, repulsed.WinnerCasualties);
        Assert.Equal(18000 - 2310, repulsedState.ArmyById(Besieger)!.TotalTroops);
        Assert.Equal(0, repulsedState.ArmyById(Besieger)!.Moves);

        var (takenState, taken) = Resolve(WeakCityFixture(), BattleTestbed.Destroyed, "hamlet");
        Assert.Equal(BattleSide.Attacker, taken.Winner);
        // defenderStrength x 6 / attackerStrength = 2,500 x 6 / 18,700 = 0 -> clamped to the 1 floor.
        var takenRatio = Math.Max(
            siege.AttritionRatioFloor,
            Math.Min(siege.AttritionRatioCeiling, (taken.DefenderPower * siege.AttritionRatioMultiplier) / taken.AttackerPower));
        Assert.Equal(1, takenRatio);
        Assert.Equal(
            troops.Select((t, i) => (t / divisors[i]) * takenRatio).ToArray(),
            taken.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(new[] { 101, 36, 17 }, taken.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(154, taken.WinnerCasualties);
        Assert.Equal(0, taken.LoserCasualties);
        Assert.Equal(18000 - 154, takenState.ArmyById(Besieger)!.TotalTroops);
    }

    /// <summary>
    /// The siege entry point's own further reduction, outside <c>FUN_0044A98C</c>: when the besieger
    /// <em>is</em> the city's allegiance, the defender's strength drops by
    /// <see cref="SiegeRules.AttackerIsAllegianceDefenderReductionPercent"/>.
    /// </summary>
    [Fact]
    public void ABesiegerThatIsTheCitysAllegianceFacesAWeakerDefence()
    {
        var reduction = BattleTestbed.Destroyed.Siege.AttackerIsAllegianceDefenderReductionPercent;
        Assert.Equal(10, reduction);

        var state = Fixture();
        var (_, plain) = Resolve(state, BattleTestbed.Destroyed);

        // The same city, its population still loyal to the besieger's own nation.
        var allegiant = state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                c.Id == "meridia" ? c with { Allegiance = "north" } : c)),
        };

        var (_, reduced) = Resolve(allegiant, BattleTestbed.Destroyed);

        // Two adjustments both apply, in order: owner (south) now differs from allegiance (north), so
        // FUN_0044A98C's own ×4/5 fires first, and the entry point's ×9/10 then applies to that.
        var nonAllegiant = (plain.DefenderPower * BattleTestbed.Destroyed.Siege.DefenderNonAllegiantNumerator)
                           / BattleTestbed.Destroyed.Siege.DefenderNonAllegiantDenominator;
        Assert.Equal(104583, plain.DefenderPower);
        Assert.Equal(83666, nonAllegiant);

        // A DIRECT ×9/10, the same (x × num) / den shape as the two sibling adjustments -- not
        // "subtract ten percent". 83,666 × 90 / 100 = 75,299.
        Assert.Equal((nonAllegiant * (100 - reduction)) / 100, reduced.DefenderPower);
        Assert.Equal(75299, reduced.DefenderPower);
        Assert.True(reduced.DefenderPower < plain.DefenderPower);

        // The subtract form is a different number on this very fixture, which is why the shape is pinned
        // rather than left to taste: it would give 75,300.
        Assert.Equal(75300, nonAllegiant - ((nonAllegiant * reduction) / 100));
        Assert.NotEqual(nonAllegiant - ((nonAllegiant * reduction) / 100), reduced.DefenderPower);
    }

    // ---- T17 DoD 7 (docs/task-catalogue.md, PR #197's amendment): the garrison-troops addend is
    // FUN_0044A98C's own last line, so ResolveSiege's own defenderPower must carry it too, not only the
    // cascade's separate evaluations (IC2.Engine.Cities.Capture.CompleteDefenderStrength). One test for
    // the term alone (Hamlet, neither scaling branch active), one combining it with both branches AND the
    // siege entry point's own x9/10 -- proving the full order: weighted sum -> x5/3 -> x4/5 -> +garrison
    // -> x9/10. Every existing DefenderPower figure in this file (104583, 75299, etc.) is UNCHANGED by
    // this addition, because none of Fixture()'s nations carry a recruitment slot targeting the besieged
    // city -- that is itself the "a figure with no garrison slots is unaffected" evidence the entry asks
    // for, still passing at its original value in TheSiegeComparisonUsesBothConfirmedStrengthFormulas and
    // ABesiegerThatIsTheCitysAllegianceFacesAWeakerDefence above. ----

    /// <summary>
    /// The garrison term alone: Hamlet is not a capital and its owner equals its allegiance, so neither
    /// scaling branch fires and the weighted sum is unbranched -- 10x150 + 0x250 + 5x200 = 2,500. One
    /// recruitment slot of 700 troops targeting Hamlet adds 700/2 = 350: 2,500 + 350 = 2,850.
    /// </summary>
    [Fact]
    public void GarrisonTermAlone_AddsToAnUnbranchedDefenderStrength()
    {
        var withoutGarrison = Resolve(WeakCityFixture(), BattleTestbed.Destroyed, "hamlet");
        Assert.Equal(2500, withoutGarrison.Result.DefenderPower);

        var withGarrison = WithSouthRecruitmentSlot(WeakCityFixture(), "hamlet", troops: 700);
        var (_, garrisoned) = Resolve(withGarrison, BattleTestbed.Destroyed, "hamlet");

        Assert.Equal(2850, garrisoned.DefenderPower);
        Assert.Equal(350, garrisoned.DefenderPower - withoutGarrison.Result.DefenderPower);
    }

    /// <summary>
    /// N2: the garrison term's target-city filter. A recruitment slot targeting a <em>different</em> city
    /// contributes nothing to the besieged city's defender strength, even though it belongs to the same
    /// (besieged city's) owner. Every other garrison test in this file gives every slot the besieged
    /// city's own id, which cannot by itself distinguish "sums every one of the owner's slots" from "sums
    /// only the slots that target this city" -- this one can.
    /// </summary>
    [Fact]
    public void GarrisonTermAlone_IgnoresASlotTargetingADifferentCity()
    {
        var withoutGarrison = Resolve(WeakCityFixture(), BattleTestbed.Destroyed, "hamlet");
        Assert.Equal(2500, withoutGarrison.Result.DefenderPower);

        var withUnrelatedSlot = WithSouthRecruitmentSlot(WeakCityFixture(), "some-other-city", troops: 700);
        var (_, result) = Resolve(withUnrelatedSlot, BattleTestbed.Destroyed, "hamlet");

        Assert.Equal(2500, result.DefenderPower);
        Assert.Equal(withoutGarrison.Result.DefenderPower, result.DefenderPower);
    }

    /// <summary>
    /// Two recruitment slots (700 and 300 troops) targeting the same city prove the addend is divided per
    /// slot before the sum, not the total divided once after -- <c>700/2 + 300/2 = 350 + 150 = 500</c>,
    /// not <c>(700+300)/2 = 500</c> (these happen to coincide; see
    /// <c>GarrisonTermAlone_DividesEachSlotBeforeSumming_NotTheTotal</c> for a pair that would not).
    /// </summary>
    [Fact]
    public void GarrisonTermAlone_SumsMultipleQualifyingSlots()
    {
        var state = WithSouthRecruitmentSlots(
            WeakCityFixture(),
            new RecruitmentSlot("hamlet", "heavy_infantry", 700, StateCode: 4),
            new RecruitmentSlot("hamlet", "heavy_infantry", 300, StateCode: 4));

        var (_, result) = Resolve(state, BattleTestbed.Destroyed, "hamlet");

        Assert.Equal(3000, result.DefenderPower); // 2,500 + 350 + 150.
    }

    /// <summary>
    /// Per-slot division, not sum-then-divide: two slots of 3 troops each give <c>3/2 + 3/2 = 1 + 1 = 2</c>
    /// under <see cref="SiegeRules.DefenderGarrisonTroopDivisor"/> = 2, not <c>(3+3)/2 = 3</c>. Mutation
    /// proof: summing the total once and dividing after would pass every other test in this file (their
    /// slot troop counts are all even) but fails only this one.
    /// </summary>
    [Fact]
    public void GarrisonTermAlone_DividesEachSlotBeforeSumming_NotTheTotal()
    {
        var state = WithSouthRecruitmentSlots(
            WeakCityFixture(),
            new RecruitmentSlot("hamlet", "heavy_infantry", 3, StateCode: 4),
            new RecruitmentSlot("hamlet", "heavy_infantry", 3, StateCode: 4));

        var (_, result) = Resolve(state, BattleTestbed.Destroyed, "hamlet");

        Assert.Equal(2502, result.DefenderPower); // 2,500 + (3/2) + (3/2) = 2,500 + 1 + 1.
        Assert.NotEqual(2503, result.DefenderPower); // What (3+3)/2 = 3 added once would give.
    }

    /// <summary>
    /// Combined with both scaling branches and the siege entry point's own separate ×9/10: the full order
    /// is weighted sum → ×5/3 (capital, loyalty &gt; 59) → ×4/5 (owner ≠ allegiance) → + garrison → ×9/10.
    /// Meridia's own non-allegiant strength before the ×9/10 is 83,666 (from
    /// <see cref="ABesiegerThatIsTheCitysAllegianceFacesAWeakerDefence"/>); a 700-troop slot adds
    /// 700/2 = 350, giving 84,016 before the ×9/10, then <c>84,016 × 90 / 100 = 75,614</c>. Adding the
    /// garrison term <em>after</em> the ×9/10 instead (the wrong order) would give
    /// <c>75,299 + 350 = 75,649</c> — a different number, which is why the order has its own assertion
    /// rather than being inferred from a single figure that could be produced either way.
    /// </summary>
    [Fact]
    public void GarrisonTerm_CombinedWithBothBranches_IsAddedAfterThemAndBeforeTheAllegianceReduction()
    {
        var state = Fixture();
        var allegiant = state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                c.Id == "meridia" ? c with { Allegiance = "north" } : c)),
        };

        var (_, withoutGarrison) = Resolve(allegiant, BattleTestbed.Destroyed);
        Assert.Equal(75299, withoutGarrison.DefenderPower);

        var withGarrison = WithSouthRecruitmentSlot(allegiant, "meridia", troops: 700);
        var (_, garrisoned) = Resolve(withGarrison, BattleTestbed.Destroyed);

        const int nonAllegiant = 83666; // From ABesiegerThatIsTheCitysAllegianceFacesAWeakerDefence.
        const int withGarrisonBeforeReduction = nonAllegiant + 350; // 700/2 = 350, added after both branches.
        Assert.Equal(84016, withGarrisonBeforeReduction);

        var reduction = BattleTestbed.Destroyed.Siege.AttackerIsAllegianceDefenderReductionPercent;
        var expected = (withGarrisonBeforeReduction * (100 - reduction)) / 100;
        Assert.Equal(75614, expected);
        Assert.Equal(expected, garrisoned.DefenderPower);

        // The wrong order -- garrison added AFTER the x9/10 -- gives a different, plausible-looking number.
        var wrongOrder = withoutGarrison.DefenderPower + 350;
        Assert.Equal(75649, wrongOrder);
        Assert.NotEqual(wrongOrder, garrisoned.DefenderPower);
    }

    private static GameState WithSouthRecruitmentSlot(GameState state, string cityId, int troops) =>
        WithSouthRecruitmentSlots(state, new RecruitmentSlot(cityId, "heavy_infantry", troops, StateCode: 4));

    private static GameState WithSouthRecruitmentSlots(GameState state, params RecruitmentSlot[] slots) =>
        state with
        {
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "south"
                ? n with { RecruitmentSlots = ValueList.From(slots) }
                : n)),
        };

    /// <summary>A named city is required, and an embarked army cannot besiege anything.</summary>
    [Fact]
    public void AnUnknownCityOrAnEmbarkedArmyIsRefused()
    {
        var state = Fixture();

        Assert.Throws<ArgumentException>(() => InstantBattleResolver.ResolveSiege(
            state, Besieger, "no-such-city", BattleTestbed.Destroyed, BattleTestbed.BattleRng(), Archers, Fortify,
            NullEventSink.Instance));

        Assert.Throws<ArgumentException>(() => InstantBattleResolver.ResolveSiege(
            state, Besieger, "meridia", BattleTestbed.Destroyed, BattleTestbed.BattleRng(), Archers, "no-such-order",
            NullEventSink.Instance));
    }

    /// <summary>A besieging army outside Meridia, South's own capital.</summary>
    private static GameState Fixture() =>
        BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(
                    Besieger, "north", 3, 3, 68, 256, 185,
                    BattleTestbed.Unit("light_infantry", 12000, 5, "1st Foot Battalion"),
                    BattleTestbed.Unit("heavy_infantry", 4000, 6, "1st Guards Battalion"),
                    BattleTestbed.Unit(Archers, 2000, 9, "1st Bowmen Battalion")),
            });

    /// <summary>The same army against an unfortified, disloyal, thinly-populated town it can actually take.</summary>
    private static GameState WeakCityFixture()
    {
        var state = Fixture();
        return state with
        {
            Cities = ValueList.From(new[]
            {
                new CityState(
                    "hamlet", "Hamlet", 4, 3, "south", "south",
                    Loyalty: 10, SupplyTons: 0, FortificationCode: 0, PopulationThousands: 5,
                    MaxPopulationThousands: 20, Tribute: 0, UnderSiege: false, ValueList<UnitSlot>.Empty),
            }),
        };
    }

    private static BattleResolution Resolve(GameState state, Ruleset ruleset, string cityId = "meridia") =>
        InstantBattleResolver.ResolveSiege(
            state,
            Besieger,
            cityId,
            ruleset,
            BattleTestbed.BattleRng(),
            Archers,
            Fortify,
            NullEventSink.Instance);
}
