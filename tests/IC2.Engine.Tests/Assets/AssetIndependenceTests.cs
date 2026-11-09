using System.Reflection;
using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>T292e: assets cannot change game state, and the cache never contains anything but plain pack files.</summary>
public sealed class AssetIndependenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic2-indep-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_rules_engine_references_no_rendering_or_audio_assembly()
    {
        // State hashes are computed in IC2.Engine; if it cannot see a pack, texture or sound, no pack can change one.
        var referenced = typeof(AssetLoader).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.DoesNotContain(referenced, n => n.Contains("Godot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_built_cache_holds_only_manifest_keys_with_relative_paths_inside_the_pack()
    {
        var install = Path.Combine(_root, "install", "WAVS");
        Directory.CreateDirectory(install);
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write("RIFF"u8); w.Write(40); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((ushort)1);
            w.Write((ushort)1); w.Write(44100); w.Write(88200); w.Write((ushort)2); w.Write((ushort)16);
            w.Write("data"u8); w.Write(4); w.Write(0);
            File.WriteAllBytes(Path.Combine(install, "SOUND1.WAV"), ms.ToArray());
            File.WriteAllBytes(Path.Combine(install, "NOTES.TXT"), [1, 2, 3]);
        }
        var output = Path.Combine(_root, "cache", "original");
        OriginalAssetPackBuilder.Build(Path.Combine(_root, "install"), output);

        var pack = AssetLoader.LoadManifest(Path.Combine(output, "manifest.json"));
        Assert.All(pack.Assets, a =>
        {
            Assert.Contains(a.Key, AssetKeys.AllKeys);
            Assert.False(Path.IsPathRooted(a.Value));
            Assert.DoesNotContain("..", a.Value);
        });
        var files = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(output, f).Replace(Path.DirectorySeparatorChar, '/')).OrderBy(f => f).ToList();
        Assert.Equal(["manifest.json", "sfx/unit_move.wav"], files);
    }
}
