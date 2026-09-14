using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using Xunit;

namespace IC2.Engine.Tests.Movement;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T09 Movement and terrain", Done When 3 and 5, plus the general
/// terrain-driven-cost behaviour Done When 1 depends on end to end.
/// </summary>
public sealed class MovementWalkerTests
{
    /// <summary>
    /// Done When 3: "A city, army or fleet marker in the path blocks the walk entirely (the walk stops
    /// before the marker; no move cost is charged for it)."
    /// </summary>
    /// <remarks>
    /// Walks the toy grid's row y=2 from (1,2) to (6,2), straight through the Portus city at (5,2). Row
    /// 2 is <c>[0,2,6,2,2,2,4,0]</c>, so the cells actually entered before the block are (2,2) River
    /// (cost 4), (3,2) Plain (cost 1), (4,2) Plain (cost 1) — 6 moves total, none of it charged for
    /// Portus itself.
    /// </remarks>
    [Fact]
    public void Walk_MarkerInPath_StopsBeforeTheMarkerAndChargesNothingForIt()
    {
        var portus = MovementTestbed.CityPosition("portus");
        Assert.Equal(new GridPoint(5, 2), portus);

        var from = new GridPoint(1, 2);
        var to = new GridPoint(6, 2);
        var sink = new RecordingEventSink();

        var result = MovementWalker.Walk(
            from,
            to,
            movesAvailable: 20,
            MovementTestbed.Ruleset.Terrain,
            MovementTestbed.TileTypeIdAt,
            isBlocked: cell => cell == portus,
            sink);

        Assert.Equal(MovementStopReason.BlockedByMarker, result.StopReason);
        Assert.Equal(portus, result.BlockedAt);

        var expectedEntered = new[]
        {
            new GridPoint(2, 2),
            new GridPoint(3, 2),
            new GridPoint(4, 2),
        };
        Assert.Equal(expectedEntered, result.EnteredCells);
        Assert.Equal(new GridPoint(4, 2), result.FinalPosition);

        // River (4) + Plain (1) + Plain (1) = 6; nothing charged for the blocked cell itself.
        Assert.Equal(6, result.MovesSpent);
        Assert.Equal(20 - 6, result.MovesRemaining);
    }

    /// <summary>A marker occupying the very first step blocks the walk before anything is entered.</summary>
    [Fact]
    public void Walk_MarkerOnFirstStep_StopsImmediatelyWithNoCellsEntered()
    {
        var from = new GridPoint(1, 1);
        var to = new GridPoint(4, 1);
        var blockedCell = new GridPoint(2, 1);
        var sink = new RecordingEventSink();

        var result = MovementWalker.Walk(
            from, to, movesAvailable: 20, MovementTestbed.Ruleset.Terrain,
            MovementTestbed.TileTypeIdAt, cell => cell == blockedCell, sink);

        Assert.Equal(MovementStopReason.BlockedByMarker, result.StopReason);
        Assert.Equal(blockedCell, result.BlockedAt);
        Assert.Empty(result.EnteredCells);
        Assert.False(result.Moved);
        Assert.Equal(0, result.MovesSpent);
        Assert.Equal(20, result.MovesRemaining);
        Assert.Equal(from, result.FinalPosition);
    }

    /// <summary>A walk with no blockers and enough moves reaches every cell of the traced path.</summary>
    [Fact]
    public void Walk_NoBlockersEnoughMoves_ReachesDestination()
    {
        var from = new GridPoint(1, 2);
        var to = new GridPoint(4, 2);
        var sink = new RecordingEventSink();

        var result = MovementWalker.Walk(
            from, to, movesAvailable: 10, MovementTestbed.Ruleset.Terrain,
            MovementTestbed.TileTypeIdAt, isBlocked: static _ => false, sink);

        Assert.Equal(MovementStopReason.ReachedDestination, result.StopReason);
        Assert.Null(result.BlockedAt);
        Assert.Equal(to, result.FinalPosition);
        Assert.Equal(3, result.EnteredCells.Count); // (2,2) river 4 + (3,2) plain 1 + (4,2) plain 1
        Assert.Equal(6, result.MovesSpent);
        Assert.Equal(4, result.MovesRemaining);
        Assert.Empty(sink.Events);
    }

    /// <summary>
    /// Done When 5: "A terrain type present in a world but absent from the ruleset's cost table defaults
    /// to 1 ... with a warning event."
    /// </summary>
    [Fact]
    public void Walk_TileTypeAbsentFromRulesetTable_DefaultsAndPublishesWarning()
    {
        var terrain = new TerrainRules(
            ValueList.Of(new TerrainMoveCost("plain", 1)),
            DefaultMoveCost: 1);

        var from = new GridPoint(0, 0);
        var to = new GridPoint(1, 0);
        var sink = new RecordingEventSink();

        var result = MovementWalker.Walk(
            from, to, movesAvailable: 5, terrain,
            tileTypeIdAt: static _ => "swamp", // a tile type this custom world defines but this ruleset never priced
            isBlocked: static _ => false,
            sink);

        Assert.Equal(MovementStopReason.ReachedDestination, result.StopReason);
        Assert.Equal(1, result.MovesSpent); // defaultMoveCost, not a hardcoded engine literal
        Assert.Equal(4, result.MovesRemaining);

        var warning = Assert.Single(sink.Events);
        var unpriced = Assert.IsType<UnpricedTerrainEncountered>(warning);
        Assert.Equal("swamp", unpriced.TileTypeId);
        Assert.Equal(1, unpriced.X);
        Assert.Equal(0, unpriced.Y);
        Assert.Equal(1, unpriced.FallbackMoveCost);
        Assert.False(unpriced.IsNewsWorthy);
    }

    /// <summary>A tile type the ruleset does price never raises the warning event.</summary>
    [Fact]
    public void Walk_TileTypeInRulesetTable_NeverPublishesWarning()
    {
        var sink = new RecordingEventSink();

        var result = MovementWalker.Walk(
            new GridPoint(1, 2), new GridPoint(2, 2), movesAvailable: 10,
            MovementTestbed.Ruleset.Terrain, MovementTestbed.TileTypeIdAt, static _ => false, sink);

        Assert.Equal(MovementStopReason.ReachedDestination, result.StopReason);
        Assert.Empty(sink.Events);
    }

    /// <summary>An unaffordable step aborts before entering that cell and charges nothing for it.</summary>
    [Fact]
    public void Walk_UnaffordableStep_AbortsBeforeThatCellWithNoChargeForIt()
    {
        // Row y=2 from (1,2): (2,2) is River, cost 4. A budget of 3 cannot afford it.
        var sink = new RecordingEventSink();

        var result = MovementWalker.Walk(
            new GridPoint(1, 2), new GridPoint(4, 2), movesAvailable: 3,
            MovementTestbed.Ruleset.Terrain, MovementTestbed.TileTypeIdAt, static _ => false, sink);

        Assert.Equal(MovementStopReason.InsufficientMoves, result.StopReason);
        Assert.Empty(result.EnteredCells);
        Assert.Equal(0, result.MovesSpent);
        Assert.Equal(3, result.MovesRemaining);
    }

    [Fact]
    public void Walk_ZeroMovesAvailable_AbortsImmediately()
    {
        var sink = new RecordingEventSink();

        var result = MovementWalker.Walk(
            new GridPoint(1, 2), new GridPoint(4, 2), movesAvailable: 0,
            MovementTestbed.Ruleset.Terrain, MovementTestbed.TileTypeIdAt, static _ => false, sink);

        Assert.Equal(MovementStopReason.InsufficientMoves, result.StopReason);
        Assert.False(result.Moved);
        Assert.Equal(0, result.MovesRemaining);
    }

    [Fact]
    public void Walk_OriginEqualsDestination_ReachesDestinationWithoutSpendingMoves()
    {
        var here = new GridPoint(1, 2);
        var sink = new RecordingEventSink();

        var result = MovementWalker.Walk(
            here, here, movesAvailable: 10,
            MovementTestbed.Ruleset.Terrain, MovementTestbed.TileTypeIdAt, static _ => false, sink);

        Assert.Equal(MovementStopReason.ReachedDestination, result.StopReason);
        Assert.Empty(result.EnteredCells);
        Assert.Equal(0, result.MovesSpent);
        Assert.Equal(10, result.MovesRemaining);
        Assert.Equal(here, result.FinalPosition);
    }

    [Fact]
    public void Walk_NegativeMovesAvailable_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementWalker.Walk(
            new GridPoint(0, 0), new GridPoint(1, 0), movesAvailable: -1,
            MovementTestbed.Ruleset.Terrain, MovementTestbed.TileTypeIdAt, static _ => false,
            new RecordingEventSink()));
    }

    [Fact]
    public void Walk_UnmappedCell_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => MovementWalker.Walk(
            new GridPoint(0, 0), new GridPoint(1, 0), movesAvailable: 10,
            MovementTestbed.Ruleset.Terrain, tileTypeIdAt: static _ => null, static _ => false,
            new RecordingEventSink()));
    }
}
