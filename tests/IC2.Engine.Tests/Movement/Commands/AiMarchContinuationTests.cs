using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Ai;
using Xunit;

namespace IC2.Engine.Tests.Movement.Commands;

/// <summary>
/// T239 (#1592): a faithful AI march keeps walking as the original's <c>FUN_0044dba8</c> does. The rows are the
/// wrapper controls of research <c>2026-10-30-ai-move-fallback-point105.md</c> (the whole wrapper emulated on the
/// #187 RAM snapshot): the classical map with every cell Plain, Rome's army at (100,40), and its march east
/// blocked by own army markers on (101,39), (101,40) and (101,41).
/// </summary>
public sealed class AiMarchContinuationTests
{
    private const string Army = "rome-probe";

    /// <summary>
    /// Open land. The first walk makes no progress, so the army walks toward CEC8's point (103,43), side-steps
    /// to (100,41), and pays 1 move more; at 1 move that leaves −1. At 3 it walks back on the next level.
    /// </summary>
    [Theory]
    [InlineData(1, 103, 100, 41, -1)]
    [InlineData(2, 103, 100, 41, 0)]
    [InlineData(3, 103, 100, 40, 0)]
    [InlineData(5, 103, 100, 41, 0)]
    [InlineData(8, 103, 100, 41, 0)]
    [InlineData(1, 106, 100, 41, -1)]
    [InlineData(3, 106, 100, 40, 0)]
    [InlineData(8, 106, 100, 41, 0)]
    public void On_open_land_the_march_side_steps_and_pays_a_move_per_fallback(
        int moves, int targetX, int endX, int endY, int movesLeft)
    {
        var (state, dispatcher) = Setup(moves);

        var result = dispatcher.Dispatch(state, March(targetX, 40));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(Army)!;
        Assert.Equal((endX, endY, movesLeft), (army.X, army.Y, army.Moves));
    }

    /// <summary>
    /// Sea at (102,42) cuts the (1,1) direction, so the last good direction is (1,0), into the blockage: the
    /// army stands still and burns one move per level.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void When_the_fallback_points_into_the_blockage_the_army_stays_and_spends_every_move(int moves)
    {
        var (state, dispatcher) = Setup(moves, sea: new[] { new GridPoint(102, 42) });

        var army = dispatcher.Dispatch(state, March(103, 40)).State.ArmyById(Army)!;

        Assert.Equal((100, 40, 0), (army.X, army.Y, army.Moves));
    }

    /// <summary>
    /// A sea ring at Chebyshev distance 3: no fallback point, 1 move spent, and no second level. The research row
    /// aims at (103,40), on the ring; since T237 an army order onto sea is refused, so this aims at (106,40),
    /// land beyond it, which reaches the same no-point fallback.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(8)]
    public void With_no_fallback_point_the_march_spends_one_move_and_ends(int moves)
    {
        var ring = new List<GridPoint>();
        for (var dx = -3; dx <= 3; dx++)
        {
            for (var dy = -3; dy <= 3; dy++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == 3)
                {
                    ring.Add(new GridPoint(100 + dx, 40 + dy));
                }
            }
        }

        var (state, dispatcher) = Setup(moves, sea: ring);

        var result = dispatcher.Dispatch(state, March(106, 40));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(Army)!;

        Assert.Equal((100, 40, moves - 1), (army.X, army.Y, army.Moves));
    }

    /// <summary>The same blocked order with the single-walk policy (a designed AI's) is unchanged: no step, no cost.</summary>
    [Fact]
    public void A_single_walk_order_is_unchanged()
    {
        var (state, dispatcher) = Setup(5);

        var result = dispatcher.Dispatch(state, new MoveArmyCommand("rome", Army, 103, 40));

        var army = result.State.ArmyById(Army)!;
        Assert.Equal((100, 40, 5), (army.X, army.Y, army.Moves));
    }

    /// <summary>
    /// An open march with no blockers continues past the point the first walk aimed at: aimed at a waypoint
    /// (104,40) on the way to (110,40), the army keeps walking toward the target until within 1 tile or out of
    /// moves. The levels are one route and one <see cref="ArmyMoved"/> event.
    /// </summary>
    [Fact]
    public void A_march_aimed_at_a_waypoint_keeps_walking_toward_the_target_as_one_order()
    {
        var (state, dispatcher) = Setup(8, blockers: false);
        var sink = new RecordingEventSink();
        dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), AiTestbed.Classical.Ruleset, PlainWorld(Array.Empty<GridPoint>()), sink);

        var result = dispatcher.Dispatch(state, new MoveArmyCommand(
            "rome", Army, 104, 40, MoveWalkPolicy.AiContinuation, 110, 40));

        var army = result.State.ArmyById(Army)!;
        Assert.Equal((108, 40, 0), (army.X, army.Y, army.Moves));
        var moved = Assert.Single(sink.Events.OfType<ArmyMoved>());
        Assert.Equal((100, 40, 108, 40, 8), (moved.FromX, moved.FromY, moved.ToX, moved.ToY, moved.MovesSpent));
    }

    /// <summary>
    /// A later level whose direct line crosses sea follows D174's intermediate point: a sea wall at x = 105 stops
    /// the first walk at (104,40), and the next level walks toward <c>Intermediate</c>'s point, not along the
    /// blocked line.
    /// </summary>
    [Fact]
    public void A_level_whose_line_crosses_sea_walks_toward_the_intermediate_point()
    {
        var wall = Enumerable.Range(25, 31).Select(y => new GridPoint(105, y)).ToArray();
        var (state, dispatcher) = Setup(8, sea: wall, blockers: false);
        var world = PlainWorld(wall);
        var at = new GridPoint(104, 40);
        var view = new IC2.Engine.Ai.AiView(
            state with { Armies = ValueList.From(state.Armies.Select(a => a with { X = at.X, Y = at.Y })) },
            AiTestbed.Classical.Ruleset, world, "rome");
        Assert.False(IC2.Engine.Ai.ClassicalArmyNavigation.DirectLine(view, at, new GridPoint(110, 40)));
        var waypoint = IC2.Engine.Ai.ClassicalArmyNavigation.Intermediate(view, at, new GridPoint(110, 40)).Point!.Value;

        var army = dispatcher.Dispatch(state, March(110, 40)).State.ArmyById(Army)!;

        Assert.NotEqual((104, 40), (army.X, army.Y));
        Assert.True(Chebyshev(new GridPoint(army.X, army.Y), waypoint) < Chebyshev(at, waypoint),
            $"the second level walked from {at} toward {waypoint}, ending at ({army.X},{army.Y})");
    }

    /// <summary>
    /// An intermediate lookup with no point ends the order: inside a sea ring at Chebyshev distance 2 around
    /// (60,100), clear of the classical waypoints, the first walk stops at (61,100), no waypoint is visible from
    /// there and no local fallback exists, so no further level runs and no fallback move is charged.
    /// </summary>
    [Fact]
    public void A_level_with_no_intermediate_point_ends_the_order()
    {
        var ring = new List<GridPoint>();
        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == 2)
                {
                    ring.Add(new GridPoint(60 + dx, 100 + dy));
                }
            }
        }

        var (state, dispatcher) = Setup(8, sea: ring, blockers: false, origin: new GridPoint(60, 100));

        var army = dispatcher.Dispatch(state, March(66, 100)).State.ArmyById(Army)!;

        Assert.Equal((61, 100, 7), (army.X, army.Y, army.Moves));
    }

    private static int Chebyshev(GridPoint a, GridPoint b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static MoveArmyCommand March(int x, int y) =>
        new("rome", Army, x, y, MoveWalkPolicy.AiContinuation, x, y);

    private static World PlainWorld(IReadOnlyCollection<GridPoint> sea)
    {
        var world = AiTestbed.Classical.World;
        var plain = world.TileTypes.First(t => t.Id == "plain").Code;
        var water = world.TileTypes.First(t => t.Id == "sea_calm").Code;
        var cells = Enumerable.Repeat(plain, world.Width * world.Height).ToArray();
        foreach (var point in sea)
        {
            cells[(point.Y * world.Width) + point.X] = water;
        }

        return world with
        {
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))),
        };
    }

    private static (GameState State, CommandDispatcher Dispatcher) Setup(
        int moves, IReadOnlyCollection<GridPoint>? sea = null, bool blockers = true, GridPoint? origin = null)
    {
        var world = PlainWorld(sea ?? Array.Empty<GridPoint>());
        var plain = world.TileTypes.First(t => t.Id == "plain").Code;
        var initial = new IC2.Engine.Presentation.GameSession(
            world, AiTestbed.Classical.Ruleset, AiTestbed.Classical.Scenario, seedOverride: 1).State;
        var template = initial.Armies.First(a => a.Nation == "rome");
        var start = origin ?? new GridPoint(100, 40);
        var marcher = template with { Id = Army, X = start.X, Y = start.Y, Moves = moves, CoveredTileCode = plain, AboardFleetId = null };
        var armies = new List<ArmyState> { marcher };
        if (blockers)
        {
            armies.AddRange(new[] { 39, 40, 41 }.Select(y =>
                template with { Id = "rome-block-" + y, X = 101, Y = y, CoveredTileCode = plain, AboardFleetId = null }));
        }

        var state = initial with
        {
            Armies = ValueList.From(armies),
            Fleets = ValueList<FleetState>.Empty,
            Cities = ValueList<CityState>.Empty,
            ActiveSeatIndex = initial.TurnOrder.ToList().IndexOf("rome"),
            Nations = ValueList.From(initial.Nations.Select(n => n.Id == "rome" ? n with { Control = SeatControl.Ai } : n)),
        };
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), AiTestbed.Classical.Ruleset, world, NullEventSink.Instance);
        return (state, dispatcher);
    }
}
