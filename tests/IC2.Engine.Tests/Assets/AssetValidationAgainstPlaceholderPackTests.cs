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
}
