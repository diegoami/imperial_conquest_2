using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Tests.Ai;
using Xunit;

namespace IC2.Engine.Tests.Naval;

public sealed class FaithfulFleetBuildSelectorTests
{
    [Theory]
    [InlineData(247, 50, true)]
    [InlineData(248, 50, false)]
    [InlineData(213, 99, true)]
    [InlineData(214, 98, true)]
    [InlineData(214, 99, false)]
    [InlineData(309, 37, false)]
    public void Classical_AI_excludes_the_eastern_zone(int x, int y, bool allowed)
    {
        var f = Fixture(x, y);
        Assert.Equal(allowed, AiFaithfulEconomy.ChoosePort(f.State, f.World, f.Nation, f.Rules) is not null);
        // Human order eligibility is a separate, unchanged caller.
        Assert.NotNull(CoastalCity.FleetBuildCity(f.State, f.World, f.Nation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_AI_distances_retain_city_table_order(bool reverse)
    {
        var f = Fixture(100, 40);
        var z = f.State.Cities[0] with { Id = "z-first", X = 99 };
        var a = z with { Id = "a-last", X = 101 };
        var cap = z with { Id = "cap", X = 100, Y = 35 };
        var state = f.State with { Cities = reverse ? ValueList.Of(a, z, cap) : ValueList.Of(z, a, cap) };
        var nation = f.Nation with { CapitalCityId = cap.Id };
        Assert.Equal(reverse ? a.Id : z.Id, AiFaithfulEconomy.ChoosePort(state, f.World, nation, f.Rules)?.Id);
        Assert.Equal(a.Id, CoastalCity.FleetBuildCity(state, f.World, nation)?.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Other_policy_or_world_preserves_the_previous_selector(bool customWorld)
    {
        var f = Fixture(309, 37);
        var rules = customWorld ? f.Rules : f.Rules with { Flags = f.Rules.Flags with { AiEconomy = AiEconomyModel.Designed } };
        var world = customWorld ? f.World with { Id = "custom" } : f.World;
        Assert.NotNull(AiFaithfulEconomy.ChoosePort(f.State, world, f.Nation, rules));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AI_port_checks_live_sea_and_any_nations_construction(bool seaOccupied)
    {
        var f = Fixture(100,40);
        var city = f.State.Cities[0];
        var fleet = new FleetState("marker", "foreign", 100,41,0,10,100,1000,100,
            seaOccupied ? null : 5, city.Id, null, 0);
        var fleets = seaOccupied
            ? Enumerable.Range(98,5).Select(x => fleet with { Id = "marker-" + x, X = x })
            : new[] { fleet };
        var state = f.State with { Fleets = ValueList.From(fleets) };
        Assert.Null(AiFaithfulEconomy.ChoosePort(state, f.World, f.Nation, f.Rules));
        Assert.NotNull(CoastalCity.FleetBuildCity(state, f.World, f.Nation));
    }

    private static (GameState State, World World, NationState Nation, Ruleset Rules) Fixture(int x, int y)
    {
        var game = AiTestbed.Classical;
        var state = GameStateFactory.CreateInitial(game.World, game.Ruleset, game.Scenario);
        var nation = state.Nations[0];
        var city = state.Cities.First(c => c.Owner == nation.Id) with { X = x, Y = y };
        var cells = Enumerable.Repeat(2, 320 * 140).ToArray();
        // Sea on both sides permits nonalphabetical tied ports at x=99 and x=101.
        for (var dx = -2; dx <= 2; dx++) cells[(y + 1) * 320 + x + dx] = 0;
        var world = game.World with { Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        nation = nation with { CapitalCityId = city.Id };
        state = state with { Cities = ValueList.Of(city), Armies = ValueList<ArmyState>.Empty, Fleets = ValueList<FleetState>.Empty };
        return (state, world, nation, game.Ruleset);
    }
}
