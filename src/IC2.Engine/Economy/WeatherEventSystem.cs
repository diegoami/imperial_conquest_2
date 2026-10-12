using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The weekly rough-sea overlay: <c>FUN_00451304</c> / <c>FUN_004511bc</c>, run in the weekly tick after
/// the fleet loop and before the calendar advances, and once at new game with (Spring, week 1)
/// <strong>[confirmed: decompiled-map-code1-overlay.md §1 and §5, research 0d2674c]</strong>. Registers
/// into <see cref="TurnPhase.WeatherEvents"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The rule.</strong> First <em>clear</em>: every launched fleet's in-rough-sea flag
/// (<see cref="FleetState.CoveredTileCode"/>) becomes <see cref="WeatherEventRules.CalmSeaTileCode"/>, and
/// every rough cell within <see cref="WeatherEventRules.ClearRadiusTiles"/> of each
/// <see cref="World.WeatherCentres"/> entry becomes calm. Then <em>roll</em>: the season and week before
/// the advance (the phase runs pre-advance, so <see cref="GameState.Calendar"/> is that) give the odds and
/// radius <c>r</c> of <see cref="WeatherEventRules.BySeason"/>; each centre, in order, hits with those odds,
/// and a hit paints the inner <c>(2r+1)²</c> square always and the ring out to <c>±2r</c> at
/// <see cref="WeatherEventRules.RingChanceNumerator"/> in <see cref="WeatherEventRules.RingChanceDenominator"/>
/// per cell. A cell is painted only when it is calm sea and has no city within
/// <see cref="WeatherEventRules.CityExclusionRadiusTiles"/>; a fleet standing on a painted cell gets its flag
/// set to <see cref="WeatherEventRules.RoughSeaTileCode"/> as well.
/// </para>
/// <para>
/// <strong>The draw order</strong> is the report's: one draw per centre (<c>Random(N) == 0</c>), then, for
/// each hit centre, one draw per outer-ring cell with <c>dx</c> the outer loop and <c>dy</c> the inner, from
/// <c>-2r</c> up. A ring cell draws whether or not it can then be painted, as the original's
/// <c>Random(2)</c> comes before <c>FUN_004511bc</c>'s tests. The report cannot confirm this order against a
/// seeded live run <strong>[derived from code]</strong>; the draws come from this system's own stream
/// (<see cref="SystemContext.Rng"/>), so no other system's roll moves.
/// </para>
/// <para>
/// <strong>A deliberate difference.</strong> The original keeps the overlay in the map grid, under the unit
/// markers, so a fleet sunk on a rough cell leaves calm sea there until the next clear. Here the overlay is
/// a set apart from the fleets, and the cell stays rough (the user's decision of 2026-10-08).
/// </para>
/// <para>
/// A world without <see cref="World.WeatherCentres"/> has no weather: the system changes nothing.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.WeatherEvents, "economy.weather-events")]
public sealed class WeatherEventSystem : IGameSystem
{
    /// <summary>The id <see cref="WeatherEventFired"/> carries when a centre's roll hits.</summary>
    public const string RoughSeaPaintedId = "rough-sea-painted";

    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Roll(
            context.State,
            context.World,
            context.Ruleset,
            context.State.Calendar.SeasonIndex,
            context.State.Calendar.Week,
            context.Rng,
            context.Events);
    }

    /// <summary>
    /// Clears and rolls the overlay once, with the odds of (<paramref name="seasonIndex"/>,
    /// <paramref name="week"/>). The weekly system passes the calendar's pre-advance values; a new game
    /// passes <see cref="WeatherEventRules.NewGameSeasonIndex"/> and <see cref="WeatherEventRules.NewGameWeek"/>.
    /// </summary>
    /// <param name="events">Where a hit centre is announced, or <see langword="null"/> for the new-game roll.</param>
    public static GameState Roll(
        GameState state, World world, Ruleset ruleset, int seasonIndex, int week, IRng rng, IEventSink? events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(rng);

        if (world.WeatherCentres is not { Count: > 0 } centres)
        {
            return state;
        }

        var rules = ruleset.Economy.Weather;

        // ---- 1. Clear: every fleet's flag, then every rough cell near a centre.
        var fleets = new List<FleetState>(state.Fleets.Count);
        foreach (var fleet in state.Fleets)
        {
            fleets.Add(fleet.CoveredTileCode is null ? fleet : fleet with { CoveredTileCode = rules.CalmSeaTileCode });
        }

        var rough = new HashSet<(int X, int Y)>();
        if (state.RoughSeaCells is { } existing)
        {
            foreach (var cell in existing)
            {
                rough.Add((cell.X, cell.Y));
            }
        }

        foreach (var centre in centres)
        {
            for (var x = centre.X - rules.ClearRadiusTiles; x <= centre.X + rules.ClearRadiusTiles; x++)
            {
                for (var y = centre.Y - rules.ClearRadiusTiles; y <= centre.Y + rules.ClearRadiusTiles; y++)
                {
                    rough.Remove((x, y));
                }
            }
        }

        // ---- 2. Roll: (N, r) from the season and week as passed, then each centre in order.
        var (numerator, denominator, radius) = OddsFor(rules, seasonIndex, week);
        var terrain = world.Terrain.Decode(world.Width, world.Height);

        void Paint(int x, int y)
        {
            if ((uint)x >= (uint)world.Width || (uint)y >= (uint)world.Height)
            {
                return;
            }

            foreach (var city in state.Cities)
            {
                if (Math.Abs(city.X - x) <= rules.CityExclusionRadiusTiles
                    && Math.Abs(city.Y - y) <= rules.CityExclusionRadiusTiles)
                {
                    return;
                }
            }

            if (terrain[(y * world.Width) + x] != rules.CalmSeaTileCode)
            {
                return;
            }

            rough.Add((x, y));

            for (var i = 0; i < fleets.Count; i++)
            {
                var fleet = fleets[i];
                if (!fleet.IsUnderConstruction && fleet.X == x && fleet.Y == y)
                {
                    fleets[i] = fleet with { CoveredTileCode = rules.RoughSeaTileCode };
                }
            }
        }

        for (var k = 0; k < centres.Count; k++)
        {
            if (!rng.NextChance(numerator, denominator))
            {
                continue;
            }

            events?.Publish(new WeatherEventFired(seasonIndex, week, RoughSeaPaintedId));

            var centre = centres[k];
            var reach = 2 * radius;
            for (var dx = -reach; dx <= reach; dx++)
            {
                for (var dy = -reach; dy <= reach; dy++)
                {
                    var inner = Math.Abs(dx) <= radius && Math.Abs(dy) <= radius;
                    if (inner || rng.NextChance(rules.RingChanceNumerator, rules.RingChanceDenominator))
                    {
                        Paint(centre.X + dx, centre.Y + dy);
                    }
                }
            }
        }

        var ordered = rough
            .OrderBy(c => c.X)
            .ThenBy(c => c.Y)
            .Select(c => new RoughSeaCell(c.X, c.Y));

        return state with { Fleets = ValueList.From(fleets), RoughSeaCells = ValueList.From(ordered) };
    }

    private static (int Numerator, int Denominator, int Radius) OddsFor(WeatherEventRules rules, int seasonIndex, int week)
    {
        foreach (var entry in rules.BySeason)
        {
            if (entry.SeasonIndex != seasonIndex)
            {
                continue;
            }

            return week < rules.EarlyLateWeekThreshold
                ? (entry.EarlyNumerator, entry.EarlyDenominator, entry.EarlyRadiusTiles)
                : (entry.LateNumerator, entry.LateDenominator, entry.LateRadiusTiles);
        }

        throw new ArgumentOutOfRangeException(
            nameof(seasonIndex), seasonIndex, "No weather odds entry for this season in the loaded ruleset.");
    }
}
