using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 10, 11, 12, 13 and 14 — the
/// <c>1_cartago_271_*</c> series from <c>docs/investigations/thracia-supply-morale.md</c> §"Fleets" §4
/// and <c>supply-driven-morale-and-fleet-attrition.md</c>'s "Part 2: the fleet side".
/// </summary>
public sealed class CartagoFleetAttritionReplayTests
{
    /// <summary>
    /// Done-when 10: the recorded per-save condition column for the starved 90-ship Carthaginian fleet
    /// (<c>spring_5</c> through <c>summer_7</c>), and the recorded moves it produces.
    /// </summary>
    private static readonly int[] CarthageConditions = { 79, 76, 66, 62, 56, 51, 48 };
    private static readonly int[] CarthageExpectedMoves = { 23, 23, 22, 21, 20, 19, 18 };

    [Fact]
    public void ZeroSupplyMovesFormula_ReproducesTheCarthaginianSeriesExactly()
    {
        // DoD 10: "The control is asserted in the same test" (N6, first review: it used to live in its
        // own separate fact) -- both the starved fleet and its supplied control are asserted here.
        var ruleset = NavalTestbed.Ruleset;

        const int starvedShips = 90;
        Assert.Equal(26, ruleset.Naval.MovesBaseValue - ((starvedShips - ruleset.Naval.MovesShipOffset) / ruleset.Naval.MovesShipDivisor));

        for (var i = 0; i < CarthageConditions.Length; i++)
        {
            var moves = FleetAttritionRule.MovesForTurn(
                starvedShips, carriedArmyTroops: null, supplyIsZero: true,
                conditionAfterZeroSupplyPenalty: CarthageConditions[i], ruleset);

            Assert.Equal(CarthageExpectedMoves[i], moves);
        }

        const int controlShips = 70;
        Assert.Equal(28, ruleset.Naval.MovesBaseValue - ((controlShips - ruleset.Naval.MovesShipOffset) / ruleset.Naval.MovesShipDivisor));

        for (var turn = 0; turn < 10; turn++)
        {
            var controlMoves = FleetAttritionRule.MovesForTurn(
                controlShips, carriedArmyTroops: null, supplyIsZero: false,
                conditionAfterZeroSupplyPenalty: 100, ruleset);

            Assert.Equal(28, controlMoves);
        }
    }

    /// <summary>
    /// Done-when 11: supplies drop by <c>ships</c> per turn for a fleet in port as well as at sea, while
    /// condition, moves and the death check are untouched in port -- one test asserting both halves.
    /// </summary>
    [Fact]
    public void SupplyConsumption_AppliesInPortAndAtSea_ButOtherAttritionOnlyAtSea()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;

        var inPort = new FleetState(
            "in-port-fleet", nationId, X: 0, Y: 0, Moves: 0, Ships: 20, ConditionPercent: 0,
            Money: 0, SupplyTons: 50, ConstructionTicksRemaining: 10, BuildCityId: "arx",
            CarriedArmyId: null, CoveredTileCode: null);

        var atSea = new FleetState(
            "at-sea-fleet", nationId, X: 0, Y: 3, Moves: 4, Ships: 10, ConditionPercent: 85,
            Money: 0, SupplyTons: 50, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);

        state = state with { Fleets = ValueList.Of(inPort, atSea), RandomSeed = 42UL };

        var coordinator = NavalTestbed.CoordinatorOnly(sink: null, typeof(FleetTickSystem));
        var result = coordinator.RunRoundTick(state).State;

        var updatedInPort = result.FleetById("in-port-fleet")!;
        var updatedAtSea = result.FleetById("at-sea-fleet")!;

        // Both lose supplies by their own ship count.
        Assert.Equal(30, updatedInPort.SupplyTons); // 50 - 20
        Assert.Equal(40, updatedAtSea.SupplyTons); // 50 - 10

        // The in-port fleet's condition, moves and under-construction status are untouched by attrition
        // (only its construction countdown moved, by the confirmed -2/turn step).
        Assert.Equal(0, updatedInPort.ConditionPercent);
        Assert.Equal(0, updatedInPort.Moves);
        Assert.True(updatedInPort.IsUnderConstruction);
        Assert.Equal(8, updatedInPort.ConstructionTicksRemaining);

        // The at-sea fleet's condition and moves DID change: the storm pass and the moves formula ran.
        Assert.NotEqual(85, updatedAtSea.ConditionPercent);
        Assert.NotEqual(4, updatedAtSea.Moves);
    }

    /// <summary>
    /// Done-when 12: storm attrition is a spiral, not a slope. Two fixed-seed runs, one condition floor
    /// held at 50 and one at 90 across many independent trials, show the expected per-turn condition
    /// loss at 50 is strictly greater than at 90 -- an ordering, not an absolute figure.
    /// </summary>
    /// <remarks>
    /// N4 (first review): the original version of this test held <c>nearFriendlyCoast: true</c> for
    /// both arms, under which condition-90 damage is identically <c>max(1, random(10)/10)/2 = 0</c> for
    /// every possible draw -- the assertion held by integer construction, not by exercising the spiral.
    /// This version uses <c>nearFriendlyCoast: false</c> (the away-from-coast branch, where both arms can
    /// actually roll a nonzero value) and the same seed for both arms, matching "a fixed-seed run"
    /// literally rather than two different ones.
    /// </remarks>
    [Fact]
    public void StormDamage_ExpectedLossAtLowerCondition_IsStrictlyGreaterThanAtHigherCondition()
    {
        var ruleset = NavalTestbed.Ruleset;
        const int trials = 2000;
        const ulong seed = 4242UL;

        long totalDamageAt50 = 0;
        long totalDamageAt90 = 0;

        var rngAt50 = new SplitMix64Rng(seed);
        var rngAt90 = new SplitMix64Rng(seed);

        for (var i = 0; i < trials; i++)
        {
            var storm50 = FleetAttritionRule.ApplyStormPass(
                ships: 50, conditionPercent: 50, isWinter: false, tripleDamageBranchActive: false,
                nearFriendlyCoast: false, rngAt50, ruleset);
            totalDamageAt50 += storm50.Damage;

            var storm90 = FleetAttritionRule.ApplyStormPass(
                ships: 50, conditionPercent: 90, isWinter: false, tripleDamageBranchActive: false,
                nearFriendlyCoast: false, rngAt90, ruleset);
            totalDamageAt90 += storm90.Damage;
        }

        var averageAt50 = (double)totalDamageAt50 / trials;
        var averageAt90 = (double)totalDamageAt90 / trials;

        Assert.True(averageAt90 > 0, "the condition-90 arm must itself be non-degenerate, not identically zero.");
        Assert.True(
            averageAt50 > averageAt90,
            $"Expected average storm damage at condition 50 ({averageAt50}) to exceed condition 90 ({averageAt90}).");
    }

    /// <summary>
    /// Done-when 12, the escalation itself demonstrated over successive turns rather than at two fixed
    /// points: a fleet at condition 80, away from friendly coast, under seed 2 (chosen because it dies
    /// within the fixed window below -- a real death spiral, not a synthetic one), takes damage that
    /// visibly grows turn over turn as its own condition falls, ending in destruction.
    /// </summary>
    [Fact]
    public void StormDamage_EscalatesTurnOverTurnAsConditionFalls_EndingInDestruction()
    {
        var ruleset = NavalTestbed.Ruleset;
        var rng = new SplitMix64Rng(2UL);
        var ships = 50;
        var condition = 80;
        var damages = new List<int>();

        for (var turn = 0; turn < 10; turn++)
        {
            var storm = FleetAttritionRule.ApplyStormPass(
                ships, condition, isWinter: false, tripleDamageBranchActive: false, nearFriendlyCoast: false, rng, ruleset);
            damages.Add(storm.Damage);
            condition = storm.ConditionPercent;
            ships = storm.Ships;
            if (condition < ruleset.Naval.DeathConditionThreshold)
            {
                break;
            }
        }

        // T63 (bug #292): the pinned values below are the old, inverted-ratio arithmetic's evidence, not
        // correct behaviour, and are replaced. Turn 8's dmg 7 now costs LESS (the corrected ratio r =
        // max(1, 10000 / 107) = 93 gives d = 86, against the old ratio's d = 114), so condition lands
        // exactly at 40 -- not below it -- and the run needs a ninth turn (dmg 3, light branch) before
        // condition 40 - 3 = 37 finally crosses the threshold. The old code broke after turn 8 at
        // condition 35.
        Assert.Equal(new[] { 3, 3, 3, 5, 3, 3, 5, 7, 3 }, damages);
        Assert.True(condition < ruleset.Naval.DeathConditionThreshold, "this seed's own run must end in destruction.");
        Assert.Equal(37, condition);
        Assert.Equal(36, ships);

        var earlyAverage = damages.Take(3).Average();
        var lateAverage = damages.Skip(damages.Count - 3).Average();
        Assert.True(
            lateAverage > earlyAverage,
            $"Expected damage to escalate: early turns averaged {earlyAverage}, the turns right before death averaged {lateAverage}.");
    }

    /// <summary>
    /// Done-when 13, part 1: condition below 40 destroys the fleet. Uses <see cref="ScriptedRng"/> to pin
    /// the storm roll precisely rather than relying on a real seed's draw to happen to cross the
    /// threshold.
    /// </summary>
    [Fact]
    public void ConditionBelowForty_DestroysTheFleetWithShipCountUnchangedThatTurn()
    {
        var ruleset = NavalTestbed.Ruleset;

        // condition 30 -> range = 100-30 = 70. rawDraw = 60 -> raw dmg = max(1, 60/10) = 6 -> near-coast
        // halved -> 3 (below the ship-loss threshold, so only condition is reduced). 30 - 3 = 27 < 40.
        var rng = new ScriptedRng(60);
        var outcome = FleetAttritionRule.ApplyLaunchedFleetTurn(
            ships: 40, conditionPercent: 30, supplyTonsBeforeConsumption: 100, carriedArmyTroops: null,
            isWinter: false, tripleDamageBranchActive: false, nearFriendlyCoast: true, rng, ruleset);

        Assert.True(outcome.Destroyed);
        Assert.Equal(40, outcome.Ships); // "the death check reads condition, not ships" -- ships unchanged.
        Assert.False(outcome.DamagedInStorm); // destroyed and damaged-in-storm are never both true.
    }

    /// <summary>
    /// Done-when 13, part 2: a fleet the zero-supply penalty (not the storm pass) pushes below 40
    /// survives that turn, because the death check runs before the zero-supply penalty -- and dies on the
    /// very next check.
    /// </summary>
    [Fact]
    public void ConditionPushedBelowFortyOnlyByTheZeroSupplyPenalty_SurvivesThatTurnThenDiesNext()
    {
        var ruleset = NavalTestbed.Ruleset;

        // Turn 1: rawDraw = 20 -> raw dmg = max(1, 20/10) = 2 -> halved (near coast) -> 1.
        // Storm alone: condition 41 - 1 = 40, which is NOT below 40 -- the death check passes.
        // Zero supply (supply = 0): condition -= NextInt(2), scripted to draw 1 -> 40 - 1 = 39.
        var rngTurn1 = new ScriptedRng(20, 1);
        var turn1 = FleetAttritionRule.ApplyLaunchedFleetTurn(
            ships: 30, conditionPercent: 41, supplyTonsBeforeConsumption: 0, carriedArmyTroops: null,
            isWinter: false, tripleDamageBranchActive: false, nearFriendlyCoast: true, rngTurn1, ruleset);

        Assert.False(turn1.Destroyed, "the death check precedes the zero-supply penalty, so ending at 39 this turn must not itself destroy the fleet.");
        Assert.Equal(39, turn1.ConditionPercent);
        Assert.Equal(30, turn1.Ships);

        // Turn 2: condition starts at 39, already below 40. Any nonzero storm damage keeps it there or
        // lower, so the death check now fires regardless of the exact roll.
        var rngTurn2 = new ScriptedRng(5);
        var turn2 = FleetAttritionRule.ApplyLaunchedFleetTurn(
            ships: 30, conditionPercent: turn1.ConditionPercent, supplyTonsBeforeConsumption: turn1.SupplyTonsAfterConsumption,
            carriedArmyTroops: null, isWinter: false, tripleDamageBranchActive: false, nearFriendlyCoast: true, rngTurn2, ruleset);

        Assert.True(turn2.Destroyed, "a fleet already below 40 dies on the very next check.");
    }

    /// <summary>
    /// Done-when 13, part 3: the full <see cref="FleetTickSystem"/> pipeline emits the confirmed literal
    /// and removes the fleet, with a carried army removed alongside it.
    /// </summary>
    [Fact]
    public void FleetTickSystem_DestroyedFleet_EmitsTheConfirmedMessageAndRemovesAnyCarriedArmy()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;

        var doomedFleet = new FleetState(
            "doomed-fleet", nationId, X: 0, Y: 3, Moves: 5, Ships: 20, ConditionPercent: 1,
            Money: 0, SupplyTons: 50, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: "doomed-army", CoveredTileCode: null);

        var carriedArmy = new ArmyState(
            "doomed-army", nationId, X: 0, Y: 3, Moves: 0, Morale: 60, Money: 0, SupplyTons: 0,
            CoveredTileCode: null, AboardFleetId: "doomed-fleet",
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", 4000, 6, "Doomed Battalion")));

        // Condition 1 guarantees death this turn regardless of the actual random draw: any dmg >= 1
        // (the formula's own floor) leaves condition at or below 0, which is below 40 either way.
        state = state with
        {
            Fleets = ValueList.Of(doomedFleet),
            Armies = ValueList.Of(carriedArmy),
            RandomSeed = 777UL,
        };

        var sink = new RecordingEventSink();
        var coordinator = NavalTestbed.CoordinatorOnly(sink, typeof(FleetTickSystem));
        var result = coordinator.RunRoundTick(state).State;

        Assert.Null(result.FleetById("doomed-fleet"));
        Assert.Null(result.ArmyById("doomed-army")); // lost with the fleet, per game-design.md's Naval section.

        var lostAtSea = Assert.Single(sink.Events.OfType<FleetLostAtSea>());
        Assert.Equal(nationId, lostAtSea.Nation);
    }

    /// <summary>
    /// Done-when 14: <c>combat.onDefeat</c> and the ruleset presets do not alter this pass -- asserted
    /// directly by running the same seeded state under both <see cref="DefeatOutcome"/> settings and
    /// getting bit-identical fleets back.
    /// </summary>
    [Fact]
    public void CombatOnDefeatFlag_HasNoEffectOnAtSeaAttrition()
    {
        // N7 (first review): this used to vary only Flags.CombatOnDefeat. DoD 14 says "at-sea attrition
        // is faithful under both classical-faithful and improved" -- the two shipped presets, which also
        // differ in Flags.SeatAsymmetry (varied elsewhere, in EmbarkArmyCommandHandlerTests). This runs
        // the full 2x2 matrix of both flags and asserts every combination gives back the identical fleet.
        var baseState = NavalTestbed.InitialState();
        var nationId = baseState.Nations[0].Id;

        var fleet = new FleetState(
            "flag-neutral-fleet", nationId, X: 0, Y: 3, Moves: 5, Ships: 25, ConditionPercent: 60,
            Money: 0, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);

        var state = baseState with { Fleets = ValueList.Of(fleet), RandomSeed = 555UL };

        var registry = SystemRegistry.FromAssemblies(
            new[] { typeof(FleetTickSystem).Assembly }, t => t == typeof(FleetTickSystem));

        FleetState RunUnder(DefeatOutcome onDefeat, SeatAsymmetryModel seatAsymmetry)
        {
            var ruleset = NavalTestbed.Ruleset with
            {
                Flags = NavalTestbed.Ruleset.Flags with { CombatOnDefeat = onDefeat, SeatAsymmetry = seatAsymmetry },
            };
            var result = new TurnCoordinator(registry, ruleset, NavalTestbed.Toy.World, NullEventSink.Instance)
                .RunRoundTick(state).State;
            return result.FleetById(fleet.Id)!;
        }

        var classicalFaithful = RunUnder(DefeatOutcome.Destroyed, SeatAsymmetryModel.Faithful);
        var improved = RunUnder(DefeatOutcome.Scatter, SeatAsymmetryModel.Normalized);
        var mixedA = RunUnder(DefeatOutcome.Destroyed, SeatAsymmetryModel.Normalized);
        var mixedB = RunUnder(DefeatOutcome.Scatter, SeatAsymmetryModel.Faithful);

        Assert.Equal(classicalFaithful, improved);
        Assert.Equal(classicalFaithful, mixedA);
        Assert.Equal(classicalFaithful, mixedB);
    }
}
