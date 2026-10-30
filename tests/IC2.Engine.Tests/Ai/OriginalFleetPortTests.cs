using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using Fx = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class OriginalFleetPortTests
{
    private static CityState City(string id, string owner, int x, int y, int stock = 500) =>
        AiReachableFallbackTests.City(id, owner, x, y, supply: stock);

    private static AiView View(params CityState[] cities)
    {
        var world = AiScriptedStates.World with
        {
            Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(0, 320 * 140))),
        };
        var state = BattleCommandTestbed.StateWith([Fx.Nation("north"), Fx.Nation("south")], cities, []);
        return new(state, AiScriptedStates.Ruleset, world, "north");
    }

    private static FleetState Ship(int money = 100, int supply = 1) =>
        new("ship", "north", 50, 50, 25, 30, 100, money, supply, null, null, null, 0);

    [Fact]
    public void Adjacent_supplied_own_port_has_minus_one_rank_but_remains_a_real_city()
    {
        var adjacent = City("adjacent", "north", 51, 50, 21);
        var onCell = City("on-cell", "north", 50, 50);
        Assert.Equal(adjacent, OriginalFleetPort.Pick(View(onCell, adjacent), Ship()));
        Assert.Equal(onCell, OriginalFleetPort.Pick(View(onCell, adjacent), Ship(supply: 0)));
    }

    [Theory]
    [InlineData(120, 11, false)]
    [InlineData(121, 10, false)]
    [InlineData(121, 11, true)]
    public void Foreign_port_uses_strict_stock_and_purse_thresholds(int stock, int money, bool selected)
    {
        var foreign = City("foreign", "south", 52, 50, stock);
        Assert.Equal(selected ? foreign : null, OriginalFleetPort.Pick(View(foreign), Ship(money)));
    }

    [Fact]
    public void Second_pass_uses_empty_own_port_and_retains_foreign_if_no_own_port_exists()
    {
        var foreign = City("foreign", "south", 52, 50);
        var empty = City("empty", "north", 200, 40, 0);
        Assert.Equal(empty, OriginalFleetPort.Pick(View(foreign, empty), Ship()));
        Assert.Equal(foreign, OriginalFleetPort.Pick(View(foreign), Ship()));
    }

    [Fact]
    public void Own_stock_twenty_is_not_a_first_pass_candidate_and_ties_keep_table_order()
    {
        var nearEmpty = City("near-empty", "north", 52, 50, 20);
        var stocked = City("z-first", "north", 55, 50, 21);
        var tied = City("a-last", "north", 50, 55, 21);
        Assert.Equal(stocked, OriginalFleetPort.Pick(View(nearEmpty, stocked, tied), Ship()));
        Assert.Equal(tied, OriginalFleetPort.Pick(View(nearEmpty, tied, stocked), Ship()));
        Assert.Equal(nearEmpty, OriginalFleetPort.Pick(View(nearEmpty), Ship(money: 0, supply: 0)));
    }

    [Theory]
    [InlineData(247, 98, true)]
    [InlineData(248, 98, false)]
    [InlineData(213, 99, true)]
    [InlineData(214, 99, false)]
    public void Eastern_exclusion_is_literal(int x, int y, bool port) =>
        Assert.Equal(port, OriginalFleetPort.IsPort(View(), new(x, y)));

    [Fact]
    public void Coastal_probe_includes_centre_but_does_not_treat_fleet_markers_as_sea_or_require_a_path()
    {
        var view = View();
        var cells = Enumerable.Repeat(2, 320 * 140).ToArray();
        cells[50 * 320 + 50] = 1;
        var world = view.World with { Terrain = new(TerrainEncoding.RunLength,
            ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        view = new(view.State, view.Ruleset, world, "north");
        Assert.True(OriginalFleetPort.IsPort(view, new(50, 50)));
        Assert.False(OriginalFleetPort.IsPort(new(view.State with { Fleets = ValueList.Of(Ship()) },
            view.Ruleset, world, "north"), new(50, 50)));
        // This isolated sea cell has no sea route to the ship; E920 is only a local probe.
        Assert.True(OriginalFleetPort.IsPort(view, new(49, 49)));
    }

    [Fact]
    public void Landing_mode_starts_at_the_explicit_target_then_skips_live_markers_and_keeps_last_scan_match()
    {
        var city = City("occupied", "north", 50, 50);
        var view = View(city);
        var cells = new int[320 * 140];
        foreach (var point in new[] { new GridPoint(49, 49), new(50, 50), new(51, 51), new(214, 99) })
            cells[point.Y * 320 + point.X] = 2;
        var world = view.World with { Terrain = new(TerrainEncoding.RunLength,
            ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        view = new(view.State, view.Ruleset, world, "north");
        Assert.Equal(new GridPoint(49, 49), ClassicalFleetNavigation.LandingTarget(view, new(49, 49)));
        Assert.Equal(new GridPoint(51, 51), ClassicalFleetNavigation.LandingTarget(view, new(50, 50)));
        var army = Fx.Army("blocker", "north", 51, 51, 60, Fx.Unit("light_infantry", 1000));
        view = new(view.State with { Armies = ValueList.Of(army) }, view.Ruleset, world, "north");
        Assert.Equal(new GridPoint(49, 49), ClassicalFleetNavigation.LandingTarget(view, new(50, 50)));
        Assert.Equal(new GridPoint(214, 99), ClassicalFleetNavigation.LandingTarget(view, new(214, 99)));
    }
}
