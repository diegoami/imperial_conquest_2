using IC2.Engine.Armies;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/tasks/T106.md</c> "Army-to-army transfer of units, supply and money", Done-when 4: the partner
/// the original picks is the <em>last</em> own unit at Chebyshev distance exactly 1
/// (<c>FUN_00449D64</c> / <c>FUN_00449DD8</c>), the fleet side skipping an unlaunched fleet and a foreign
/// one, and none when nothing qualifies.
/// </summary>
public sealed class AdjacentPartnerTests
{
    private static IEnumerable<UnitSlot> Units(int count, string prefix = "u") =>
        Enumerable.Range(0, count).Select(i => RegularUnit($"{prefix}{i}", troops: 100));

    private static FleetState Fleet(string id, string nation, int x, int y, bool launched) => new(
        id,
        nation,
        x,
        y,
        Moves: 5,
        Ships: 10,
        ConditionPercent: 100,
        Money: 0,
        SupplyTons: 0,
        ConstructionTicksRemaining: launched ? null : 12,
        BuildCityId: launched ? null : "pella",
        CarriedArmyId: null,
        CoveredTileCode: 5);

    [Fact]
    public void Army_ReturnsTheLaterOfTwoOwnArmiesAtDistanceOneInStateOrder()
    {
        var state = WithArmies(
            InitialState(),
            Army("selected", NorthNationId, 10, 10, Units(1, "s")),
            Army("first-near", NorthNationId, 11, 10, Units(1, "a")),
            Army("far", NorthNationId, 12, 10, Units(1, "b")),
            Army("second-near", NorthNationId, 10, 11, Units(1, "c")));

        var partner = AdjacentPartner.Army(state, "selected");

        Assert.NotNull(partner);
        Assert.Equal("second-near", partner!.Id);
    }

    [Fact]
    public void Army_WithNoneAtDistanceOne_ReturnsNone()
    {
        var state = WithArmies(
            InitialState(),
            Army("selected", NorthNationId, 10, 10, Units(1, "s")),
            Army("far", NorthNationId, 12, 10, Units(1, "b")));

        Assert.Null(AdjacentPartner.Army(state, "selected"));
    }

    [Fact]
    public void Army_SkipsAForeignArmyAtDistanceOne()
    {
        var state = WithArmies(
            InitialState(),
            Army("selected", NorthNationId, 10, 10, Units(1, "s")),
            Army("own-near", NorthNationId, 11, 10, Units(1, "a")),
            Army("foreign-near", SouthNationId, 10, 11, Units(1, "f")));

        var partner = AdjacentPartner.Army(state, "selected");

        Assert.NotNull(partner);
        Assert.Equal("own-near", partner!.Id);
    }

    [Fact]
    public void Fleet_SkipsAnUnlaunchedAndAForeignFleetAtDistanceOne()
    {
        var state = InitialState() with
        {
            Fleets = ValueList.Of(
                Fleet("selected", NorthNationId, 10, 10, launched: true),
                Fleet("own-launched-near", NorthNationId, 11, 10, launched: true),
                Fleet("own-under-construction-near", NorthNationId, 10, 11, launched: false),
                Fleet("foreign-launched-near", SouthNationId, 11, 11, launched: true)),
        };

        var partner = AdjacentPartner.Fleet(state, "selected");

        Assert.NotNull(partner);
        Assert.Equal("own-launched-near", partner!.Id);
    }

    [Fact]
    public void Fleet_WithNoOwnLaunchedFleetAtDistanceOne_ReturnsNone()
    {
        var state = InitialState() with
        {
            Fleets = ValueList.Of(
                Fleet("selected", NorthNationId, 10, 10, launched: true),
                Fleet("own-under-construction-near", NorthNationId, 11, 10, launched: false),
                Fleet("foreign-launched-near", SouthNationId, 10, 11, launched: true)),
        };

        Assert.Null(AdjacentPartner.Fleet(state, "selected"));
    }

    [Fact]
    public void Fleet_WithNoneAtDistanceOne_ReturnsNone()
    {
        var state = InitialState() with
        {
            Fleets = ValueList.Of(
                Fleet("selected", NorthNationId, 10, 10, launched: true),
                Fleet("own-launched-far", NorthNationId, 12, 10, launched: true)),
        };

        Assert.Null(AdjacentPartner.Fleet(state, "selected"));
    }

    [Fact]
    public void Army_SkipsACoLocatedOwnArmy_AndReturnsTheDistanceOneOne()
    {
        // Distance exactly 1, not AreAdjacent's <= 1: a sibling on the selected army's own tile is never
        // the partner, even when it is later in the state's order (review B3, M6).
        var state = WithArmies(
            InitialState(),
            Army("selected", NorthNationId, 10, 10, Units(1, "s")),
            Army("distance-one", NorthNationId, 11, 10, Units(1, "a")),
            Army("same-tile", NorthNationId, 10, 10, Units(1, "c")));

        var partner = AdjacentPartner.Army(state, "selected");

        Assert.NotNull(partner);
        Assert.Equal("distance-one", partner!.Id);
    }

    [Fact]
    public void Army_WithOnlyACoLocatedOwnArmy_ReturnsNone()
    {
        var state = WithArmies(
            InitialState(),
            Army("selected", NorthNationId, 10, 10, Units(1, "s")),
            Army("same-tile", NorthNationId, 10, 10, Units(1, "c")));

        Assert.Null(AdjacentPartner.Army(state, "selected"));
    }

    [Fact]
    public void Fleet_SkipsACoLocatedOwnLaunchedFleet_AndReturnsTheDistanceOneOne()
    {
        // Distance exactly 1: a launched sibling on the selected fleet's own tile is never the partner,
        // even when it is later in the state's order (review B3, M7).
        var state = InitialState() with
        {
            Fleets = ValueList.Of(
                Fleet("selected", NorthNationId, 10, 10, launched: true),
                Fleet("distance-one", NorthNationId, 11, 10, launched: true),
                Fleet("same-tile", NorthNationId, 10, 10, launched: true)),
        };

        var partner = AdjacentPartner.Fleet(state, "selected");

        Assert.NotNull(partner);
        Assert.Equal("distance-one", partner!.Id);
    }

    [Fact]
    public void Fleet_WithOnlyACoLocatedOwnLaunchedFleet_ReturnsNone()
    {
        var state = InitialState() with
        {
            Fleets = ValueList.Of(
                Fleet("selected", NorthNationId, 10, 10, launched: true),
                Fleet("same-tile", NorthNationId, 10, 10, launched: true)),
        };

        Assert.Null(AdjacentPartner.Fleet(state, "selected"));
    }
}
