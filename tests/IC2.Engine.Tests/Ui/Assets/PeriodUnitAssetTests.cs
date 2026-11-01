using System.Buffers.Binary;
using IC2.Engine.Assets;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Ui.Assets;

public sealed class PeriodUnitAssetTests
{
    [Theory]
    [InlineData("authored")]
    [InlineData("placeholder")]
    public void Optional_period_keys_resolve_to_conforming_neutral_and_tintable_BMPs(string packId)
    {
        var directory = Path.Combine(TestPaths.DataRoot, "..", "assets", "packs", packId);
        var pack = AssetLoader.LoadManifest(Path.Combine(directory, "manifest.json"));
        foreach (var type in new[] { "light_chariot", "heavy_chariot" })
        foreach (var size in new[] { "icon", "small", "medium", "large" })
        {
            var battle = size != "icon";
            var key = battle ? $"battle.unit.{type}.{size}" : $"unit.{type}.icon";
            Assert.True(pack.TryResolveAsset(key, out var path), key);
            var bytes = File.ReadAllBytes(Path.Combine(directory, path));
            Assert.Equal(32, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4)));
            Assert.Equal(32, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4)));
            Assert.Equal(32, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(28, 2)));
            var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
            var colors = new List<(byte R, byte G, byte B, byte A)>();
            for (var i = offset; i < bytes.Length; i += 4) colors.Add((bytes[i + 2], bytes[i + 1], bytes[i], bytes[i + 3]));
            if (battle)
            {
                Assert.All(colors, p => Assert.Equal(255, p.A));
                Assert.Contains(((byte)128, (byte)0, (byte)128, (byte)255), colors);
                Assert.Contains(((byte)255, (byte)255, (byte)255, (byte)255), colors);
                Assert.Contains(((byte)0, (byte)0, (byte)255, (byte)255), colors);
                Assert.Contains(((byte)0, (byte)255, (byte)0, (byte)255), colors);
            }
            else
            {
                Assert.Contains(colors, p => p.A == 0);
                Assert.Contains(colors, p => p.A == 255);
                Assert.All(colors, p => { Assert.Equal(p.R, p.G); Assert.Equal(p.G, p.B); });
            }
        }
    }
}
