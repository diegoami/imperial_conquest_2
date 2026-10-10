using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using Fx = IC2.Engine.Tests.Ai.AiReachableFallbackTests;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925), rework round 3. Two Hazards rules of the reachable fallback, each on hand-built
/// states with full-turn proofs where a turn is the point:
/// (1) a chosen resupply or garrison march must make progress this turn, priced with the real walker
/// and the ruleset's terrain costs (the final reviews' R1 and N1), and
/// (2) every city tie breaks by city id ascending, whatever order the state lists the cities in
/// (R3 and N2).
/// </summary>
public sealed class AiFirstStepAndTieBreakTests
{
    private const string Us = "north";
    private const string Them = "south";
    private const string ArmyId = "a1";

    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    private static AiView View(GameState state, World world) => new(state, Ruleset, world, Us);

    private static World MountainColumnAtSix() =>
        Fx.Terrain(cells =>
        {
            for (var y = 0; y < 32; y++)
            {
                cells[(y * 32) + 6] = 5;
            }
        });

    private sealed class CapturingDispatch(ICommandDispatch inner, List<ICommand> seen) : ICommandDispatch
    {
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, null!);

        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            seen.Add(command);
            return events is null ? inner.Dispatch(state, command) : inner.Dispatch(state, command, events);
        }
    }

    private static AiTurnOutcome RunTurn(GameState state, World world, List<ICommand> seen)
    {
        var sink = new RecordingEventSink();
        return AiTurn.Run(
            state, Ruleset, world,
            new CapturingDispatch(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Ruleset, world, sink), seen),
            SplitMix64Rng.ForStream(1UL, "ai.turn"), sink);
    }

    // ---- R1 / N1: the first step must be affordable ----

    /// <summary>
    /// The final review's proof: a mountain column at x = 6, the army at (5, 5) with 2 moves, and the
    /// only resupply city and capital at (20, 5), straight across the mountain (cost 4). The march to
    /// (20, 5) would be accepted and then aborted with the army parked at (5, 5) and its moves zeroed.
    /// The turn must end with the army moved: the chain passes over the unaffordable resupply and
    /// garrison destinations and takes the emergency move.
    /// </summary>
    [Fact]
    public void A_turn_whose_resupply_march_starts_across_a_mountain_still_moves_the_army()
    {
        var world = MountainColumnAtSix();
        var army = Fx.Army(5, 5, moves: 2);
        var state = Fx.State(
            [Fx.City("n-cap", Us, 20, 5, supply: 0), Fx.City("s-cap", Them, 30, 30)],
            army);
        var view = View(state, world);

        // The tree itself has no usable resupply or garrison destination.
        Assert.Null(AiArmyTargetTree.ResolveResupplyCity(view, army, atWar: true).City);
        Assert.Null(AiArmyTargetTree.GarrisonFallback(view, army, state));

        var seen = new List<ICommand>();
        var outcome = RunTurn(state, world, seen);

        var moves = seen.OfType<MoveArmyCommand>().Where(m => m.ArmyId == ArmyId).ToList();
        Assert.DoesNotContain(moves, m => m.X == 20 && m.Y == 5);
        var after = outcome.State.ArmyById(ArmyId)!;
        Assert.NotEqual((5, 5), (after.X, after.Y));
        Assert.Equal((5, 4), (after.X, after.Y));
        Assert.Equal(0, outcome.CommandsRejected);
    }

    /// <summary>
    /// With a second own city that qualifies and whose first step is affordable, the chain takes it
    /// before the emergency move: the original's nearest pick (8, 5) lies across the mountain (the walk
    /// from (5, 5) enters (6, 5) first, cost 4), the next candidate (5, 15) lies along plain.
    /// </summary>
    [Fact]
    public void An_unaffordable_first_step_gives_way_to_the_next_resupply_candidate()
    {
        var world = MountainColumnAtSix();
        var army = Fx.Army(5, 5, moves: 2);
        var state = Fx.State(
            [
                Fx.City("n-cap", Us, 5, 15, supply: 0),
                Fx.City("n-near", Us, 8, 5, supply: 0),
                Fx.City("s-cap", Them, 30, 30),
            ],
            army);
        var view = View(state, world);

        // The original's pick is the nearest qualifying city, across the mountain.
        Assert.Equal("n-near", AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: true).City!.Id);
        Assert.Equal("n-cap", AiArmyTargetTree.ResolveResupplyCity(view, army, atWar: true).City!.Id);

        var seen = new List<ICommand>();
        var outcome = RunTurn(state, world, seen);

        var march = Assert.Single(seen.OfType<MoveArmyCommand>(), m => m.ArmyId == ArmyId);
        Assert.Equal((5, 15), (march.X, march.Y));
        var after = outcome.State.ArmyById(ArmyId)!;
        Assert.Equal((5, 7), (after.X, after.Y));
    }

    /// <summary>The same rule for the garrison fallback, in both of its branches.</summary>
    [Fact]
    public void The_garrison_fallback_passes_over_a_destination_whose_first_step_is_unaffordable()
    {
        var world = MountainColumnAtSix();
        var army = Fx.Army(5, 5, moves: 2);

        // Branch "capital alone": no own army within 10 of the capital (the capital is at (20, 5), the
        // army 15 away), and the capital's first step is the mountain.
        var farState = Fx.State(
            [Fx.City("n-cap", Us, 20, 5, supply: 5_000), Fx.City("s-cap", Them, 30, 30)],
            army);
        Assert.Null(AiArmyTargetTree.GarrisonFallback(View(farState, world), army, farState));

        // Branch "near the capital": the capital at (8, 5) is within 10, the nearest city of any owner
        // is the capital across the mountain, and the next city along plain takes over.
        var nearState = Fx.State(
            [
                Fx.City("n-cap", Us, 8, 5, supply: 5_000),
                Fx.City("p-city", Them, 5, 12, supply: 5_000),
                Fx.City("s-cap", Them, 30, 30),
            ],
            army);
        Assert.Equal((5, 12), AiArmyTargetTree.GarrisonFallback(View(nearState, world), army, nearState));
    }

    // ---- R3 / N2: every tie breaks by city id ascending ----

    /// <summary>
    /// Two own cities equally far from an army at (10, 10), listed in the order asked for.
    /// <paramref name="supply"/> is the stock of both: 0 makes both qualify (equal score), 5,000 makes
    /// neither qualify (the nearest-own-city answer, equal distance).
    /// </summary>
    private static (GameState State, ArmyState Army) TwoEquidistantOwnCities(int supply, bool greaterIdFirst)
    {
        var army = Fx.Army(10, 10, moves: 2);
        var z = Fx.City("z-city", Us, 13, 10, supply);
        var a = Fx.City("a-city", Us, 10, 13, supply);
        var cities = new List<CityState>(greaterIdFirst ? [z, a] : [a, z])
        {
            Fx.City("n-cap", Us, 0, 0, supply: 5_000),
            Fx.City("s-cap", Them, 30, 30),
        };
        return (Fx.State(cities, army), army);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(5_000, true)]
    [InlineData(5_000, false)]
    public void Equally_good_resupply_cities_tie_break_by_city_id_whatever_the_list_order(int supply, bool greaterIdFirst)
    {
        var (state, army) = TwoEquidistantOwnCities(supply, greaterIdFirst);
        var view = View(state, Fx.Terrain());

        Assert.Equal("a-city", AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: true).City!.Id);
        Assert.Equal("a-city", AiArmyTargetTree.ResolveResupplyCity(view, army, atWar: true).City!.Id);
    }

    /// <summary>
    /// The emergency anchor is the nearest own city, ties by city id: with two equally near cities the
    /// destination is the tile nearest a-city (10, 13), (9, 12) by the Y-then-X rule, and not the tile
    /// nearest z-city (13, 10).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_emergency_anchor_breaks_a_tie_between_equally_near_cities_by_city_id(bool greaterIdFirst)
    {
        var (state, army) = TwoEquidistantOwnCities(supply: 5_000, greaterIdFirst);
        var view = View(state, Fx.Terrain());

        Assert.Equal((9, 12), AiArmyTargetTree.EmergencyMoveDestination(view, army, state));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Equally_scored_enemy_cities_and_armies_tie_break_by_id(bool greaterIdFirst)
    {
        var army = Fx.Army(10, 10, moves: 5, troops: 40_000);
        var zCity = CaptureFixtures.City("z-city", "z-city", 13, 10, Them, Them, 90, 100, 100, 100, 0);
        var aCity = CaptureFixtures.City("a-city", "a-city", 10, 13, Them, Them, 90, 100, 100, 100, 0);
        var zArmy = CaptureFixtures.Army("z-army", Them, 7, 10, 60, CaptureFixtures.Unit("light_infantry", 10_000));
        var aArmy = CaptureFixtures.Army("a-army", Them, 10, 7, 60, CaptureFixtures.Unit("light_infantry", 10_000));
        var cities = new List<CityState>
        {
            Fx.City("n-cap", Us, 0, 0, supply: 5_000),
            Fx.City("s-cap", Them, 30, 30),
        };
        cities.AddRange(greaterIdFirst ? [zCity, aCity] : [aCity, zCity]);
        var armies = greaterIdFirst ? new[] { army, zArmy, aArmy } : new[] { army, aArmy, zArmy };
        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(
                [
                    CaptureFixtures.Nation(Us, treasury: 1_000, capitalCityId: "n-cap")
                        with { Personality = AiScriptedStates.DefaultPersonality },
                    CaptureFixtures.Nation(Them, capitalCityId: "s-cap")
                        with { Personality = AiScriptedStates.DefaultPersonality },
                ],
                cities,
                armies),
            Us);
        state = state with { Relations = state.Relations.WithRelation(Us, Them, Ruleset.Diplomacy.StateCodes.War) };
        var view = View(state, Fx.Terrain());

        Assert.Equal("a-city", AiArmyTargetTree.ScoreCityTarget(view, army).City!.Id);
        Assert.Equal("a-army", AiArmyTargetTree.ScoreArmyTarget(view, army).Army!.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Equally_near_mercenary_offers_tie_break_by_city_id(bool greaterIdFirst)
    {
        var army = Fx.Army(10, 10, moves: 2);
        var state = Fx.State(
            [
                Fx.City("z-city", Them, 13, 10),
                Fx.City("a-city", Them, 10, 13),
                Fx.City("n-cap", Us, 0, 0, supply: 5_000),
                Fx.City("s-cap", Them, 30, 30),
            ],
            army);
        var zOffer = new MercenaryPoolSlot(0, 13, 10, 11, "light_infantry", 100, 6);
        var aOffer = new MercenaryPoolSlot(1, 10, 13, 11, "light_infantry", 100, 6);
        state = state with { MercenaryPool = ValueList.Of(greaterIdFirst ? new[] { zOffer, aOffer } : new[] { aOffer, zOffer }) };
        var view = View(state, Fx.Terrain());

        Assert.Equal("a-city", AiArmyTargetTree.MercenaryRunDestination(view, army)!.Id);
    }
}
