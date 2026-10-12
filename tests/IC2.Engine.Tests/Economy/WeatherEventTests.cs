using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Import;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// T81 Done-when 3 and 4: the weekly rough-sea overlay is the original's rule
/// (<c>decompiled-map-code1-overlay.md</c> §1 and §5, <c>FUN_00451304</c> / <c>FUN_004511bc</c>), at the
/// tick position the report gives, and a new game rolls it once with (Spring, week 1). Each test pins one
/// part of the rule and fails if that part is deleted: the clear, the inner square, the ring chance over a
/// fixed seed, the city exclusion, the calm-sea-only rule, the fleet flag, the pre-advance odds, and the
/// new-game roll. Runs on the real committed classical world, whose 20 centres and terrain are the
/// original's.
/// </summary>
public sealed class WeatherEventTests
{
    private const int WinterIndex = 3;
    private const int SpringIndex = 0;

    // Winter's radius is 5 (inner 11x11, ring out to 10). The hit odds below are 1-in-1000 for the scripted
    // tests so a draw's denominator tells a hit roll (1000) from a ring roll (2).
    private const int ScriptedHitDenominator = 1000;
    private const int RingDenominator = 2;

    private static World World => RealGameData.World;

    private static Ruleset Classical => RealGameData.Ruleset;

    /// <summary>The initial classical state with an empty overlay, so a test starts from calm sea.</summary>
    private static GameState CleanState()
    {
        var initial = GameStateFactory.CreateInitial(World, Classical, RealGameData.Scenario);
        return initial with { RoughSeaCells = ValueList<RoughSeaCell>.Empty };
    }

    /// <summary>A ruleset whose every season hits with <paramref name="numerator"/> in <paramref name="denominator"/>.</summary>
    private static Ruleset WithHitOdds(int numerator, int denominator) =>
        Classical with
        {
            Economy = Classical.Economy with
            {
                Weather = Classical.Economy.Weather with
                {
                    BySeason = ValueList.From(Classical.Economy.Weather.BySeason.Select(o => o with
                    {
                        EarlyNumerator = numerator,
                        EarlyDenominator = denominator,
                        LateNumerator = numerator,
                        LateDenominator = denominator,
                    })),
                },
            },
        };

    private static World WithCentres(params WeatherCentre[] centres) =>
        World with { WeatherCentres = ValueList.From(centres) };

    private static readonly Lazy<int[]> TerrainCells = new(() => World.Terrain.Decode(World.Width, World.Height));

    /// <summary>Whether a hit may paint (x, y) at all: in the map, calm sea, and no city in its 3x3.</summary>
    private static bool Eligible(GameState state, int x, int y) =>
        x >= 0 && y >= 0 && x < World.Width && y < World.Height
        && TerrainCells.Value[(y * World.Width) + x] == Classical.Economy.Weather.CalmSeaTileCode
        && !state.Cities.Any(c => Math.Abs(c.X - x) <= 1 && Math.Abs(c.Y - y) <= 1);

    private static GameState Roll(GameState state, World world, Ruleset ruleset, int season, int week, IRng rng) =>
        WeatherEventSystem.Roll(state, world, ruleset, season, week, rng, events: null);

    // ------------------------------------------------------------------------------ the clear

    [Fact]
    public void The_weekly_clear_calms_every_rough_cell_within_ten_tiles_of_a_centre_and_leaves_the_rest()
    {
        var centre = new WeatherCentre(74, 50);
        var inside = new RoughSeaCell(centre.X + 10, centre.Y - 10); // the clear box's far corner
        var outside = new RoughSeaCell(centre.X + 11, centre.Y);     // one tile beyond it
        var state = CleanState() with { RoughSeaCells = ValueList.From(new[] { inside, outside }) };

        // 0-in-N: no centre ever hits, so only the clear acts.
        var after = Roll(state, WithCentres(centre), WithHitOdds(0, ScriptedHitDenominator), WinterIndex, 5, new SplitMix64Rng(1));

        Assert.DoesNotContain(inside, after.RoughSeaCells!);
        Assert.Contains(outside, after.RoughSeaCells!);
    }

    [Fact]
    public void The_weekly_clear_resets_every_launched_fleets_flag_and_leaves_a_fleet_under_construction_alone()
    {
        var state = CleanState();
        var launched = state.Fleets[0] with { CoveredTileCode = 1 };
        var building = launched with { Id = "building", CoveredTileCode = null, ConstructionTicksRemaining = 4, BuildCityId = state.Cities[0].Id };
        state = state with { Fleets = ValueList.Of(launched, building) };

        var after = Roll(state, World, WithHitOdds(0, ScriptedHitDenominator), WinterIndex, 5, new SplitMix64Rng(1));

        Assert.Equal(Classical.Economy.Weather.CalmSeaTileCode, after.FleetById(launched.Id)!.CoveredTileCode);
        Assert.Null(after.FleetById("building")!.CoveredTileCode);
    }

    // ------------------------------------------------------------------------------ the roll

    [Fact]
    public void A_hit_paints_the_whole_inner_square_and_with_every_ring_draw_failing_nothing_else()
    {
        var centre = new WeatherCentre(10, 10);
        var state = CleanState();
        var rng = new ChanceRng(hits: new[] { true }, ring: false);

        var after = Roll(state, WithCentres(centre), WithHitOdds(1, ScriptedHitDenominator), WinterIndex, 5, rng);

        const int radius = 5; // Winter, decompiled-map-code1-overlay.md §1 table
        var expected = new List<RoughSeaCell>();
        for (var x = centre.X - radius; x <= centre.X + radius; x++)
        {
            for (var y = centre.Y - radius; y <= centre.Y + radius; y++)
            {
                if (Eligible(state, x, y))
                {
                    expected.Add(new RoughSeaCell(x, y));
                }
            }
        }

        Assert.Equal(121, expected.Count); // the corner of the Atlantic: the whole 11x11 square is open sea.
        Assert.Equal(expected, after.RoughSeaCells!.ToList());
        Assert.Equal(1, rng.HitDraws); // one hit draw per centre
        Assert.Equal((21 * 21) - (11 * 11), rng.RingDraws); // and one ring draw per cell outside the inner square
    }

    [Fact]
    public void The_ring_paints_each_outer_cell_at_one_in_two_over_a_fixed_seed()
    {
        var centre = new WeatherCentre(10, 10);
        var state = CleanState();

        var after = Roll(
            state, WithCentres(centre), WithHitOdds(1, 1), WinterIndex, 5, new SplitMix64Rng(20261012));

        const int radius = 5;
        var ringEligible = 0;
        var ringPainted = 0;
        var painted = after.RoughSeaCells!.Select(c => (c.X, c.Y)).ToHashSet();
        for (var x = centre.X - (2 * radius); x <= centre.X + (2 * radius); x++)
        {
            for (var y = centre.Y - (2 * radius); y <= centre.Y + (2 * radius); y++)
            {
                var inner = Math.Abs(x - centre.X) <= radius && Math.Abs(y - centre.Y) <= radius;
                if (inner)
                {
                    Assert.True(painted.Contains((x, y)) == Eligible(state, x, y), $"inner ({x},{y})");
                    continue;
                }

                if (!Eligible(state, x, y))
                {
                    Assert.DoesNotContain((x, y), painted);
                    continue;
                }

                ringEligible++;
                if (painted.Contains((x, y)))
                {
                    ringPainted++;
                }
            }
        }

        // 21x21 - 11x11 = 320 ring cells (the centre is 10 tiles from each edge, so all are in the map). A
        // 50 % chance: neither none (the ring deleted) nor all of them (the chance deleted).
        Assert.Equal(320, ringEligible);
        Assert.InRange(ringPainted / (double)ringEligible, 0.40, 0.60);

        // The same seed gives the same overlay.
        var again = Roll(state, WithCentres(centre), WithHitOdds(1, 1), WinterIndex, 5, new SplitMix64Rng(20261012));
        Assert.Equal(after.RoughSeaCells, again.RoughSeaCells);
    }

    [Fact]
    public void A_cell_with_a_city_in_its_three_by_three_is_never_painted()
    {
        var centre = new WeatherCentre(10, 10);
        var state = CleanState();
        var city = state.Cities[0] with { X = centre.X + 2, Y = centre.Y };
        state = state with { Cities = ValueList.From(state.Cities.Append(city)) };

        var after = Roll(state, WithCentres(centre), WithHitOdds(1, ScriptedHitDenominator), WinterIndex, 5,
            new ChanceRng(hits: new[] { true }, ring: true));

        var painted = after.RoughSeaCells!.Select(c => (c.X, c.Y)).ToHashSet();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                Assert.DoesNotContain((city.X + dx, city.Y + dy), painted);
            }
        }

        // The cells just outside that 3x3 still paint (so the hole is the city's, not a missing roll).
        Assert.Contains((city.X + 2, city.Y), painted);
        Assert.Contains((city.X, city.Y + 2), painted);
    }

    [Fact]
    public void Every_painted_cell_of_every_centre_is_calm_sea_with_no_city_beside_it()
    {
        var state = CleanState();
        var after = Roll(state, World, WithHitOdds(1, 1), WinterIndex, 5, new SplitMix64Rng(7));

        Assert.True(after.RoughSeaCells!.Count > 1000, "A Winter roll where every centre hits paints thousands of cells.");
        foreach (var cell in after.RoughSeaCells!)
        {
            Assert.True(Eligible(state, cell.X, cell.Y), $"({cell.X},{cell.Y}) is land or beside a city.");
        }

        // The roll is not trivially satisfied: some inner square really reaches land or a city and is cut short.
        var anyCut = World.WeatherCentres!.Any(c =>
            Enumerable.Range(-5, 11).Any(dx => Enumerable.Range(-5, 11).Any(dy => !Eligible(state, c.X + dx, c.Y + dy))));
        Assert.True(anyCut);
    }

    [Fact]
    public void A_fleet_standing_on_a_painted_cell_gets_its_flag_set_and_one_elsewhere_stays_calm()
    {
        var centre = new WeatherCentre(10, 10);
        var state = CleanState();
        var on = state.Fleets[0] with { Id = "on-the-patch", X = centre.X, Y = centre.Y, CoveredTileCode = 0 };
        var off = state.Fleets[0] with { Id = "off-the-patch", X = centre.X + 30, Y = centre.Y, CoveredTileCode = 1 };
        state = state with { Fleets = ValueList.Of(on, off) };

        var after = Roll(state, WithCentres(centre), WithHitOdds(1, ScriptedHitDenominator), WinterIndex, 5,
            new ChanceRng(hits: new[] { true }, ring: false));

        Assert.Equal(Classical.Economy.Weather.RoughSeaTileCode, after.FleetById("on-the-patch")!.CoveredTileCode);
        Assert.Equal(Classical.Economy.Weather.CalmSeaTileCode, after.FleetById("off-the-patch")!.CoveredTileCode);
        Assert.True(after.IsRoughSea(centre.X, centre.Y)); // and the cell itself stays in the overlay for when it sails off
    }

    [Fact]
    public void Draws_are_one_per_centre_in_order_and_a_hit_at_one_centre_paints_only_around_that_centre()
    {
        var centres = new[] { new WeatherCentre(10, 10), new WeatherCentre(233, 22) };
        var rng = new ChanceRng(hits: new[] { false, true }, ring: false);

        var after = Roll(CleanState(), WithCentres(centres), WithHitOdds(1, ScriptedHitDenominator), WinterIndex, 5, rng);

        Assert.Equal(2, rng.HitDraws);
        Assert.True(after.RoughSeaCells!.Count > 0);
        Assert.All(after.RoughSeaCells!, c => Assert.InRange(c.X, 233 - 5, 233 + 5));
    }

    // ------------------------------------------------------------------------------ the odds and the tick position

    [Theory]
    [InlineData(SpringIndex, 1, 15, 4)]
    [InlineData(SpringIndex, 5, 15, 4)]
    [InlineData(SpringIndex, 7, 30, 3)]
    [InlineData(1, 1, 40, 2)]
    [InlineData(1, 11, 40, 2)]
    [InlineData(2, 5, 30, 3)]
    [InlineData(2, 7, 15, 4)]
    [InlineData(WinterIndex, 1, 5, 5)]
    [InlineData(WinterIndex, 11, 5, 5)]
    public void The_shipped_ruleset_carries_the_reports_odds_and_radius_for_every_season_and_week_band(
        int season, int week, int denominator, int radius)
    {
        var rules = Classical.Economy.Weather;
        var odds = rules.BySeason.Single(o => o.SeasonIndex == season);
        var early = week < rules.EarlyLateWeekThreshold;

        Assert.Equal(1, early ? odds.EarlyNumerator : odds.LateNumerator);
        Assert.Equal(denominator, early ? odds.EarlyDenominator : odds.LateDenominator);
        Assert.Equal(radius, early ? odds.EarlyRadiusTiles : odds.LateRadiusTiles);
        Assert.Equal(10, rules.ClearRadiusTiles);
        Assert.Equal(1, rules.RingChanceNumerator);
        Assert.Equal(2, rules.RingChanceDenominator);
        Assert.Equal(1, rules.CityExclusionRadiusTiles);
        Assert.Equal(3, rules.RoughSeaMoveCost);
    }

    [Fact]
    public void The_roll_uses_the_odds_of_the_season_before_the_calendar_advances()
    {
        // Winter always hits (1-in-1), every other season never does (0-in-1). The tick position: the weather
        // phase runs BEFORE the calendar advance, so the tick that starts in Winter, week 11 rolls with Winter's
        // odds even though the calendar then moves to Spring, week 1. Reading the post-advance calendar would
        // paint nothing.
        var rules = Classical.Economy.Weather;
        var ruleset = Classical with
        {
            Economy = Classical.Economy with
            {
                Weather = rules with
                {
                    BySeason = ValueList.From(rules.BySeason.Select(o => o.SeasonIndex == WinterIndex
                        ? o with { EarlyNumerator = 1, EarlyDenominator = 1, LateNumerator = 1, LateDenominator = 1 }
                        : o with { EarlyNumerator = 0, EarlyDenominator = 1, LateNumerator = 0, LateDenominator = 1 })),
                },
            },
        };

        var coordinator = new TurnCoordinator(
            SystemRegistry.FromAssemblies(
                new[] { typeof(CalendarSystem).Assembly },
                type => type == typeof(CalendarSystem) || type == typeof(WeatherEventSystem)),
            ruleset,
            World,
            NullEventSink.Instance);
        var winterEnd = CleanState() with { Calendar = new CalendarState(Week: 11, SeasonIndex: WinterIndex, YearBc: 270, TurnIndex: 0) };

        var firstTick = coordinator.RunRoundTick(winterEnd).State;

        Assert.Equal(SpringIndex, firstTick.Calendar.SeasonIndex);
        Assert.Equal(1, firstTick.Calendar.Week);
        Assert.True(firstTick.RoughSeaCells!.Count > 1000, "The Winter-week-11 tick rolls with Winter's odds.");

        // The next tick starts in Spring, week 1 (0-in-1): it clears last week's overlay and paints nothing.
        var secondTick = coordinator.RunRoundTick(firstTick).State;
        Assert.Empty(secondTick.RoughSeaCells!);
    }

    [Fact]
    public void The_weather_system_registers_between_the_fleet_tick_and_the_calendar_advance()
    {
        var registry = SystemRegistry.FromEngineAssembly();

        Assert.True(TurnPhase.FleetTick < TurnPhase.WeatherEvents && TurnPhase.WeatherEvents < TurnPhase.CalendarAdvance);
        Assert.Contains(registry.InPhase(TurnPhase.WeatherEvents), s => s.ImplementationType == typeof(WeatherEventSystem));
    }

    [Fact]
    public void A_world_without_weather_centres_has_no_weather_and_changes_nothing()
    {
        var toy = EconomyTestbed.Toy;
        var coordinator = EconomyTestbed.CoordinatorOnly(NullEventSink.Instance, typeof(CalendarSystem), typeof(WeatherEventSystem));
        var state = EconomyTestbed.InitialState();

        Assert.Null(toy.World.WeatherCentres);
        var after = state;
        for (var round = 0; round < 24; round++)
        {
            after = coordinator.RunRoundTick(after).State;
        }

        Assert.Equal(state.Fleets, after.Fleets);
        Assert.True(after.RoughSeaCells is { Count: 0 });
    }

    // ------------------------------------------------------------------------------ a new game

    [Fact]
    public void A_new_game_rolls_once_with_spring_week_one_not_with_the_start_calendar()
    {
        // Spring always hits (the (Spring, 1) band has radius 4); every other season never does. The start
        // calendar is Winter, so a roll that read it would paint nothing.
        var rules = Classical.Economy.Weather;
        var ruleset = Classical with
        {
            Calendar = Classical.Calendar with { StartSeasonIndex = WinterIndex },
            Economy = Classical.Economy with
            {
                Weather = rules with
                {
                    BySeason = ValueList.From(rules.BySeason.Select(o => o.SeasonIndex == SpringIndex
                        ? o with { EarlyNumerator = 1, EarlyDenominator = 1 }
                        : o with { EarlyNumerator = 0, EarlyDenominator = 1, LateNumerator = 0, LateDenominator = 1 })),
                },
            },
        };

        var state = GameStateFactory.CreateInitial(World, ruleset, RealGameData.Scenario);

        Assert.Equal(WinterIndex, state.Calendar.SeasonIndex);
        Assert.True(state.RoughSeaCells!.Count > 500, "Spring week 1 always hits here, so a new game starts with an overlay.");

        // A single centre shows the radius is the Spring-week-1 one (4: a 9x9 inner square), not Winter's 5.
        var oneCentre = WithCentres(new WeatherCentre(10, 10));
        var single = GameStateFactory.CreateInitial(oneCentre, ruleset, RealGameData.Scenario);
        for (var x = 10 - 4; x <= 10 + 4; x++)
        {
            for (var y = 10 - 4; y <= 10 + 4; y++)
            {
                Assert.True(single.IsRoughSea(x, y), $"inner ({x},{y})");
            }
        }

        Assert.All(single.RoughSeaCells!, c => Assert.InRange(c.X, 10 - 8, 10 + 8)); // the ring reaches 2r = 8, no further

        // Deterministic: the same world, ruleset and scenario give the same overlay.
        Assert.Equal(single.RoughSeaCells, GameStateFactory.CreateInitial(oneCentre, ruleset, RealGameData.Scenario).RoughSeaCells);
    }

    [Fact]
    public void A_new_toy_game_has_an_empty_overlay()
    {
        var state = EconomyTestbed.InitialState();

        Assert.True(state.RoughSeaCells is { Count: 0 });
    }

    // ------------------------------------------------------------------------------ the centres, in the shipped world

    [Fact]
    public void The_shipped_classical_world_carries_the_twenty_centres_in_dat_order()
    {
        var centres = World.WeatherCentres!;

        Assert.Equal(20, centres.Count);
        Assert.Equal(new WeatherCentre(10, 10), centres[0]);
        Assert.Equal(new WeatherCentre(233, 22), centres[19]);
    }

    /// <summary>
    /// An <see cref="IRng"/> that answers a hit roll (its denominator is <see cref="ScriptedHitDenominator"/>)
    /// from a script, in order, and every ring roll (denominator 2) with one fixed answer, counting both.
    /// </summary>
    private sealed class ChanceRng : IRng
    {
        private readonly bool[] _hits;
        private readonly bool _ring;

        public ChanceRng(bool[] hits, bool ring)
        {
            _hits = hits;
            _ring = ring;
        }

        public int HitDraws { get; private set; }

        public int RingDraws { get; private set; }

        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64() => throw new NotSupportedException();

        public int NextInt(int exclusiveUpperBound) => throw new NotSupportedException();

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) => throw new NotSupportedException();

        public bool NextChance(int numerator, int denominator)
        {
            if (denominator == RingDenominator)
            {
                RingDraws++;
                return _ring;
            }

            Assert.Equal(ScriptedHitDenominator, denominator);
            return _hits[HitDraws++];
        }

        public IRng ForStream(string streamName) => throw new NotSupportedException();
    }
}
