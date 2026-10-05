using IC2.Engine.Armies;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Tests.Core;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// T142 Done-when 5: both army scans reproduce <c>FUN_004492C0</c>'s nesting — <c>dx</c> outer over
/// −1, 0, +1 and <c>dy</c> inner — so when the south-east cell is blocked the new army falls back to
/// <c>(+1, 0)</c>, then to <c>(+1, −1)</c>, and only when the whole <c>x = +1</c> column is blocked to
/// <c>(0, +1)</c>. <see cref="SplitPlacement.ArmyCell"/> (a split) and
/// <see cref="MobilizationArmyCreation.PlacementCell"/> (a mobilized recruit) are one and the same
/// function, so both are pinned here.
/// </summary>
/// <remarks>
/// The toy map's <c>(4, 3)</c> block is all army-passable ground once the fixture's own cities are
/// cleared, so only the scripted blockers decide the cell. On <c>main</c> (row-major, <c>dy</c> outer)
/// the first two blocked cases would land on <c>(0, +1)</c> instead.
/// </remarks>
public sealed class PlacementScanOrderTests
{
    private static World World => CoreTestbed.Toy.World;

    private static readonly GridPoint Centre = new(4, 3);

    private static GameState OpenState() => InitialState() with
    {
        Cities = ValueList<CityState>.Empty,
        Armies = ValueList<ArmyState>.Empty,
        Fleets = ValueList<FleetState>.Empty,
    };

    private static ArmyState Blocker(string id, int x, int y) =>
        Army(id, NorthNationId, x, y, new[] { RegularUnit(id) });

    private static GridPoint? ArmyScanWith(params (int Dx, int Dy)[] blocked)
    {
        var state = OpenState();
        state = WithArmies(
            state,
            new[] { Blocker("parent", Centre.X, Centre.Y) }
                .Concat(blocked.Select((offset, i) => Blocker($"blocker-{i}", Centre.X + offset.Dx, Centre.Y + offset.Dy)))
                .ToArray());
        return SplitPlacement.ArmyCell(state, World, Centre);
    }

    private static CityState CityAtCentre() =>
        InitialState().CityById("arx")! with { X = Centre.X, Y = Centre.Y };

    private static GridPoint? CityScanWith(params (int Dx, int Dy)[] blocked)
    {
        var state = OpenState() with { Cities = ValueList.Of(CityAtCentre()) };
        state = WithArmies(
            state,
            blocked.Select((offset, i) => Blocker($"blocker-{i}", Centre.X + offset.Dx, Centre.Y + offset.Dy)).ToArray());
        return MobilizationArmyCreation.PlacementCell(state, World, state.CityById("arx")!);
    }

    [Fact]
    public void An_army_with_every_neighbour_free_scans_to_plus_one_plus_one() =>
        Assert.Equal(new GridPoint(Centre.X + 1, Centre.Y + 1), ArmyScanWith());

    [Fact]
    public void An_army_with_its_south_east_neighbour_occupied_scans_to_plus_one_zero() =>
        Assert.Equal(new GridPoint(Centre.X + 1, Centre.Y), ArmyScanWith((1, 1)));

    [Fact]
    public void An_army_with_its_south_east_and_east_neighbours_occupied_scans_to_plus_one_minus_one() =>
        Assert.Equal(new GridPoint(Centre.X + 1, Centre.Y - 1), ArmyScanWith((1, 1), (1, 0)));

    [Fact]
    public void An_army_with_its_whole_east_column_occupied_scans_to_zero_plus_one() =>
        Assert.Equal(new GridPoint(Centre.X, Centre.Y + 1), ArmyScanWith((1, 1), (1, 0), (1, -1)));

    [Fact]
    public void A_city_with_every_neighbour_free_scans_to_plus_one_plus_one() =>
        Assert.Equal(new GridPoint(Centre.X + 1, Centre.Y + 1), CityScanWith());

    [Fact]
    public void A_city_with_its_south_east_neighbour_occupied_scans_to_plus_one_zero() =>
        Assert.Equal(new GridPoint(Centre.X + 1, Centre.Y), CityScanWith((1, 1)));

    [Fact]
    public void A_city_with_its_south_east_and_east_neighbours_occupied_scans_to_plus_one_minus_one() =>
        Assert.Equal(new GridPoint(Centre.X + 1, Centre.Y - 1), CityScanWith((1, 1), (1, 0)));

    [Fact]
    public void A_city_with_its_whole_east_column_occupied_scans_to_zero_plus_one() =>
        Assert.Equal(new GridPoint(Centre.X, Centre.Y + 1), CityScanWith((1, 1), (1, 0), (1, -1)));
}
