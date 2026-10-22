using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using Xunit;

namespace IC2.Engine.Tests.Movement;

/// <summary>
/// #1012: the original's single strategic step (<c>FUN_0044D420</c>, helper <c>FUN_0044D31C</c>), checked
/// against the bounded original-instruction controls of
/// <c>2026-10-21-strategic-lateral-steps-and-callers.md</c> (research <c>f698190</c>, "Bounded
/// original-instruction controls"). Every control starts at U(10,10) with 8 moves on an open 20 × 20 map; a
/// northward control walks toward (10,5), so the first cursor is P(10,9). The expected first cell and the
/// moves left after it are the report's table, value for value.
/// </summary>
public sealed class MovementWalkerLateralStepTests
{
    private static readonly GridPoint Start = new(10, 10);
    private static readonly GridPoint North = new(10, 5);
    private static readonly GridPoint Size = new(20, 20);

    private static MovementWalkResult Step(
        GridPoint to,
        IReadOnlyDictionary<GridPoint, string> tiles,
        string ground = "plain",
        int moves = 8,
        Func<GridPoint, bool>? isBlocked = null,
        StrategicStepPolicy step = StrategicStepPolicy.LateralAlternatives) =>
        MovementWalker.Walk(
            Start, to, moves, MovementTestbed.Ruleset.Terrain,
            cell => tiles.TryGetValue(cell, out var id) ? id : ground,
            isBlocked ?? (_ => false),
            NullEventSink.Instance,
            step: step,
            worldSize: Size);

    private static int Cost(string tileTypeId) =>
        TerrainCostLookup.MoveCostFor(MovementTestbed.Ruleset.Terrain, tileTypeId).MoveCost;

    private static void AssertFirstStep(MovementWalkResult walk, GridPoint cell, int movesLeft, string tileTypeId)
    {
        Assert.True(walk.Moved, "the walk entered no cell");
        Assert.Equal(cell, walk.EnteredCells[0]);
        Assert.Equal(movesLeft, 8 - Cost(tileTypeId));
    }

    [Fact]
    public void A_forest_on_the_line_is_left_for_the_first_of_two_plain_alternatives()
    {
        var walk = Step(North, new Dictionary<GridPoint, string> { [new(10, 9)] = "forest" });
        AssertFirstStep(walk, new GridPoint(11, 9), 7, "plain");
    }

    [Fact]
    public void All_three_plain_the_line_wins_the_tie()
    {
        var walk = Step(North, new Dictionary<GridPoint, string>());
        AssertFirstStep(walk, new GridPoint(10, 9), 7, "plain");
    }

    [Fact]
    public void A_mountain_on_the_line_takes_the_strictly_cheaper_second_alternative()
    {
        var walk = Step(North, new Dictionary<GridPoint, string>
        {
            [new(10, 9)] = "mountains",
            [new(11, 9)] = "forest",
            [new(9, 9)] = "plain",
        });
        AssertFirstStep(walk, new GridPoint(9, 9), 7, "plain");
    }

    [Fact]
    public void An_occupied_first_alternative_costs_blocked_and_the_second_is_taken()
    {
        var occupied = new GridPoint(11, 9);
        var walk = Step(
            North, new Dictionary<GridPoint, string> { [new(10, 9)] = "forest" }, isBlocked: cell => cell == occupied);
        AssertFirstStep(walk, new GridPoint(9, 9), 7, "plain");
    }

    [Fact]
    public void A_forest_destination_is_entered_without_the_comparison()
    {
        var goal = new GridPoint(10, 9);
        var walk = Step(goal, new Dictionary<GridPoint, string> { [goal] = "forest" });
        Assert.Equal(MovementStopReason.ReachedDestination, walk.StopReason);
        Assert.Equal(new[] { goal }, walk.EnteredCells);
        Assert.Equal(6, walk.MovesRemaining);
    }

    [Fact]
    public void Every_candidate_unaffordable_aborts_with_no_retry_and_the_abort_rule_decides_the_moves()
    {
        var walk = Step(North, new Dictionary<GridPoint, string>(), ground: "mountains", moves: 3);
        Assert.Equal(MovementStopReason.InsufficientMoves, walk.StopReason);
        Assert.False(walk.Moved);
        Assert.Equal(3, walk.MovesRemaining);
        Assert.Equal(3, MovementAbortRule.MovesAfterAbortedStep(3, SeatControl.Human, SeatAsymmetryModel.Faithful));
        Assert.Equal(0, MovementAbortRule.MovesAfterAbortedStep(3, SeatControl.Ai, SeatAsymmetryModel.Faithful));
    }

    [Fact]
    public void A_diagonal_x_major_step_takes_the_x_alternative_on_a_tie()
    {
        // (10,10) -> (15,13): X-major; the first cursor is (11,11).
        var walk = Step(new GridPoint(15, 13), new Dictionary<GridPoint, string> { [new(11, 11)] = "forest" });
        AssertFirstStep(walk, new GridPoint(11, 10), 7, "plain");
    }

    [Fact]
    public void A_diagonal_y_major_step_takes_the_y_alternative_on_a_tie()
    {
        // (10,10) -> (13,15): Y-major; the first cursor is (11,11).
        var walk = Step(new GridPoint(13, 15), new Dictionary<GridPoint, string> { [new(11, 11)] = "forest" });
        AssertFirstStep(walk, new GridPoint(10, 11), 7, "plain");
    }

    [Fact]
    public void A_fleet_leaves_rough_sea_for_a_calm_alternative()
    {
        var walk = Step(North, new Dictionary<GridPoint, string> { [new(10, 9)] = "sea_rough" }, ground: "sea_calm");
        AssertFirstStep(walk, new GridPoint(11, 9), 7, "sea_calm");
    }

    [Fact]
    public void A_fleet_enters_a_rough_sea_destination_directly()
    {
        var goal = new GridPoint(10, 9);
        var walk = Step(goal, new Dictionary<GridPoint, string> { [goal] = "sea_rough" }, ground: "sea_calm");
        Assert.Equal(new[] { goal }, walk.EnteredCells);
        Assert.Equal(5, walk.MovesRemaining);
    }

    [Fact]
    public void A_city_on_the_line_is_stepped_around_and_the_walk_goes_on()
    {
        // The 0-move orders of #1012: a marker on the line's first cell no longer stops the walk.
        var city = new GridPoint(10, 9);
        var walk = Step(North, new Dictionary<GridPoint, string>(), isBlocked: cell => cell == city);
        Assert.Equal(new GridPoint(11, 9), walk.EnteredCells[0]);
        Assert.Equal(MovementStopReason.ReachedDestination, walk.StopReason);
        Assert.Equal(North, walk.FinalPosition);
        Assert.DoesNotContain(city, walk.EnteredCells);
    }

    [Fact]
    public void A_marker_on_the_destination_still_stops_the_walk_beside_it()
    {
        var walk = Step(North, new Dictionary<GridPoint, string>(), isBlocked: cell => cell == North);
        Assert.Equal(MovementStopReason.BlockedByMarker, walk.StopReason);
        Assert.Equal(North, walk.BlockedAt);
        Assert.Equal(new GridPoint(10, 6), walk.FinalPosition);
    }

    [Fact]
    public void The_step_helper_refuses_row_and_column_zero_under_the_original_step()
    {
        // Walking west along row 0 from (3,0): FUN_0044D31C refuses every y = 0 cell, so the cursor (2,0)
        // costs "blocked" and the step takes the open alternative (2,1); the destination (1,0) is then
        // chosen without the comparison and refused, so the walk stops beside it.
        var destination = new GridPoint(1, 0);
        var walk = MovementWalker.Walk(
            new GridPoint(3, 0), destination, 8, MovementTestbed.Ruleset.Terrain, _ => "plain", _ => false,
            NullEventSink.Instance, step: StrategicStepPolicy.LateralAlternatives, worldSize: Size);
        Assert.Equal(new[] { new GridPoint(2, 1) }, walk.EnteredCells);
        Assert.Equal(MovementStopReason.BlockedByMarker, walk.StopReason);
        Assert.Equal(destination, walk.BlockedAt);
    }

    [Fact]
    public void The_straight_line_keeps_a_forest_on_the_line_but_steps_around_a_marker()
    {
        var forest = Step(
            North, new Dictionary<GridPoint, string> { [new(10, 9)] = "forest" }, step: StrategicStepPolicy.StraightLine);
        AssertFirstStep(forest, new GridPoint(10, 9), 6, "forest");

        var city = new GridPoint(10, 9);
        var around = Step(
            North, new Dictionary<GridPoint, string>(), isBlocked: cell => cell == city, step: StrategicStepPolicy.StraightLine);
        Assert.Equal(new GridPoint(11, 9), around.EnteredCells[0]);
        Assert.Equal(North, around.FinalPosition);
    }
}
