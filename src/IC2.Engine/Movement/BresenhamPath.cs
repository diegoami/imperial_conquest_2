namespace IC2.Engine.Movement;

/// <summary>
/// The original's one-click move order: a destination is clicked once, and the engine walks a
/// straight-line path to it in a single step rather than taking tile-by-tile player input
/// <strong>[confirmed: decompiled-army-movement-and-river-cost.md]</strong>
/// (<c>docs/game-design.md</c> §Movement: "A move order is issued once (click a destination) and the
/// engine walks a straight-line path (Bresenham) to it in one step").
/// </summary>
/// <remarks>
/// <para>
/// This is pure grid geometry: no terrain, no ruleset, no game state. <see cref="MovementWalker"/>
/// layers cost, blocking and the abort rule on top of the sequence this type produces.
/// </para>
/// <para>
/// The report names the algorithm ("Bresenham") but not a specific line-drawing variant's tie-breaking
/// rule, so this implements the standard integer, all-octant Bresenham line algorithm — symmetric under
/// swapping the two endpoints' roles, no floating point, and deterministic by construction (the same
/// pair of points always produces the same sequence). <c>tests/IC2.Engine.Tests/Movement</c> pins the
/// exact cell sequence this produces over the shipped toy world's own terrain grid as a committed
/// fixture, so a future change to the tie-breaking rule is a visible, deliberate diff rather than a
/// silent behaviour change.
/// </para>
/// </remarks>
public static class BresenhamPath
{
    /// <summary>
    /// Traces the straight-line path from <paramref name="from"/> to <paramref name="to"/>, inclusive of
    /// both endpoints.
    /// </summary>
    /// <param name="from">The starting cell — the mover's current position.</param>
    /// <param name="to">The destination cell.</param>
    /// <returns>
    /// The cell sequence a one-click move walks, starting with <paramref name="from"/> itself and ending
    /// with <paramref name="to"/>. A single-cell result means <paramref name="from"/> equals
    /// <paramref name="to"/>.
    /// </returns>
    public static IReadOnlyList<GridPoint> Trace(GridPoint from, GridPoint to)
    {
        var points = new List<GridPoint>();

        var x0 = from.X;
        var y0 = from.Y;
        var x1 = to.X;
        var y1 = to.Y;

        var deltaX = Math.Abs(x1 - x0);
        var stepX = x0 < x1 ? 1 : -1;
        var deltaY = -Math.Abs(y1 - y0);
        var stepY = y0 < y1 ? 1 : -1;
        var error = deltaX + deltaY;

        var x = x0;
        var y = y0;
        while (true)
        {
            points.Add(new GridPoint(x, y));
            if (x == x1 && y == y1)
            {
                break;
            }

            var doubledError = 2 * error;
            if (doubledError >= deltaY)
            {
                error += deltaY;
                x += stepX;
            }

            if (doubledError <= deltaX)
            {
                error += deltaX;
                y += stepY;
            }
        }

        return points;
    }
}
