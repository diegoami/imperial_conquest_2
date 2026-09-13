using IC2.Engine.Core;
using Xunit;

namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// DoD 6-7: the turn order places army and fleet attrition before the calendar advance, every round,
/// observing the season that has not yet advanced -- and the attrition phase is a declared, named phase
/// a system can register into with neither T08 nor T14 present.
/// </summary>
/// <remarks>
/// "Turn" here means one round -- see <see cref="CalendarSequenceTests"/>'s remarks for why. The two
/// probe systems in <see cref="AttritionProbeFixtures"/> stand in for T08's supply/morale rule
/// (<see cref="TurnPhase.ArmyTick"/>) and T14's fleet attrition (<see cref="TurnPhase.FleetTick"/>);
/// neither task exists in this codebase, which is itself part of what DoD 7 requires this test to show.
/// </remarks>
public sealed class AttritionPhaseOrderingTests
{
    /// <summary>
    /// DoD 7: the attrition phases are declared, named phases in T03's ordered pipeline that a system
    /// can register into with nothing else present -- not an implicit side effect of another phase.
    /// </summary>
    [Fact]
    public void AttritionPhasesAcceptRegistrationWithNeitherT08NorT14Present()
    {
        var registry = CalendarTestbed.RegistryFor(AttritionProbeFixtures.Group);

        var armyTickSystems = registry.InPhase(TurnPhase.ArmyTick);
        var fleetTickSystems = registry.InPhase(TurnPhase.FleetTick);

        var armySystem = Assert.Single(armyTickSystems);
        Assert.Equal("test.calendar.attrition-probe.army", armySystem.Id);
        Assert.IsType<ArmyAttritionProbeSystem>(armySystem.Instance);

        var fleetSystem = Assert.Single(fleetTickSystems);
        Assert.Equal("test.calendar.attrition-probe.fleet", fleetSystem.Id);
        Assert.IsType<FleetAttritionProbeSystem>(fleetSystem.Instance);
    }

    /// <summary>
    /// DoD 6: over a 12-round run, both probes fire exactly 12 times each, both always before
    /// <c>calendar.advance-week</c> in the trace, and each firing observes the season the round started
    /// with -- the one the original's tick actually consumed -- not the season the same round's calendar
    /// advance moves on to.
    /// </summary>
    [Fact]
    public void AttritionFiresBeforeCalendarAdvanceEveryRoundObservingThePreAdvanceSeason()
    {
        var coordinator = CalendarTestbed.CoordinatorFor(AttritionProbeFixtures.Group);
        var state = CalendarTestbed.InitialState();

        var armyFirings = 0;
        var fleetFirings = 0;

        for (var round = 1; round <= 12; round++)
        {
            var beforeRound = state.Calendar;
            var result = coordinator.RunRoundTick(state);

            var trace = result.Trace;
            var armyIndex = IndexOfSystem(trace, "test.calendar.attrition-probe.army");
            var fleetIndex = IndexOfSystem(trace, "test.calendar.attrition-probe.fleet");
            var calendarIndex = IndexOfSystem(trace, "calendar.advance-week");

            Assert.True(armyIndex >= 0, $"Round {round}: the army probe did not run.");
            Assert.True(fleetIndex >= 0, $"Round {round}: the fleet probe did not run.");
            Assert.True(calendarIndex >= 0, $"Round {round}: the calendar advance did not run.");
            Assert.True(armyIndex < calendarIndex, $"Round {round}: army attrition must run before the calendar advance.");
            Assert.True(fleetIndex < calendarIndex, $"Round {round}: fleet attrition must run before the calendar advance.");

            var armyFired = Assert.Single(result.Events.OfType<ArmyAttritionProbeFired>());
            var fleetFired = Assert.Single(result.Events.OfType<FleetAttritionProbeFired>());
            armyFirings++;
            fleetFirings++;

            // The probes must observe the round's PRE-advance calendar (the season the original's tick
            // actually consumed that round), not the state the same round's calendar advance produces --
            // this is DoD 6's "season the attrition phase observes is the season before that turn's
            // advance", and the one T08's per-season figures are wrong by 7x on if it is backwards.
            Assert.Equal(beforeRound.Week, armyFired.ObservedWeek);
            Assert.Equal(beforeRound.SeasonIndex, armyFired.ObservedSeasonIndex);
            Assert.Equal(beforeRound.YearBc, armyFired.ObservedYearBc);
            Assert.Equal(beforeRound.Week, fleetFired.ObservedWeek);
            Assert.Equal(beforeRound.SeasonIndex, fleetFired.ObservedSeasonIndex);
            Assert.Equal(beforeRound.YearBc, fleetFired.ObservedYearBc);

            // Cross-check against the same hand-computed sequence DoD 1 uses: the pre-advance season for
            // round N is the sequence's own entry for round N-1 (or the shipped start, for round 1).
            var expectedPreAdvance = round == 1
                ? (Week: CalendarTestbed.Toy.Ruleset.Calendar.StartWeek, Season: CalendarTestbed.Toy.Ruleset.Calendar.StartSeasonIndex, Year: CalendarTestbed.Toy.Ruleset.Calendar.StartYearBc)
                : (Week: ExpectedCalendarSequence.Entries[round - 2].Week, Season: ExpectedCalendarSequence.Entries[round - 2].SeasonIndex, Year: ExpectedCalendarSequence.Entries[round - 2].YearBc);

            Assert.Equal(expectedPreAdvance.Week, beforeRound.Week);
            Assert.Equal(expectedPreAdvance.Season, beforeRound.SeasonIndex);
            Assert.Equal(expectedPreAdvance.Year, beforeRound.YearBc);

            state = result.State;
        }

        Assert.Equal(12, armyFirings);
        Assert.Equal(12, fleetFirings);

        // Round 6 of this 12-round run is a Spring->Summer wrap (see expected-calendar-sequence.json,
        // turn 6): the probe on that round must still see Spring (week 11, season 0), not Summer.
        // Re-run is unnecessary -- already checked turn-by-turn above -- this is a plain-language
        // restatement of the DoD line for a reviewer scanning this file.
    }

    private static int IndexOfSystem(IReadOnlyList<SystemExecution> trace, string systemId)
    {
        for (var i = 0; i < trace.Count; i++)
        {
            if (string.Equals(trace[i].SystemId, systemId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
