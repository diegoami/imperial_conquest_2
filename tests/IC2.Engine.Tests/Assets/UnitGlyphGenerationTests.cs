using System.Text.Json;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>T182's committed glyph contract. Geometry is owner-designed; these checks do
/// not establish human recognition or original-art equivalence, and require no Python.</summary>
public sealed class UnitGlyphGenerationTests
{
    private static readonly string[] Types =
        ["light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry"];
    private static readonly string[] Sizes = ["small", "medium", "large"];
    private const uint Purple = 0xff800080, White = 0xffffffff, Blue = 0xff0000ff;
    private const uint Lime = 0xff00ff00, Black = 0xff000000;

    [Theory]
    [InlineData("authored")]
    [InlineData("placeholder")]
    public void Pack_HasExactly15Battle3Army3FleetKeys_WithRequiredPixels(string pack)
    {
        var root = Path.Combine(FixturePaths.RepositoryRoot, "assets", "packs", pack);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        var assets = manifest.RootElement.GetProperty("assets");
        var expected = Types.SelectMany(type => Sizes.Select(size => $"battle.unit.{type}.{size}"))
            .Concat(new[] { "army", "fleet" }.SelectMany(kind => Enumerable.Range(1, 3)
                .Select(tier => $"{kind}.tier{tier}.icon"))).Order().ToArray();
        var actual = assets.EnumerateObject().Select(p => p.Name).Where(k =>
            k.StartsWith("battle.unit.", StringComparison.Ordinal) ||
            k.StartsWith("army.", StringComparison.Ordinal) || k.StartsWith("fleet.", StringComparison.Ordinal))
            .Order().ToArray();
        Assert.Equal(21, actual.Length);
        Assert.Equal(expected, actual);
        foreach (var key in expected)
        {
            var pixels = ReadPixels(Path.Combine(root, assets.GetProperty(key).GetString()!));
            if (key.StartsWith("battle.", StringComparison.Ordinal))
            {
                foreach (var color in new[] { Purple, White, Blue, Black, Lime })
                    Assert.Contains(color, pixels);
                Assert.All(pixels, p => Assert.Contains(p, new[] { Purple, White, Blue, Black, Lime }));
                // Exact dotted outer ring, including the four corners' union.
                for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    if (x != 0 && x != 31 && y != 0 && y != 31) continue;
                    var dot = ((y is 0 or 31) && x % 2 == 0) || ((x is 0 or 31) && y % 2 == 0);
                    Assert.Equal(dot ? Lime : Purple, pixels[y * 32 + x]);
                }
            }
            else if (pack == "authored")
            {
                // The owner's request of 2026-10-21 (scripts/generate-strategic-markers.py): an opaque
                // recolour template on the purple field, white outline, blue body/sail and a black hull.
                Assert.Equal(Purple, pixels[0]);
                foreach (var color in new[] { Purple, White, Blue })
                    Assert.Contains(color, pixels);
                Assert.All(pixels, p => Assert.Contains(p, new[] { Purple, White, Blue, Black }));
            }
            else
            {
                Assert.Contains(0u, pixels);
                Assert.Contains(White, pixels);
                Assert.All(pixels, p => Assert.True(p is 0 or White, $"{key}: non-neutral/partial-alpha pixel {p:x8}"));
                for (var i = 0; i < 32; i++)
                {
                    Assert.Equal(0u, pixels[i]);
                    Assert.Equal(0u, pixels[31 * 32 + i]);
                    Assert.Equal(0u, pixels[i * 32]);
                    Assert.Equal(0u, pixels[i * 32 + 31]);
                }
            }
        }
    }

    [Theory]
    [InlineData("authored")]
    [InlineData("placeholder")]
    public void AdjacentTiers_HaveStrictlyIncreasingSilhouetteArea_AndDifferentMotifs(string pack)
    {
        var root = Path.Combine(FixturePaths.RepositoryRoot, "assets", "packs", pack);
        var silhouettes = new List<string>();
        foreach (var type in Types)
        {
            var areas = Sizes.Select(size => ReadPixels(Path.Combine(root, "battle", $"unit_{type}_{size}.bmp"))
                .Count(p => p is White or Blue)).ToArray();
            Assert.True(areas[0] < areas[1] && areas[1] < areas[2], $"{type}: unordered areas {string.Join(",", areas)}");
            silhouettes.Add(Convert.ToHexString(File.ReadAllBytes(Path.Combine(root, "battle", $"unit_{type}_small.bmp"))));
        }
        Assert.Equal(5, silhouettes.Distinct().Count());
        foreach (var kind in new[] { "army", "fleet" })
        {
            var areas = Enumerable.Range(1, 3).Select(tier =>
                ReadPixels(Path.Combine(root, kind, $"tier{tier}.bmp")).Count(p => p != 0 && p != Purple)).ToArray();
            Assert.True(areas[0] < areas[1] && areas[1] < areas[2], $"{kind}: unordered areas {string.Join(",", areas)}");
        }
    }

    private static uint[] ReadPixels(string path)
    {
        var data = File.ReadAllBytes(path);
        Assert.Equal(4150, data.Length);
        Assert.Equal((byte)'B', data[0]);
        Assert.Equal((byte)'M', data[1]);
        Assert.Equal(54, BitConverter.ToInt32(data, 10));
        Assert.Equal(32, BitConverter.ToInt32(data, 18));
        Assert.Equal(32, BitConverter.ToInt32(data, 22));
        Assert.Equal(32, BitConverter.ToUInt16(data, 28));
        Assert.Equal(0, BitConverter.ToInt32(data, 30));
        var pixels = new uint[1024];
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
            pixels[y * 32 + x] = BitConverter.ToUInt32(data, 54 + ((31 - y) * 32 + x) * 4);
        return pixels;
    }
}
