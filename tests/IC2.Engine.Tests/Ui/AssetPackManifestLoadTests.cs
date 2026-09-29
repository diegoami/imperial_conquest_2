using System.Text.Json;
using IC2.Engine.Assets;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// <c>docs/tasks/T94.md</c> folded follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/454">#454</see> item 1:
/// <see cref="AssetPackManifestLoader.TryLoad"/> — the manifest half of
/// <c>godot/Assets/AssetPackTextureLoader.cs</c>'s "degrade, don't throw" contract — returns
/// <see langword="null"/> rather than throwing on a malformed manifest, the exact input for which
/// <see cref="AssetLoader.LoadManifest"/> raises <see cref="JsonException"/>. The class under test is
/// the same file the Godot loader calls (linked by <c>&lt;Compile Include&gt;</c>, never a copy).
/// </summary>
public sealed class AssetPackManifestLoadTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ic2-t94-asset-pack-manifest");

    public AssetPackManifestLoadTests()
    {
        // Never a shared fixed path other tests could race on: created fresh here, removed in Dispose.
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void TryLoad_returns_null_with_the_reason_for_a_malformed_manifest()
    {
        var path = Path.Combine(_directory, "manifest.json");
        File.WriteAllText(path, "{ \"assets\": [ this is not json ]");

        // The input really is the one the old catch did not cover: the engine's own loader throws
        // JsonException for it, which is exactly why the loader's catch had to grow.
        Assert.Throws<JsonException>(() => AssetLoader.LoadManifest(path));

        var pack = AssetPackManifestLoader.TryLoad(path, out var failure);

        Assert.Null(pack);
        Assert.NotNull(failure);
        Assert.Contains(path, failure);
    }

    [Fact]
    public void TryLoad_returns_null_and_no_pack_for_a_missing_manifest()
    {
        var path = Path.Combine(_directory, "absent.json");

        var pack = AssetPackManifestLoader.TryLoad(path, out var failure);

        Assert.Null(pack);
        Assert.NotNull(failure);
        Assert.Contains(path, failure);
    }

    [Fact]
    public void TryLoad_returns_the_pack_for_the_real_placeholder_manifest()
    {
        var path = Path.Combine(
            ModelTestPaths.RepositoryRoot, "assets", "packs", "placeholder", "manifest.json");

        var pack = AssetPackManifestLoader.TryLoad(path, out var failure);

        Assert.Null(failure);
        Assert.NotNull(pack);
        Assert.True(pack!.TryResolveAsset(AssetKeys.ArmyTier1Icon, out _));
    }
}
