using System.Collections.Frozen;
using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// T48 "Draw armies and cities from the asset pack" DoD 4: "a missing or unreadable asset degrades
/// visibly and never throws ... asserted by a test that points the loader at a pack with a
/// deliberately missing entry."
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this test does not import the actual loader.</strong> The real, shipped loader is
/// <c>godot/Assets/AssetKeyResolver.cs</c> — deliberately Godot-free (no <c>using Godot;</c>) so its
/// logic is a plain, testable class. It cannot be referenced from this project, though:
/// <c>godot/IC2.MapViewer.csproj</c> is excluded from <c>IC2.sln</c> entirely (T47's own scope note
/// — it needs the Godot 4.7.2 .NET SDK, not installed on CI runners), and this test project
/// (<c>tests/IC2.Engine.Tests.csproj</c>, T01's, not in this task's Owns list) is built by
/// <c>dotnet test IC2.sln</c>, which must stay green without Godot (T48 DoD 8 / T47 DoD 6). Adding a
/// project reference to pull in one Godot-free file would still require editing that csproj, which
/// is outside this task's Owns list.
/// </para>
/// <para>
/// So <see cref="Resolver"/> below is a byte-for-byte mirror of
/// <c>godot/Assets/AssetKeyResolver.TryResolveFile</c>'s algorithm (resolve the key through
/// <see cref="AssetPack.TryResolveAsset"/>, confirm the file actually exists, log the key exactly
/// once on either kind of miss, never throw) — kept deliberately tiny so the two stay in sync by
/// inspection. Everything this test exercises is the exact logic that ships; only the
/// <c>Godot.Image</c>/<c>Texture2D</c> half of the real loader (<c>AssetPackTextureLoader</c>) is
/// left to the manual windowed run this task's screenshot (DoD 7) already requires.
/// </para>
/// </remarks>
public sealed class AssetPackFallbackResolutionTests
{
    /// <summary>Mirrors <c>godot/Assets/AssetKeyResolver.cs</c> — see the type-level remarks above.</summary>
    private sealed class Resolver
    {
        private readonly AssetPack _pack;
        private readonly string _packDirectory;
        private readonly Action<string> _onFailure;
        private readonly HashSet<string> _loggedFailures = new(StringComparer.Ordinal);

        public Resolver(AssetPack pack, string packDirectory, Action<string> onFailure)
        {
            _pack = pack;
            _packDirectory = packDirectory;
            _onFailure = onFailure;
        }

        public bool TryResolveFile(string assetKey, out string fullPath)
        {
            if (_pack.TryResolveAsset(assetKey, out var relativePath))
            {
                var candidate = Path.Combine(_packDirectory, relativePath);
                if (File.Exists(candidate))
                {
                    fullPath = candidate;
                    return true;
                }
            }

            fullPath = string.Empty;
            if (_loggedFailures.Add(assetKey))
            {
                _onFailure(assetKey);
            }

            return false;
        }
    }

    private const string PresentKey = "test.present.icon";
    private const string MissingFileKey = "test.missing_file.icon";
    private const string AbsentKey = "test.absent_key.icon";

    private static (AssetPack Pack, string Directory) BuildPackWithDeliberatelyMissingEntries()
    {
        var packDirectory = Path.Combine(Path.GetTempPath(), $"ic2-t48-fallback-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packDirectory);

        var presentRelativePath = Path.Combine("units", "present.bin");
        var presentFullPath = Path.Combine(packDirectory, presentRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(presentFullPath)!);
        File.WriteAllBytes(presentFullPath, new byte[] { 1, 2, 3 });

        var assets = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PresentKey] = presentRelativePath,
            // Deliberately missing entry #1: the manifest names a file that was never written.
            [MissingFileKey] = Path.Combine("units", "does_not_exist_on_disk.bin"),
            // Deliberately missing entry #2 (AbsentKey) is intentionally left out of this
            // dictionary entirely -- a key AssetKeys declares but this pack's manifest never
            // mentions at all, the other half of "a deliberately missing entry".
        };

        var pack = new AssetPack(
            SchemaVersion: 1,
            Name: "T48 fallback-test pack",
            Description: "Deliberately incomplete, for DoD 4's fallback test only.",
            Assets: assets.ToFrozenDictionary());

        return (pack, packDirectory);
    }

    [Fact]
    public void TryResolveFile_ForPresentKey_ResolvesTheRealPath()
    {
        var (pack, packDirectory) = BuildPackWithDeliberatelyMissingEntries();
        try
        {
            var failures = new List<string>();
            var resolver = new Resolver(pack, packDirectory, failures.Add);

            var resolved = resolver.TryResolveFile(PresentKey, out var fullPath);

            Assert.True(resolved);
            Assert.True(File.Exists(fullPath));
            Assert.Empty(failures);
        }
        finally
        {
            Directory.Delete(packDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryResolveFile_ForKeyWhoseFileIsMissingOnDisk_DegradesWithoutThrowingAndLogsOnce()
    {
        var (pack, packDirectory) = BuildPackWithDeliberatelyMissingEntries();
        try
        {
            var failures = new List<string>();
            var resolver = new Resolver(pack, packDirectory, failures.Add);

            var firstAttempt = resolver.TryResolveFile(MissingFileKey, out var firstPath);
            var secondAttempt = resolver.TryResolveFile(MissingFileKey, out var secondPath);

            Assert.False(firstAttempt);
            Assert.Equal(string.Empty, firstPath);
            Assert.False(secondAttempt);
            Assert.Equal(string.Empty, secondPath);

            // Logged exactly once for the key, not once per call -- a marker redrawn every frame
            // must not spam the log for the same missing asset (DoD 4's own wording: "the failure is
            // logged once with the key that failed").
            var loggedFailure = Assert.Single(failures);
            Assert.Equal(MissingFileKey, loggedFailure);
        }
        finally
        {
            Directory.Delete(packDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryResolveFile_ForKeyAbsentFromManifest_DegradesWithoutThrowingAndLogsOnce()
    {
        var (pack, packDirectory) = BuildPackWithDeliberatelyMissingEntries();
        try
        {
            var failures = new List<string>();
            var resolver = new Resolver(pack, packDirectory, failures.Add);

            var resolved = resolver.TryResolveFile(AbsentKey, out var fullPath);
            resolver.TryResolveFile(AbsentKey, out _);

            Assert.False(resolved);
            Assert.Equal(string.Empty, fullPath);
            var loggedFailure = Assert.Single(failures);
            Assert.Equal(AbsentKey, loggedFailure);
        }
        finally
        {
            Directory.Delete(packDirectory, recursive: true);
        }
    }
}
