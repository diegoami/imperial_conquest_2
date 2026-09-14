using IC2.Engine.Movement;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Movement;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T09 Movement and terrain", Done When 1: "The 12-entry table drives
/// costs: Sea 1, Sea 3, Plain 1, Desert 1, Forest 2, Mountains 4, and all six River codes 4."
/// </summary>
/// <remarks>
/// Every expected value comes from the T04 fixtures corpus (<c>terrain.moveCost.code0</c> …
/// <c>code11</c>, sourced from <c>terrain-move-cost-table-in-dat.md</c>), never a C# literal repeating
/// the number — the toy ruleset's own <c>terrain.moveCosts</c> block is transcribed from the same
/// report, and this test cross-checks the two rather than trusting either alone.
/// </remarks>
public sealed class TerrainCostTableTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void MoveCostFor_EveryTerrainCode_MatchesFixtureCorpus(int cellCode)
    {
        var tileType = MovementTestbed.World.TileTypeByCode(cellCode)
            ?? throw new InvalidOperationException($"The toy world defines no tile type for code {cellCode}.");
        var expected = FixtureCorpus.Get($"terrain.moveCost.code{cellCode}").AsInt();

        var result = TerrainCostLookup.MoveCostFor(MovementTestbed.Ruleset.Terrain, tileType.Id);

        Assert.True(result.WasPricedByRuleset);
        Assert.Equal(expected, result.MoveCost);
    }

    [Fact]
    public void MoveCostFor_AllSixRiverCodes_Cost4()
    {
        var riverCodes = new[] { 6, 7, 8, 9, 10, 11 };

        foreach (var code in riverCodes)
        {
            var tileType = MovementTestbed.World.TileTypeByCode(code)!;
            Assert.Equal("River", tileType.Name);

            var result = TerrainCostLookup.MoveCostFor(MovementTestbed.Ruleset.Terrain, tileType.Id);
            Assert.Equal(4, result.MoveCost);
        }
    }

    [Fact]
    public void MoveCostFor_TheTwelveEntryTable_HasExactlyTwelveEntries()
    {
        // terrain-move-cost-table-in-dat.md: "DAT offset 0x1F622, table ends at 11; entry 12 onward is
        // an unrelated string block" (tests/fixtures/corpus.json, terrain.datOffset's note).
        Assert.Equal(12, MovementTestbed.Ruleset.Terrain.MoveCosts.Count);
    }

    [Theory]
    [InlineData("sea_coastal", 1)]
    [InlineData("sea_deep", 3)]
    [InlineData("plain", 1)]
    [InlineData("desert", 1)]
    [InlineData("forest", 2)]
    [InlineData("mountains", 4)]
    public void MoveCostFor_NamedTileTypes_MatchesExpectedCost(string tileTypeId, int expectedCost)
    {
        var result = TerrainCostLookup.MoveCostFor(MovementTestbed.Ruleset.Terrain, tileTypeId);
        Assert.Equal(expectedCost, result.MoveCost);
    }
}
