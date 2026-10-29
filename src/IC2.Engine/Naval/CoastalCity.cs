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

    /// <summary>
    /// Whether <paramref name="nationId"/> already has a fleet under construction at
    /// <paramref name="cityId"/>. A city building a fleet is not free for another order: the original's
    /// Build fleet dialog refuses with <em>"You do not have a free coastal city at this time."</em> (R56)
    /// when no coastal city is free, and T237 (#920 item 1) has the engine refuse the order too.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static bool IsBuildingFleet(GameState state, string nationId, string cityId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nationId);
        ArgumentNullException.ThrowIfNull(cityId);

        foreach (var fleet in state.Fleets)
        {
            if (fleet.IsUnderConstruction
                && string.Equals(fleet.Nation, nationId, StringComparison.Ordinal)
                && string.Equals(fleet.BuildCityId, cityId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The coastal cities <paramref name="nationId"/> owns, in the world's order, that are not already
    /// building one of its fleets: the cities <see cref="Commands.OrderFleetCommandHandler"/> accepts an
    /// order for, and the Build fleet dialog's list.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<CityState> FreeForFleetOrder(GameState state, World world, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(nationId);

        var list = new List<CityState>();
        foreach (var city in state.Cities)
        {
            if (string.Equals(city.Owner, nationId, StringComparison.Ordinal)
                && IsCoastal(city, world)
                && !IsBuildingFleet(state, nationId, city.Id))
            {
                list.Add(city);
            }
        }

        return list;
    }

    /// <summary>
    /// Where <paramref name="nation"/>'s next fleet order is built: its own coastal city nearest its capital
    /// by Chebyshev distance that is not already building a fleet, or <see langword="null"/> when none is
    /// free. The original chooses the city itself, and the manual's Rome sequence is Caere, Antium,
    /// Tarquinii, Pisae, Neapolis, Ariminum, the sea-touching cities nearest first
    /// (<c>2026-10-09-fleet-build-city-nearest-capital.md</c>, research <c>c36b255</c>; T241, #918).
    /// The Build fleet dialog and the AI's <c>AiFaithfulEconomy.ChoosePort</c> (<c>FUN_004496e0</c>) both
    /// use it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Ties</strong> go to the lower city id, ordinal: [designed], the AI's existing rule. The
    /// original's tie-break is not established.
    /// </para>
    /// <para>
    /// <strong>No capital</strong> (no <see cref="NationState.CapitalCityId"/>, or one not in the state):
    /// the first free coastal city in the world's order, the behaviour before T241. The AI never reaches
    /// this: <c>ChoosePort</c> orders no fleet without a capital.
    /// </para>
    /// <para>
    /// <strong>The free test.</strong> By default a city is busy when this nation builds a fleet there
    /// (<see cref="IsBuildingFleet"/>, T237), the test the engine's order refuses on. With
    /// <paramref name="busyWhenAnyNationBuilds"/> a fleet of any nation under construction there makes it
    /// busy, the AI's own test, kept as it was.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static CityState? FleetBuildCity(
        GameState state, World world, NationState nation, bool busyWhenAnyNationBuilds = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(nation);

        var capital = nation.CapitalCityId is { } capitalId ? state.CityById(capitalId) : null;
        CityState? best = null;
        var bestDistance = int.MaxValue;
        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, nation.Id, StringComparison.Ordinal)
                || !IsCoastal(city, world)
                || (busyWhenAnyNationBuilds ? HostsConstruction(state, city.Id) : IsBuildingFleet(state, nation.Id, city.Id)))
            {
                continue;
            }

            if (capital is null)
            {
                return city;
            }

            var distance = LandingTile.ChebyshevDistance(
                new GridPoint(city.X, city.Y), new GridPoint(capital.X, capital.Y));
            if (distance < bestDistance
                || (distance == bestDistance && best is not null && string.CompareOrdinal(city.Id, best.Id) < 0))
            {
                best = city;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static bool HostsConstruction(GameState state, string cityId)
    {
        foreach (var fleet in state.Fleets)
        {
            if (fleet.IsUnderConstruction && string.Equals(fleet.BuildCityId, cityId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
