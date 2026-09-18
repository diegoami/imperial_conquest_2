using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// The naval variant of the instant resolver — <c>FUN_0044B5D0</c> and its winner-damage helper
/// <c>FUN_0044B4F8</c>. Done-when lines 6 (the sea half), 7 and 9, under
/// <see cref="BattleTestbed.Seed"/>, whose first two random bands are <c>2</c> then <c>0</c>.
/// </summary>
public class NavalBattleTests
{
    private const string Attacker = "north-fleet";
    private const string Defender = "south-fleet";
    private const string Cargo = "south-cargo";
    private const string Archers = "archers";

    /// <summary>
    /// Done-when 7, first half: the loser's fleet <em>and any army aboard it</em> are annihilated, and
    /// neither leaves a reference behind.
    /// </summary>
    [Fact]
    public void DoD07_TheLosersFleetAndTheArmyAboardItAreBothAnnihilated()
    {
        var state = CarriedCargoFixture();
        var events = new RecordingEventSink();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed, events);

        // base 20×100/10 = 200, band 200×10/100 = 20, first draw 3 → 200 + 60 = 260.
        Assert.Equal(260, result.AttackerPower);

        // base 10×80/10 = 80, plus the carried army's siege strength (1,000/80 = 12; ×60 = 720) / 50 = 14
        // → 94; band 9; second draw 1 → 103.
        Assert.Equal(103, result.DefenderPower);
        Assert.Equal(BattleSide.Attacker, result.Winner);
        Assert.Equal(LoserFate.Destroyed, result.LoserFate);

        Assert.Null(after.FleetById(Defender));
        Assert.Null(after.ArmyById(Cargo));
        Assert.Single(after.Fleets);
        Assert.Empty(after.Armies);
    }

    /// <summary>
    /// Done-when 7, second half: the winner's ships and condition fall in proportion to how close the
    /// fight was — <c>r = max(1, loserStrength × 100 / winnerStrength)</c>, <c>d = r² / 100</c>, and the
    /// winner loses <c>ships × d / 300</c> and <c>condition × d / 300</c>.
    /// </summary>
    [Fact]
    public void DoD07_TheWinnersShipsAndConditionFallInProportionToTheClosenessOfTheFight()
    {
        var naval = BattleTestbed.Destroyed.Combat.Naval;

        var (lopsidedAfter, lopsided) = Resolve(CarriedCargoFixture(), BattleTestbed.Destroyed);

        // r = max(1, 103 × 100 / 260) = 39; d = 39² / 100 = 15.
        var ratio = Math.Max(1, (lopsided.LoserPower * naval.DamageRatioScale) / lopsided.WinnerPower);
        var damage = (ratio * ratio) / naval.DamageRatioScale;
        Assert.Equal(39, ratio);
        Assert.Equal(15, damage);

        Assert.Equal((20 * damage) / naval.WinnerDamageDivisor, lopsided.WinnerShipsLost);
        Assert.Equal(1, lopsided.WinnerShipsLost);
        Assert.Equal((100 * damage) / naval.WinnerDamageDivisor, lopsided.WinnerConditionLost);
        Assert.Equal(5, lopsided.WinnerConditionLost);
        Assert.Equal(19, lopsidedAfter.FleetById(Attacker)!.Ships);
        Assert.Equal(95, lopsidedAfter.FleetById(Attacker)!.ConditionPercent);

        // The same 20-ship attacker against a nearly equal fleet pays far more: 209 vs 260 gives
        // r = 80, d = 64, so 4 ships and 21 condition instead of 1 and 5.
        var closeState = BattleTestbed.StateWith(
            fleets: new[]
            {
                BattleTestbed.Fleet(Attacker, "north", 0, 2, 20, 100),
                BattleTestbed.Fleet(Defender, "south", 0, 3, 20, 95),
            });

        var (_, close) = Resolve(closeState, BattleTestbed.Destroyed);
        Assert.Equal(209, close.DefenderPower);
        Assert.Equal(4, close.WinnerShipsLost);
        Assert.Equal(21, close.WinnerConditionLost);
        Assert.True(close.WinnerShipsLost > lopsided.WinnerShipsLost);
        Assert.True(close.WinnerConditionLost > lopsided.WinnerConditionLost);
    }

    /// <summary>
    /// Done-when 7's remaining confirmed clause: above <c>d &gt; 70</c> the winner's own carried army also
    /// loses whole unit slots at random, <c>unitCount × d / 250</c> of them.
    /// </summary>
    [Fact]
    public void DoD07_AboveTheDamageThresholdTheWinnersCarriedArmyAlsoLosesWholeUnits()
    {
        var naval = BattleTestbed.Destroyed.Combat.Naval;

        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.EmbarkedArmy(
                    "north-cargo", "north", Attacker, 0, 2, 60,
                    BattleTestbed.Unit("light_infantry", 1000, 6, "A"),
                    BattleTestbed.Unit("light_infantry", 1000, 6, "B"),
                    BattleTestbed.Unit("light_infantry", 1000, 6, "C"),
                    BattleTestbed.Unit("light_infantry", 1000, 6, "D")),
            },
            fleets: new[]
            {
                BattleTestbed.Fleet(Attacker, "north", 0, 2, 20, 100, "north-cargo"),
                BattleTestbed.Fleet(Defender, "south", 0, 3, 29, 100),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        // 200 + (4,000/80 × 60) / 50 = 200 + 60 = 260; band 26; first draw 3 → 338.
        Assert.Equal(338, result.AttackerPower);
        Assert.Equal(319, result.DefenderPower);

        var damageRatio = (result.LoserPower * naval.DamageRatioScale) / result.WinnerPower;
        var damage = (damageRatio * damageRatio) / naval.DamageRatioScale;
        Assert.Equal(94, damageRatio);
        Assert.Equal(88, damage);
        Assert.True(damage > naval.UnitLossDamageThreshold);

        Assert.Equal((4 * damage) / naval.UnitLossDivisor, result.WinnerUnitsLost);
        Assert.Equal(1, result.WinnerUnitsLost);

        // The carried army takes the same per-unit expression as a field winner, at ratio
        // 319 x 40 / 338 = 37 and divisors 116, 115, 105, 111:
        //   1000/116 = 8 -> 296     1000/115 = 8 -> 296
        //   1000/105 = 9 -> 333     1000/111 = 9 -> 333
        var casualtyRatio =
            (result.LoserPower * BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator) / result.WinnerPower;
        Assert.Equal(37, casualtyRatio);
        Assert.Equal(
            new[] { 116, 115, 105, 111 }.Select(d => (1000 / d) * casualtyRatio).ToArray(),
            result.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(new[] { 296, 296, 333, 333 }, result.UnitCasualties.Select(c => c.TroopsLost).ToArray());

        // Then the d > 70 branch removes one whole slot, picked by the next draw (1) -- slot "B".
        var cargo = after.ArmyById("north-cargo")!;
        Assert.Equal(new[] { "A", "C", "D" }, cargo.Units.Select(u => u.Name).ToArray());
        Assert.Equal(new[] { 704, 667, 667 }, cargo.Units.Select(u => u.Troops).ToArray());

        // Every troop the winner's carried army lost is accounted for: 1,258 to attrition plus the 704
        // that went down with the removed slot.
        Assert.Equal(296 + 296 + 333 + 333 + 704, result.WinnerCasualties);
        Assert.Equal(1962, result.WinnerCasualties);
        Assert.Equal(4000 - result.WinnerCasualties, cargo.TotalTroops);
    }

    /// <summary>
    /// Done-when 6, the sea half: unity moves by <c>± floor(loserShips / 2)</c> rather than by the field
    /// battle's flat swing — and is unaffected by <c>combat.onDefeat</c>.
    /// </summary>
    [Fact]
    public void DoD06_AtSeaUnityMovesByHalfTheLosersShips()
    {
        var naval = BattleTestbed.Destroyed.Combat.Naval;
        Assert.Equal(2, naval.UnitySwingShipDivisor);

        var state = CarriedCargoFixture();
        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        var expected = 10 / naval.UnitySwingShipDivisor;
        Assert.Equal(5, expected);
        Assert.Equal(expected, result.WinnerUnityDelta);
        Assert.Equal(-expected, result.LoserUnityDelta);
        Assert.NotEqual(BattleTestbed.Destroyed.Combat.UnitySwing, result.WinnerUnityDelta);
        Assert.Equal(600 + expected, after.NationById("north")!.Unity);
        Assert.Equal(520 - expected, after.NationById("south")!.Unity);

        var (_, scattered) = Resolve(CarriedCargoFixture(), BattleTestbed.Scatter);
        Assert.Equal(result.WinnerUnityDelta, scattered.WinnerUnityDelta);
        Assert.Equal(result.LoserUnityDelta, scattered.LoserUnityDelta);
    }

    /// <summary>Done-when 1 at sea: an exact tie still goes to the defender.</summary>
    [Fact]
    public void DoD01_AnExactTieAtSeaGoesToTheDefender()
    {
        // Identical fleets, and the seed's two bands differ (2 then 0), so the bands are neutralised by
        // giving the fleet a base below the band's own granularity: 9 × 10 / 10 = 9, and 9 × 10 / 100 = 0.
        var state = BattleTestbed.StateWith(
            fleets: new[]
            {
                BattleTestbed.Fleet(Attacker, "north", 0, 2, 9, 10),
                BattleTestbed.Fleet(Defender, "south", 0, 3, 9, 10),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(result.AttackerPower, result.DefenderPower);
        Assert.Equal(BattleSide.Defender, result.Winner);
        Assert.Null(after.FleetById(Attacker));
    }

    /// <summary>
    /// Done-when 9: the confirmed <c>"X sinks fleet of Y."</c> message is emitted and renders through the
    /// already-registered catalog template.
    /// </summary>
    [Fact]
    public void DoD09_TheConfirmedSinksFleetNewsMessageIsEmitted()
    {
        var state = CarriedCargoFixture();
        var events = new RecordingEventSink();
        var (after, _) = Resolve(state, BattleTestbed.Destroyed, events);

        var sunk = Assert.Single(events.Events.OfType<BattleFleetSunk>());
        Assert.Equal("battle.fleet-sunk", sunk.Kind);
        Assert.True(sunk.IsNewsWorthy);
        Assert.Equal("Northern League", sunk.Winner);
        Assert.Equal("Southern League", sunk.Loser);

        Assert.Equal("<winner> sinks fleet of <loser>.", NewsMessageCatalog.GetTemplate(sunk.Kind));

        var logged = NewsLogWriter.Append(after, events.Events, BattleTestbed.Destroyed.NewsLog);
        Assert.Equal(
            "Northern League sinks fleet of Southern League.",
            logged.NewsLog.Slots[logged.NewsLog.MostRecentSlot].Text);
    }

    /// <summary>
    /// The delete-then-dangle sweep for Done-when 7 (<c>docs/build-process.md</c> §4.2 gate 5): an
    /// annihilated fleet that was carrying an army removes both records together, so the state it leaves
    /// behind still serializes, reloads and validates.
    /// </summary>
    [Fact]
    public void DoD07_AnAnnihilatedCarrierLeavesNoDanglingReferenceAndStillReloads()
    {
        var state = CarriedCargoFixture();
        var (after, _) = Resolve(state, BattleTestbed.Destroyed);

        // Nothing still points at either deleted record, in either direction.
        Assert.DoesNotContain(after.Fleets, f => f.CarriedArmyId is not null);
        Assert.DoesNotContain(after.Armies, a => a.AboardFleetId is not null);
        Assert.DoesNotContain(after.Fleets, f => f.Id == Defender);
        Assert.DoesNotContain(after.Armies, a => a.Id == Cargo);

        // And the state is still one the loader accepts: a save that will not reload is exactly the
        // failure this sweep exists to catch.
        var reloaded = GameDataLoader.Load<GameState>("battle-state.json", GameJson.Serialize(after));
        GameDataValidation.Validate("battle-state.json", reloaded);
        Assert.Equal(GameJson.Serialize(after), GameJson.Serialize(reloaded));
    }

    /// <summary>
    /// The mirror case: a fleet that <em>scatters</em> keeps its carried army, and takes it along rather
    /// than leaving it on the old tile — the other way the carrier link can go wrong.
    /// </summary>
    [Fact]
    public void AScatteredCarrierTakesItsArmyWithItAndKeepsTheLinkIntact()
    {
        // A fleet only survives the mirrored casualty figure when it is large: the figure is
        // winnerPower × 40 / loserPower, which is never below the numerator itself, so a beaten fleet
        // needs more than 40 hulls to have any left. 100 against 100 at equal condition is the smallest
        // clean fixture that both loses and survives.
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.EmbarkedArmy(
                    Cargo, "south", Defender, 0, 3, 60,
                    BattleTestbed.Unit("light_infantry", 1000, 6, "Marine Foot")),
            },
            fleets: new[]
            {
                BattleTestbed.Fleet(Attacker, "north", 0, 2, 100, 100),
                BattleTestbed.Fleet(Defender, "south", 0, 3, 100, 100, Cargo),
            });

        var (after, result) = Resolve(state, BattleTestbed.Scatter);

        Assert.Equal(1300, result.AttackerPower);
        Assert.Equal(1115, result.DefenderPower);
        Assert.Equal(LoserFate.Scattered, result.LoserFate);
        Assert.Equal(46, result.LoserCasualties);
        Assert.Equal(100 - 46, after.FleetById(Defender)!.Ships);

        var survivor = after.FleetById(Defender)!;
        var cargo = after.ArmyById(Cargo)!;

        Assert.Equal(Cargo, survivor.CarriedArmyId);
        Assert.Equal(Defender, cargo.AboardFleetId);
        Assert.Equal(survivor.X, cargo.X);
        Assert.Equal(survivor.Y, cargo.Y);
        Assert.Equal(0, survivor.Moves);

        var reloaded = GameDataLoader.Load<GameState>("battle-state.json", GameJson.Serialize(after));
        GameDataValidation.Validate("battle-state.json", reloaded);
    }

    /// <summary>A fleet still counting down its construction is not on the map and cannot fight.</summary>
    [Fact]
    public void AFleetUnderConstructionCannotFight()
    {
        var state = BattleTestbed.StateWith(
            fleets: new[]
            {
                BattleTestbed.Fleet(Attacker, "north", 0, 2, 20, 100) with
                {
                    ConstructionTicksRemaining = 5,
                    BuildCityId = "arx",
                },
                BattleTestbed.Fleet(Defender, "south", 0, 3, 10, 80),
            });

        Assert.Throws<ArgumentException>(() => Resolve(state, BattleTestbed.Destroyed));
    }

    /// <summary>
    /// The attacker: 20 ships at full condition. The defender: 10 ships at 80%, carrying one army, so the
    /// carried-army term and the destruction of that army are both exercised.
    /// </summary>
    private static GameState CarriedCargoFixture() =>
        BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.EmbarkedArmy(
                    Cargo, "south", Defender, 0, 3, 60,
                    BattleTestbed.Unit("light_infantry", 1000, 6, "Marine Foot")),
            },
            fleets: new[]
            {
                BattleTestbed.Fleet(Attacker, "north", 0, 2, 20, 100),
                BattleTestbed.Fleet(Defender, "south", 0, 3, 10, 80, Cargo),
            });

    private static BattleResolution Resolve(GameState state, Ruleset ruleset, IEventSink? events = null) =>
        InstantBattleResolver.ResolveNaval(
            state,
            Attacker,
            Defender,
            ruleset,
            BattleTestbed.World,
            BattleTestbed.BattleRng(),
            Archers,
            events ?? NullEventSink.Instance);
}
