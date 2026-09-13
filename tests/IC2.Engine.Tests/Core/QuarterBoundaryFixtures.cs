using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Stand-ins for the three real subscribers to the quarter boundary — T08's quarterly billing, T19's
/// diplomatic thaw — written so the hook can be exercised with no calendar implementation present.
/// </summary>
public static class QuarterBoundaryFixtures
{
    /// <summary>Subscribers fired directly, with nothing registered in any phase.</summary>
    public const string Group = "quarter";

    /// <summary>A subscriber plus the system that fires the hook from inside its own phase.</summary>
    public const string FiredFromPhaseGroup = "quarter-from-phase";
}

/// <summary>Charges quarterly ship upkeep, the way T08's economy will.</summary>
[TestFixtureGroup(QuarterBoundaryFixtures.Group)]
[QuarterBoundaryHandler("test.quarter.upkeep", Order = 10)]
public sealed class QuarterUpkeepHandler : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        var perShip = context.Ruleset.Economy.ShipUpkeepPerQuarter;

        var nations = context.State.Nations.Select(nation =>
        {
            var ships = 0;
            foreach (var fleet in context.State.Fleets)
            {
                if (string.Equals(fleet.Nation, nation.Id, StringComparison.Ordinal))
                {
                    ships += fleet.Ships;
                }
            }

            return nation with { Treasury = nation.Treasury - (ships * perShip) };
        });

        context.Events.Publish(new TestQuarterBilled(context.EndingSeasonIndex));
        return context.State with { Nations = ValueList.From(nations) };
    }
}

/// <summary>Nudges unity by a drawn amount, the way T19's thaw will draw its 1-in-3 improvement.</summary>
[TestFixtureGroup(QuarterBoundaryFixtures.Group)]
[QuarterBoundaryHandler("test.quarter.thaw", Order = 20)]
public sealed class QuarterThawHandler : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        var cap = context.Ruleset.Economy.UnityCap;
        var nations = context.State.Nations.Select(nation =>
            nation with { Unity = context.Rng.NextInt(cap + 1) });

        return context.State with { Nations = ValueList.From(nations) };
    }
}

/// <summary>
/// Stands in for T06's calendar: fires the hook from inside <see cref="TurnPhase.CalendarAdvance"/>,
/// before it would advance the season counter, exactly as
/// <c>decompiled-quarterly-billing-and-economy.md</c> describes <c>FUN_00451b40</c> being called.
/// </summary>
[TestFixtureGroup(QuarterBoundaryFixtures.FiredFromPhaseGroup)]
[GameSystem(TurnPhase.CalendarAdvance, "test.quarter.calendar")]
public sealed class QuarterFiringCalendarSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var calendar = context.Ruleset.Calendar;
        var wraps = context.State.Calendar.Week == calendar.SeasonAdvanceFromWeek;
        var week = (context.State.Calendar.Week + calendar.WeekStep) % calendar.WeekModulus;

        if (!wraps)
        {
            return context.State with { Calendar = context.State.Calendar with { Week = week } };
        }

        // The hook fires while the season counter still reads the season that is ending.
        var endingSeason = context.State.Calendar.SeasonIndex;
        var afterBoundary = context.QuarterBoundary.Fire(context.State, endingSeason);

        return afterBoundary with
        {
            Calendar = afterBoundary.Calendar with
            {
                Week = week,
                SeasonIndex = (endingSeason + 1) % calendar.SeasonsPerYear,
            },
        };
    }
}

/// <summary>
/// A subscriber in the group whose hook is fired from inside a phase. It <em>draws</em>, so that the
/// direct and in-pipeline firing paths can be compared on the values they produce rather than only on the
/// fact that both ran.
/// </summary>
[TestFixtureGroup(QuarterBoundaryFixtures.FiredFromPhaseGroup)]
[QuarterBoundaryHandler("test.quarter.from-phase")]
public sealed class QuarterFromPhaseHandler : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        var cap = context.Ruleset.Economy.UnityCap;
        var nations = context.State.Nations.Select(nation =>
            nation with { Unity = context.Rng.NextInt(cap + 1) });

        context.Events.Publish(new TestQuarterBilled(context.EndingSeasonIndex));
        return context.State with { Nations = ValueList.From(nations) };
    }
}

/// <summary>A quarter's billing ran, for the season that was ending.</summary>
[DomainEvent("test.quarter-billed", NewsWorthy = true)]
public sealed record TestQuarterBilled(int EndingSeasonIndex) : DomainEvent;
