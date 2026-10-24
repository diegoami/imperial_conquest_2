using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Tests.Import;
using Xunit;

namespace IC2.Engine.Tests.Ai;

/// <summary>Public, synthetic controls from the navigation187 matrix; no original files are loaded.</summary>
public sealed class ClassicalArmyNavigationTests
{
    private static AiView View(int defaultCode, params (int X, int Y, int Code)[] patches)
    {
        var original = RealGameData.World;
        var cells = Enumerable.Repeat(defaultCode, original.Width * original.Height).ToArray();
        foreach (var (x, y, code) in patches) cells[y * original.Width + x] = code;
        var runs = new List<TerrainRun>();
        for (var i = 0; i < cells.Length;)
        {
            var start = i;
            var code = cells[i];
            while (i < cells.Length && cells[i] == code) i++;
            runs.Add(new TerrainRun(code, i - start));
        }
        var world = original with
        {
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(runs)),
        };
        var state = GameStateFactory.CreateInitial(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario) with
        {
            Cities = ValueList<CityState>.Empty,
            Armies = ValueList<ArmyState>.Empty,
            Fleets = ValueList<FleetState>.Empty,
        };
        return new AiView(state, RealGameData.Ruleset, world, state.ActiveNationId);
    }

    [Fact]
    public void Direct_line_ignores_the_final_medium_but_not_an_intermediate_sea_cell()
    {
        var origin = new GridPoint(230, 80);
        var target = new GridPoint(240, 90);
        Assert.True(ClassicalArmyNavigation.DirectLine(View(2), origin, target));
        Assert.False(ClassicalArmyNavigation.DirectLine(View(2, (235, 85, 0)), origin, target));
        Assert.True(ClassicalArmyNavigation.DirectLine(View(2, (235, 85, 203)), origin, target));
        Assert.True(ClassicalArmyNavigation.DirectLine(View(2, (240, 90, 0)), origin, target));
        Assert.True(ClassicalArmyNavigation.DirectLine(View(2, (231, 81, 0)), origin,
            new GridPoint(231, 81)));
    }

    [Fact]
    public void A_single_medium_change_selects_the_original_intermediate_instead_of_the_direct_goal()
    {
        var origin = new GridPoint(230, 80);
        var target = new GridPoint(240, 90);
        var view = View(2, (235, 85, 0));

        Assert.True(ClassicalArmyNavigation.Classify(origin.X, origin.Y)
            == ClassicalArmyNavigation.Classify(target.X, target.Y));
        Assert.False(ClassicalArmyNavigation.DirectLine(view, origin, target));
        var choice = ClassicalArmyNavigation.Intermediate(view, origin, target);
        Assert.Equal(new GridPoint(221, 94), choice.Point);
        Assert.Equal(32, choice.NodeId);
    }

    [Fact]
    public void A_sea_wall_without_nodes_or_local_land_ray_has_no_waypoint()
    {
        var patches = new List<(int X, int Y, int Code)>();
        for (var x = 229; x <= 231; x++)
            for (var y = 79; y <= 81; y++) patches.Add((x, y, 2));
        for (var x = 239; x <= 241; x++)
            for (var y = 89; y <= 91; y++) patches.Add((x, y, 2));
        var view = View(0, patches.ToArray());

        var choice = ClassicalArmyNavigation.Intermediate(view,
            new GridPoint(230, 80), new GridPoint(240, 90));
        Assert.Null(choice.Point);
    }
}
