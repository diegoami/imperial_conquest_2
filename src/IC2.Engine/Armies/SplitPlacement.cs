using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Armies;

/// <summary>
/// Where a split places its new army or new fleet — the 3×3 scan that keeps the <em>last</em> cell
/// qualifying for the unit's kind, centred on the parent's own tile (or, for an embarked army, on its
/// carrying fleet's). <c>docs/tasks/T114.md</c>, folding bugs #584 and #596.
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
/// of the scan. That a split army uses the same creator is <strong>[derived]</strong>; the scan itself
/// is <strong>[confirmed: code]</strong>
/// (<c>2026-10-05-split-army-aboard-a-fleet.md</c>, rule 2, <c>:47942-47943</c> and
/// <c>:47960-47962</c>). For an embarked army the scan's centre is the carrying fleet's tile
/// (<c>state.FleetById(army.AboardFleetId)</c>'s <c>X</c>, <c>Y</c>); <see cref="ArmyCellFor"/> resolves
/// the centre and returns <see langword="null"/> for a dangling link. A cell qualifies for an army when
/// <see cref="LandingTile.IsPassableForArmy"/> holds and nothing occupies it — the same occupancy set
/// <see cref="MobilizationArmyCreation"/> uses: a city, an army on the map, or a launched fleet.
/// </para>
/// <para>
/// <strong>The fleet half is <c>[designed]</c> by analogy.</strong> Which routine places a split fleet
/// is unread; the one observation — fleet 2 at <c>(101,46)</c> splitting to fleet 5 at
/// <c>(101,47)</c> in <c>T_SPLIT_FLEET.SAV</c>, where <c>(+1, +1)</c> is land — fits the same
/// last-qualifying-cell scan with <see cref="TileType.PassableByFleets"/> and the same occupancy test.
/// <see cref="FleetCell"/> keeps <c>dy</c> outer: the two nestings may separate that observation, and
/// which routine places a split fleet is unread, so changing it would be a new <c>[designed]</c> rule.
/// What was searched and came up empty: the fleet-orders and unit-map reports both describe the split
/// and neither quotes the placement routine.
/// </para>
/// <para>
/// <strong><c>x</c> outer, <c>dy</c> inner.</strong> <c>FUN_004492C0</c> scans <c>dx</c> over
/// <c>−1, 0, +1</c> in the outer loop and <c>dy</c> over <c>−1, 0, +1</c> in the inner loop and keeps
/// the <em>last</em> cell that qualifies <strong>[confirmed: code,
/// <c>2026-10-05-split-army-aboard-a-fleet.md</c>, rule 2, <c>:47942-47943</c> and
/// <c>:47960-47962</c>]</strong>, so when the south-east cell is blocked the new army goes to
/// <c>(+1, 0)</c>, not <c>(0, +1)</c>. When <c>(+1, +1)</c> is free the two nestings agree, which is
/// why the observed splits do not separate them. The engine's other 3×3 scans
/// (<see cref="LandingTile.FirstAdjacentLandTile"/>, <c>CoastalCity.FirstAdjacentSeaTile</c>) are not
/// this scan and are unchanged.
/// </para>
/// </remarks>
public static class SplitPlacement
{
    /// <summary>
    /// The cell a split army's new unit is placed on: the <em>last</em> cell of the 3×3 block centred on
    /// <paramref name="centre"/>, scanned with <c>x</c> outer and <c>dy</c> inner, that an army may stand
    /// on and nothing occupies. <see langword="null"/> when no cell qualifies.
    /// </summary>
    /// <param name="state">The live state, read for occupancy (the parent itself sits on the centre).</param>
    /// <param name="world">The world, read for terrain and bounds.</param>
    /// <param name="centre">
    /// The centre of the scan — the splitting army's own tile, or its carrying fleet's tile when it is
    /// embarked (see <see cref="ArmyCellFor"/>).
    /// </param>
    public static GridPoint? ArmyCell(GameState state, World world, GridPoint centre)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);

        GridPoint? chosen = null;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var candidate = new GridPoint(centre.X + dx, centre.Y + dy);
                if (LandingTile.IsPassableForArmy(candidate, world) && !IsOccupied(state, candidate))
                {
                    // No break and no early return: the last qualifying cell wins, which for an open
                    // centre is (+1, +1), its south-east neighbour. x is the outer loop, so the
                    // fallback when the south-east cell is blocked is (+1, 0), not (0, +1).
                    chosen = candidate;
                }
            }
        }

        return chosen;
    }

    /// <summary>
    /// The cell a split places <paramref name="army"/>'s new unit on: <see cref="ArmyCell"/> centred on
    /// the army's own tile, or on its carrying fleet's tile when the army is embarked. A dangling fleet
    /// link resolves to no tile and returns <see langword="null"/>, which the split refuses as
    /// <see cref="Commands.SplitArmyRejections.NoFreeAdjacentTile"/>.
    /// </summary>
    /// <param name="state">The live state, read for the fleet link and occupancy.</param>
    /// <param name="world">The world, read for terrain and bounds.</param>
    /// <param name="army">The army being split; it supplies the centre, directly or through its fleet.</param>
    public static GridPoint? ArmyCellFor(GameState state, World world, ArmyState army)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(army);

        if (army.IsEmbarked)
        {
            if (army.AboardFleetId is not { } fleetId || state.FleetById(fleetId) is not { } fleet)
            {
                return null;
            }

            return ArmyCell(state, world, new GridPoint(fleet.X, fleet.Y));
        }

        return ArmyCell(state, world, new GridPoint(army.X, army.Y));
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
