using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Strength;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// The field variant of the instant resolver — <c>FUN_0044AEE4</c>. Done-when lines 1, 2, 3, 4, 5, 6,
/// 8 and 9 of <c>docs/task-catalogue.md</c> T16, all under <see cref="BattleTestbed.Seed"/> with exact
/// assertions.
/// </summary>
/// <remarks>
/// The fixture is deliberately lopsided so that who wins is never in doubt and every derived number is
/// checkable by hand from the ruleset: the attacker's <see cref="ArmyPower"/> is 6,120 and the
/// defender's 3,600, on the toy ruleset's own weights.
/// </remarks>
public class FieldBattleTests
{
    private const string Attacker = "north-attacker";
    private const string Defender = "south-defender";

    /// <summary>Done-when 1, first half: the higher-power side wins.</summary>
    [Fact]
    public void DoD01_TheHigherPowerSideWins()
    {
        var state = Fixture();
        var (_, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(6120, result.AttackerPower);
        Assert.Equal(3600, result.DefenderPower);
        Assert.Equal(BattleSide.Attacker, result.Winner);
        Assert.Equal(Attacker, result.WinnerId);
        Assert.Equal(Defender, result.LoserId);
    }

    /// <summary>
    /// Done-when 1, second half: an exact tie goes to the defender — <c>winner = (pB &lt; pA) ? attacker
    /// : defender</c> is a strict less-than (<c>battle.instantResolver.tieGoesToDefender</c>).
    /// </summary>
    [Fact]
    public void DoD01_AnExactTieGoesToTheDefender()
    {
        var identical = new[]
        {
            BattleTestbed.Unit("light_infantry", 9000, 5, "Twin A"),
        };

        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(Attacker, "north", 1, 2, 60, 100, 50, identical),
                BattleTestbed.Army(Defender, "south", 2, 2, 60, 100, 50, identical),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(result.AttackerPower, result.DefenderPower);
        Assert.Equal(BattleSide.Defender, result.Winner);
        Assert.Null(after.ArmyById(Attacker));
        Assert.NotNull(after.ArmyById(Defender));
    }

    /// <summary>Done-when 2: under <c>classical-faithful</c> the loser's army is destroyed outright.</summary>
    [Fact]
    public void DoD02_TheLosersArmyIsDestroyedOutright()
    {
        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(LoserFate.Destroyed, result.LoserFate);
        Assert.Equal(DefeatOutcome.Destroyed, result.AppliedDefeatOutcome);
        Assert.Null(after.ArmyById(Defender));
        Assert.Single(after.Armies);

        // Destroyed outright means every troop, not a fraction: the loser's whole force is the casualty
        // figure this result reports.
        Assert.Equal(12000, result.LoserCasualties);
        Assert.Null(result.Scatter);
    }

    /// <summary>
    /// Done-when 3: winner casualties equal <c>loserPower × 40 / winnerPower</c>, with the integer
    /// semantics pinned — and unaffected by <c>combat.onDefeat</c>.
    /// </summary>
    [Fact]
    public void DoD03_WinnerCasualtiesAreLoserPowerTimesNumeratorOverWinnerPower()
    {
        var rules = BattleTestbed.Destroyed.Combat;
        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        // The expression, computed here from the ruleset's own numerator rather than pasted in:
        // 3600 * 40 / 6120 = 144000 / 6120 = 23 (23.52 truncated).
        var expected = (result.LoserPower * rules.WinnerCasualtyNumerator) / result.WinnerPower;
        Assert.Equal(23, expected);
        Assert.Equal(expected, result.WinnerCasualties);

        // Multiplication first, then ONE truncating division. The other reading, loserPower * (40 /
        // winnerPower), truncates the inner quotient to zero for any winnerPower above the numerator.
        Assert.Equal(0, rules.WinnerCasualtyNumerator / result.WinnerPower);
        Assert.NotEqual(result.LoserPower * (rules.WinnerCasualtyNumerator / result.WinnerPower), result.WinnerCasualties);

        // Spread over the winner's slots in proportion to their troops, exactly summing to the figure.
        Assert.Equal(
            new[] { (0, 15), (1, 5), (2, 3) },
            result.UnitCasualties.Select(c => (c.SlotIndex, c.TroopsLost)).ToArray());
        Assert.Equal(23, result.UnitCasualties.Sum(c => c.TroopsLost));

        var winner = after.ArmyById(Attacker)!;
        Assert.Equal(new[] { 11985, 3995, 1997 }, winner.Units.Select(u => u.Troops).ToArray());
        Assert.Equal(18000 - 23, winner.TotalTroops);

        // Unaffected by combat.onDefeat.
        var (_, scattered) = Resolve(Fixture(), BattleTestbed.Scatter);
        Assert.Equal(result.WinnerCasualties, scattered.WinnerCasualties);
        Assert.Equal(result.UnitCasualties, scattered.UnitCasualties);
    }

    /// <summary>
    /// Done-when 4: the winner absorbs the loser's money, and supplies capped at <c>troops / 100</c> —
    /// unaffected by <c>combat.onDefeat</c>.
    /// </summary>
    /// <remarks>
    /// This is the instant-battle <em>attacker</em>-wins branch, the one
    /// <c>supply-capacity-rounding.md</c> records as capped
    /// (<c>FUN_0044AEE4</c> lines 49647-49654). The defender-wins branch is
    /// <see cref="DoD04_ADefenderThatWinsTakesAPlainSumWithNoCap"/>, which the same report records as
    /// uncapped — an asymmetry copied exactly rather than made consistent.
    /// </remarks>
    [Fact]
    public void DoD04_TheWinnerAbsorbsMoneyAndSuppliesCappedAtTroopsOverOneHundred()
    {
        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        var winner = after.ArmyById(Attacker)!;
        Assert.Equal(90, result.AbsorbedMoney);
        Assert.Equal(256 + 90, winner.Money);

        // 185 + 40 = 225 tons on offer; the cap is the winner's POST-casualty troops over the ruleset's
        // own divisor, 17,977 / 100 = 179, and it binds.
        var cap = winner.TotalTroops / BattleTestbed.Destroyed.Economy.ArmySupplyTonsPerTroops;
        Assert.Equal(179, cap);
        Assert.Equal(cap, result.AbsorbedSupplyTons);
        Assert.Equal(cap, winner.SupplyTons);
        Assert.True(225 > cap, "the fixture must put more supply on offer than the cap allows");

        var (_, scattered) = Resolve(Fixture(), BattleTestbed.Scatter);
        Assert.Equal(result.AbsorbedMoney, scattered.AbsorbedMoney);
        Assert.Equal(result.AbsorbedSupplyTons, scattered.AbsorbedSupplyTons);
    }

    /// <summary>
    /// Done-when 4, the other half of the asymmetry: an instant-battle <em>defender</em> that wins takes
    /// a plain sum with no cap at all (<c>supply-capacity-rounding.md</c>, lines 49687-49690).
    /// </summary>
    [Fact]
    public void DoD04_ADefenderThatWinsTakesAPlainSumWithNoCap()
    {
        // The same two armies, with the stronger one defending.
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(
                    Attacker, "north", 1, 2, 60, 90, 40,
                    BattleTestbed.Unit("light_infantry", 9000, 6, "Weak Foot"),
                    BattleTestbed.Unit("heavy_infantry", 3000, 6, "Weak Guards")),
                BattleTestbed.Army(
                    Defender, "south", 2, 2, 68, 256, 185,
                    BattleTestbed.Unit("light_infantry", 12000, 5, "Strong Foot"),
                    BattleTestbed.Unit("heavy_infantry", 4000, 6, "Strong Guards"),
                    BattleTestbed.Unit("archers", 2000, 9, "Strong Bowmen")),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(BattleSide.Defender, result.Winner);

        var winner = after.ArmyById(Defender)!;
        var cap = winner.TotalTroops / BattleTestbed.Destroyed.Economy.ArmySupplyTonsPerTroops;

        Assert.Equal(185 + 40, result.AbsorbedSupplyTons);
        Assert.Equal(225, winner.SupplyTons);
        Assert.True(result.AbsorbedSupplyTons > cap, "the uncapped branch must exceed the cap the other branch would apply");
    }

    /// <summary>
    /// Done-when 5: every surviving unit ends at ≥ "average", and exactly the 1-in-4 further promotions
    /// fire for the seeded roll.
    /// </summary>
    [Fact]
    public void DoD05_EverySurvivorReachesTheFloorAndExactlyTheSeededPromotionsFire()
    {
        var combat = BattleTestbed.Destroyed.Combat;
        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        var winner = after.ArmyById(Attacker)!;

        // The floor applies to every survivor: the slot that started at 5 is raised to 6 with no roll.
        Assert.All(winner.Units, unit => Assert.True(unit.Quality >= combat.QualityFloor));

        // Exactly one of the three rolls fires under this seed -- the middle slot's -- and it is the only
        // slot whose quality moved for that reason.
        Assert.Equal(
            new[] { (0, 5, 6, false), (1, 6, 7, true), (2, 9, 9, false) },
            result.Promotions
                .Select(p => (p.SlotIndex, p.QualityBefore, p.QualityAfter, p.PromotedByRoll))
                .ToArray());
        Assert.Equal(1, result.Promotions.Count(p => p.PromotedByRoll));
        Assert.Equal(new[] { 6, 7, 9 }, winner.Units.Select(u => u.Quality).ToArray());

        // Only the winner is promoted; nothing touches the loser (which in this ruleset no longer exists).
        Assert.Single(after.Armies);
    }

    /// <summary>
    /// Done-when 5, the cap clause: a unit already at "elite" that wins the further roll stays at
    /// "elite" rather than climbing past it.
    /// </summary>
    [Fact]
    public void DoD05_QualityIsCappedAtElite()
    {
        var combat = BattleTestbed.Destroyed.Combat;

        // Two winner slots, so the seed's second draw -- the 0 -- lands on the elite one.
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(
                    Attacker, "north", 1, 2, 68, 0, 0,
                    BattleTestbed.Unit("heavy_infantry", 4000, 5, "Green Guards"),
                    BattleTestbed.Unit("heavy_infantry", 4000, combat.QualityCap, "Elite Guards")),
                BattleTestbed.Army(
                    Defender, "south", 2, 2, 30, 0, 0,
                    BattleTestbed.Unit("light_infantry", 1000, 6, "Doomed Foot")),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(BattleSide.Attacker, result.Winner);
        Assert.True(result.Promotions[1].PromotedByRoll, "the seed's second draw must be the promoting one");
        Assert.Equal(combat.QualityCap, result.Promotions[1].QualityAfter);
        Assert.Equal(combat.QualityCap, after.ArmyById(Attacker)!.Units[1].Quality);
    }

    /// <summary>
    /// Done-when 6, the land half: unity moves the loser −25 and the winner +25, and is unaffected by
    /// <c>combat.onDefeat</c>.
    /// </summary>
    [Fact]
    public void DoD06_UnityMovesLoserDownAndWinnerUpByTheRulesetsSwing()
    {
        var swing = BattleTestbed.Destroyed.Combat.UnitySwing;
        Assert.Equal(25, swing);

        var state = Fixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(swing, result.WinnerUnityDelta);
        Assert.Equal(-swing, result.LoserUnityDelta);
        Assert.Equal(600 + swing, after.NationById("north")!.Unity);
        Assert.Equal(560 - swing, after.NationById("south")!.Unity);

        var (_, scattered) = Resolve(Fixture(), BattleTestbed.Scatter);
        Assert.Equal(result.WinnerUnityDelta, scattered.WinnerUnityDelta);
        Assert.Equal(result.LoserUnityDelta, scattered.LoserUnityDelta);
    }

    /// <summary>Done-when 6, the clamp: the winner's unity stops at the ruleset's cap.</summary>
    [Fact]
    public void DoD06_TheWinnersUnityIsClampedAtTheCap()
    {
        var cap = BattleTestbed.Destroyed.Economy.UnityCap;
        Assert.Equal(990, cap);

        var baseState = Fixture();
        var state = baseState with
        {
            Nations = ValueList.From(new[]
            {
                BattleTestbed.NationWithUnity(baseState, "north", cap - 10),
                BattleTestbed.NationWithUnity(baseState, "south", 560),
            }),
        };

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(cap, after.NationById("north")!.Unity);
        Assert.Equal(10, result.WinnerUnityDelta);
    }

    /// <summary>
    /// Done-when 8: <c>PeaceTreatyTriggered</c> is published on the 2-in-5 roll, gated on the loser's
    /// unity above 500 and its city count above 7 — and is observable with no diplomacy system
    /// registered.
    /// </summary>
    [Fact]
    public void DoD08_PeaceTreatyTriggeredIsPublishedWhenTheRollAndBothGatesPass()
    {
        var combat = BattleTestbed.Destroyed.Combat;
        var state = PeaceGateFixture(loserUnity: 560, loserCityCount: 9);

        var events = new RecordingEventSink();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed, events);

        // Both gates are read AFTER the battle's own unity swing, as the original's own order does.
        var loser = after.NationById("south")!;
        Assert.True(loser.Unity > combat.AutoPeaceLoserUnityThreshold);
        Assert.True(after.CountCitiesOwnedBy("south") > combat.AutoPeaceLoserCityThreshold);

        Assert.True(result.PeaceTreatyFired);
        var treaty = Assert.Single(events.Events.OfType<PeaceTreatyTriggered>());
        Assert.Equal("north", treaty.WinnerNationId);
        Assert.Equal("south", treaty.LoserNationId);
        Assert.Equal(535, treaty.LoserUnity);
        Assert.Equal(9, treaty.LoserCityCount);

        // "Observable in a test with no diplomacy system registered": nothing was registered at all --
        // this resolver publishes rather than calling diplomacy, so the event exists on its own.
        Assert.DoesNotContain(
            SystemRegistry.FromEngineAssembly().Systems,
            s => s.Id.StartsWith("diplomacy.", StringComparison.Ordinal));
    }

    /// <summary>Done-when 8, the unity gate: the same seeded roll fires, and the gate still refuses.</summary>
    [Fact]
    public void DoD08_TheUnityGateBlocksTheTreaty()
    {
        var combat = BattleTestbed.Destroyed.Combat;

        // 520 − 25 = 495, one swing below the threshold.
        var state = PeaceGateFixture(loserUnity: 520, loserCityCount: 9);
        var events = new RecordingEventSink();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed, events);

        Assert.Equal(495, after.NationById("south")!.Unity);
        Assert.False(after.NationById("south")!.Unity > combat.AutoPeaceLoserUnityThreshold);
        Assert.False(result.PeaceTreatyFired);
        Assert.DoesNotContain(events.Events, e => e is PeaceTreatyTriggered);
    }

    /// <summary>Done-when 8, the city gate: exactly at the threshold is not above it.</summary>
    [Fact]
    public void DoD08_TheCityCountGateBlocksTheTreaty()
    {
        var combat = BattleTestbed.Destroyed.Combat;
        var state = PeaceGateFixture(loserUnity: 560, loserCityCount: combat.AutoPeaceLoserCityThreshold);

        var events = new RecordingEventSink();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed, events);

        Assert.Equal(7, after.CountCitiesOwnedBy("south"));
        Assert.False(result.PeaceTreatyFired);
        Assert.DoesNotContain(events.Events, e => e is PeaceTreatyTriggered);
    }

    /// <summary>
    /// Done-when 9, the land half: the confirmed news message
    /// <c>"&lt;winner&gt; destroys army of &lt;loser&gt;."</c> is emitted and renders through the
    /// already-registered catalog template.
    /// </summary>
    [Fact]
    public void DoD09_TheConfirmedDestroysArmyNewsMessageIsEmitted()
    {
        var state = Fixture();
        var events = new RecordingEventSink();
        var (after, _) = Resolve(state, BattleTestbed.Destroyed, events);

        var destroyed = Assert.Single(events.Events.OfType<BattleArmyDestroyed>());
        Assert.Equal("battle.army-destroyed", destroyed.Kind);
        Assert.True(destroyed.IsNewsWorthy);
        Assert.Equal("Northern League", destroyed.Winner);
        Assert.Equal("Southern League", destroyed.Loser);

        var logged = NewsLogWriter.Append(after, events.Events, BattleTestbed.Destroyed.NewsLog);
        Assert.Equal(
            "Northern League destroys army of Southern League.",
            logged.NewsLog.Slots[logged.NewsLog.MostRecentSlot].Text);
    }

    /// <summary>
    /// The battle spends the attacker's whole move whatever the outcome
    /// (<c>attacker.moves = 0</c>, the first line of <c>FUN_0044AEE4</c>) — which is also what stops the
    /// victor chasing a scattered survivor in the same turn.
    /// </summary>
    [Fact]
    public void TheAttackersMoveIsSpentWhetherItWinsOrLoses()
    {
        var (won, _) = Resolve(Fixture(), BattleTestbed.Destroyed);
        Assert.Equal(0, won.ArmyById(Attacker)!.Moves);

        var lostState = BattleTestbed.StateWith(
            armies: new[]
            {
                // Close enough that the mirrored casualty figure leaves survivors to relocate.
                BattleTestbed.Army(Attacker, "north", 1, 2, 60, 0, 0, BattleTestbed.Unit("light_infantry", 6000, 6, "Outmatched Foot")),
                BattleTestbed.Army(Defender, "south", 2, 2, 68, 0, 0, BattleTestbed.Unit("heavy_infantry", 6000, 6, "Strong Guards")),
            });

        var (lost, result) = Resolve(lostState, BattleTestbed.Scatter);
        Assert.Equal(BattleSide.Defender, result.Winner);
        Assert.Equal(LoserFate.Scattered, result.LoserFate);
        Assert.Equal(0, lost.ArmyById(Attacker)!.Moves);
    }

    /// <summary>A battle between two of one nation's own armies is a caller bug, not an outcome.</summary>
    [Fact]
    public void TwoArmiesOfOneNationCannotFightEachOther()
    {
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.Army(Attacker, "north", 1, 2, 60, 0, 0, BattleTestbed.Unit("light_infantry", 1000, 6, "A")),
                BattleTestbed.Army("north-other", "north", 2, 2, 60, 0, 0, BattleTestbed.Unit("light_infantry", 1000, 6, "B")),
            });

        Assert.Throws<ArgumentException>(() => InstantBattleResolver.ResolveField(
            state, Attacker, "north-other", BattleTestbed.Destroyed, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance));
    }

    /// <summary>The lopsided fixture every Done-when line above is measured against.</summary>
    internal static GameState Fixture()
    {
        var initial = BattleTestbed.Initial();

        return BattleTestbed.StateWith(
            armies: new[]
            {
                // 20×12000/100 + 100×4000/100 + 40×2000/100 = 7,200; 7,200/80 = 90; 90 × 68 = 6,120.
                BattleTestbed.Army(
                    Attacker, "north", 1, 2, 68, 256, 185,
                    BattleTestbed.Unit("light_infantry", 12000, 5, "1st Foot Battalion"),
                    BattleTestbed.Unit("heavy_infantry", 4000, 6, "1st Guards Battalion"),
                    BattleTestbed.Unit("archers", 2000, 9, "1st Bowmen Battalion")),

                // 20×9000/100 + 100×3000/100 = 4,800; 4,800/80 = 60; 60 × 60 = 3,600.
                BattleTestbed.Army(
                    Defender, "south", 2, 2, 60, 90, 40,
                    BattleTestbed.Unit("light_infantry", 9000, 6, "2nd Foot Battalion"),
                    BattleTestbed.Unit("heavy_infantry", 3000, 6, "2nd Guards Battalion")),
            },
            nations: new[]
            {
                BattleTestbed.NationWithUnity(initial, "north", 600),
                BattleTestbed.NationWithUnity(initial, "south", 560),
            });
    }

    /// <summary>The same battle, with the loser given enough unity and cities to reach the peace gate.</summary>
    private static GameState PeaceGateFixture(int loserUnity, int loserCityCount)
    {
        var initial = BattleTestbed.Initial();
        var baseState = Fixture();

        return baseState with
        {
            // Placed on the map's southern water row, well clear of the battle's own tiles.
            Cities = ValueList.From(BattleTestbed.FillerCities("south", loserCityCount, 0, 5)),
            Nations = ValueList.From(new[]
            {
                BattleTestbed.NationWithUnity(initial, "north", 600),
                BattleTestbed.NationWithUnity(initial, "south", loserUnity),
            }),
        };
    }

    private static BattleResolution Resolve(GameState state, Ruleset ruleset, IEventSink? events = null) =>
        InstantBattleResolver.ResolveField(
            state,
            Attacker,
            Defender,
            ruleset,
            BattleTestbed.World,
            BattleTestbed.BattleRng(),
            events ?? NullEventSink.Instance);
}
