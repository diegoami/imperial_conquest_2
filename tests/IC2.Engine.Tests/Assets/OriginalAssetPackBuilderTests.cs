using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>T292b: the cache builder, tested only with synthetic files created here; no original bytes.</summary>
public sealed class OriginalAssetPackBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic2-orig-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static byte[] Wave(ushort format = 1, ushort bits = 16, int samples = 16)
    {
        var data = new byte[samples * bits / 8];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write(format); w.Write((ushort)1); w.Write(44100);
        w.Write(44100 * bits / 8); w.Write((ushort)(bits / 8)); w.Write(bits);
        w.Write("data"u8); w.Write(data.Length); w.Write(data);
        return ms.ToArray();
    }

    private string Install(int sounds, string folderName = "WAVS")
    {
        var install = Path.Combine(_root, "install");
        var wavs = Path.Combine(install, folderName);
        Directory.CreateDirectory(wavs);
        for (var n = 1; n <= sounds; n++) File.WriteAllBytes(Path.Combine(wavs, $"SOUND{n}.WAV"), Wave());
        return install;
    }

    [Fact]
    public void A_full_installation_yields_ten_sound_keys_that_the_normal_loader_reads()
    {
        var output = Path.Combine(_root, "cache", "original");
        var report = OriginalAssetPackBuilder.Build(Install(10), output);

        Assert.Equal(10, report.KeysWritten);
        Assert.True(report.Usable);
        var pack = AssetLoader.LoadManifest(Path.Combine(output, "manifest.json"));
        foreach (var (_, key) in OriginalAssetPackBuilder.Sounds)
        {
            Assert.True(pack.TryResolveAsset(key, out var path), key);
            Assert.True(File.Exists(Path.Combine(output, path)), path);
        }
        Assert.DoesNotContain(AssetLoader.ValidateAssets(pack, output), k => k.StartsWith("sfx.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_partial_installation_writes_only_the_keys_it_has_and_says_so()
    {
        var output = Path.Combine(_root, "cache", "original");
        var report = OriginalAssetPackBuilder.Build(Install(4), output);

        var sounds = report.Categories.Single(c => c.Category == "sounds");
        Assert.Equal((4, 10), (sounds.Found, sounds.Total));
        var pack = AssetLoader.LoadManifest(Path.Combine(output, "manifest.json"));
        Assert.True(pack.TryResolveAsset(AssetKeys.SfxBattleJavelin, out _));
        Assert.False(pack.TryResolveAsset(AssetKeys.SfxBattle, out _));
    }

    [Fact]
    public void A_missing_source_reports_nothing_found_and_still_leaves_a_valid_empty_pack()
    {
        var output = Path.Combine(_root, "cache", "original");
        var report = OriginalAssetPackBuilder.Build(Path.Combine(_root, "nowhere"), output);

        Assert.False(report.Usable);
        Assert.Contains("no WAVS folder", report.Categories.Single(c => c.Category == "sounds").Note);
        Assert.Empty(AssetLoader.LoadManifest(Path.Combine(output, "manifest.json")).Assets);
    }

    [Fact]
    public void Unsupported_audio_is_skipped_with_a_reason_instead_of_being_copied()
    {
        var install = Install(0);
        File.WriteAllBytes(Path.Combine(install, "WAVS", "SOUND1.WAV"), Wave(format: 3));
        File.WriteAllBytes(Path.Combine(install, "WAVS", "SOUND2.WAV"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(install, "WAVS", "SOUND3.WAV"), Wave());
        var output = Path.Combine(_root, "cache", "original");

        var report = OriginalAssetPackBuilder.Build(install, output);

        Assert.Equal(1, report.KeysWritten);
        Assert.Contains("SOUND1.WAV", report.Categories.Single(c => c.Category == "sounds").Note);
        Assert.Contains("SOUND2.WAV", report.Categories.Single(c => c.Category == "sounds").Note);
    }

    [Fact]
    public void Folder_and_file_names_are_matched_without_regard_to_case()
    {
        var report = OriginalAssetPackBuilder.Build(Install(10, "wavs"), Path.Combine(_root, "cache", "original"));
        Assert.Equal(10, report.KeysWritten);
    }

    [Fact]
    public void Rebuilding_replaces_the_previous_cache_and_leaves_no_staging_directory()
    {
        var output = Path.Combine(_root, "cache", "original");
        OriginalAssetPackBuilder.Build(Install(10), output);
        File.WriteAllText(Path.Combine(output, "stale.txt"), "old");

        Directory.Delete(Path.Combine(_root, "install"), recursive: true);
        OriginalAssetPackBuilder.Build(Install(2), output);

        Assert.False(File.Exists(Path.Combine(output, "stale.txt")));
        Assert.False(Directory.Exists(output + ".building"));
        Assert.Equal(2, AssetLoader.LoadManifest(Path.Combine(output, "manifest.json")).Assets.Count);
    }

    [Fact]
    public void The_pack_id_is_a_plain_directory_name_the_existing_resolver_accepts()
    {
        Assert.NotNull(IC2.Slice.Assets.AssetPackManifestLoader.ResolvePackDirectory(_root, OriginalAssetPackBuilder.PackId));
    }
}
