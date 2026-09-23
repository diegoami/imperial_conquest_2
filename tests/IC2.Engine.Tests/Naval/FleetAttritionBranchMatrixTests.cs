using IC2.Engine.Core;
using IC2.Engine.Naval;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval" Hazards: "Every specified behaviour carries a test that
/// fails when it is removed." The first review proved eight behaviours could each be deleted with the
/// full 1,723-test suite staying green, because every direct call in the suite passed
/// <c>isWinter: false</c>, <c>tripleDamageBranchActive: false</c>, <c>nearFriendlyCoast: true</c> and
/// <c>carriedArmyTroops: null</c>. Each test below varies exactly one of those axes (or, for the heavy
/// branch, a real seed known to trigger it) and pins a specific numeric consequence, so deleting the
/// underlying constant or branch makes the assertion fail, not merely produce a different valid number.
/// </summary>
public sealed class FleetAttritionBranchMatrixTests
{
    private static readonly IC2.Engine.Model.Ruleset Ruleset = NavalTestbed.Ruleset;

    /// <summary>
    /// Winter doubling (<c>NavalRules.StormWinterDamageMultiplier</c>). condition 90 (range 10), rawDraw
    /// 9 -&gt; rawDmg 1. Winter: doubled to 2, halved (near coast) to 1 -&gt; condition loses 1. Non-winter:
    /// stays 1, halved to 0 -&gt; condition loses 0. Deleting the doubling collapses both to 0.
    /// </summary>
    [Fact]
    public void WinterDoubling_ChangesTheStormResultRelativeToNonWinter()
    {
        var winter = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 90, isWinter: true, tripleDamageBranchActive: false,
            nearFriendlyCoast: true, new ScriptedRng(9), Ruleset);
        var nonWinter = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 90, isWinter: false, tripleDamageBranchActive: false,
            nearFriendlyCoast: true, new ScriptedRng(9), Ruleset);

        Assert.Equal(89, winter.ConditionPercent); // 90 - 1.
        Assert.Equal(90, nonWinter.ConditionPercent); // 90 - 0.
        Assert.NotEqual(winter.ConditionPercent, nonWinter.ConditionPercent);
    }

    /// <summary>
    /// The Winter cap (<c>NavalRules.StormWinterDamageCap</c> = 5). condition 50 (range 50), rawDraw 45
    /// -&gt; rawDmg 4. Doubled would be 8, capped to 5. Halved (near coast): 5/2 = 2. Uncapped would halve
    /// 8/2 = 4 -- a different, distinguishable value.
    /// </summary>
    [Fact]
    public void WinterDoublingCap_HoldsAtFiveBeforeHalving()
    {
        var storm = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 50, isWinter: true, tripleDamageBranchActive: false,
            nearFriendlyCoast: true, new ScriptedRng(45), Ruleset);

        Assert.Equal(2, storm.Damage); // capped path; an uncapped doubling would give 4.
    }

    /// <summary>
    /// The tripling branch (<c>NavalRules.StormTripleDamageMultiplier</c>). condition 90 (range 10),
    /// rawDraw 9 -&gt; rawDmg 1. Tripled to 3, halved to 1. Without the branch: stays 1, halved to 0.
    /// </summary>
    [Fact]
    public void TriplingBranch_ChangesTheStormResultRelativeToNoTripling()
    {
        var tripled = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 90, isWinter: false, tripleDamageBranchActive: true,
            nearFriendlyCoast: true, new ScriptedRng(9), Ruleset);
        var notTripled = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 90, isWinter: false, tripleDamageBranchActive: false,
            nearFriendlyCoast: true, new ScriptedRng(9), Ruleset);

        Assert.Equal(1, tripled.Damage);
        Assert.Equal(0, notTripled.Damage);
        Assert.NotEqual(tripled.Damage, notTripled.Damage);
    }

    /// <summary>
    /// The tripling cap (<c>NavalRules.StormTripleDamageCap</c> = 8). condition 50 (range 50), rawDraw 45
    /// -&gt; rawDmg 4. Tripled would be 12, capped to 8. Halved: 8/2 = 4. Uncapped would halve 12/2 = 6.
    /// </summary>
    [Fact]
    public void TriplingCap_HoldsAtEightBeforeHalving()
    {
        var storm = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 50, isWinter: false, tripleDamageBranchActive: true,
            nearFriendlyCoast: true, new ScriptedRng(45), Ruleset);

        Assert.Equal(4, storm.Damage); // capped path; an uncapped tripling would give 6.
    }

    /// <summary>
    /// Away-from-coast <c>x2 + 1</c> (<c>NavalRules.StormAwayFromCoastDamageMultiplier</c>/
    /// <c>Addend</c>). condition 90 (range 10), rawDraw 9 -&gt; rawDmg 1. Away: 1x2+1 = 3. Near coast:
    /// halved to 0.
    /// </summary>
    [Fact]
    public void AwayFromCoast_ChangesTheStormResultRelativeToNearCoast()
    {
        var away = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 90, isWinter: false, tripleDamageBranchActive: false,
            nearFriendlyCoast: false, new ScriptedRng(9), Ruleset);
        var near = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 90, isWinter: false, tripleDamageBranchActive: false,
            nearFriendlyCoast: true, new ScriptedRng(9), Ruleset);

        Assert.Equal(3, away.Damage);
        Assert.Equal(0, near.Damage);
    }

    /// <summary>
    /// The Winter 1-in-20 spike (<c>NavalRules.StormWinterSpikeChanceDenominator</c>/<c>Damage</c>), only
    /// reachable away from coast in Winter. condition 90, rawDraw 5 -&gt; rawDmg 1, doubled 2, away 2x2+1
    /// = 5. A successful roll overrides that to exactly 30; a failed roll leaves it at 5.
    /// </summary>
    [Fact]
    public void WinterSpike_OverridesDamageToThirtyOnlyWhenTheRollSucceeds()
    {
        var spiked = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 90, isWinter: true, tripleDamageBranchActive: false,
            nearFriendlyCoast: false, new ScriptedRng(new[] { 5 }, new[] { true }), Ruleset);
        var notSpiked = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 90, isWinter: true, tripleDamageBranchActive: false,
            nearFriendlyCoast: false, new ScriptedRng(new[] { 5 }, new[] { false }), Ruleset);

        Assert.Equal(30, spiked.Damage);
        Assert.Equal(5, notSpiked.Damage);
    }

    /// <summary>
    /// The heavy <c>dmg &gt;= 6</c> ship-loss branch, using a real seed known to land on it
    /// (<c>SplitMix64Rng(44)</c> at ships 40, condition 68, away from coast, no Winter/triple): dmg 7,
    /// ships fall 40 -&gt; 29, condition falls to 49 (survives), and <see cref="FleetAttritionRule.TurnOutcome.DamagedInStorm"/>
    /// is true. Replacing the heavy branch with the light one (condition-only) would leave ships at 40.
    /// </summary>
    /// <remarks>
    /// T63 (bug #292): the pinned values here are the OLD, inverted-ratio arithmetic's evidence, not
    /// correct behaviour, and are replaced. The original's r = max(1, 10000 / (dmg + 100)) = max(1,
    /// 10000 / 107) = 93; d = 93^2 / 100 = 86; shipsLost = 40 x 86 / 300 = 11 -&gt; 29 ships;
    /// conditionLost = 68 x 86 / 300 = 19 -&gt; condition 49. The old code computed ratio = dmg + 100 =
    /// 107 (the numerator and denominator swapped), giving d = 107^2 / 100 = 114, ships 25, condition 43.
    /// </remarks>
    [Fact]
    public void HeavyDamageBranch_CostsShipsAndFiresTheDamagedInStormFlag()
    {
        var rng = new SplitMix64Rng(44);
        var outcome = FleetAttritionRule.ApplyLaunchedFleetTurn(
            ships: 40, conditionPercent: 68, supplyTonsBeforeConsumption: 1000, carriedArmyTroops: null,
            isWinter: false, tripleDamageBranchActive: false, nearFriendlyCoast: false, rng, Ruleset);

        Assert.Equal(7, outcome.Damage);
        Assert.Equal(29, outcome.Ships); // fell from 40 -- the light branch would leave it at 40.
        Assert.Equal(49, outcome.ConditionPercent);
        Assert.False(outcome.Destroyed);
        Assert.True(outcome.DamagedInStorm);
    }

    /// <summary>
    /// B5 (round-2 review): asserting the pure rule's <c>DamagedInStorm</c> flag (the test above) is not
    /// the same claim as asserting <see cref="FleetTickSystem"/> actually publishes
    /// <see cref="FleetDamagedInStorm"/> from it. Deleting the publish at
    /// <c>FleetTickSystem.cs</c>'s <c>context.Events.Publish(new FleetDamagedInStorm(...))</c> line left
    /// every one of the 1,764 round-2 tests green, including the one above, because nothing ran the real
    /// system with a sink and checked. This runs the real pipeline (seed 14, found by search to land a
    /// heavy-but-survivable hit on the same 40-ship/condition-68/away-from-friendly-coast fleet) and
    /// checks the sink directly -- the same standard <see cref="FleetLostAtSea"/> and
    /// <see cref="FleetFinished"/> already meet elsewhere in this test project.
    /// </summary>
    [Fact]
    public void HeavyDamageBranch_FleetTickSystemActuallyPublishesFleetDamagedInStorm()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;

        var fleet = new IC2.Engine.Model.FleetState(
            "damaged-publish-fleet", nationId, X: 0, Y: 3, Moves: 5, Ships: 40, ConditionPercent: 68,
            Money: 0, SupplyTons: 1000, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);

        var seededState = state with { Fleets = IC2.Engine.Model.ValueList.Of(fleet), RandomSeed = 14UL };
        var sink = new RecordingEventSink();
        var coordinator = NavalTestbed.CoordinatorOnly(sink, typeof(FleetTickSystem));
        var result = coordinator.RunRoundTick(seededState).State;

        var updated = result.FleetById(fleet.Id)!;
        Assert.True(updated.Ships < 40, "the seed must land on the heavy branch for this test to mean anything.");
        Assert.False(updated.ConditionPercent < Ruleset.Naval.DeathConditionThreshold, "must survive, not die, for DamagedInStorm rather than FleetLostAtSea to be the expected event.");

        Assert.Single(sink.Events.OfType<FleetDamagedInStorm>());
    }

    /// <summary>
    /// The carried-army moves term (<c>NavalRules.MovesCarriedArmyTroopDivisor</c>/<c>Addend</c>). ships
    /// 40 (base moves 30 - (40-50)/10 = 31), no zero-supply penalty, condition 100 (no slowdown). Carrying
    /// 8,000 troops subtracts <c>8000/100/40 + 1 = 3</c>.
    /// </summary>
    [Fact]
    public void CarriedArmy_ReducesMovesByTheConfirmedTerm()
    {
        var withArmy = FleetAttritionRule.MovesForTurn(
            ships: 40, carriedArmyTroops: 8000, supplyIsZero: false, conditionAfterZeroSupplyPenalty: 100, Ruleset);
        var withoutArmy = FleetAttritionRule.MovesForTurn(
            ships: 40, carriedArmyTroops: null, supplyIsZero: false, conditionAfterZeroSupplyPenalty: 100, Ruleset);

        Assert.Equal(31, withoutArmy);
        Assert.Equal(28, withArmy); // 31 - (8000/100/40 + 1) = 31 - 3.
    }

    /// <summary>
    /// T63 (bug #292): <c>r</c> FALLS as <c>dmg</c> rises, so the Winter spike (<c>dmg</c> fixed at 30,
    /// the LARGEST reachable heavy-branch <c>dmg</c>) costs the LEAST of any heavy storm -- the opposite
    /// of what the old, inverted formula would have given. Forced via the Winter-spike branch itself
    /// (<c>isWinter</c> true, away from coast, the 1-in-20 chance scripted to hit), rather than hunting
    /// for a seed that lands on <c>dmg == 30</c> by chance.
    /// </summary>
    [Fact]
    public void WinterSpike_TheLargestReachableDmg_CostsTheLeastOfAnyHeavyStorm()
    {
        // rawDraw doesn't matter -- the Winter-spike chance below overwrites dmg to 30 regardless.
        var rng = new ScriptedRng(new[] { 0 }, new[] { true });

        var storm = FleetAttritionRule.ApplyStormPass(
            ships: 100, conditionPercent: 100, isWinter: true, tripleDamageBranchActive: false,
            nearFriendlyCoast: false, rng, Ruleset);

        Assert.Equal(30, storm.Damage);

        // r = max(1, 10000 / (30 + 100)) = max(1, 76.9) = 76; d = 76^2 / 100 = 57.
        // shipsLost = 100 x 57 / 300 = 19; conditionLost = 100 x 57 / 300 = 19 -- against the dmg-7
        // heavy branch's 11 ships / 19 condition lost from a 40-ship fleet (28% of the fleet), this is
        // about 19% -- smaller, exactly as game-design.md's own table states.
        Assert.Equal(81, storm.Ships);
        Assert.Equal(81, storm.ConditionPercent);
    }

    /// <summary>
    /// T63 (bug #292): the heavy storm's missing steps 3 and 4 -- the carried army takes
    /// <see cref="BattleCasualties.Apply"/> at ratio <c>d</c> (including its own deletion pass, #289),
    /// then, since <c>d</c> here (86, the confirmed dmg-7 figure) exceeds the storm's own whole-unit-loss
    /// threshold, also loses whole units. Proves <see cref="FleetAttritionRule.ApplyStormCasualtiesToCarriedArmy"/>
    /// reads <c>naval.stormUnitLossDamageThreshold</c>/<c>Divisor</c> -- the storm's OWN pair, not the
    /// naval battle's <c>combat.naval.unitLossDamageThreshold</c>/<c>Divisor</c> -- since this task's Owns
    /// list keeps the two records separate (either field's own remarks explain why).
    /// </summary>
    [Fact]
    public void ApplyStormCasualtiesToCarriedArmy_CostsTroopsAndWholeUnitsAboveTheThreshold()
    {
        Assert.Equal(70, Ruleset.Naval.StormUnitLossDamageThreshold);
        Assert.Equal(250, Ruleset.Naval.StormUnitLossDivisor);

        var units = IC2.Engine.Model.ValueList.Of(
            new IC2.Engine.Model.UnitSlot(0, "heavy_cavalry", 2000, 6, "A"),
            new IC2.Engine.Model.UnitSlot(0, "heavy_cavalry", 2000, 6, "B"));

        var rng = new SplitMix64Rng(0x517UL);

        // d = 86, the confirmed dmg-7 figure (game-design.md's own table): well above the 70 threshold.
        var result = FleetAttritionRule.ApplyStormCasualtiesToCarriedArmy(units, damage: 86, rng, Ruleset);

        Assert.True(result.TroopsLost > 0, "the casualty pass (step 3) must have cost some troops.");
        Assert.True(result.UnitsLost >= 1, "d = 86 > 70 must remove at least the '+ 1' whole unit (step 4).");
    }
}
