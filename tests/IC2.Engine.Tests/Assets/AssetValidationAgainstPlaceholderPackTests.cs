using IC2.Engine.Assets;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// T48 "Draw armies and cities from the asset pack" DoD 5: <see cref="AssetLoader.ValidateAssets"/>
/// -- the check T11 wrote in <c>src/IC2.Engine/Assets/AssetLoader.cs</c> and nothing had ever
/// called -- run against the real, committed placeholder pack (T11's
/// <c>assets/packs/placeholder/</c>), reporting zero missing files.
/// </summary>
/// <remarks>
/// T11's own <c>PlaceholderPackIntegrationTests</c> (this same folder) checks every
/// <see cref="AssetKeys.AllKeys"/> entry by hand-rolling its own missing-key/missing-file loops
/// (<c>AllRequiredAssetKeys_ResolveToPack</c>, <c>AllAssetFiles_ExistOnDisk</c>) -- it never once
/// calls the actual <see cref="AssetLoader.ValidateAssets"/> method it also wrote. This is a new
/// test file, not a duplicate of those: it exercises the method itself, not a hand-rolled
/// equivalent, closing exactly the "T11 wrote a check, nothing called it" DoD 5 gap.
/// </para>
/// <para>
/// T148 Done-when 2 widens it: the real placeholder pack must resolve every terrain key and every
/// terrain file must conform to the format the section 1.2–1.3 rules require — 24-bit for a base,
/// variant or river tile, 32-bit BGRA with transparency for a shore overlay, whose opaque pixels
/// lie within 10 px of its named edge. <see cref="TerrainPackConformance"/> is shared with
/// <c>AuthoredPackConformanceTests</c> so the two packs are checked by one rule.
/// </para>
/// </remarks>
public sealed class AssetValidationAgainstPlaceholderPackTests
{
    private static readonly string PlaceholderPackDirectory =
        Path.Combine(FixturePaths.RepositoryRoot, "assets", "packs", "placeholder");

    [Fact]
    public void ValidateAssets_AgainstPlaceholderPack_ReportsNoMissingFiles()
    {
        var manifestPath = Path.Combine(PlaceholderPackDirectory, "manifest.json");
        var pack = AssetLoader.LoadManifest(manifestPath);

        var missing = AssetLoader.ValidateAssets(pack, PlaceholderPackDirectory);

        Assert.Empty(missing);
    }

    /// <summary>T148 Done-when 2, placeholder half: every terrain key resolves and every file conforms.</summary>
    [Fact]
    public void PlaceholderPack_TerrainKeys_ResolveAndConformToTheTerrainFormat()
    {
        TerrainPackConformance.AssertEveryTerrainKeyResolvesAndConforms(PlaceholderPackDirectory);
    }
}

/// <summary>
/// T148 Done-when 2's terrain-format rule, shared by both packs: the 35 terrain keys (the seven
/// pre-existing tiles, T148's 18 variants, six river pieces and four shore overlays) each resolve to a
/// 32 × 32 uncompressed BMP — 24-bit for a tile, 32-bit BGRA for a shore overlay — and every shore
/// overlay has a transparent background and an opaque band within 10 px of its named edge.
/// </summary>
internal static class TerrainPackConformance
{
    private const int BmpHeaderSize = 54;

    public static void AssertEveryTerrainKeyResolvesAndConforms(string packDirectory)
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(packDirectory, "manifest.json"));
        var terrainKeys = AssetKeys.AllKeys
            .Where(key => key.StartsWith("terrain.", StringComparison.Ordinal))
            .ToList();

        // The seven pre-existing tiles plus T148's 28. A missing account would let a key slip through
        // this file while every loop below still passed.
        Assert.Equal(35, terrainKeys.Count);

        foreach (var key in terrainKeys)
        {
            // ResolveAsset throws for a key the manifest does not carry; File.Exists then catches a
            // manifest entry whose file is absent.
            var fullPath = Path.Combine(packDirectory, pack.ResolveAsset(key));
            Assert.True(File.Exists(fullPath), $"Terrain file not found: {key} ({fullPath})");

            var bytes = File.ReadAllBytes(fullPath);
            var isShore = key.StartsWith("terrain.shore.", StringComparison.Ordinal);
            AssertBmpStructure(bytes, key, isShore ? 32 : 24);
            if (isShore)
            {
                AssertShoreOverlayFitsItsEdge(bytes, key);
            }
        }
    }

    /// <summary>
    /// Recomputes every BMP field independently, parameterized on the expected bit depth: the "BM"
    /// signature, the declared file size against the actual length, the 54-byte header offset, the
    /// 40-byte BITMAPINFOHEADER, 32 × 32 bottom-up dimensions, 1 colour plane, BI_RGB, and the pixel
    /// array's size recomputed from width/height/depth with 4-byte row padding.
    /// </summary>
    private static void AssertBmpStructure(byte[] bytes, string assetKey, int expectedBitCount)
    {
        Assert.True(bytes.Length >= BmpHeaderSize,
            $"{assetKey}: file too short to hold a BMP file header + DIB header ({bytes.Length} bytes)");
        Assert.True(bytes[0] == (byte)'B' && bytes[1] == (byte)'M', $"{assetKey}: missing 'BM' signature");

        Assert.Equal((uint)bytes.Length, BitConverter.ToUInt32(bytes, 2));
        Assert.Equal((uint)BmpHeaderSize, BitConverter.ToUInt32(bytes, 10));

        Assert.Equal(40u, BitConverter.ToUInt32(bytes, 14));
        Assert.Equal(32, BitConverter.ToInt32(bytes, 18));
        Assert.Equal(32, BitConverter.ToInt32(bytes, 22));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 26));

        var bitCount = BitConverter.ToUInt16(bytes, 28);
        Assert.True(bitCount == expectedBitCount,
            $"{assetKey}: bit depth is {bitCount}, expected {expectedBitCount} "
            + "(docs/asset-specification.md 1.2: 24-bit opaque terrain, 32-bit BGRA overlays)");

        Assert.Equal(0u, BitConverter.ToUInt32(bytes, 30)); // BI_RGB, uncompressed

        var rowSizeUnpadded = 32 * (bitCount / 8);
        var rowPadding = (4 - rowSizeUnpadded % 4) % 4;
        var expectedPixelDataSize = (rowSizeUnpadded + rowPadding) * 32;
        Assert.Equal((uint)expectedPixelDataSize, BitConverter.ToUInt32(bytes, 34));
        Assert.Equal(expectedPixelDataSize, bytes.Length - BmpHeaderSize);
    }

    /// <summary>
    /// A shore overlay is 32-bit BGRA: its background is transparent somewhere (so the land tile
    /// shows through) and its opaque band lies within 10 px of the edge the key names — north
    /// <c>y ≤ 9</c>, south <c>y ≥ 22</c>, west <c>x ≤ 9</c>, east <c>x ≥ 22</c> in top-down pixels.
    /// A BMP stores rows bottom-up, so the DIB row is <c>height − 1 − fileRow</c>.
    /// </summary>
    private static void AssertShoreOverlayFitsItsEdge(byte[] bytes, string key)
    {
        var edge = key.Split('.')[^1];
        Assert.True(edge is "n" or "e" or "s" or "w", $"{key}: unknown shore edge '{edge}'");

        var transparentPixels = 0;
        var opaquePixels = 0;
        var outsideBand = 0;
        for (var offset = BmpHeaderSize; offset + 3 < bytes.Length; offset += 4)
        {
            var alpha = bytes[offset + 3];
            if (alpha == 0)
            {
                transparentPixels++;
                continue;
            }

            opaquePixels++;
            var pixelIndex = (offset - BmpHeaderSize) / 4;
            var fileRow = pixelIndex / 32;
            var x = pixelIndex % 32;
            var y = 31 - fileRow; // top-down, from the bottom-up file rows

            var within = edge switch
            {
                "n" => y <= 9,
                "s" => y >= 22,
                "w" => x <= 9,
                _ => x >= 22,
            };
            if (!within)
            {
                outsideBand++;
            }
        }

        Assert.True(transparentPixels > 0,
            $"{key}: no transparent pixel - a shore overlay is 32-bit BGRA over a land tile, so its "
            + "background must be keyed out");
        Assert.True(opaquePixels > 0, $"{key}: no opaque pixel - the shore band is missing");
        Assert.True(outsideBand == 0,
            $"{key}: {outsideBand} opaque pixel(s) lie more than 10 px from its '{edge}' edge");
    }
}
