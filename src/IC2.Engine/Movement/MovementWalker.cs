using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Movement;

/// <summary>Why a walk stopped before reaching every cell of its traced path.</summary>
public enum MovementStopReason
{
    /// <summary>The walk entered every cell up to and including the destination.</summary>
    ReachedDestination,

    /// <summary>
    /// A city, army or fleet marker occupied the next cell. The walk stops before that cell — no move
    /// cost is charged for it <strong>[confirmed: terrain-move-cost-table-in-dat.md]</strong>.
    /// </summary>
    BlockedByMarker,

    /// <summary>
    /// The next cell's move cost exceeded the moves remaining. The walk stops before that cell; whether
    /// the mover's remaining moves are then left alone or zeroed is <see cref="MovementAbortRule"/>'s
    /// decision, not this type's.
    /// </summary>
    InsufficientMoves,
}

/// <summary>What one call to <see cref="MovementWalker.Walk"/> produced.</summary>
/// <param name="Origin">The cell the walk started from.</param>
/// <param name="EnteredCells">
/// The cells actually entered, in order, <em>excluding</em> <see cref="Origin"/> — the mover already
/// stands there and pays nothing to remain. Empty when the walk stopped at the first step, or when
/// <see cref="Origin"/> already equals the destination.
/// </param>
/// <param name="MovesSpent">Total move cost charged across <see cref="EnteredCells"/>.</param>
/// <param name="MovesRemaining">
/// Moves left after <see cref="MovesSpent"/> is deducted from the budget the walk was given — before any
/// <see cref="MovementAbortRule"/> zeroing on an aborted move.
/// </param>
/// <param name="StopReason">Why the walk stopped where it did.</param>
/// <param name="BlockedAt">
/// The occupied cell that stopped the walk, when <see cref="StopReason"/> is
/// <see cref="MovementStopReason.BlockedByMarker"/>; <see langword="null"/> otherwise.
/// </param>
public sealed record MovementWalkResult(
    GridPoint Origin,
    IReadOnlyList<GridPoint> EnteredCells,
    int MovesSpent,
    int MovesRemaining,
    MovementStopReason StopReason,
    GridPoint? BlockedAt)
{
    /// <summary>Whether the walk entered at least one cell before stopping.</summary>
    public bool Moved => EnteredCells.Count > 0;

    /// <summary>The cell the mover ends the walk on: the last entered cell, or <see cref="Origin"/>.</summary>
    public GridPoint FinalPosition => EnteredCells.Count > 0 ? EnteredCells[^1] : Origin;
}

/// <summary>
/// The terrain-table-driven walker: <see cref="BresenhamPath"/>'s straight line, priced cell by cell
/// through the loaded <see cref="Ruleset"/>'s terrain table, stopping at a blocking marker or an
/// unaffordable step.
/// </summary>
/// <remarks>
/// <para>
/// Shared by armies (this task) and fleets (T14) — <c>docs/task-catalogue.md</c> "T09 Movement and
/// terrain": "the walker is terrain-table-driven and does not special-case sea." A fleet's caller simply
/// supplies a <paramref name="isBlocked"/>/tile-lookup pair drawn from sea-only tiles; nothing in this
/// type reads or branches on which tile types happen to be water.
/// </para>
/// <para>
/// <strong>Blocking is occupancy, not a decoded map-cell code.</strong> The original overlays a marker
/// (a city, army or fleet) onto the same cell array the terrain lives in, so a "blocked" cell reads as a
/// numeric code outside the terrain range (city 20-99, army 200-247, fleet 300-347 — the ranges the T04
/// corpus carries as <c>movement.blockingMarkerRange.*</c>, for the DAT/SAV import tasks that actually
/// parse those bytes). This engine keeps terrain (<see cref="Model.World.Terrain"/>) and occupancy
/// (<see cref="Model.GameState"/>'s cities, armies and fleets) as separate data, so a caller here answers
/// "is anything standing on this cell" directly rather than this type re-deriving it from a marker-code
/// range that does not exist in the reimplemented grid.
/// </para>
/// </remarks>
public static class MovementWalker
{
    /// <summary>Walks the straight-line path from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">The mover's current cell.</param>
    /// <param name="to">The destination cell.</param>
    /// <param name="movesAvailable">The mover's moves budget for this order. May be zero.</param>
    /// <param name="terrain">The loaded ruleset's terrain rules — the source of every move cost.</param>
    /// <param name="tileTypeIdAt">
    /// Resolves a cell to the tile type id occupying it (<see cref="Model.TileType.Id"/>). Called only for
    /// cells the walk actually reaches, never for <paramref name="from"/>.
    /// </param>
    /// <param name="isBlocked">
    /// Whether a city, army or fleet marker occupies a cell. Called before pricing that cell, so a
    /// blocked cell is never priced or charged for.
    /// </param>
    /// <param name="events">
    /// Where <see cref="UnpricedTerrainEncountered"/> is published when a reached cell's tile type has no
    /// entry in <paramref name="terrain"/>'s table.
    /// </param>
    /// <exception cref="ArgumentNullException">Any reference parameter is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="movesAvailable"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="tileTypeIdAt"/> returns <see langword="null"/> for a reached cell — an unmapped
    /// cell is a caller error (a world/terrain-grid mismatch), not something this pure function can price.
    /// </exception>
    public static MovementWalkResult Walk(
        GridPoint from,
        GridPoint to,
        int movesAvailable,
        TerrainRules terrain,
        Func<GridPoint, string?> tileTypeIdAt,
        Func<GridPoint, bool> isBlocked,
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(tileTypeIdAt);
        ArgumentNullException.ThrowIfNull(isBlocked);
        ArgumentNullException.ThrowIfNull(events);
        if (movesAvailable < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(movesAvailable), movesAvailable, "Moves available may not be negative.");
        }

        var path = BresenhamPath.Trace(from, to);
        var entered = new List<GridPoint>();
        var movesLeft = movesAvailable;

        // path[0] == from: the mover already stands there, so pricing and blocking start at path[1].
        for (var i = 1; i < path.Count; i++)
        {
            var cell = path[i];

            if (isBlocked(cell))
            {
                return new MovementWalkResult(
                    from, entered, movesAvailable - movesLeft, movesLeft, MovementStopReason.BlockedByMarker, cell);
            }

            var tileTypeId = tileTypeIdAt(cell)
                ?? throw new InvalidOperationException(
                    $"No tile type resolves at {cell}; the walker cannot price a cell the world does not map.");

            var lookup = TerrainCostLookup.MoveCostFor(terrain, tileTypeId);
            if (!lookup.WasPricedByRuleset)
            {
                events.Publish(new UnpricedTerrainEncountered(tileTypeId, cell.X, cell.Y, lookup.MoveCost));
            }

            if (lookup.MoveCost > movesLeft)
            {
                return new MovementWalkResult(
                    from, entered, movesAvailable - movesLeft, movesLeft, MovementStopReason.InsufficientMoves, null);
            }

            movesLeft -= lookup.MoveCost;
            entered.Add(cell);
        }

        return new MovementWalkResult(
            from, entered, movesAvailable - movesLeft, movesLeft, MovementStopReason.ReachedDestination, null);
    }
}
