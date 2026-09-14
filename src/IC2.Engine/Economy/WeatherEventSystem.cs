using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The seasonal weather-event frequency curve — <c>docs/task-catalogue.md</c>
/// "T08 Economy, supply, and purses", Done-when 8. Registers into
/// <see cref="TurnPhase.WeatherEvents"/>, the round-scoped phase T03/T06 declared for exactly this
/// (<c>FUN_00451304</c>).
/// </summary>
/// <remarks>
/// <strong>[confirmed: decompiled-weather-events.md]</strong> for the frequency: <see
/// cref="EconomyRules.Weather"/>'s odds are keyed by the pre-advance season and whether the current
/// week is in that season's "early" or "late" half (Spring and Autumn only; Summer and Winter are flat
/// all season), and the confirmed roll is made independently, every tick, for each of
/// <see cref="WeatherEventRules.LocationCount"/> tracked locations (<c>DAT_00479540</c>) — not once per
/// tick overall. Review round 1, B4: an earlier pass rolled once per tick, understating the confirmed
/// absolute frequency by that factor (the Winter/Summer <em>ratio</em> DoD 8 checks was unaffected,
/// since both seasons were understated equally). Each successful roll draws through
/// <see cref="SystemContext.Rng"/> and can fire its own event, so more than one may fire in a single
/// tick. What a fired event actually does is <strong>[designed]</strong>: the effect table is data so
/// it can grow later without an engine change, but this system does not apply any game-state change for
/// the effect it draws — only <see cref="WeatherEventFired"/> is published, naming which placeholder
/// effect was drawn. Applying an invented magnitude (fleet damage, a supply reduction) would be
/// inventing a number this task found no evidence for; a later task with real evidence for an effect's
/// magnitude is the place to add it. The locations' own identity stays <c>[open]</c>, exactly as the
/// report leaves it — <see cref="WeatherEventFired"/> carries no location.
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

        if (rules.Effects.Count == 0)
        {
            return context.State;
        }

        for (var location = 0; location < rules.LocationCount; location++)
        {
            if (!context.Rng.NextChance(numerator, denominator))
            {
                continue;
            }

            var effect = rules.Effects[context.Rng.NextInt(rules.Effects.Count)];
            context.Events.Publish(new WeatherEventFired(calendar.SeasonIndex, calendar.Week, effect.Id));
        }

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
