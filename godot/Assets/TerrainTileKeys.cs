using IC2.Engine.Assets;
using IC2.Engine.Model;

namespace IC2.Slice.Assets;

/// <summary>
/// T148 "The map draws terrain from tiles" — the one place that decides which asset-pack tile a
/// map cell draws, from the cell's own terrain code, its position and its four neighbours.
/// Godot-free by construction (the same seam <see cref="MapMarkerKeys"/> established): the map
/// screen (<c>godot/UI/GameMapView.cs</c>) calls it, and
/// <c>tests/IC2.Engine.Tests/Ui/TerrainTileKeysTests.cs</c> exercises it directly rather than
/// through a hand-copied mirror.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The code table is <c>[confirmed]</c>.</strong> The world's own tile-type list
/// (<c>data/worlds/classical-mediterranean.json</c>, <c>terrain-move-cost-table-in-dat.md</c>)
/// gives 12 codes in six names: sea ×2 (coastal <c>0</c>, deep <c>1</c>), plain <c>2</c>, desert
/// <c>3</c>, forest <c>4</c>, mountains <c>5</c>, and river ×6 (<c>6</c>–<c>11</c>). The six river
/// codes are not flavours — they are connectivity codes: <c>godot/MapViewer.cs</c>'s own
/// <c>DrawRiver</c> (<c>:161–169</c>) reads code 6 as east+west, 7 as north+south, 8 as east+north,
/// 9 as east+south, 10 as west+south and 11 as west+north, and
/// <c>docs/asset-specification.md</c> §4.5 records exactly this as the river gap a sprite renderer
/// hits. Each maps 1:1 to its <see cref="AssetKeys"/> piece; the generic
/// <see cref="AssetKeys.TerrainRiverTile"/> stays the fallback for a pack that lacks a piece.
/// </para>
/// <para>
/// <strong>The variant choice is <c>[designed]</c>.</strong> Four variants per variant-bearing
/// type break up the repeating grid the original's own tiles avoid. The choice is a fixed hash of
/// the cell's position — <c>((x × 73856093) ^ (y × 19349663)) mod 4 + 1</c>, evaluated in 64-bit
/// arithmetic so the products cannot overflow — so the map looks identical on every run and after
/// every save, and no random draw is spent. Variant 1 is the existing <c>terrain.&lt;type&gt;.tile</c>
/// key; 2–4 are the keys T148 added. A key the pack lacks falls back to variant 1 in the draw.
/// </para>
/// <para>
/// <strong>Shores are <c>[designed]</c>, from the task entry's own rule.</strong> A shore is a
/// 32-bit overlay drawn over a <em>land</em> cell on each of its four sides whose neighbour is
/// sea. A cell beyond the map's edge counts as not sea, so the map's border keeps no shore on the
/// outside. Land and sea are read from the world's own <see cref="TileType"/> table
/// (<see cref="TileType.PassableByArmies"/>/<see cref="TileType.PassableByFleets"/>), never from a
/// second code table here.
/// </para>
/// </remarks>
public static class TerrainTileKeys
{
    /// <summary>
    /// The drawn cell size, in screen pixels, at or above which the map draws terrain from the
    /// tiles; below it the flat-colour texture is drawn as before. Tiles shrunk under 12 px read as
    /// noise (the task entry's own "[designed]" draw rule), so this is the one threshold both
    /// <c>GameMapView</c> and its check read — never a second literal.
    /// </summary>
    public const int TilePixelThreshold = 12;

    /// <summary>
    /// The four keys per variant-bearing terrain code, variant 1 first. Only the six non-river
    /// types have variants.
    /// </summary>
    private static readonly Dictionary<int, string[]> VariantsByCode = new()
    {
        [0] = new[] { AssetKeys.TerrainSeaCoastalTile, AssetKeys.TerrainSeaCoastalTile2, AssetKeys.TerrainSeaCoastalTile3, AssetKeys.TerrainSeaCoastalTile4 },
        [1] = new[] { AssetKeys.TerrainSeaDeepTile, AssetKeys.TerrainSeaDeepTile2, AssetKeys.TerrainSeaDeepTile3, AssetKeys.TerrainSeaDeepTile4 },
        [2] = new[] { AssetKeys.TerrainPlainTile, AssetKeys.TerrainPlainTile2, AssetKeys.TerrainPlainTile3, AssetKeys.TerrainPlainTile4 },
        [3] = new[] { AssetKeys.TerrainDesertTile, AssetKeys.TerrainDesertTile2, AssetKeys.TerrainDesertTile3, AssetKeys.TerrainDesertTile4 },
        [4] = new[] { AssetKeys.TerrainForestTile, AssetKeys.TerrainForestTile2, AssetKeys.TerrainForestTile3, AssetKeys.TerrainForestTile4 },
        [5] = new[] { AssetKeys.TerrainMountainTile, AssetKeys.TerrainMountainTile2, AssetKeys.TerrainMountainTile3, AssetKeys.TerrainMountainTile4 },
    };

    /// <summary>
    /// The six river connectivity pieces, as a set the draw tests a base key against to decide
    /// whether the generic <see cref="AssetKeys.TerrainRiverTile"/> is that key's fallback.
    /// </summary>
    private static readonly HashSet<string> RiverPieceKeys = new(StringComparer.Ordinal)
    {
        AssetKeys.TerrainRiverEw,
        AssetKeys.TerrainRiverNs,
        AssetKeys.TerrainRiverEn,
        AssetKeys.TerrainRiverEs,
        AssetKeys.TerrainRiverWs,
        AssetKeys.TerrainRiverWn,
    };

    /// <summary>
    /// The base tile key for a terrain grid code — no variant applied, so a river code already
    /// resolves to its connectivity piece. <see langword="null"/> for a code the map does not know
    /// (the tile-type list is open), which the draw renders as the flat colour.
    /// </summary>
    public static string? KeyForCode(int code) => code switch
    {
        0 => AssetKeys.TerrainSeaCoastalTile,
        1 => AssetKeys.TerrainSeaDeepTile,
        2 => AssetKeys.TerrainPlainTile,
        3 => AssetKeys.TerrainDesertTile,
        4 => AssetKeys.TerrainForestTile,
        5 => AssetKeys.TerrainMountainTile,
        6 => AssetKeys.TerrainRiverEw,
        7 => AssetKeys.TerrainRiverNs,
        8 => AssetKeys.TerrainRiverEn,
        9 => AssetKeys.TerrainRiverEs,
        10 => AssetKeys.TerrainRiverWs,
        11 => AssetKeys.TerrainRiverWn,
        _ => null,
    };

    /// <summary>
    /// The variant (1–4) a cell draws, from its position alone: the fixed hash
    /// <c>((x × 73856093) ^ (y × 19349663)) mod 4 + 1</c> in 64-bit arithmetic, so it is
    /// deterministic across runs and saves and draws no random number.
    /// </summary>
    public static int VariantFor(int x, int y)
    {
        var hash = ((long)x * 73856093L) ^ ((long)y * 19349663L);
        return (int)(hash % 4) + 1;
    }

    /// <summary>
    /// The key a cell draws from its code and position: the variant-bearing types pick one of
    /// their four keys through <see cref="VariantFor"/>, rivers (which have no variants) and
    /// unknown codes fall back to <see cref="KeyForCode"/>. <see langword="null"/> for an unknown
    /// code.
    /// </summary>
    public static string? VariantKeyForCode(int code, int x, int y) =>
        VariantsByCode.TryGetValue(code, out var variants)
            ? variants[VariantFor(x, y) - 1]
            : KeyForCode(code);

    /// <summary>
    /// Whether <paramref name="key"/> is one of the six river connectivity pieces — the keys whose
    /// draw-time fallback is the generic <see cref="AssetKeys.TerrainRiverTile"/>.
    /// </summary>
    public static bool IsRiverPieceKey(string key) => RiverPieceKeys.Contains(key);

    /// <summary>Whether the world's tile type for <paramref name="code"/> is land a shore sits on.</summary>
    public static bool IsLand(World world, int code)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.TileTypeByCode(code)?.PassableByArmies == true;
    }

    /// <summary>
    /// Whether the world's tile type for <paramref name="code"/> is sea — passable by fleets and
    /// not by armies. A code the world does not know is not sea.
    /// </summary>
    public static bool IsSea(World world, int code)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.TileTypeByCode(code) is { } tileType
            && tileType.PassableByFleets
            && !tileType.PassableByArmies;
    }

    /// <summary>
    /// The shore overlays a land cell draws, in north, east, south, west order: one per side whose
    /// in-map neighbour is sea. A sea cell draws none, and a side whose neighbour is beyond the
    /// map's edge draws none ("a cell beyond the map's edge counts as not sea").
    /// </summary>
    public static IReadOnlyList<string> ShoreKeysFor(
        World world, int[] cells, int width, int height, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cells);
        return ShoreKeysFor(
            cells, width, height, x, y,
            code => IsLand(world, code),
            code => IsSea(world, code));
    }

    /// <summary>
    /// The predicate form of <see cref="ShoreKeysFor(World,int[],int,int,int,int)"/>: the caller
    /// supplies the world's own land and sea tests, so a test can drive a grid of codes directly
    /// without building a <see cref="World"/>.
    /// </summary>
    public static IReadOnlyList<string> ShoreKeysFor(
        int[] cells, int width, int height, int x, int y,
        Func<int, bool> isLand, Func<int, bool> isSea)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(isLand);
        ArgumentNullException.ThrowIfNull(isSea);

        if (x < 0 || x >= width || y < 0 || y >= height
            || !isLand(cells[(y * width) + x]))
        {
            return Array.Empty<string>();
        }

        var keys = new List<string>(4);
        if (IsSeaNeighbour(cells, width, height, x, y - 1, isSea))
        {
            keys.Add(AssetKeys.TerrainShoreN);
        }

        if (IsSeaNeighbour(cells, width, height, x + 1, y, isSea))
        {
            keys.Add(AssetKeys.TerrainShoreE);
        }

        if (IsSeaNeighbour(cells, width, height, x, y + 1, isSea))
        {
            keys.Add(AssetKeys.TerrainShoreS);
        }

        if (IsSeaNeighbour(cells, width, height, x - 1, y, isSea))
        {
            keys.Add(AssetKeys.TerrainShoreW);
        }

        return keys;
    }

    private static bool IsSeaNeighbour(
        int[] cells, int width, int height, int x, int y, Func<int, bool> isSea) =>
        x >= 0 && x < width && y >= 0 && y < height && isSea(cells[(y * width) + x]);
}
