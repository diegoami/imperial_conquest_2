using System.Text.Json;
using IC2.Engine.Assets;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// Bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see>'s
/// reproduction, Godot-free: the pack a game's map resolves — the id-to-directory step and the
/// authored→placeholder fallback order, both in <see cref="AssetPackManifestLoader"/> so a plain
/// xunit test can drive them (the Godot-side <c>AssetPackTextureLoader</c> cannot be linked here).
/// </summary>
/// <remarks>
/// <para>
/// The three cases the bug names: with the repository's real <c>assets/packs/authored/</c> present, a
/// <see langword="null"/> selection (no pack chosen in Settings) resolves <c>authored</c>; selecting
/// <c>placeholder</c> resolves <c>placeholder</c>; a selected pack that is missing or malformed falls
/// back to <c>placeholder</c>, reporting through <c>onFailure</c> and never throwing. Before bug
/// #517's fix there was no by-id entry point at all (<c>AssetPackTextureLoader</c> could only call
/// <c>TryLoadPlaceholderPack</c>), which is why this file's own tests cannot compile against the base
/// commit — the PR body shows that error as the "fails before" evidence.
/// </para>
/// <para>
/// The malformed and missing packs are copies written under the repository's git-ignored
/// <c>rendered/</c> scratch directory (never under <c>assets/</c>, which is T11's and T51's), and
/// removed again in <see cref="Dispose"/>.
/// </para>
/// </remarks>
public sealed class AssetPackSelectionTests : IDisposable
{
    private const string RealAuthoredDirectory = "authored";
    private const string RealPlaceholderDirectory = "placeholder";

    private readonly string _scratchRoot =
        Path.Combine(ModelTestPaths.RepositoryRoot, "rendered", "fix-517-asset-pack-selection");

    public AssetPackSelectionTests()
    {
        RemoveScratch();
    }

    public void Dispose()
    {
        RemoveScratch();
    }

    /// <summary>
    /// DoD 1, first case: the repository really ships T51's generated pack, and with no pack selected
    /// the default resolves it — not the placeholder, which every caller before bug #517 got.
    /// </summary>
    [Fact]
    public void The_default_pack_with_the_real_authored_pack_present_is_authored()
    {
        var failures = new List<string>();

        var pack = AssetPackManifestLoader.TryLoadPack(
            ModelTestPaths.RepositoryRoot, null, failures.Add, out var resolvedId, out var resolvedDirectory);

        Assert.NotNull(pack);
        Assert.Equal(RealAuthoredDirectory, resolvedId);
        Assert.Equal(
            Path.Combine(ModelTestPaths.RepositoryRoot, "assets", "packs", RealAuthoredDirectory),
            resolvedDirectory);
        Assert.Empty(failures);
        Assert.True(pack!.TryResolveAsset(AssetKeys.ArmyTier1Icon, out _));
    }

    /// <summary>
    /// DoD 1, second case: choosing <c>placeholder</c> in Settings must make the next map load the
    /// placeholder pack even though the authored pack is present and would win by default.
    /// </summary>
    [Fact]
    public void Selecting_placeholder_resolves_the_placeholder_pack()
    {
        var failures = new List<string>();

        var pack = AssetPackManifestLoader.TryLoadPack(
            ModelTestPaths.RepositoryRoot, RealPlaceholderDirectory, failures.Add, out var resolvedId, out var resolvedDirectory);

        Assert.NotNull(pack);
        Assert.Equal(RealPlaceholderDirectory, resolvedId);
        Assert.Equal(
            Path.Combine(ModelTestPaths.RepositoryRoot, "assets", "packs", RealPlaceholderDirectory),
            resolvedDirectory);
        Assert.Empty(failures);
        Assert.True(pack!.TryResolveAsset(AssetKeys.ArmyTier1Icon, out _));
    }

    /// <summary>
    /// DoD 1, third case (missing): a selected pack whose directory has no manifest falls back to the
    /// placeholder and reports why, instead of throwing or loading nothing.
    /// </summary>
    [Fact]
    public void A_missing_selected_pack_falls_back_to_placeholder_and_reports_through_onFailure()
    {
        WritePlaceholderPack();
        var failures = new List<string>();

        var pack = AssetPackManifestLoader.TryLoadPack(
            _scratchRoot, RealAuthoredDirectory, failures.Add, out var resolvedId, out var resolvedDirectory);

        Assert.NotNull(pack);
        Assert.Equal(RealPlaceholderDirectory, resolvedId);
        Assert.Equal(
            Path.Combine(_scratchRoot, "assets", "packs", RealPlaceholderDirectory),
            resolvedDirectory);
        var failure = Assert.Single(failures);
        Assert.Contains(RealAuthoredDirectory, failure);
    }

    /// <summary>
    /// DoD 1, third case (malformed): a selected pack whose manifest is broken (the exact input
    /// <see cref="AssetLoader.LoadManifest"/> throws <see cref="JsonException"/> for) falls back to the
    /// placeholder and reports why.
    /// </summary>
    [Fact]
    public void A_malformed_selected_pack_falls_back_to_placeholder_and_reports_through_onFailure()
    {
        WritePlaceholderPack();
        var malformedManifest = WriteMalformedAuthoredPack();

        // The input really is malformed, and it is the engine's own loader that rejects it.
        Assert.Throws<JsonException>(() => AssetLoader.LoadManifest(malformedManifest));

        var failures = new List<string>();

        var pack = AssetPackManifestLoader.TryLoadPack(
            _scratchRoot, RealAuthoredDirectory, failures.Add, out var resolvedId, out _);

        Assert.NotNull(pack);
        Assert.Equal(RealPlaceholderDirectory, resolvedId);
        var failure = Assert.Single(failures);
        Assert.Contains(RealAuthoredDirectory, failure);
    }

    /// <summary>The default's "present and valid" clause: a malformed authored pack does not win the
    /// default when no pack was selected — the placeholder is loaded instead.</summary>
    [Fact]
    public void The_default_falls_back_to_placeholder_when_authored_is_malformed()
    {
        WritePlaceholderPack();
        WriteMalformedAuthoredPack();
        var failures = new List<string>();

        var pack = AssetPackManifestLoader.TryLoadPack(_scratchRoot, null, failures.Add, out var resolvedId, out _);

        Assert.NotNull(pack);
        Assert.Equal(RealPlaceholderDirectory, resolvedId);
        Assert.Single(failures);
    }

    /// <summary>The default's "otherwise" clause: an absent authored pack is not an error when nothing
    /// was selected, so the placeholder loads without a report.</summary>
    [Fact]
    public void The_default_resolves_placeholder_without_a_report_when_authored_is_absent()
    {
        WritePlaceholderPack();
        var failures = new List<string>();

        var pack = AssetPackManifestLoader.TryLoadPack(_scratchRoot, null, failures.Add, out var resolvedId, out _);

        Assert.NotNull(pack);
        Assert.Equal(RealPlaceholderDirectory, resolvedId);
        Assert.Empty(failures);
    }

    /// <summary>A selected id must be one plain directory name under <c>assets/packs/</c>: a traversal
    /// or separator never escapes the packs directory, and is reported like any other bad selection.
    /// </summary>
    [Fact]
    public void A_pack_id_that_is_not_a_plain_directory_name_cannot_escape_the_packs_directory()
    {
        WritePlaceholderPack();
        var failures = new List<string>();

        var pack = AssetPackManifestLoader.TryLoadPack(
            _scratchRoot, "../authored", failures.Add, out var resolvedId, out _);

        Assert.NotNull(pack);
        Assert.Equal(RealPlaceholderDirectory, resolvedId);
        Assert.Contains("is not a plain pack directory name", Assert.Single(failures));
    }

    /// <summary>The seam the acceptance criterion above rests on, one level down.</summary>
    [Fact]
    public void ResolvePackDirectory_accepts_only_a_single_plain_directory_name()
    {
        var packsRoot = Path.Combine(_scratchRoot, "assets", "packs");

        Assert.Equal(Path.Combine(packsRoot, "authored"), AssetPackManifestLoader.ResolvePackDirectory(_scratchRoot, "authored"));
        Assert.Null(AssetPackManifestLoader.ResolvePackDirectory(_scratchRoot, string.Empty));
        Assert.Null(AssetPackManifestLoader.ResolvePackDirectory(_scratchRoot, "."));
        Assert.Null(AssetPackManifestLoader.ResolvePackDirectory(_scratchRoot, ".."));
        Assert.Null(AssetPackManifestLoader.ResolvePackDirectory(_scratchRoot, "../authored"));
        Assert.Null(AssetPackManifestLoader.ResolvePackDirectory(_scratchRoot, "a/b"));
        Assert.Null(AssetPackManifestLoader.ResolvePackDirectory(_scratchRoot, "a\\b"));
    }

    /// <summary>The "never throws" half of the contract: when even the fallback cannot be loaded this
    /// returns <see langword="null"/> after reporting both attempts, rather than throwing.</summary>
    [Fact]
    public void Returns_null_with_a_report_when_even_the_placeholder_pack_is_missing()
    {
        Directory.CreateDirectory(Path.Combine(_scratchRoot, "assets", "packs"));
        var failures = new List<string>();

        var pack = AssetPackManifestLoader.TryLoadPack(
            _scratchRoot, "missing-pack", failures.Add, out var resolvedId, out var resolvedDirectory);

        Assert.Null(pack);
        Assert.Null(resolvedId);
        Assert.Null(resolvedDirectory);
        Assert.Equal(2, failures.Count);
        Assert.Contains("missing-pack", failures[0]);
        Assert.Contains(RealPlaceholderDirectory, failures[1]);
    }

    private void WritePlaceholderPack()
    {
        var directory = Path.Combine(_scratchRoot, "assets", "packs", RealPlaceholderDirectory);
        Directory.CreateDirectory(directory);
        File.Copy(
            Path.Combine(ModelTestPaths.RepositoryRoot, "assets", "packs", RealPlaceholderDirectory, "manifest.json"),
            Path.Combine(directory, "manifest.json"));
    }

    private string WriteMalformedAuthoredPack()
    {
        var directory = Path.Combine(_scratchRoot, "assets", "packs", RealAuthoredDirectory);
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        File.WriteAllText(manifestPath, "{ \"assets\": [ this is not json ]");
        return manifestPath;
    }

    private void RemoveScratch()
    {
        if (Directory.Exists(_scratchRoot))
        {
            Directory.Delete(_scratchRoot, recursive: true);
        }
    }
}
