using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Ai;
using Xunit;

namespace IC2.Engine.Tests.Movement.Commands;

/// <summary>
/// T238 (#1585): after a human move order, the original walks once more on a fresh line when the first
/// walk ends short with moves left (<c>TUnitMap_MoveHumanArmy</c>, <c>TUnitMap_MoveHumanFleet</c>); the AI's
/// orders walk once. Research <c>2026-10-29-walk-stops-at-own-city-on-line230.md</c> and
/// <c>2026-10-30-paths-in-play-and-fleet-walks42.md</c> (Wine and emulator).
/// </summary>
public sealed class HumanSecondWalkTests
{
    private static GameSession RomeSession() =>
        new(AiTestbed.Classical.World, AiTestbed.Classical.Ruleset, AiTestbed.Classical.Scenario,
            seedOverride: 1, humanSeatNationId: "rome");

    /// <summary>
    /// The research's Venusia case from the new Rome game: army 1 at (120,53) with 8 moves. The first walk
    /// stops at (118,52) beside Venusia (118,51), Rome's own city; the second walk's fresh line passes it.
    /// The original ends at (115,49) with 1 move left, and at (116,49) with 3 left.
    /// </summary>
    [Theory]
    [InlineData(99, 32, 115, 49, 1, "army-1 moved from (120,53) to (115,49), spending 7 of 8 moves.")]
    [InlineData(116, 49, 116, 49, 3, "army-1 moved from (120,53) to (116,49), spending 5 of 8 moves.")]
    public void A_human_army_order_walks_past_venusia_as_the_original_does(
        int x, int y, int endX, int endY, int movesLeft, string receipt)
    {
        var session = RomeSession();
        Assert.Equal("rome", session.State.ActiveNationId);

        var output = session.Submit($"move army-1 {x} {y}");

        var army = session.State.ArmyById("army-1")!;
        Assert.Equal((endX, endY, movesLeft), (army.X, army.Y, army.Moves));
        Assert.Single(output.Lines, line => line.Contains(" moved from ", StringComparison.Ordinal));
        Assert.Contains(receipt, output.Lines);
        var route = Assert.Single(output.StrategicTimeline.Steps.SelectMany(s => s.Routes), r => r.EntityId == "army-1");
        Assert.Equal(new GridPoint(120, 53), route.Origin);
        Assert.Equal(new GridPoint(endX, endY), route.Destination);
        Assert.Equal(new GridPoint(118, 52), route.EnteredCells[1]);
    }

    /// <summary>An AI order (the default policy) still walks once: the remake's old (118,52), 6 left.</summary>
    [Fact]
    public void An_ai_order_walks_once()
    {
        var session = RomeSession();
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), session.Ruleset, session.World, NullEventSink.Instance);

        var result = dispatcher.Dispatch(session.State, new MoveArmyCommand("rome", "army-1", 99, 32));

        var army = result.State.ArmyById("army-1")!;
        Assert.Equal((118, 52, 6), (army.X, army.Y, army.Moves));
    }

    /// <summary>
    /// A first walk stopped by an unaffordable step: mountains (armies) or rough sea (fleets) at (1,2) cost
    /// more than the 2 moves left at (1,1), and a human keeps them under the faithful rule. The second walk's
    /// fresh line toward (2,3) avoids the cell.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void After_an_unaffordable_step_the_second_walk_reaches_the_target(bool fleet)
    {
        var session = Session(fleet, moves: 3, SeatAsymmetryModel.Faithful, costlyCell: true);

        Assert.Equal((1, 1, 2), Single(session, fleet, 2, 3));
        var output = session.Submit(Line(fleet, 2, 3));

        Assert.Equal((2, 3, 0), Position(session.State, fleet));
        var route = StrategicTravelObservationTests.Route(output);
        Assert.Equal(new GridPoint(1, 1), route.EnteredCells[0]);
        Assert.Equal(new GridPoint(2, 3), route.Destination);
        Assert.Equal(MovementStopReason.ReachedDestination, route.StopReason);
    }

    /// <summary>Under the normalized rule a human's aborted step zeroes the moves, so no second walk follows.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_zeroed_abort_leaves_nothing_for_a_second_walk(bool fleet)
    {
        var session = Session(fleet, moves: 3, SeatAsymmetryModel.Normalized, costlyCell: true);

        session.Submit(Line(fleet, 2, 3));

        Assert.Equal((1, 1, 0), Position(session.State, fleet));
    }

    /// <summary>
    /// A first walk stopped by a marker at (1,0): it side-steps to (1,1) and ends there with 8 moves; the
    /// second walk's fresh line reaches (3,1).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void After_a_marker_stops_the_first_walk_the_second_walk_reaches_the_target(bool fleet)
    {
        var session = Session(fleet, moves: 10, SeatAsymmetryModel.Faithful, blocker: (1, 0));

        Assert.Equal((1, 1, 8), Single(session, fleet, 3, 1));
        var output = session.Submit(Line(fleet, 3, 1));

        Assert.Equal((3, 1, 6), Position(session.State, fleet));
        Assert.Single(output.StrategicTimeline.Steps.SelectMany(s => s.Routes));
    }

    /// <summary>
    /// An order onto the marker itself: the first walk stops beside it, and the second is blocked as well,
    /// so the order ends where one walk would.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_second_walk_that_is_blocked_too_changes_nothing(bool fleet)
    {
        var session = Session(fleet, moves: 10, SeatAsymmetryModel.Faithful, blocker: (2, 1));

        Assert.Equal((1, 1, 9), Single(session, fleet, 2, 1));
        session.Submit(Line(fleet, 2, 1));

        Assert.Equal((1, 1, 9), Position(session.State, fleet));
    }

    private static GameSession Session(
        bool fleet, int moves, SeatAsymmetryModel asymmetry, bool costlyCell = false, (int X, int Y)? blocker = null)
    {
        World ChangeWorld(World world)
        {
            if (!costlyCell)
            {
                return world;
            }

            var costly = world.TileTypes.First(t => t.Id == (fleet ? "sea_rough" : "mountains")).Code;
            var cells = world.Terrain.Decode(world.Width, world.Height).ToArray();
            cells[(2 * world.Width) + 1] = costly;
            return world with
            {
                Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))),
            };
        }

        GameState Change(GameState state) => blocker is not { } at
            ? state
            : fleet
                ? state with { Fleets = ValueList.From(state.Fleets.Append(state.Fleets[0] with { Id = "g", X = at.X, Y = at.Y })) }
                : state with { Armies = ValueList.From(state.Armies.Append(state.Armies[0] with { Id = "b", X = at.X, Y = at.Y })) };

        return StrategicTravelObservationTests.Session(
            fleet, moves, SeatControl.Human, asymmetry, change: Change, changeWorld: ChangeWorld);
    }

    private static string Line(bool fleet, int x, int y) => fleet ? $"move-fleet f {x} {y}" : $"move a {x} {y}";

    /// <summary>Where the same order ends with the single-walk policy, dispatched outside the session.</summary>
    private static (int, int, int) Single(GameSession session, bool fleet, int x, int y)
    {
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), session.Ruleset, session.World, NullEventSink.Instance);
        ICommand command = fleet ? new MoveFleetCommand("north", "f", x, y) : new MoveArmyCommand("north", "a", x, y);
        var result = dispatcher.Dispatch(session.State, command);
        Assert.True(result.IsAccepted, result.ToString());
        return Position(result.State, fleet);
    }

    private static (int, int, int) Position(GameState state, bool fleet) => fleet
        ? (state.Fleets[0].X, state.Fleets[0].Y, state.Fleets[0].Moves)
        : (state.Armies[0].X, state.Armies[0].Y, state.Armies[0].Moves);
}
