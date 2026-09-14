using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The seasonal weather-event frequency curve — <c>docs/build-orchestration-plan.md</c>
/// "T08 Economy, supply, and purses", Done-when 8. Registers into
/// <see cref="TurnPhase.WeatherEvents"/>, the round-scoped phase T03/T06 declared for exactly this
/// (<c>FUN_00451304</c>).
/// </summary>
/// <remarks>
/// <strong>[confirmed: decompiled-weather-events.md]</strong> for the frequency: one
/// <c>numerator</c>-in-<c>denominator</c> roll per round, drawn through <see cref="SystemContext.Rng"/>,
/// with odds keyed by the pre-advance season and whether the current week is in that season's "early" or
/// "late" half (Spring and Autumn only; Summer and Winter are flat all season) —
/// <see cref="EconomyRules.Weather"/>. What a fired event actually does is <strong>[designed]</strong>: the
/// effect table is data so it can grow later without an engine change, but this system does not apply any
/// game-state change for the effect it draws — only <see cref="WeatherEventFired"/> is published, naming
/// which placeholder effect was drawn. Applying an invented magnitude (fleet damage, a supply reduction)
/// would be inventing a number this task found no evidence for; a later task with real evidence for an
/// effect's magnitude is the place to add it.
/// </remarks>
[GameSystem(TurnPhase.WeatherEvents, "economy.weather-events")]
public sealed class WeatherEventSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rules = context.Ruleset.Economy.Weather;
        var calendar = context.State.Calendar;
        var odds = OddsFor(rules, calendar.SeasonIndex);
        var isEarly = calendar.Week < rules.EarlyLateWeekThreshold;
        var numerator = isEarly ? odds.EarlyNumerator : odds.LateNumerator;
        var denominator = isEarly ? odds.EarlyDenominator : odds.LateDenominator;

        if (!context.Rng.NextChance(numerator, denominator) || rules.Effects.Count == 0)
        {
            return context.State;
        }

        var effect = rules.Effects[context.Rng.NextInt(rules.Effects.Count)];
        context.Events.Publish(new WeatherEventFired(calendar.SeasonIndex, calendar.Week, effect.Id));
        return context.State;
    }

    private static WeatherSeasonOdds OddsFor(WeatherEventRules rules, int seasonIndex)
    {
        foreach (var entry in rules.BySeason)
        {
            if (entry.SeasonIndex == seasonIndex)
            {
                return entry;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(seasonIndex), seasonIndex, "No weather odds entry for this season in the loaded ruleset.");
    }
}
