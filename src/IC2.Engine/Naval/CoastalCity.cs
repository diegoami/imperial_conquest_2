using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval;

/// <summary>
/// Whether a city touches the sea — <c>docs/game-design.md</c> §Naval: "only nations with coastal
/// cities can build" — and where a fleet actually sits once launched there.
/// </summary>
/// <remarks>
/// The world model carries no explicit "coastal" flag on <see cref="CityDefinition"/>/
/// <see cref="CityState"/> (nothing in the cited reports names one), so this reads it directly off the
/// terrain grid: a city is coastal when any of its eight neighbouring cells is a tile type a fleet may
/// enter (<see cref="TileType.PassableByFleets"/>). Adjacency here is the literal geometric fact "touches
/// the sea," not a tunable design radius, so it is a fixed 8-neighbour check rather than a further
/// ruleset field.
/// <para>
/// <strong>A launched fleet sits on the nearest sea tile, not on the city's own (land) tile.</strong>
/// <see cref="Naval.Commands.OrderFleetCommandHandler"/> places a new order at its build city's
/// coordinates while under construction — matching the confirmed <c>(0, 0)</c>-under-construction
/// convention loosely, in that the fleet is not really "on the map" yet — but a real, launched fleet
/// occupies a sea tile: <see cref="Naval.FleetTickSystem"/> and <see cref="Naval.Commands.MoveFleetCommandHandler"/>
/// price cells through <see cref="TileType.PassableByFleets"/>, which a city's own land tile never
/// satisfies, so a fleet that could only ever sit on its build city's tile could never be represented by
/// the same terrain-passability rule its own movement uses. <see cref="FirstAdjacentSeaTile"/> is the one
/// place that resolves it.
/// </para>
/// </remarks>
public static class CoastalCity
{
    /// <summary>Whether <paramref name="city"/> is adjacent to at least one sea tile in <paramref name="world"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="city"/> or <paramref name="world"/> is null.</exception>
    public static bool IsCoastal(CityState city, World world) => FirstAdjacentSeaTile(city, world) is not null;

    /// <summary>
    /// The first sea tile adjacent to <paramref name="city"/>, scanning its eight neighbours in a fixed,
    /// deterministic order (row-major, north-west first) — or <see langword="null"/> if the city is not
    /// coastal. This is where a fleet built at this city actually launches onto the map.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="city"/> or <paramref name="world"/> is null.</exception>
    public static GridPoint? FirstAdjacentSeaTile(CityState city, World world)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(world);

        var terrainCells = world.Terrain.Decode(world.Width, world.Height);

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var x = city.X + dx;
                var y = city.Y + dy;
                if ((uint)x >= (uint)world.Width || (uint)y >= (uint)world.Height)
                {
                    continue;
                }

                var code = terrainCells[(y * world.Width) + x];
                if (world.TileTypeByCode(code)?.PassableByFleets == true)
                {
                    return new GridPoint(x, y);
                }
            }
        }

        return null;
    }
}
