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
/// <see cref="BattleTestbed.Seed"/>, whose first two random bands are <c>3</c> then <c>1</c>.
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
    /// loses whole unit slots at random, <c>(unitCount × d / 250) + 1</c> of them (bug #290 part 3;
    /// <c>unitCount</c> counted after <see cref="BattleCasualties.Apply"/>'s own casualties).
    /// </summary>
    [Fact]
    public void DoD07_AboveTheDamageThresholdTheWinnersCarriedArmyAlsoLosesWholeUnits()
    {
        var naval = BattleTestbed.Destroyed.Combat.Naval;

        // T63 (bug #290 part 2): the carried army's casualty ratio is the damage figure `d` itself, not
        // the field-battle ratio loserPower x 40 / winnerPower an earlier round used here. `d` runs much
        // higher than that ratio ever could (up to 100, against a ceiling of 40), so this fixture's units
        // are sized in heavy_cavalry troops (the smallest small-unit-deletion threshold, 250 national --
        // bug #289) rather than the original's 1,000-troop light infantry, which the new casualty pass
        // now wipes out entirely before the whole-unit-loss branch ever runs.
        var state = BattleTestbed.StateWith(
            armies: new[]
            {
                BattleTestbed.EmbarkedArmy(
                    "north-cargo", "north", Attacker, 0, 2, 60,
                    BattleTestbed.Unit("heavy_cavalry", 1200, 6, "A"),
                    BattleTestbed.Unit("heavy_cavalry", 1200, 6, "B"),
                    BattleTestbed.Unit("heavy_cavalry", 1200, 6, "C"),
                    BattleTestbed.Unit("heavy_cavalry", 1200, 6, "D")),
            },
            fleets: new[]
            {
                BattleTestbed.Fleet(Attacker, "north", 0, 2, 20, 100, "north-cargo"),
                BattleTestbed.Fleet(Defender, "south", 0, 3, 29, 100),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        // 200 + (4,800/80 × 60) / 50 = 200 + 72 = 272; band 27; first draw 3 → 353.
        Assert.Equal(353, result.AttackerPower);
        Assert.Equal(319, result.DefenderPower);

        var damageRatio = (result.LoserPower * naval.DamageRatioScale) / result.WinnerPower;
        var damage = (damageRatio * damageRatio) / naval.DamageRatioScale;
        Assert.Equal(90, damageRatio);
        Assert.Equal(81, damage);
        Assert.True(damage > naval.UnitLossDamageThreshold);

        // The carried army takes ratio `d` (81) itself -- not a fresh loserPower x 40 / winnerPower
        // figure -- at divisors 116, 115, 105, 111 (the same four draws the pre-T63 fixture used; the
        // draw COUNT and order up to here are unchanged):
        //   1200/116 = 10 -> 810     1200/115 = 10 -> 810
        //   1200/105 = 11 -> 891     1200/111 = 10 -> 810
        Assert.Equal(
            new[] { 116, 115, 105, 111 }.Select(d => (1200 / d) * damage).ToArray(),
            result.UnitCasualties.Select(c => c.TroopsLost).ToArray());
        Assert.Equal(new[] { 810, 810, 891, 810 }, result.UnitCasualties.Select(c => c.TroopsLost).ToArray());

        // Every survivor (390, 390, 309, 390 troops) is still above heavy_cavalry's national deletion
        // threshold (2,500 / 10 = 250, bug #289), so DeleteBelowThreshold removes none of them here --
        // the whole-unit-loss branch below is the only thing that removes a slot in this fixture.
        Assert.Equal((4 * damage) / naval.UnitLossDivisor + 1, result.WinnerUnitsLost);
        Assert.Equal(2, result.WinnerUnitsLost);

        // Then the d > 70 branch removes two whole slots, swap-with-last, picked by the next two draws.
        var cargo = after.ArmyById("north-cargo")!;
        Assert.Equal(new[] { "A", "C" }, cargo.Units.Select(u => u.Name).ToArray());
        Assert.Equal(new[] { 390, 309 }, cargo.Units.Select(u => u.Troops).ToArray());

        // Every troop the winner's carried army lost is accounted for: 3,321 to attrition (810 + 810 +
        // 891 + 810) plus the 390 and 390 that went down with the two removed slots ("B" and "D").
        Assert.Equal(810 + 810 + 891 + 810 + 390 + 390, result.WinnerCasualties);
        Assert.Equal(4101, result.WinnerCasualties);
        Assert.Equal(4800 - result.WinnerCasualties, cargo.TotalTroops);
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
        // Identical fleets, and the seed's two bands differ (3 then 1), so the bands are neutralised by
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
        // 100 against 100 at equal condition is large enough that the scaled loss (T52 DoD 1/2) leaves a
        // survivor: the mirrored ratio is winnerPower × 40 / loserPower and the loss is
        // (ships × ratio) / divisor, so a bigger fleet loses a smaller share of itself for the same ratio.
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

        // The winner (Attacker) carries no army, so the scatter branch's own divisor draw is the third
        // draw of the battle -- the same NextInt(15) stream position
        // DoD07_AboveTheDamageThresholdTheWinnersCarriedArmyAlsoLosesWholeUnits's first carried-army
        // divisor lands on, 11, divisor 116. Mirrored ratio 1300 × 40 / 1115 = 46; (100 × 46) / 116 = 39.
        Assert.Equal(39, result.LoserCasualties);
        Assert.Equal(100 - 39, after.FleetById(Defender)!.Ships);

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

    /// <summary>
    /// T52 DoD 3: <c>classical-faithful</c> is untouched by the naval scatter scaling fix -- a beaten
    /// fleet is still annihilated outright, asserted directly so this task cannot change the faithful
    /// preset by accident.
    /// </summary>
    [Fact]
    public void DoD03_ClassicalFaithfulStillAnnihilatesABeatenFleetOutright()
    {
        var state = BattleTestbed.StateWith(
            fleets: new[]
            {
                BattleTestbed.Fleet(Attacker, "north", 0, 2, 100, 100),
                BattleTestbed.Fleet(Defender, "south", 0, 3, 10, 80),
            });

        var (after, result) = Resolve(state, BattleTestbed.Destroyed);

        Assert.Equal(DefeatOutcome.Destroyed, result.AppliedDefeatOutcome);
        Assert.Equal(LoserFate.Destroyed, result.LoserFate);
        Assert.Null(after.FleetById(Defender));
        Assert.Single(after.Fleets);

        // The whole force, not a scaled fraction -- BattleCasualties.ApplyToFleet is never called on this
        // path, since it lives entirely inside the ruleset.Flags.CombatOnDefeat == Scatter branch.
        Assert.Equal(10, result.LoserCasualties);
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
