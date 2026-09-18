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
    /// ships fall 40 -&gt; 25, condition falls to 43 (survives), and <see cref="FleetAttritionRule.TurnOutcome.DamagedInStorm"/>
    /// is true. Replacing the heavy branch with the light one (condition-only) would leave ships at 40.
    /// </summary>
    [Fact]
    public void HeavyDamageBranch_CostsShipsAndFiresTheDamagedInStormFlag()
    {
        var rng = new SplitMix64Rng(44);
        var outcome = FleetAttritionRule.ApplyLaunchedFleetTurn(
            ships: 40, conditionPercent: 68, supplyTonsBeforeConsumption: 1000, carriedArmyTroops: null,
            isWinter: false, tripleDamageBranchActive: false, nearFriendlyCoast: false, rng, Ruleset);

        Assert.Equal(7, outcome.Damage);
        Assert.Equal(25, outcome.Ships); // fell from 40 -- the light branch would leave it at 40.
        Assert.Equal(43, outcome.ConditionPercent);
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
}
