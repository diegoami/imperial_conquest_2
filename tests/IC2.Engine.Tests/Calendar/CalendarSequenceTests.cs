using IC2.Engine.Core;
using Xunit;

namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// DoD 1-3: the week/season/year sequence over 48 rounds, the quarterly hook's 4-per-year firing rate,
/// and the year decrementing only at the Winter-&gt;Spring wrap.
/// </summary>
/// <remarks>
/// "Turn" throughout these tests means one completed round -- one call to
/// <see cref="TurnCoordinator.RunRoundTick"/>, the original's own global weekly tick
/// (<c>FUN_004514ec</c>) -- matching <c>docs/investigations/thracia-supply-morale.md</c>'s "one save =
/// one turn = two weeks" and the convention DoD 6 itself uses ("over a 12-turn run"). Calling
/// <see cref="TurnCoordinator.RunRoundTick"/> directly (rather than driving it through
/// <see cref="TurnCoordinator.RunTurn"/> once per seat) isolates the calendar/attrition mechanics DoD
/// 1-3 and 6-7 are about from seat rotation, which DoD 4 tests separately -- and it is exactly the entry
/// point <see cref="TurnCoordinator"/>'s own remarks say exists so a round-scoped system "can be tested
/// before the calendar that would normally trigger it exists".
/// </remarks>
public sealed class CalendarSequenceTests
{
    /// <summary>
    /// DoD 1: advancing 48 rounds from the shipped start produces a week/season/year sequence byte-equal
    /// to the hand-computed fixture in <c>expected-calendar-sequence.json</c> (reproduced in the PR body).
    /// </summary>
    [Fact]
    public void AdvancingFortyEightRoundsMatchesHandComputedSequence()
    {
        var coordinator = CalendarTestbed.CoordinatorFor("calendar.sequence-only");
        var state = CalendarTestbed.InitialState();

        var actual = new List<ExpectedCalendarEntry>();
        for (var turn = 1; turn <= 48; turn++)
        {
            var result = coordinator.RunRoundTick(state);
            state = result.State;
            actual.Add(new ExpectedCalendarEntry(turn, state.Calendar.Week, state.Calendar.SeasonIndex, state.Calendar.YearBc));
        }

        Assert.Equal(ExpectedCalendarSequence.Entries, actual);
    }

    /// <summary>
    /// DoD 2: the quarterly hook fires exactly 4 times in each of the two in-game years the 48-round run
    /// covers (8 times total) -- asserted by counting published <see cref="QuarterBoundaryObserved"/>
    /// events, never by inspecting a side effect.
    /// </summary>
    [Fact]
    public void QuarterlyHookFiresExactlyFourTimesPerYear()
    {
        var coordinator = CalendarTestbed.CoordinatorFor(QuarterCountFixtures.Group);
        var state = CalendarTestbed.InitialState();

        var firingsByRound = new List<int>();
        for (var turn = 1; turn <= 48; turn++)
        {
            var result = coordinator.RunRoundTick(state);
            state = result.State;

            var firedThisRound = result.Events.Count(e => e is QuarterBoundaryObserved);
            Assert.True(firedThisRound is 0 or 1, $"Round {turn} published {firedThisRound} quarter boundaries; expected 0 or 1.");
            firingsByRound.Add(firedThisRound);
        }

        var firstYear = firingsByRound.Take(24).Sum();
        var secondYear = firingsByRound.Skip(24).Take(24).Sum();

        Assert.Equal(4, firstYear);
        Assert.Equal(4, secondYear);
        Assert.Equal(8, firingsByRound.Sum());

        // And it fires on exactly the rounds the hand-computed sequence says wrap: 6, 12, 18, 24, 30,
        // 36, 42, 48 (every round whose OWN week input is 11, i.e. every 6th round).
        var expectedFiringRounds = new[] { 6, 12, 18, 24, 30, 36, 42, 48 };
        var actualFiringRounds = firingsByRound
            .Select((fired, index) => (Turn: index + 1, Fired: fired))
            .Where(t => t.Fired == 1)
            .Select(t => t.Turn)
            .ToArray();

        Assert.Equal(expectedFiringRounds, actualFiringRounds);
    }

    /// <summary>
    /// DoD 3: the year decrements only at the Winter-&gt;Spring wrap. Walks every one of the eight season
    /// wraps in the 48-round run and asserts the year is unchanged on the other three of every four.
    /// </summary>
    [Fact]
    public void YearDecrementsOnlyAtWinterToSpringWrap()
    {
        var coordinator = CalendarTestbed.CoordinatorFor("calendar.sequence-only");
        var state = CalendarTestbed.InitialState();

        var previousYear = state.Calendar.YearBc;
        var previousSeason = state.Calendar.SeasonIndex;
        var wrapsChecked = 0;

        for (var turn = 1; turn <= 48; turn++)
        {
            var result = coordinator.RunRoundTick(state);
            state = result.State;

            // SeasonIndex only ever changes on the round that wraps the week (CalendarSystem leaves it
            // untouched on every other round), so "did the season change" and "did this round wrap" are
            // the same question.
            var didWrap = state.Calendar.SeasonIndex != previousSeason;

            if (didWrap)
            {
                wrapsChecked++;
                var isWinterToSpring = state.Calendar.SeasonIndex == 0;
                if (isWinterToSpring)
                {
                    Assert.Equal(previousYear - 1, state.Calendar.YearBc);
                }
                else
                {
                    Assert.Equal(previousYear, state.Calendar.YearBc);
                }

                previousYear = state.Calendar.YearBc;
            }
            else
            {
                Assert.Equal(previousYear, state.Calendar.YearBc);
            }

            previousSeason = state.Calendar.SeasonIndex;
        }

        // Eight season wraps over the 48-round run (one every six rounds); exactly two of them (every
        // fourth) are the Winter->Spring wrap that decrements the year -- verified above, one round at a
        // time, rather than only by this count.
        Assert.Equal(8, wrapsChecked);
    }
}
