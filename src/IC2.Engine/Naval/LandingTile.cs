using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval;

/// <summary>
/// Where a disembarking army may land — <c>docs/task-catalogue.md</c> "T14 Naval" DoD 15.
/// <c>FUN_0044B840</c> restores the army's covered map cell and "takes an automatic landing-tile branch
/// for an AI seat"; a human seat presumably picks the tile by clicking it, the same way embarkation
/// itself is a click on a target. This mirrors <see cref="CoastalCity"/>'s own fixed, deterministic
/// 8-neighbour scan for the AI-automatic case, since no report gives the original's actual selection
/// order and this only needs to name <em>a</em> valid tile, not reproduce a specific one.
/// </summary>
public static class LandingTile
{
    /// <summary>Whether <paramref name="point"/> is in bounds and a tile an army may stand on.</summary>
    public static bool IsPassableForArmy(GridPoint point, World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if ((uint)point.X >= (uint)world.Width || (uint)point.Y >= (uint)world.Height)
        {
            return false;
        }

        var terrainCells = world.Terrain.Decode(world.Width, world.Height);
        var code = terrainCells[(point.Y * world.Width) + point.X];
        return world.TileTypeByCode(code)?.PassableByArmies == true;
    }

    /// <summary>
    /// The first passable-for-army tile in the 3x3 block centred on <paramref name="from"/>, scanned
    /// row-major, north-west first (so <paramref name="from"/> itself is the fifth cell checked, not the
    /// first) — the same convention <see cref="CoastalCity.FirstAdjacentSeaTile"/> uses for the
    /// equivalent sea-side lookup. <see langword="null"/> if none is passable.
    /// </summary>
    public static GridPoint? FirstAdjacentLandTile(GridPoint from, World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var candidate = new GridPoint(from.X + dx, from.Y + dy);
                if (IsPassableForArmy(candidate, world))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Chebyshev ("king move") distance between two points -- adjacency, for the "within one tile" checks this task uses throughout.</summary>
    public static int ChebyshevDistance(GridPoint a, GridPoint b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
