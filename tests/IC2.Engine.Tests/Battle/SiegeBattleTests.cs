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
/// What this variant deliberately leaves alone is as much the point as what it does: the city itself is
/// untouched here — no transfer, no garrison, no loyalty move, no defection cascade, no unity, no news
/// line. All of that is T17, which routes its siege resolution through this method.
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
    /// under both flags. The besieged city comes through both resolutions completely unchanged, and so
    /// does which side won.
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

        Assert.Equal(before, destroyedState.CityById("meridia"));
        Assert.Equal(before, scatterState.CityById("meridia"));
        Assert.Equal(state.Cities, destroyedState.Cities);
        Assert.Equal(state.Cities, scatterState.Cities);

        // And nothing outside the besieging army moved either: no unity, no money, no supplies.
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
    [Fact]
    public void TheBesiegingArmyTakesAttritionWhetherItWinsOrLoses()
    {
        var combat = BattleTestbed.Destroyed.Combat;

        // The same FUN_0044AE20 the field variant calls, with the same seeded divisors 118, 110, 116.
        var divisors = new[] { 118, 110, 116 };
        var troops = new[] { 12000, 4000, 2000 };

        var (repulsedState, repulsed) = Resolve(Fixture(), BattleTestbed.Destroyed);
        var repulsedRatio = (repulsed.LoserPower * combat.WinnerCasualtyNumerator) / repulsed.WinnerPower;
        Assert.Equal(7, repulsedRatio);
        Assert.Equal(
            troops.Select((t, i) => (t / divisors[i]) * repulsedRatio).ToArray(),
            repulsed.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(new[] { 707, 252, 119 }, repulsed.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(1078, repulsed.LoserCasualties);
        Assert.Equal(0, repulsed.WinnerCasualties);
        Assert.Equal(18000 - 1078, repulsedState.ArmyById(Besieger)!.TotalTroops);
        Assert.Equal(0, repulsedState.ArmyById(Besieger)!.Moves);

        var (takenState, taken) = Resolve(WeakCityFixture(), BattleTestbed.Destroyed, "hamlet");
        Assert.Equal(BattleSide.Attacker, taken.Winner);
        var takenRatio = (taken.LoserPower * combat.WinnerCasualtyNumerator) / taken.WinnerPower;
        Assert.Equal(5, takenRatio);
        Assert.Equal(
            troops.Select((t, i) => (t / divisors[i]) * takenRatio).ToArray(),
            taken.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(new[] { 505, 180, 85 }, taken.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(770, taken.WinnerCasualties);
        Assert.Equal(0, taken.LoserCasualties);
        Assert.Equal(18000 - 770, takenState.ArmyById(Besieger)!.TotalTroops);
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
