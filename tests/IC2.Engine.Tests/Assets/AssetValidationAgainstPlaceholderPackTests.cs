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
/// to a 256 × 256 uncompressed 24-bit BMP that <em>tiles</em> — for each of the six (axis, channel)
/// pairs (left|right columns and top|bottom rows, per R/G/B) the mean absolute difference is under 15
/// of 255, asserted separately and never pooled (the Opus review of the splatting round, R3; the bar
/// moved from 6 to 15 by the user's decisions of 2026-10-06 at the visual review, because detailed
/// textures differ more than 6 at a natural wrap). The authored pack's textures are conformed to this
/// by <c>scripts/generate-authored-assets.py</c>; the placeholder pack's are procedural stand-ins with
/// an exactly periodic pattern.
/// </summary>
internal static class TerrainSurfaceConformance
{
    private const int SurfaceSize = 256;
    private const int BmpHeaderSize = 54;
    private const double EdgeDifferenceLimit = 15.0;

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

            // The six (axis, channel) means asserted SEPARATELY (the Opus review, R3): pooling them
            // would let a texture with a visible seam in one channel and clean edges elsewhere pass.
            var (horizontal, vertical) = EdgeDifferencesPerChannel(bytes);
            for (var channel = 0; channel < 3; channel++)
            {
                var channelName = ChannelName(channel);
                Assert.True(horizontal[channel] < EdgeDifferenceLimit,
                    $"{key}: the surface does not tile — the left/right edge columns differ by "
                    + $"{horizontal[channel]:F2} in {channelName}, expected under {EdgeDifferenceLimit} of 255");
                Assert.True(vertical[channel] < EdgeDifferenceLimit,
                    $"{key}: the surface does not tile — the top/bottom rows differ by "
                    + $"{vertical[channel]:F2} in {channelName}, expected under {EdgeDifferenceLimit} of 255");
            }
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

    /// <summary>
    /// The mean absolute difference per channel (BMP byte order: B, G, R) between the left and right
    /// edge columns (horizontal) and between the top and bottom rows (vertical). BMP rows are
    /// bottom-up, but the edge columns are the same at any row and the top/bottom rows are symmetric,
    /// so the file's own row 0 and row 255 are compared as-is. Each of the six values is asserted
    /// against the limit separately — never pooled (the Opus review of the splatting round, R3).
    /// </summary>
    private static (double[] Horizontal, double[] Vertical) EdgeDifferencesPerChannel(byte[] bytes)
    {
        const int rowSize = SurfaceSize * 3;
        var horizontal = new double[3];
        var vertical = new double[3];

        for (var row = 0; row < SurfaceSize; row++)
        {
            var rowStart = BmpHeaderSize + (row * rowSize);
            for (var c = 0; c < 3; c++)
            {
                horizontal[c] += Math.Abs(bytes[rowStart + c] - bytes[rowStart + ((SurfaceSize - 1) * 3) + c]);
            }
        }

        var topStart = BmpHeaderSize + ((SurfaceSize - 1) * rowSize);
        for (var x = 0; x < rowSize; x++)
        {
            var c = x % 3;
            vertical[c] += Math.Abs(bytes[BmpHeaderSize + x] - bytes[topStart + x]);
        }

        for (var c = 0; c < 3; c++)
        {
            horizontal[c] /= SurfaceSize;
            vertical[c] /= SurfaceSize;
        }

        return (horizontal, vertical);
    }

    private static string ChannelName(int channel) => channel switch
    {
        0 => "blue",
        1 => "green",
        _ => "red",
    };
}
