using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Armies;

/// <summary>
/// Where a split places its new army or new fleet — the 3×3 scan that keeps the <em>last</em> cell
/// qualifying for the unit's kind, centred on the parent's own tile. <c>docs/tasks/T114.md</c>, folding
/// bugs #584 and #596.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The army scan is <c>FUN_004492C0</c>'s.</strong> A split army's unit is created by
/// <c>FUN_00449F08</c> (<strong>[derived: code]</strong> — the 2026-10-03 army-to-army report reads
/// "Split army uses the same form" and <c>TUnitMap_SplitArmy</c> calls it), the same creator
/// <see cref="MobilizationArmyCreation"/> reproduces for a mobilized recruit, and that creator places
/// its army with the 3×3 scan centred on the cell it is given. Two observed splits put the new army at
/// <c>(+1, +1)</c> from the parent — <c>(100,37) → (101,38)</c> in <c>E_AFTER_SPLIT.SAV</c> and
/// <c>(92,27) → (93,28)</c> a turn apart in the <c>1_rome_270_winter</c> pair — which is the last cell
/// of the row-major scan. That a split army uses the same creator is <strong>[derived]</strong>; the
/// scan itself is <strong>[confirmed: code, for mobilisation]</strong>
/// (<c>decompiled-mobilization-and-mercenary-restock.md</c> §3). A cell qualifies for an army when
/// <see cref="LandingTile.IsPassableForArmy"/> holds and nothing occupies it — the same occupancy set
/// <see cref="MobilizationArmyCreation"/> uses: a city, an army on the map, or a launched fleet.
/// </para>
/// <para>
/// <strong>The fleet half is <c>[designed]</c> by analogy.</strong> Which routine places a split fleet
/// is unread; the one observation — fleet 2 at <c>(101,46)</c> splitting to fleet 5 at
/// <c>(101,47)</c> in <c>T_SPLIT_FLEET.SAV</c>, where <c>(+1, +1)</c> is land — fits the same
/// last-qualifying-cell scan with <see cref="TileType.PassableByFleets"/> and the same occupancy test.
/// What was searched and came up empty: the fleet-orders and unit-map reports both describe the split
/// and neither quotes the placement routine.
/// </para>
/// <para>
/// <strong>Row-major, <c>dy</c> outer.</strong> The nesting is the one <see cref="MobilizationArmyCreation"/>
/// pins as <c>[derived]</c> — <c>dy</c> outer, <c>dx</c> inner, each over −1, 0, +1 — so this scan and
/// the mobilisation scan fall back identically when the south-east cell is blocked. The engine's other
/// 3×3 scans (<see cref="LandingTile.FirstAdjacentLandTile"/>, <c>CoastalCity.FirstAdjacentSeaTile</c>)
/// already use it.
/// </para>
/// </remarks>
public static class SplitPlacement
{
    /// <summary>
    /// The cell a split army's new unit is placed on: the <em>last</em> cell of the 3×3 block centred on
    /// <paramref name="parent"/>, scanned row-major, that an army may stand on and nothing occupies.
    /// <see langword="null"/> when no cell qualifies.
    /// </summary>
    /// <param name="state">The live state, read for occupancy (the parent itself sits on the centre).</param>
    /// <param name="world">The world, read for terrain and bounds.</param>
    /// <param name="parent">The splitting army's own tile, the centre of the scan.</param>
    public static GridPoint? ArmyCell(GameState state, World world, GridPoint parent)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);

        GridPoint? chosen = null;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var candidate = new GridPoint(parent.X + dx, parent.Y + dy);
                if (LandingTile.IsPassableForArmy(candidate, world) && !IsOccupied(state, candidate))
                {
                    // No break and no early return: the last qualifying cell wins, which for a parent
                    // with open ground all round is (+1, +1), its south-east neighbour.
                    chosen = candidate;
                }
            }
        }

        return chosen;
    }

    /// <summary>
    /// The cell a split fleet's new unit is placed on: the <em>last</em> cell of the 3×3 block centred on
    /// <paramref name="parent"/>, scanned row-major, whose tile type is
    /// <see cref="TileType.PassableByFleets"/> and which nothing occupies. <see langword="null"/> when no
    /// cell qualifies.
    /// </summary>
    /// <param name="state">The live state, read for occupancy (the parent itself sits on the centre).</param>
    /// <param name="world">The world, read for terrain and bounds.</param>
    /// <param name="parent">The splitting fleet's own tile, the centre of the scan.</param>
    public static GridPoint? FleetCell(GameState state, World world, GridPoint parent)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);

        GridPoint? chosen = null;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var candidate = new GridPoint(parent.X + dx, parent.Y + dy);
                if (IsPassableForFleet(candidate, world) && !IsOccupied(state, candidate))
                {
                    chosen = candidate;
                }
            }
        }

        return chosen;
    }

    /// <summary>
    /// The terrain code under <paramref name="point"/> — the value a new unit's
    /// <c>CoveredTileCode</c> takes, as <c>MobilizationArmyCreation</c> writes it for an army
    /// (<c>army[+8] = map[x][y]</c>) and, by analogy, for a fleet.
    /// </summary>
    /// <param name="world">The world whose terrain grid is read.</param>
    /// <param name="point">A cell known to be in bounds (a scan result).</param>
    public static int TerrainCodeAt(World world, GridPoint point)
    {
        ArgumentNullException.ThrowIfNull(world);

        var cells = world.Terrain.Decode(world.Width, world.Height);
        return cells[(point.Y * world.Width) + point.X];
    }

    /// <summary>
    /// Whether a cell is in bounds and its tile type lets a fleet stand on it — the fleet half of the
    /// scan's terrain test, the same check <c>MoveFleetCommandHandler</c> makes.
    /// </summary>
    private static bool IsPassableForFleet(GridPoint point, World world)
    {
        if ((uint)point.X >= (uint)world.Width || (uint)point.Y >= (uint)world.Height)
        {
            return false;
        }

        var cells = world.Terrain.Decode(world.Width, world.Height);
        var code = cells[(point.Y * world.Width) + point.X];
        return world.TileTypeByCode(code)?.PassableByFleets == true;
    }

    /// <summary>
    /// The occupancy test both scans share, identical to <see cref="MobilizationArmyCreation"/>'s own:
    /// a city, another army on the map, or a launched fleet. An embarked army covers no map cell; a
    /// fleet still under construction blocks nothing.
    /// </summary>
    private static bool IsOccupied(GameState state, GridPoint point)
    {
        foreach (var city in state.Cities)
        {
            if (city.X == point.X && city.Y == point.Y)
            {
                return true;
            }
        }

        foreach (var army in state.Armies)
        {
            if (!army.IsEmbarked && army.X == point.X && army.Y == point.Y)
            {
                return true;
            }
        }

        foreach (var fleet in state.Fleets)
        {
            if (!fleet.IsUnderConstruction && fleet.X == point.X && fleet.Y == point.Y)
            {
                return true;
            }
        }

        return false;
    }
}
