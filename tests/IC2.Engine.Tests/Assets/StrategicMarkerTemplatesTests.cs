using System.Text.Json;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// The owner's request of 2026-10-21: the authored pack's army, fleet and town markers are opaque
/// recolour templates (scripts/generate-strategic-markers.py), recoloured per owner at draw time the
/// way the original recolours its unit icons: purple field, white outline, blue body; a fleet's black
/// hull is kept.
/// </summary>
public sealed class StrategicMarkerTemplatesTests
{
    private const uint Purple = 0xff800080, White = 0xffffffff, Blue = 0xff0000ff, Black = 0xff000000;

    public static TheoryData<string> Keys => new()
    {
        "army.tier1.icon", "army.tier2.icon", "army.tier3.icon",
        "fleet.tier1.icon", "fleet.tier2.icon", "fleet.tier3.icon",
        "city.tier1.icon", "city.tier2.icon", "city.tier3.icon", "city.tier4.icon", "city.capital.icon",
    };

    [Theory]
    [MemberData(nameof(Keys))]
    public void Authored_marker_is_an_opaque_template_on_the_purple_field(string key)
    {
        var pixels = ReadPixels(key);
        Assert.Equal(Purple, pixels[0]);
        foreach (var color in new[] { Purple, White, Blue })
        {
            Assert.Contains(color, pixels);
        }

        var allowed = key.StartsWith("fleet.", StringComparison.Ordinal)
            ? new[] { Purple, White, Blue, Black }
            : new[] { Purple, White, Blue };
        Assert.All(pixels, p => Assert.Contains(p, allowed));
    }

    [Fact]
    public void Each_tier_draws_a_larger_subject_than_the_one_before()
    {
        foreach (var keys in new[]
                 {
                     new[] { "army.tier1.icon", "army.tier2.icon", "army.tier3.icon" },
                     new[] { "fleet.tier1.icon", "fleet.tier2.icon", "fleet.tier3.icon" },
                     new[] { "city.tier1.icon", "city.tier2.icon", "city.tier3.icon", "city.tier4.icon" },
                 })
        {
            var areas = keys.Select(k => ReadPixels(k).Count(p => p != Purple)).ToArray();
            for (var i = 1; i < areas.Length; i++)
            {
                Assert.True(areas[i - 1] < areas[i], $"{keys[i]}: subject area {areas[i]} not above {areas[i - 1]}");
            }
        }
    }

    private static uint[] ReadPixels(string key)
    {
        var root = Path.Combine(FixturePaths.RepositoryRoot, "assets", "packs", "authored");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        var data = File.ReadAllBytes(Path.Combine(root, manifest.RootElement.GetProperty("assets").GetProperty(key).GetString()!));
        Assert.Equal(4150, data.Length);
        Assert.Equal(32, BitConverter.ToUInt16(data, 28));
        var pixels = new uint[1024];
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
            pixels[y * 32 + x] = BitConverter.ToUInt32(data, 54 + ((31 - y) * 32 + x) * 4);
        return pixels;
    }
}
