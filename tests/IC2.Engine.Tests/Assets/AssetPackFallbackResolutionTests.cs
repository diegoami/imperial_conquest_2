using System.Collections.Frozen;
using IC2.Engine.Assets;
using IC2.Slice.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// T48 "Draw armies and cities from the asset pack" DoD 4: "a missing or unreadable asset degrades
/// visibly and never throws ... asserted by a test that points the loader at a pack with a
/// deliberately missing entry."
/// </summary>
/// <remarks>
/// <para>
/// This test exercises the real, shipped <see cref="AssetKeyResolver"/> — not a copy. That type
/// lives at <c>godot/Assets/AssetKeyResolver.cs</c>, inside <c>godot/IC2.MapViewer.csproj</c> (which
/// is excluded from <c>IC2.sln</c> entirely — T47's own scope note, it needs the Godot 4.7.2 .NET
/// SDK). It reaches this CI-covered test project through exactly one linked
/// <c>&lt;Compile Include&gt;</c> in <c>tests/IC2.Engine.Tests.csproj</c> (the same physical file,
/// compiled into a second assembly, not a copy) — granted narrowly by the user on
/// <see href="https://github.com/diegoami/imperial_conquest_2/pull/450">PR #450</see>'s first
/// review (blocking finding 2) and recorded in <c>docs/tasks/T48.md</c>'s Owns list via
/// <see href="https://github.com/diegoami/imperial_conquest_2/pull/451">plan PR #451</see>. See that
/// csproj's own comment for the grant's exact wording and its "no other change to this file" limit.
/// </para>
/// <para>
/// Before this grant, this test hand-mirrored <see cref="AssetKeyResolver.TryResolveFile"/>'s
/// algorithm in a private nested class — reviewed and correctly flagged as not actually proving
/// anything about the shipped type (it would have kept passing even if the real fallback logic were
/// deleted). That mirror is gone; every assertion below runs against
/// <see cref="AssetKeyResolver"/> itself.
/// </para>
/// </remarks>
public sealed class AssetPackFallbackResolutionTests
{
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
            var resolver = new AssetKeyResolver(pack, packDirectory, failures.Add);

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
            var resolver = new AssetKeyResolver(pack, packDirectory, failures.Add);

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
            var resolver = new AssetKeyResolver(pack, packDirectory, failures.Add);

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
