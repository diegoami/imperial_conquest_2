using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// A quarter-boundary subscriber whose only job is to be counted, so DoD 2 ("the quarterly hook fires
/// exactly 4 times per in-game year") is asserted by counting published events, not by inspecting state
/// that some other system happened to change.
/// </summary>
public static class QuarterCountFixtures
{
    /// <summary>The fixture group this handler belongs to.</summary>
    public const string Group = "calendar.quarter-count";
}

/// <inheritdoc cref="QuarterCountFixtures"/>
[TestFixtureGroup(QuarterCountFixtures.Group)]
[QuarterBoundaryHandler("test.calendar.quarter-count")]
public sealed class QuarterCountHandler : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        context.Events.Publish(new QuarterBoundaryObserved(context.EndingSeasonIndex));
        return context.State;
    }
}

/// <summary>One quarter boundary observed, naming the season that was ending.</summary>
[DomainEvent("test.calendar.quarter-boundary-observed")]
public sealed record QuarterBoundaryObserved(int EndingSeasonIndex) : DomainEvent;
