using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using Xunit;

namespace IC2.Engine.Tests.Ai;

public sealed class OriginalFleetNavigationMatrixTests
{
    // Public interpreted coordinates/expected nodes from promoted part3-v3. No RAM/save bytes.
    public static TheoryData<string, int, int, int, int, int, int, int> D174 => new()
    {
        { "phase-alliance-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "phase-peace-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "phase-same-owner-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "phase-same-owner-f1-d0", 48, 65, 46, 69, 75, 69, 5 },
        { "phase-score100-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "phase-score101-f0-d0", 49, 62, 41, 80, 75, 69, 5 },
        { "phase-stored-b-f0-d0", 49, 62, 47, 67, 75, 69, 5 },
        { "phase-target-construction-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "phase-trade-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "phase-war-stronger-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "port-protected-f0-d0", 186, 90, 125, 104, 120, 84, 9 },
        { "route-landing-cargo-f0-d0", 49, 62, 48, 65, 75, 69, 5 },
        { "route-landing-no-cargo-f0-d0", 49, 62, 48, 65, 75, 69, 5 },
        { "route-own-port-f0-d0", 49, 62, 48, 64, 75, 69, 5 },
        { "route-inland-f0-d0", 49, 62, 48, 64, 75, 69, 5 },
        { "route-x-minus2-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "route-x-minus1-y-kept-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "route-x-0-residue-f0-d0", 49, 62, 0, 0, 75, 69, 5 },
        { "route-reached-f0-d0", 48, 66, 48, 66, 39, 78, 4 },
        { "hunt-protected-f0-d0", 49, 62, 46, 69, 75, 69, 5 },
        { "p3-carried-across-coast-f0-d0", 49, 62, 48, 65, 75, 69, 5 },
    };

    [Theory]
    [MemberData(nameof(D174))]
    public void All_twenty_one_promoted_group_zero_checks_match(string name, int ox, int oy,
        int tx, int ty, int ex, int ey, int node)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        var game = AiTestbed.Classical;
        var initial = GameStateFactory.CreateInitial(game.World, game.Ruleset, game.Scenario);
        var cells = game.World.Terrain.Decode(320, 140).ToArray();
        cells[oy * 320 + ox] = 0;
        var world = game.World with { Terrain = new(TerrainEncoding.RunLength,
            ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        var fleet = initial.Fleets.First(f => f.Nation == "carthage") with
            { Id = "probe", X = ox, Y = oy, ConstructionTicksRemaining = null, CoveredTileCode = 0 };
        var state = initial with { Armies = ValueList<ArmyState>.Empty, Cities = ValueList<CityState>.Empty,
            Fleets = ValueList.Of(fleet) };
        var result = ClassicalArmyNavigation.FleetIntermediate(new(state, game.Ruleset, world, "carthage"),
            new(ox, oy), new(tx, ty));
        Assert.Equal(new GridPoint(ex, ey), result.Point);
        Assert.Equal(node, result.NodeId);
    }
}
