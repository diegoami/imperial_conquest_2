using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925), the Hazards bullet "The fallback is reachable too", steps 1 to 3, on hand-built
/// states (the re-review's R2, R3 and R4). Step 1: the original's resupply pick stands when the
/// reachability rule does not skip it, and only a skipped pick gives way to the next candidate in score
/// order; with every candidate skipped the tree has no resupply destination at all. Step 3: the
/// emergency move's candidates are the tiles the existing move command reaches this turn, priced by
/// movement cost and not by step count.
/// </summary>
public sealed class AiReachableFallbackTests
{
    private const string Us = "north";
    private const string Them = "south";
    private const string Third = "east";
    private const string ArmyId = "a1";

    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    // The toy ruleset's terrain codes: 0 sea (armies cannot enter), 2 plain, 4 forest (cost 2), 5 mountains (cost 4).
    private const int Sea = 0;
    private const int Plain = 2;
    private const int Forest = 4;
    private const int Mountains = 5;

    /// <summary>A 32 by 32 world of plain with <paramref name="edit"/> applied to its cell codes.</summary>
    internal static World Terrain(Action<int[]>? edit = null)
    {
        var cells = new int[32 * 32];
        Array.Fill(cells, Plain);
        edit?.Invoke(cells);

        var runs = new List<TerrainRun>();
        foreach (var cell in cells)
        {
            if (runs.Count > 0 && runs[^1].Code == cell)
            {
                runs[^1] = runs[^1] with { Count = runs[^1].Count + 1 };
            }
            else
            {
                runs.Add(new TerrainRun(cell, 1));
            }
        }

        return AiScriptedStates.World with
        {
            Width = 32,
            Height = 32,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(runs)),
        };
    }

    /// <summary>Sets every cell at Chebyshev distance exactly <paramref name="radius"/> from (cx, cy) to sea.</summary>
    internal static void SeaRing(int[] cells, int cx, int cy, int radius)
    {
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) == radius)
                {
                    cells[(y * 32) + x] = Sea;
                }
            }
        }
    }

    internal static CityState City(string id, string owner, int x, int y, int supply = 0) =>
        CaptureFixtures.City(id, id, x, y, owner, owner, 90, 100, 100, 100, 0) with { SupplyTons = supply };

    internal static ArmyState Army(int x, int y, int moves, int troops = 10_000) =>
        CaptureFixtures.Army(ArmyId, Us, x, y, 60, CaptureFixtures.Unit("light_infantry", troops))
            with { Moves = moves, Money = 500 };

    internal static GameState State(IEnumerable<CityState> cities, ArmyState army, string capital = "n-cap") =>
        AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(
                [
                    CaptureFixtures.Nation(Us, treasury: 1_000, capitalCityId: capital)
                        with { Personality = AiScriptedStates.DefaultPersonality },
                    CaptureFixtures.Nation(Them, capitalCityId: "s-cap")
                        with { Personality = AiScriptedStates.DefaultPersonality },
                ],
                cities,
                [army]),
            Us);

    private static AiView View(GameState state, World world) => new(state, Ruleset, world, Us);

    // ---- R2: the emergency move is priced by movement cost ----

    /// <summary>
    /// A mountain column at x = 6 and an army at (5, 5) with 2 moves. The tile (7, 3) is only two steps
    /// away but its path crosses the mountain (6, 4), which costs 4: the walker stops before it and the
    /// abort rule zeroes the army's moves. The emergency move must pick a tile the army really reaches:
    /// the tile nearest the capital among the plain tiles within two steps, which is (5, 4).
    /// </summary>
    [Fact]
    public void The_emergency_move_prices_each_step_and_never_picks_a_tile_across_a_mountain()
    {
        var world = Terrain(cells =>
        {
            for (var y = 0; y < 32; y++)
            {
                cells[(y * 32) + 6] = Mountains;
            }
        });
        var army = Army(5, 5, moves: 2);
        var state = State([City("n-cap", Us, 20, 5, supply: 5_000), City("s-cap", Them, 30, 30)], army);
        var view = View(state, world);

        var destination = AiArmyTargetTree.EmergencyMoveDestination(view, army, state);

        Assert.Equal((5, 4), destination);

        // The chosen tile is one the real move command reaches: the army ends on it.
        var sink = new RecordingEventSink();
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Ruleset, world, sink);
        var result = dispatcher.Dispatch(state, new MoveArmyCommand(Us, ArmyId, 5, 4), sink);
        Assert.True(result.IsAccepted);
        var moved = result.State.ArmyById(ArmyId)!;
        Assert.Equal((5, 4), (moved.X, moved.Y));
    }

    /// <summary>
    /// Forest costs 2. With 1 move left the army cannot pay for the forest at (5, 4), the tile nearest
    /// the capital at (5, 0), so it takes a plain neighbour at the same distance from the capital:
    /// (4, 4) and (6, 4) tie (distance 4 from the capital, 1 from the army, Y 4), and X ascending gives (4, 4).
    /// </summary>
    [Fact]
    public void The_emergency_move_does_not_pick_a_forest_it_cannot_pay_for()
    {
        var world = Terrain(cells => cells[(4 * 32) + 5] = Forest);
        var army = Army(5, 5, moves: 1);
        var state = State([City("n-cap", Us, 5, 0, supply: 5_000), City("s-cap", Them, 30, 30)], army);
        var view = View(state, world);

        Assert.Equal((4, 4), AiArmyTargetTree.EmergencyMoveDestination(view, army, state));
    }

    // ---- R3: no reachable resupply city means no resupply destination ----

    /// <summary>
    /// The army at (10, 10) with an own city at (15, 10) holding plenty of supply (it does not qualify,
    /// so it is the original's "nearest own city" answer) and a foreign city at (13, 10) on the straight
    /// line. The only candidate is skipped by the reachability rule, so the tree has no resupply
    /// destination, and the caller falls through to the garrison fallback.
    /// </summary>
    private static (GameState State, World World, ArmyState Army) BlockedResupplyState()
    {
        var army = Army(10, 10, moves: 9);
        var state = State(
            [
                City("n-cap", Us, 10, 2, supply: 5_000),
                City("c-own", Us, 15, 10, supply: 5_000),
                City("x-block", Them, 13, 10),
                City("s-cap", Them, 30, 30),
            ],
            army);
        return (state, Terrain(), army);
    }

    [Fact]
    public void An_unreachable_nearest_own_city_is_not_a_resupply_destination()
    {
        var (state, world, army) = BlockedResupplyState();
        var view = View(state, world);

        // The original's pick is c-own, and it is unreachable.
        var original = AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: true);
        Assert.Equal("c-own", original.City!.Id);
        Assert.False(AiArmyTargetTree.IsReachable(view, army, original.City.X, original.City.Y));

        var resolved = AiArmyTargetTree.ResolveResupplyCity(view, army, atWar: true);
        Assert.Null(resolved.City);

        var decision = AiArmyTargetTree.Decide(view, army, atWar: true);
        Assert.Equal(AiArmyTargetTree.Kind.MoveToResupplyCity, decision.Selected);
        Assert.Null(decision.Resupply!.City);
    }

    [Fact]
    public void A_turn_whose_resupply_city_is_unreachable_marches_at_the_garrison_fallback_instead()
    {
        var (state, world, army) = BlockedResupplyState();
        var view = View(state, world);
        var garrison = AiArmyTargetTree.GarrisonFallback(view, army, state);
        Assert.NotNull(garrison);
        Assert.NotEqual((15, 10), garrison!.Value);

        var sink = new RecordingEventSink();
        var seen = new List<ICommand>();
        var outcome = AiTurn.Run(
            state, Ruleset, world,
            new CapturingDispatch(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Ruleset, world, sink), seen),
            SplitMix64Rng.ForStream(1UL, "ai.turn"), sink);

        var moves = seen.OfType<MoveArmyCommand>().Where(m => m.ArmyId == ArmyId).ToList();
        Assert.NotEmpty(moves);
        Assert.Equal(garrison.Value, (moves[0].X, moves[0].Y));
        Assert.DoesNotContain(moves, m => m.X == 15 && m.Y == 10);
        Assert.Equal(0, outcome.CommandsRejected);
    }

    /// <summary>
    /// The mercenary run's "defend the resupply city" continuation (armyScore under 71, cityScore over
    /// 85) marches at the same resupply destination, so it takes the same step 1: with the own city
    /// behind a sea tile the continuation has no destination (and the turn marches at the garrison
    /// fallback), and with the way open it defends the own city. The target is a lightly defended
    /// foreign city 12 tiles south that scores 98 for a 40,000-man army.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_defend_the_resupply_city_continuation_follows_the_same_reachable_destination(bool seaBlocksTheOwnCity)
    {
        var world = Terrain(cells =>
        {
            if (seaBlocksTheOwnCity)
            {
                cells[(10 * 32) + 13] = Sea;
            }
        });
        var army = Army(10, 10, moves: 9, troops: 40_000) with { SupplyTons = 1_000 };
        var state = State(
            [
                City("n-cap", Us, 10, 2, supply: 5_000),
                City("c-own", Us, 15, 10, supply: 5_000),
                CaptureFixtures.City("s-target", "s-target", 10, 22, Them, Them, 90, 0, 100, 100, 0),
                City("s-cap", Them, 30, 30),
            ],
            army);
        state = state with { Relations = state.Relations.WithRelation(Us, Them, Ruleset.Diplomacy.StateCodes.War) };
        var view = View(state, world);

        var decision = AiArmyTargetTree.Decide(view, army, atWar: true);

        Assert.Equal(AiArmyTargetTree.Kind.MercenaryRun, decision.Selected);
        Assert.Equal(AiArmyTargetTree.Continuation.DefendResupplyCity, decision.After);
        if (seaBlocksTheOwnCity)
        {
            Assert.Null(decision.Resupply!.City);
        }
        else
        {
            Assert.Equal("c-own", decision.Resupply!.City!.Id);
        }

        var sink = new RecordingEventSink();
        var seen = new List<ICommand>();
        AiTurn.Run(
            state, Ruleset, world,
            new CapturingDispatch(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Ruleset, world, sink), seen),
            SplitMix64Rng.ForStream(1UL, "ai.turn"), sink);
        var marchedAtOwnCity = seen.OfType<MoveArmyCommand>().Any(m => m.ArmyId == ArmyId && m.X == 15 && m.Y == 10);
        Assert.Equal(!seaBlocksTheOwnCity, marchedAtOwnCity);
    }

    private sealed class CapturingDispatch(ICommandDispatch inner, List<ICommand> seen) : ICommandDispatch
    {
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, null!);

        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            seen.Add(command);
            return events is null ? inner.Dispatch(state, command) : inner.Dispatch(state, command, events);
        }
    }

    // ---- R4: the original's pick is not overridden when it is reachable ----

    /// <summary>
    /// At war, an army at (10, 10) with strength 100; an own city a-low at (13, 10) with no supply (it
    /// qualifies, score -3), an own city b-near at (8, 10) with plenty (the nearest own city), and a
    /// foreign non-war city f-far 16 tiles away (score 4, beyond the range of 15). The original's rule:
    /// the best foreign city is beyond 15, so the nearest own city, b-near.
    /// </summary>
    private static (GameState State, World World, ArmyState Army) BeyondFifteenState(bool blockNearest)
    {
        var army = Army(10, 10, moves: 9);
        var cities = new List<CityState>
        {
            City("n-cap", Us, 10, 0, supply: 5_000),
            City("a-low", Us, 13, 10, supply: 0),
            City("b-near", Us, 8, 10, supply: 5_000),
            City("f-far", Third, 26, 10, supply: 5_000),
            City("s-cap", Them, 30, 30),
        };
        if (blockNearest)
        {
            cities.Add(City("blocker", Them, 9, 10));
        }

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(
                [
                    CaptureFixtures.Nation(Us, treasury: 1_000, capitalCityId: "n-cap")
                        with { Personality = AiScriptedStates.DefaultPersonality },
                    CaptureFixtures.Nation(Them, capitalCityId: "s-cap")
                        with { Personality = AiScriptedStates.DefaultPersonality },
                    CaptureFixtures.Nation(Third, capitalCityId: "f-far")
                        with { Personality = AiScriptedStates.DefaultPersonality },
                ],
                cities,
                [army]),
            Us);
        return (state, Terrain(), army);
    }

    [Fact]
    public void A_reachable_nearest_own_city_stays_the_pick_when_the_best_foreign_city_is_beyond_fifteen()
    {
        var (state, world, army) = BeyondFifteenState(blockNearest: false);
        var view = View(state, world);

        Assert.Equal("b-near", AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: true).City!.Id);
        Assert.Equal("b-near", AiArmyTargetTree.ResolveResupplyCity(view, army, atWar: true).City!.Id);

        var decision = AiArmyTargetTree.Decide(view, army, atWar: true);
        Assert.Equal(AiArmyTargetTree.Kind.MoveToResupplyCity, decision.Selected);
        Assert.Equal("b-near", decision.Resupply!.City!.Id);
    }

    [Fact]
    public void A_skipped_pick_gives_way_to_the_next_candidate_in_score_order()
    {
        var (state, world, army) = BeyondFifteenState(blockNearest: true);
        var view = View(state, world);

        // The original's pick b-near is behind the city at (9, 10): skipped. The next candidate is a-low
        // (score -3); the foreign city beyond 15 is not a candidate at all.
        Assert.Equal("b-near", AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: true).City!.Id);
        Assert.False(AiArmyTargetTree.IsReachable(view, army, 8, 10));

        var resolved = AiArmyTargetTree.ResolveResupplyCity(view, army, atWar: true);
        Assert.Equal("a-low", resolved.City!.Id);
    }
}
