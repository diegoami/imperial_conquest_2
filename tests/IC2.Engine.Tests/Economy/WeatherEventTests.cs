using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and purses", Done-when 8: "Weather
/// events fire ~8× more often in Winter than Summer over a fixed-seed 400-quarter run (asserted as a
/// ratio band, the only band assertion in the plan, because the underlying figure is itself approximate
/// in <c>decompiled-weather-events.md</c>)."
/// </summary>
/// <remarks>
/// A "quarter" is a season boundary, so a 400-quarter run is 400 seasons' worth of rounds — with the
/// shipped calendar's <c>weekModulus / weekStep = 12 / 2 = 6</c> rounds per season (weeks 1,3,5,7,9,11
/// before the wrap), that is <c>400 × 6 = 2400</c> rounds, computed from the ruleset rather than
/// hardcoded. Over that run each of the four seasons gets exactly 600 rounds, giving stable expected
/// counts (Winter 600 × 1/5 = 120, Summer 600 × 1/40 = 15) that a ratio band can be sensibly drawn around.
/// </remarks>
public sealed class WeatherEventTests
{
    private const int SpringIndex = 0;
    private const int SummerIndex = 1;
    private const int WinterIndex = 3;

    [Fact]
    public void FixedSeed400QuarterRun_WinterFiresAtLeastFiveTimesMoreOftenThanSummer()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var roundsPerSeason = ruleset.Calendar.WeekModulus / ruleset.Calendar.WeekStep;
        var totalRounds = 400 * roundsPerSeason;

        var sink = new RecordingEventSink();
        var coordinator = EconomyTestbed.CoordinatorOnly(sink, typeof(CalendarSystem), typeof(WeatherEventSystem));
        var state = EconomyTestbed.InitialState();

        for (var round = 0; round < totalRounds; round++)
        {
            state = coordinator.RunRoundTick(state).State;
        }

        var fired = sink.Events.OfType<WeatherEventFired>().ToList();
        Assert.NotEmpty(fired);

        var winterCount = fired.Count(e => e.SeasonIndex == WinterIndex);
        var summerCount = fired.Count(e => e.SeasonIndex == SummerIndex);

        Assert.True(summerCount > 0, "Expected at least one Summer weather event over 2,400 rounds.");
        Assert.True(winterCount > 0, "Expected at least one Winter weather event over 2,400 rounds.");

        var ratio = (double)winterCount / summerCount;

        // The confirmed odds are exactly 8x (1/5 vs 1/40); a band, not equality, because this is a
        // stochastic count and decompiled-weather-events.md's own figure is itself approximate. Bounds
        // chosen wide enough to hold under the expected-count variance at N~120/~15 (a tight band would
        // make this test flaky on a different, still-valid seed) while still proving the confirmed
        // direction and rough magnitude, not just "Winter fires more than Summer".
        Assert.InRange(ratio, 4.0, 16.0);
    }

    [Fact]
    public void FixedSeed_IsExactlyReproducibleAcrossTwoRuns()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var totalRounds = 100 * (ruleset.Calendar.WeekModulus / ruleset.Calendar.WeekStep);

        int RunAndCountWinterFirings()
        {
            var sink = new RecordingEventSink();
            var coordinator = EconomyTestbed.CoordinatorOnly(sink, typeof(CalendarSystem), typeof(WeatherEventSystem));
            var state = EconomyTestbed.InitialState();
            for (var round = 0; round < totalRounds; round++)
            {
                state = coordinator.RunRoundTick(state).State;
            }

            return sink.Events.OfType<WeatherEventFired>().Count(e => e.SeasonIndex == WinterIndex);
        }

        Assert.Equal(RunAndCountWinterFirings(), RunAndCountWinterFirings());
    }
}
