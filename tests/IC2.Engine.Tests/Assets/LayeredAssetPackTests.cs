using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>T292c: per-key fallback and the status states, with synthetic files only.</summary>
public sealed class LayeredAssetPackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic2-layer-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string MakePack(string id, params (string Key, string File)[] items)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        var entries = string.Join(",", items.Select(i => $"\"{i.Key}\":\"{i.File}\""));
        File.WriteAllText(Path.Combine(dir, "manifest.json"), "{\"schemaVersion\":1,\"name\":\"t\",\"description\":\"t\",\"assets\":{" + entries + "}}");
        foreach (var i in items) File.WriteAllText(Path.Combine(dir, i.File), "x");
        return dir;
    }

    [Fact]
    public void Keys_the_overlay_has_win_and_the_rest_fall_back_per_key()
    {
        var authored = MakePack("authored", (AssetKeys.SfxBattle, "b.wav"), (AssetKeys.SfxUnitMove, "m.wav"));
        var original = MakePack("original", (AssetKeys.SfxBattle, "b.wav"));
        var layered = new LayeredAssetPack(AssetLoader.LoadManifest(Path.Combine(authored, "manifest.json")), authored, "authored",
            AssetLoader.LoadManifest(Path.Combine(original, "manifest.json")), original);

        Assert.True(layered.TryResolve(AssetKeys.SfxBattle, out var a));
        Assert.Equal("original", a.PackId);
        Assert.True(layered.TryResolve(AssetKeys.SfxUnitMove, out var b));
        Assert.Equal("authored", b.PackId);
        Assert.False(layered.TryResolve(AssetKeys.SfxFleetSunk, out _));
    }

    [Fact]
    public void An_overlay_entry_whose_file_is_missing_or_escapes_the_pack_falls_back()
    {
        var authored = MakePack("authored", (AssetKeys.SfxBattle, "b.wav"));
        var original = MakePack("original", (AssetKeys.SfxBattle, "b.wav"));
        File.Delete(Path.Combine(original, "b.wav"));
        var layered = new LayeredAssetPack(AssetLoader.LoadManifest(Path.Combine(authored, "manifest.json")), authored, "authored",
            AssetLoader.LoadManifest(Path.Combine(original, "manifest.json")), original);
        Assert.Equal("authored", Resolve(layered).PackId);

        File.WriteAllText(Path.Combine(original, "manifest.json"),
            "{\"schemaVersion\":1,\"name\":\"t\",\"description\":\"t\",\"assets\":{\"" + AssetKeys.SfxBattle + "\":\"../authored/b.wav\"}}");
        layered = new LayeredAssetPack(AssetLoader.LoadManifest(Path.Combine(authored, "manifest.json")), authored, "authored",
            AssetLoader.LoadManifest(Path.Combine(original, "manifest.json")), original);
        Assert.Equal("authored", Resolve(layered).PackId);
    }

    private static ResolvedAsset Resolve(LayeredAssetPack p)
    {
        Assert.True(p.TryResolve(AssetKeys.SfxBattle, out var r));
        return r;
    }

    [Fact]
    public void Status_covers_every_state()
    {
        var cache = Path.Combine(_root, "cache", "original");
        Assert.Equal(OriginalCacheState.NotSet, OriginalCacheStatus.Read(null, cache).State);
        Assert.Equal(OriginalCacheState.SourceMissing, OriginalCacheStatus.Read(Path.Combine(_root, "gone"), cache).State);

        var source = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(source, "WAVS"));
        Assert.Equal(OriginalCacheState.NoCache, OriginalCacheStatus.Read(source, cache).State);

        OriginalAssetPackBuilder.Build(source, cache);
        Assert.Equal(OriginalCacheState.Empty, OriginalCacheStatus.Read(source, cache).State);

        var wave = new byte[48];
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write("RIFF"u8); w.Write(40); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((ushort)1);
            w.Write((ushort)1); w.Write(44100); w.Write(88200); w.Write((ushort)2); w.Write((ushort)16);
            w.Write("data"u8); w.Write(4); w.Write(0);
            wave = ms.ToArray();
        }
        File.WriteAllBytes(Path.Combine(source, "WAVS", "SOUND1.WAV"), wave);
        OriginalAssetPackBuilder.Build(source, cache);
        var partial = OriginalCacheStatus.Read(source, cache);
        Assert.Equal(OriginalCacheState.Partial, partial.State);
        Assert.True(partial.InUse);
        Assert.Contains("1 of", partial.Message);

        Directory.Delete(source, recursive: true);
        var lost = OriginalCacheStatus.Read(source, cache);
        Assert.Equal(OriginalCacheState.SourceMissing, lost.State);
        Assert.False(lost.InUse);
    }
}
