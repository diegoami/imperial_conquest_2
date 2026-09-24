using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T46 Fleet-to-fleet transfer, and the supply path that keeps fleets
/// alive" (issue #148), Done-when 9: "a fleet run to the edge of starvation, resupplied, and still afloat
/// some turns later" -- the line that proves the starvation hole T14 opened (attrition drains
/// <c>supplies -= ships</c> every turn from a launch stock of 50, with nothing to refill it) is actually
/// closed by <see cref="BuyFleetSupplyCommand"/>, not merely addressable in principle.
/// </summary>
public sealed class FleetSupplyEndToEndTests
{
    private const string NationId = "north";

    /// <summary>
    /// End to end: a fleet is run through the real <see cref="FleetTickSystem"/> pipeline until its
    /// supply hits zero (the zero-supply penalty visibly applies), then <see cref="BuyFleetSupplyCommand"/>
    /// is dispatched through the real <see cref="CommandDispatcher"/> against a co-located owned city, and
    /// the fleet is then run through many more real turns and is still on the map.
    /// </summary>
    /// <remarks>
    /// The scenario is deliberately built so the storm pass itself contributes no randomness at all: the
    /// fleet starts at <see cref="NavalRules.MaxConditionPercent"/> (100) and stays within
    /// <see cref="NavalRules.FriendlyCoastRadiusTiles"/> of its own city throughout, so
    /// <c>range = MaxConditionPercent - conditionPercent</c> is 0 and <c>FleetAttritionRule.ApplyStormPass</c>
    /// skips its random draw entirely, landing on <c>dmg = max(1, 0) = 1</c>, halved by
    /// <c>StormNearCoastDamageDivisor</c> (2) to <c>0</c> — deterministically, for any seed, as long as
    /// condition stays at 100. Only the zero-supply condition penalty
    /// (<c>-random(0, ZeroSupplyConditionRandomBound)</c>, i.e. 0 or 1 per turn while supply is 0) ever
    /// draws, so the test is safe from flakiness under a real seed: even a run of unlucky draws across the
    /// handful of turns here cannot approach <see cref="NavalRules.DeathConditionThreshold"/> (40).
    /// </remarks>
    [Fact]
    public void FleetRunToTheEdgeOfStarvation_Resupplied_IsStillAfloatManyTurnsLater()
    {
        var baseState = NavalTestbed.InitialState();
        var rules = NavalTestbed.Ruleset.Naval;

        const int ships = 10;
        var capacity = ships * NavalTestbed.Ruleset.Economy.FleetSupplyTonsPerShip; // 80 tons.

        // Co-located with an owned city, so "near friendly coast" holds throughout and the city is a
        // valid resupply provider (within one tile) without any diplomacy machinery.
        var homeCity = baseState.Cities[0] with { Owner = NationId, X = 0, Y = 0, SupplyTons = 1000 };
        var fleet = new FleetState(
            "starving-fleet", NationId, X: 0, Y: 0, Moves: 4, Ships: ships, ConditionPercent: rules.MaxConditionPercent,
            Money: 0, SupplyTons: 5, ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null,
            CoveredTileCode: null);

        var state = baseState with
        {
            Fleets = ValueList.Of(fleet),
            Cities = ValueList.From(baseState.Cities.Select(c =>
                string.Equals(c.Id, homeCity.Id, StringComparison.Ordinal) ? homeCity : c)),
            RandomSeed = 20250918UL,
        };

        var coordinator = NavalTestbed.CoordinatorOnly(sink: null, typeof(FleetTickSystem));

        // Turn 1: consumption (ships=10) exceeds the starting 5 tons -- the fleet reaches the edge of
        // starvation in a single turn, exactly as T14's catalogue entry describes ("starves within five
        // turns" from a much larger launch stock).
        state = coordinator.RunRoundTick(state).State;
        var edge = state.FleetById(fleet.Id)!;
        Assert.Equal(0, edge.SupplyTons);

        // The storm pass itself contributes nothing at condition 100, near coast (see this type's
        // remarks) -- only the zero-supply penalty (-0 or -1) can move condition at all here, so one turn
        // can lose at most 1 point regardless of the seed's actual draw.
        Assert.InRange(edge.ConditionPercent, rules.MaxConditionPercent - 1, rules.MaxConditionPercent);
        Assert.NotNull(state.FleetById(fleet.Id)); // still afloat at the edge -- the penalty has not killed it yet.

        // Resupply, end to end, through the real command dispatcher.
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var resupplyResult = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, fleet.Id, homeCity.Id, ProviderFleetId: null, Tons: capacity));

        Assert.True(resupplyResult.IsAccepted, resupplyResult.ToString());
        state = resupplyResult.State;
        var resupplied = state.FleetById(fleet.Id)!;
        Assert.Equal(capacity, resupplied.SupplyTons); // topped up to the ships x 8 cap, free at an owned city.

        // Many more real turns -- long enough for the resupplied 80 tons to be consumed again (8 turns at
        // 10/turn) and for several further zero-supply turns to run past that, well beyond "some turns
        // later".
        const int turnsAfterResupply = 20;
        for (var i = 0; i < turnsAfterResupply; i++)
        {
            state = coordinator.RunRoundTick(state).State;
            Assert.NotNull(state.FleetById(fleet.Id)); // still afloat, every single turn -- not just at the end.
        }

        var final = state.FleetById(fleet.Id)!;
        Assert.NotNull(final);

        // The mechanism actually engaged, not merely "nothing happened": supply was consumed all the way
        // back down (no unbounded free resupply loop was silently keeping it topped up), and moves
        // reflect the zero-supply penalty once it recurs, exactly as they did before the resupply.
        Assert.Equal(0, final.SupplyTons);
        Assert.True(
            final.ConditionPercent >= rules.DeathConditionThreshold,
            $"the resupplied fleet must survive well clear of the death threshold; ended at {final.ConditionPercent}.");
    }

    /// <summary>
    /// T50 Done-when 2 (issue #165 item 1): the previous survival assertion was inert -- an unsupplied
    /// fleet in the same scenario was still afloat after the same 21 turns (condition 92 against 95) and
    /// only sank at turn 89, because the zero-supply penalty (<c>-random(0, ZeroSupplyConditionRandomBound)</c>,
    /// i.e. 0 or 1 per turn) is slow against a 60-point margin to
    /// <see cref="NavalRules.DeathConditionThreshold"/>. This test puts an unsupplied control fleet in the
    /// very same run: both fleets start identically (same ships, same starting supply, same position, same
    /// seed) and only the "starving-fleet" ever receives <see cref="BuyFleetSupplyCommand"/>. Run long
    /// enough, the control fleet must sink while the resupplied fleet does not -- so the assertion fails
    /// outright if resupply stops working (the two fleets would then behave identically and either both
    /// survive or both sink together).
    /// </summary>
    [Fact]
    public void FleetRunToTheEdgeOfStarvation_UnsuppliedControlSinks_WhileResuppliedSurvives()
    {
        var baseState = NavalTestbed.InitialState();
        var rules = NavalTestbed.Ruleset.Naval;

        const int ships = 10;
        var capacity = ships * NavalTestbed.Ruleset.Economy.FleetSupplyTonsPerShip; // 80 tons.

        // T70 (#190 N3): topped up every turn below rather than just once, so the city needs stock for
        // the whole run, not just the first top-up.
        var homeCity = baseState.Cities[0] with { Owner = NationId, X = 0, Y = 0, SupplyTons = 10_000 };

        var resuppliedFleet = new FleetState(
            "starving-fleet", NationId, X: 0, Y: 0, Moves: 4, Ships: ships, ConditionPercent: rules.MaxConditionPercent,
            Money: 0, SupplyTons: 5, ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null,
            CoveredTileCode: null);

        // The control: identical in every respect except it is never resupplied.
        var controlFleet = new FleetState(
            "control-fleet", NationId, X: 0, Y: 0, Moves: 4, Ships: ships, ConditionPercent: rules.MaxConditionPercent,
            Money: 0, SupplyTons: 5, ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null,
            CoveredTileCode: null);

        var state = baseState with
        {
            Fleets = ValueList.Of(resuppliedFleet, controlFleet),
            Cities = ValueList.From(baseState.Cities.Select(c =>
                string.Equals(c.Id, homeCity.Id, StringComparison.Ordinal) ? homeCity : c)),
            RandomSeed = 20250918UL,
        };

        var coordinator = NavalTestbed.CoordinatorOnly(sink: null, typeof(FleetTickSystem));

        // Both fleets reach zero supply on turn 1, from the same 5-ton starting stock against a 10/turn
        // draw.
        state = coordinator.RunRoundTick(state).State;
        Assert.Equal(0, state.FleetById(resuppliedFleet.Id)!.SupplyTons);
        Assert.Equal(0, state.FleetById(controlFleet.Id)!.SupplyTons);

        // Resupply only the "starving-fleet", end to end, through the real command dispatcher. The
        // control fleet gets nothing, ever.
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var resupplyResult = dispatcher.Dispatch(
            state,
            new BuyFleetSupplyCommand(NationId, resuppliedFleet.Id, homeCity.Id, ProviderFleetId: null, Tons: capacity));
        Assert.True(resupplyResult.IsAccepted, resupplyResult.ToString());
        state = resupplyResult.State;
        Assert.Equal(capacity, state.FleetById(resuppliedFleet.Id)!.SupplyTons);

        // Run well past the lethal point the brief documents for this exact seed and scenario (turn 89
        // for a single unsupplied fleet) -- generous enough to absorb the shift in the RNG draw sequence
        // from having a second fleet drawing every turn too.
        //
        // T70 (#190 N3): the previous version of this test resupplied only once, at the start -- the
        // resupplied fleet's own 80-ton cap covers just 8 turns of consumption, so for the other 142 of
        // 150 turns it was drawing the very same zero-supply penalty as the control, ending at condition
        // 42 against a 40 death threshold, a 2-point margin. A mutation that applies the zero-supply
        // penalty to every fleet regardless of supply state was invisible to that assertion, because the
        // real code was *already* applying the penalty to the resupplied fleet for almost the whole run.
        // Topping the resupplied fleet up to its cap every turn, before that turn's own consumption,
        // means its own supply never reaches zero for the entire run, so the real code never applies the
        // penalty to it at all -- and the mutation above, which ignores supply state, still sinks it.
        const int horizonTurns = 150;
        var controlDied = false;
        for (var turn = 0; turn < horizonTurns; turn++)
        {
            var topUp = dispatcher.Dispatch(
                state,
                new BuyFleetSupplyCommand(NationId, resuppliedFleet.Id, homeCity.Id, ProviderFleetId: null, Tons: capacity));
            Assert.True(topUp.IsAccepted, topUp.ToString());
            state = topUp.State;

            state = coordinator.RunRoundTick(state).State;

            // The resupplied fleet must never disappear -- not just "at the end", every single turn.
            Assert.NotNull(state.FleetById(resuppliedFleet.Id));

            if (state.FleetById(controlFleet.Id) is null)
            {
                controlDied = true;
                break;
            }
        }

        Assert.True(controlDied, $"the unsupplied control fleet must sink within {horizonTurns} turns.");

        // Never having reached zero supply, the resupplied fleet draws no zero-supply condition penalty
        // at all, so it must still sit at (or within the storm pass's own harmless 0-1 point rounding of)
        // full condition -- a real, near-zero margin, not merely "above the death threshold" (#190 N3).
        var finalResupplied = state.FleetById(resuppliedFleet.Id)!;
        Assert.True(
            finalResupplied.ConditionPercent >= rules.MaxConditionPercent - 1,
            $"a fleet that never runs out of supply must stay essentially at full condition; ended at {finalResupplied.ConditionPercent}.");
    }
}
