using IC2.Engine.Assets;
using IC2.Engine.Serialization;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// <c>docs/tasks/T148.md</c> Done-when 1: <see cref="TerrainTileKeys"/> maps every terrain code to its
/// asset-pack key, picks the four variants deterministically, and decides a cell's shore overlays from
/// its neighbours. Godot-free, so it is exercised here directly rather than through the headless map
/// check.
/// </summary>
public sealed class TerrainTileKeysTests
{
    private static readonly GameDataRepository Repository =
        GameDataRepository.Load(ModelTestPaths.DataRoot);

    /// <summary>
    /// Done-when 1, the code table: all twelve codes of the world's own terrain list
    /// (<c>data/worlds/classical-mediterranean.json</c>) map to their exact key, with the six river
    /// codes resolving to their connectivity pieces in the order <c>MapViewer.DrawRiver</c> reads
    /// them. An unknown code has no key, which the draw renders as the flat colour.
    /// </summary>
    [Theory]
    [InlineData(0, AssetKeys.TerrainSeaCoastalTile)]
    [InlineData(1, AssetKeys.TerrainSeaDeepTile)]
    [InlineData(2, AssetKeys.TerrainPlainTile)]
    [InlineData(3, AssetKeys.TerrainDesertTile)]
    [InlineData(4, AssetKeys.TerrainForestTile)]
    [InlineData(5, AssetKeys.TerrainMountainTile)]
    [InlineData(6, AssetKeys.TerrainRiverEw)]
    [InlineData(7, AssetKeys.TerrainRiverNs)]
    [InlineData(8, AssetKeys.TerrainRiverEn)]
    [InlineData(9, AssetKeys.TerrainRiverEs)]
    [InlineData(10, AssetKeys.TerrainRiverWs)]
    [InlineData(11, AssetKeys.TerrainRiverWn)]
    public void Every_tile_code_maps_to_its_key(int code, string expectedKey)
    {
        Assert.Equal(expectedKey, TerrainTileKeys.KeyForCode(code));
        Assert.Contains(expectedKey, AssetKeys.AllKeys);
    }

    [Fact]
    public void An_unknown_tile_code_has_no_key()
    {
        Assert.Null(TerrainTileKeys.KeyForCode(-1));
        Assert.Null(TerrainTileKeys.KeyForCode(12));
    }

    /// <summary>
    /// Done-when 1: a land cell with sea to its north and west draws exactly those two shore
    /// overlays; a sea cell draws none; a side whose neighbour is beyond the map's edge counts as not
    /// sea, so an edge cell draws none on that side. The predicates stand in for the world's own
    /// land/sea table (codes 2–11 vs 0–1), which the tests above already tie to
    /// <see cref="TerrainTileKeys.KeyForCode"/>.
    /// </summary>
    [Fact]
    public void Shore_keys_are_the_sea_facing_sides_of_a_land_cell()
    {
        static bool IsLand(int code) => code == 2;
        static bool IsSea(int code) => code is 0 or 1;

        // 3x3: land at the centre, sea to its north and west, land east and south. The centre draws
        // north and west, and nothing else.
        var grid = new[]
        {
            2, 0, 2,
            1, 2, 2,
            2, 2, 2,
        };

        Assert.Equal(
            new[] { AssetKeys.TerrainShoreN, AssetKeys.TerrainShoreW },
            TerrainTileKeys.ShoreKeysFor(grid, 3, 3, 1, 1, IsLand, IsSea));

        // The sea cell itself draws none.
        Assert.Empty(TerrainTileKeys.ShoreKeysFor(grid, 3, 3, 1, 0, IsLand, IsSea));

        // The north-west land corner: north and west are off-map (not sea), so neither can add a
        // shore, whatever its in-map sides do. The exact in-map-side case is the next test.
        var corner = TerrainTileKeys.ShoreKeysFor(grid, 3, 3, 0, 0, IsLand, IsSea);
        Assert.DoesNotContain(AssetKeys.TerrainShoreN, corner);
        Assert.DoesNotContain(AssetKeys.TerrainShoreW, corner);
    }

    /// <summary>
    /// Done-when 1: a side whose in-map neighbour is sea, on a land cell that itself touches the map
    /// edge, draws that side and leaves the off-map sides alone.
    /// </summary>
    [Fact]
    public void A_land_cell_on_the_map_edge_keeps_only_its_in_map_sea_sides()
    {
        static bool IsLand(int code) => code == 2;
        static bool IsSea(int code) => code is 0 or 1;

        // 2x2: land at (0,0), land at (0,1), land at (1,1), sea at (1,0). (0,0)'s north and west are
        // off-map; its east is sea, so exactly east is a shore.
        var grid = new[]
        {
            2, 0,
            2, 2,
        };

        Assert.Equal(
            new[] { AssetKeys.TerrainShoreE },
            TerrainTileKeys.ShoreKeysFor(grid, 2, 2, 0, 0, IsLand, IsSea));
    }

    /// <summary>
    /// Done-when 1: the variant is the same on two calls and always in 1–4, for a spread of
    /// positions.
    /// </summary>
    [Fact]
    public void Cell_variant_is_deterministic_and_within_one_to_four()
    {
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (319, 139), (137, 58), (200, 17) })
        {
            var first = TerrainTileKeys.VariantFor(x, y);
            var second = TerrainTileKeys.VariantFor(x, y);

            Assert.Equal(first, second);
            Assert.InRange(first, 1, 4);

            var key = TerrainTileKeys.VariantKeyForCode(2, x, y);
            Assert.Contains(key, new[]
            {
                AssetKeys.TerrainPlainTile,
                AssetKeys.TerrainPlainTile2,
                AssetKeys.TerrainPlainTile3,
                AssetKeys.TerrainPlainTile4,
            });
            Assert.Equal(key, TerrainTileKeys.VariantKeyForCode(2, x, y));
        }
    }

    /// <summary>
    /// Done-when 1: a river code keeps its connectivity piece (it has no variants), so
    /// <see cref="TerrainTileKeys.VariantKeyForCode"/> returns the same key
    /// <see cref="TerrainTileKeys.KeyForCode"/> does, whatever the position.
    /// </summary>
    [Theory]
    [InlineData(6, AssetKeys.TerrainRiverEw)]
    [InlineData(8, AssetKeys.TerrainRiverEn)]
    [InlineData(11, AssetKeys.TerrainRiverWn)]
    public void River_codes_have_no_variant(int code, string expectedKey)
    {
        Assert.Equal(expectedKey, TerrainTileKeys.VariantKeyForCode(code, 7, 9));
    }

    /// <summary>
    /// Done-when 1: over the shipped classical world's 320 × 140 cells, each of the four plain
    /// variants covers between 15% and 35% of that world's plain cells — the fixed hash spreads the
    /// variants roughly evenly, so the map does not show a single repeating variant.
    /// </summary>
    [Fact]
    public void Each_plain_variant_covers_a_fair_share_of_the_classical_world()
    {
        var world = Repository.Resolve("classical-mediterranean").World;
        var cells = world.Terrain.Decode(world.Width, world.Height);

        var counts = new int[5];
        var plainCells = 0;
        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
            {
                if (cells[(y * world.Width) + x] != 2)
                {
                    continue;
                }

                plainCells++;
                counts[TerrainTileKeys.VariantFor(x, y)]++;
            }
        }

        Assert.True(plainCells > 0, "the classical world has plain cells to measure");
        for (var variant = 1; variant <= 4; variant++)
        {
            var share = (double)counts[variant] / plainCells;
            Assert.InRange(share, 0.15, 0.35);
        }
    }

    /// <summary>
    /// Done-when 1: the shore-key overload that takes the world reads the world's own tile table, so
    /// land/sea classification cannot drift from the engine's. A known sea cell (code 0) and a known
    /// land cell (code 2) are placed directly in a two-cell grid.
    /// </summary>
    [Fact]
    public void The_world_backed_shore_overload_uses_the_worlds_own_land_and_sea_table()
    {
        var world = Repository.Resolve("toy-3city").World;

        // One row: land (2) at x=0, sea (0) at x=1. The land cell's east side is sea; a cell with no
        // in-map sea neighbour draws none.
        var grid = new[] { 2, 0 };
        Assert.Equal(
            new[] { AssetKeys.TerrainShoreE },
            TerrainTileKeys.ShoreKeysFor(world, grid, 2, 1, 0, 0));
        Assert.Empty(TerrainTileKeys.ShoreKeysFor(world, grid, 2, 1, 1, 0));
    }
}
