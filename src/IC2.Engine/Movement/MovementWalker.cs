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
    /// <param name="moveCostOverride">
    /// T81: a per-cell price that replaces the terrain table's, or <see langword="null"/> (the default)
    /// to price every cell from the table. Returns <see langword="null"/> for a cell the table prices.
    /// A fleet entering a rough-sea cell pays <see cref="WeatherEventRules.RoughSeaMoveCost"/> through it
    /// (<c>FUN_0044dcac</c>, decompiled-map-code1-overlay.md §2). Called only for a cell that was not blocked.
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
        IEventSink events,
        Func<GridPoint, int?>? moveCostOverride = null,
        StrategicStepPolicy step = StrategicStepPolicy.StraightLine,
        GridPoint? worldSize = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(tileTypeIdAt);
        ArgumentNullException.ThrowIfNull(isBlocked);
        ArgumentNullException.ThrowIfNull(events);
        if (movesAvailable < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(movesAvailable), movesAvailable, "Moves available may not be negative.");
        }

        // #1012 (research f698190, 2026-10-21-strategic-lateral-steps-and-callers.md): the Bresenham cursor
        // P advances along the line independently of the mover U; each step prices P and two lateral
        // alternatives (FUN_0044D420), every candidate costing "blocked" unless it is open, unoccupied and
        // within one cell of both P and U (FUN_0044D31C).
        var path = BresenhamPath.Trace(from, to);
        var entered = new List<GridPoint>();
        var movesLeft = movesAvailable;
        var mover = from;
        var signX = Math.Sign(to.X - from.X);
        var signY = Math.Sign(to.Y - from.Y);
        var yMajor = Math.Abs(to.Y - from.Y) > Math.Abs(to.X - from.X);
        var lateral = step == StrategicStepPolicy.LateralAlternatives;

        int TerrainCost(GridPoint cell)
        {
            if (moveCostOverride?.Invoke(cell) is { } price)
            {
                return price;
            }

            var tileTypeId = tileTypeIdAt(cell)
                ?? throw new InvalidOperationException(
                    $"No tile type resolves at {cell}; the walker cannot price a cell the world does not map.");
            var lookup = TerrainCostLookup.MoveCostFor(terrain, tileTypeId);
            if (!lookup.WasPricedByRuleset)
            {
                events.Publish(new UnpricedTerrainEncountered(tileTypeId, cell.X, cell.Y, lookup.MoveCost));
            }

            return lookup.MoveCost;
        }

        bool Open(GridPoint cell, GridPoint cursor)
        {
            if (lateral && worldSize is { } size
                && !(cell.X > 0 && cell.X < size.X && cell.Y > 0 && cell.Y < size.Y))
            {
                return false;
            }

            return Chebyshev(cell, cursor) <= 1 && Chebyshev(cell, mover) <= 1 && !isBlocked(cell);
        }

        // path[0] == from: the mover already stands there, so pricing and blocking start at path[1].
        for (var i = 1; i < path.Count; i++)
        {
            var cursor = path[i];
            var chosen = cursor;
            if (cursor != to)
            {
                var (first, second) = Alternatives(cursor, mover, signX, signY, yMajor);
                var cursorOpen = Open(cursor, cursor);
                // Straight line: the cursor is taken whenever it is open, so only a blocked cursor (cost 10
                // in the original's terms) lets an alternative in. Lateral: strictly cheaper only.
                var costCursor = cursorOpen ? (lateral ? TerrainCost(cursor) : 0) : BlockedCost;
                var costFirst = Open(first, cursor) ? TerrainCost(first) : BlockedCost;
                var costSecond = Open(second, cursor) ? TerrainCost(second) : BlockedCost;
                if (costFirst < costCursor || costSecond < costCursor)
                {
                    chosen = costSecond < costFirst ? second : first;
                }
            }

            // The original re-checks the chosen cell in this order (FUN_0044D420 after selection): a
            // terrain/marker code it cannot enter, then the cost against the moves left (only this abort
            // zeroes an AI seat's moves), then the helper's reach and bounds.
            if (isBlocked(chosen))
            {
                return new MovementWalkResult(
                    from, entered, movesAvailable - movesLeft, movesLeft, MovementStopReason.BlockedByMarker, chosen);
            }

            var moveCost = TerrainCost(chosen);
            if (moveCost > movesLeft)
            {
                return new MovementWalkResult(
                    from, entered, movesAvailable - movesLeft, movesLeft, MovementStopReason.InsufficientMoves, null);
            }

            if (!Open(chosen, cursor))
            {
                return new MovementWalkResult(
                    from, entered, movesAvailable - movesLeft, movesLeft, MovementStopReason.BlockedByMarker, chosen);
            }

            movesLeft -= moveCost;
            entered.Add(chosen);
            mover = chosen;
        }

        return new MovementWalkResult(
            from, entered, movesAvailable - movesLeft, movesLeft, MovementStopReason.ReachedDestination, null);
    }

    /// <summary>The original step helper's "blocked" cost (<c>FUN_0044D31C</c> starts at 10).</summary>
    private const int BlockedCost = 10;

    /// <summary>
    /// #1012: the two lateral alternatives <c>FUN_0044D420</c> prices against the cursor, in order, the
    /// first winning a tie: beside the cursor when the line is axis-aligned, else the mover's own two
    /// one-axis steps, the major axis first.
    /// </summary>
    internal static (GridPoint First, GridPoint Second) Alternatives(
        GridPoint cursor, GridPoint mover, int signX, int signY, bool yMajor)
    {
        if (signX == 0)
        {
            return (new GridPoint(cursor.X + 1, cursor.Y), new GridPoint(cursor.X - 1, cursor.Y));
        }

        if (signY == 0)
        {
            return (new GridPoint(cursor.X, cursor.Y + 1), new GridPoint(cursor.X, cursor.Y - 1));
        }

        var alongX = new GridPoint(mover.X + signX, mover.Y);
        var alongY = new GridPoint(mover.X, mover.Y + signY);
        return yMajor ? (alongY, alongX) : (alongX, alongY);
    }

    private static int Chebyshev(GridPoint a, GridPoint b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
