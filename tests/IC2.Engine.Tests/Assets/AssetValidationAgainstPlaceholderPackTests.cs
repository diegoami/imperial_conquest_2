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

    /// <summary>T148 Done-when 2, placeholder half: the six surface keys resolve to 256 × 256
    /// 24-bit BMPs that tile.</summary>
    [Fact]
    public void PlaceholderPack_SurfaceKeys_ResolveAndTile()
    {
        TerrainSurfaceConformance.AssertEverySurfaceKeyResolvesAndTiles(PlaceholderPackDirectory);
    }
}

/// <summary>
/// T148 Done-when 2's surface rule, shared by both packs: every <c>terrain.*.surface</c> key resolves
/// to a 256 × 256 uncompressed 24-bit BMP whose left and right edge columns, and top and bottom rows,
/// have a mean absolute difference under 6 of 255 per channel — the mechanical proof that the texture
/// tiles. The authored pack's textures are conformed to this by
/// <c>scripts/generate-authored-assets.py</c>; the placeholder pack's are procedural stand-ins with an
/// exactly periodic pattern.
/// </summary>
internal static class TerrainSurfaceConformance
{
    private const int SurfaceSize = 256;
    private const int BmpHeaderSize = 54;
    private const double EdgeDifferenceLimit = 6.0;

    public static void AssertEverySurfaceKeyResolvesAndTiles(string packDirectory)
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(packDirectory, "manifest.json"));
        var surfaceKeys = AssetKeys.AllKeys
            .Where(key => key.StartsWith("terrain.", StringComparison.Ordinal)
                       && key.EndsWith(".surface", StringComparison.Ordinal))
            .ToList();

        // The six classes of T148; a missing account would let a key slip through while every loop
        // below still passed.
        Assert.Equal(6, surfaceKeys.Count);

        foreach (var key in surfaceKeys)
        {
            var fullPath = Path.Combine(packDirectory, pack.ResolveAsset(key));
            Assert.True(File.Exists(fullPath), $"Surface file not found: {key} ({fullPath})");

            var bytes = File.ReadAllBytes(fullPath);
            AssertBmp256Structure(bytes, key);

            var difference = EdgeMeanAbsoluteDifference(bytes);
            Assert.True(difference < EdgeDifferenceLimit,
                $"{key}: the surface does not tile — its edge mean absolute difference is "
                + $"{difference:F2}, expected under {EdgeDifferenceLimit} of 255");
        }
    }

    /// <summary>Recomputes the BMP fields independently: 54-byte header, 40-byte DIB header,
    /// 256 × 256 bottom-up, 1 plane, 24-bit BI_RGB, and the pixel array 256 × 256 × 3 with no row
    /// padding (768 bytes a row is already 4-byte aligned).</summary>
    private static void AssertBmp256Structure(byte[] bytes, string key)
    {
        Assert.True(bytes.Length >= BmpHeaderSize, $"{key}: file too short for a BMP header");
        Assert.True(bytes[0] == (byte)'B' && bytes[1] == (byte)'M', $"{key}: missing 'BM' signature");
        Assert.Equal((uint)bytes.Length, BitConverter.ToUInt32(bytes, 2));
        Assert.Equal((uint)BmpHeaderSize, BitConverter.ToUInt32(bytes, 10));
        Assert.Equal(40u, BitConverter.ToUInt32(bytes, 14));
        Assert.Equal(SurfaceSize, BitConverter.ToInt32(bytes, 18));
        Assert.Equal(SurfaceSize, BitConverter.ToInt32(bytes, 22));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 26));

        var bitCount = BitConverter.ToUInt16(bytes, 28);
        Assert.True(bitCount == 24,
            $"{key}: bit depth is {bitCount}, expected 24 (docs/asset-specification.md 1.2: a surface is "
            + "a 24-bit opaque tile)");

        Assert.Equal(0u, BitConverter.ToUInt32(bytes, 30)); // BI_RGB, uncompressed

        var expectedPixelDataSize = SurfaceSize * SurfaceSize * 3;
        Assert.Equal((uint)expectedPixelDataSize, BitConverter.ToUInt32(bytes, 34));
        Assert.Equal(expectedPixelDataSize, bytes.Length - BmpHeaderSize);
    }

    /// <summary>The mean absolute difference, over the three channels and both axes, between the left
    /// and right edge columns and between the top and bottom rows. BMP rows are bottom-up, but the
    /// edge columns are the same at any row and the top/bottom rows are symmetric, so the file's own
    /// row 0 and row 255 are compared as-is.</summary>
    private static double EdgeMeanAbsoluteDifference(byte[] bytes)
    {
        const int rowSize = SurfaceSize * 3;
        double total = 0;
        var count = 0;

        for (var row = 0; row < SurfaceSize; row++)
        {
            var rowStart = BmpHeaderSize + (row * rowSize);
            for (var c = 0; c < 3; c++)
            {
                total += Math.Abs(bytes[rowStart + c] - bytes[rowStart + ((SurfaceSize - 1) * 3) + c]);
                count++;
            }
        }

        var topStart = BmpHeaderSize + ((SurfaceSize - 1) * rowSize);
        for (var x = 0; x < rowSize; x++)
        {
            total += Math.Abs(bytes[BmpHeaderSize + x] - bytes[topStart + x]);
            count++;
        }

        return total / count;
    }
}
