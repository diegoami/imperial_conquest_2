using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Definition of Done item 5: "The <c>OnQuarterBoundary</c> hook can be fired directly in a test without
/// a calendar implementation present."
/// </summary>
/// <remarks>
/// The registry these tests build contains subscribers and <em>no system in any phase at all</em>, so
/// there is demonstrably no calendar in the pipeline. That is the situation T08's economy and T19's thaw
/// will be written in: both merge before the work that would normally trigger them is wired up.
/// </remarks>
public class QuarterBoundaryTests
{
    [Fact]
    public void The_hook_fires_directly_with_no_calendar_present()
    {
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorFor(QuarterBoundaryFixtures.Group, sink);
        var registry = CoreTestbed.RegistryFor(QuarterBoundaryFixtures.Group);

        // No system is registered in any phase: nothing here can advance a calendar.
        Assert.Empty(registry.Systems);
        Assert.Equal(2, registry.QuarterBoundaryHandlers.Count);

        var before = CoreTestbed.InitialState();
        var endingSeason = CoreTestbed.Toy.Ruleset.Calendar.StartSeasonIndex;

        var after = coordinator.FireQuarterBoundary(before, endingSeason);

        var perShip = CoreTestbed.Toy.Ruleset.Economy.ShipUpkeepPerQuarter;
        foreach (var nation in after.Nations)
        {
            var ships = before.Fleets
                .Where(fleet => string.Equals(fleet.Nation, nation.Id, StringComparison.Ordinal))
                .Sum(fleet => fleet.Ships);

            Assert.Equal(before.NationById(nation.Id)!.Treasury - (ships * perShip), nation.Treasury);
        }

        var billed = Assert.IsType<TestQuarterBilled>(Assert.Single(sink.Events));
        Assert.Equal(endingSeason, billed.EndingSeasonIndex);
    }

    [Fact]
    public void Subscribers_run_in_declared_order()
    {
        var registry = CoreTestbed.RegistryFor(QuarterBoundaryFixtures.Group);

        Assert.Equal(
            new[] { "test.quarter.upkeep", "test.quarter.thaw" },
            registry.QuarterBoundaryHandlers.Select(handler => handler.Id).ToArray());
    }

    [Fact]
    public void Firing_the_hook_twice_on_the_same_state_produces_the_same_result()
    {
        var coordinator = CoreTestbed.CoordinatorFor(QuarterBoundaryFixtures.Group);
        var before = CoreTestbed.InitialState();
        var endingSeason = CoreTestbed.Toy.Ruleset.Calendar.StartSeasonIndex;

        var first = coordinator.FireQuarterBoundary(before, endingSeason);
        var second = coordinator.FireQuarterBoundary(before, endingSeason);

        Assert.Equal(GameStateHash.Compute(first), GameStateHash.Compute(second));
    }

    [Fact]
    public void Each_season_of_a_year_draws_independently()
    {
        var coordinator = CoreTestbed.CoordinatorFor(QuarterBoundaryFixtures.Group);
        var before = CoreTestbed.InitialState();

        var hashes = new List<string>();
        for (var season = 0; season < CoreTestbed.Toy.Ruleset.Calendar.SeasonsPerYear; season++)
        {
            hashes.Add(GameStateHash.Compute(coordinator.FireQuarterBoundary(before, season)));
        }

        // The ending season is folded into each subscriber's stream, so the four boundaries of one year
        // do not replay the same rolls even where the root value has not moved between them.
        Assert.Equal(hashes.Count, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Firing_the_hook_with_no_subscribers_changes_nothing()
    {
        var coordinator = new TurnCoordinator(
            SystemRegistry.Empty, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);

        var before = CoreTestbed.InitialState();

        Assert.Same(before, coordinator.FireQuarterBoundary(before, endingSeasonIndex: 0));
    }

    [Fact]
    public void A_calendar_system_can_fire_the_hook_from_inside_its_own_phase()
    {
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorFor(QuarterBoundaryFixtures.FiredFromPhaseGroup, sink);
        var calendar = CoreTestbed.Toy.Ruleset.Calendar;

        var state = CoreTestbed.InitialState();

        // Walk the confirmed week cycle until the wrap that fires the boundary. The fixture calendar
        // system runs in a round-scoped phase, so RunRoundTick is what drives it.
        var boundaryFired = false;
        for (var tick = 0; tick < calendar.WeekModulus && !boundaryFired; tick++)
        {
            var seasonBefore = state.Calendar.SeasonIndex;
            state = coordinator.RunRoundTick(state).State;
            boundaryFired = state.Calendar.SeasonIndex != seasonBefore;
        }

        Assert.True(boundaryFired, "The fixture calendar never reached a season boundary.");

        var billed = Assert.IsType<TestQuarterBilled>(Assert.Single(sink.Events));

        // The subscriber saw the season that was ending, not the one that had already begun.
        Assert.Equal(calendar.StartSeasonIndex, billed.EndingSeasonIndex);
        Assert.NotEqual(billed.EndingSeasonIndex, state.Calendar.SeasonIndex);
    }
}
