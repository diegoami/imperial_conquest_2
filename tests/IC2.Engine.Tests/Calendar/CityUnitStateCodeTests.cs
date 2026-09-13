using IC2.Engine.Calendar;
using Xunit;

namespace IC2.Engine.Tests.Calendar;

/// <summary>DoD 5: <c>StateCode</c> reaches 24 and stays there -- a cap, not a reset.</summary>
public sealed class CityUnitStateCodeTests
{
    private static IC2.Engine.Model.CalendarRules Rules => CalendarTestbed.Toy.Ruleset.Calendar;

    [Fact]
    public void AdvanceStepsByTheRulesetIncrement()
    {
        Assert.Equal(Rules.CityUnitStateCodeStep, CityUnitStateCode.Advance(0, Rules));
        Assert.Equal(Rules.CityUnitStateCodeStep * 2, CityUnitStateCode.Advance(Rules.CityUnitStateCodeStep, Rules));
    }

    [Fact]
    public void AdvanceReachesTheCapAndHoldsThereRatherThanResetting()
    {
        var value = 0;
        var reachedCap = false;

        // Twenty weeks is comfortably more than enough to reach the cap (24, stepping by 2) and prove it
        // holds rather than wrapping back down.
        for (var week = 0; week < 20; week++)
        {
            var previous = value;
            value = CityUnitStateCode.Advance(value, Rules);

            Assert.True(value >= previous, "StateCode must never decrease week over week.");
            Assert.True(value <= Rules.CityUnitStateCodeCap, "StateCode must never exceed the cap.");

            if (value == Rules.CityUnitStateCodeCap)
            {
                reachedCap = true;
            }
            else
            {
                Assert.False(reachedCap, "Once StateCode has reached the cap, it must never fall below it again.");
            }
        }

        Assert.True(reachedCap, "Twenty weeks of stepping by the ruleset's increment must reach the cap.");
        Assert.Equal(Rules.CityUnitStateCodeCap, value);

        // One more week at the cap: still the cap, not a reset back to zero or the step value.
        Assert.Equal(Rules.CityUnitStateCodeCap, CityUnitStateCode.Advance(value, Rules));
    }

    [Fact]
    public void AdvanceClampsAValueAlreadyAboveTheCap()
    {
        // Defensive: a value that somehow arrives above the cap (e.g. a ruleset change) is clamped down
        // to the cap on the very next step rather than climbing further.
        var aboveCap = Rules.CityUnitStateCodeCap + Rules.CityUnitStateCodeStep;
        Assert.Equal(Rules.CityUnitStateCodeCap, CityUnitStateCode.Advance(aboveCap, Rules));
    }
}
