using IC2.Data;
using IC2.Inspect;
using Xunit;

namespace IC2.Data.Tests.CorpusFixtures;

/// <summary>
/// T34 Done-when line 1: the corpus fixture is keyed by file name, found across saves/,
/// saves-processed/ and saves-processed/processed/, so moving a save between those folders — what
/// <c>/process-evidence</c> does once a report cites it (bug #57) — changes no test outcome, and a
/// name found in two folders is flagged naming both paths. Uses a synthetic directory tree, not the
/// user's original files, so no Skip is needed.
/// </summary>
public class CorpusFileLocatorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ic2-corpus-locator-" + Guid.NewGuid().ToString("N"));

    public CorpusFileLocatorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "saves"));
        Directory.CreateDirectory(Path.Combine(_root, "saves-processed"));
        Directory.CreateDirectory(Path.Combine(_root, "saves-processed", "processed"));
        // AssetSettings.Load only checks the DAT's existence, not its content.
        File.WriteAllBytes(Path.Combine(_root, "Imperial Conquest 2.dat"), new byte[] { 0 });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private AssetSettings LoadSettings()
    {
        var configPath = Path.Combine(_root, "assets.local.ini");
        File.WriteAllText(configPath, $"[assets]{Environment.NewLine}directory = .{Environment.NewLine}");
        return AssetSettings.Load(configPath);
    }

    [Fact]
    public void A_save_is_found_regardless_of_which_of_the_three_folders_holds_it()
    {
        var expected = Path.Combine(_root, "saves-processed", "processed", "example.sav");
        File.WriteAllBytes(expected, new byte[] { 0 });
        var settings = LoadSettings();

        var resolved = CorpusFileLocator.TryResolve(settings, "example.sav");

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void Moving_a_save_between_folders_changes_no_lookup_outcome()
    {
        var before = Path.Combine(_root, "saves", "example.sav");
        File.WriteAllBytes(before, new byte[] { 0 });
        var settings = LoadSettings();
        Assert.NotNull(CorpusFileLocator.TryResolve(settings, "example.sav"));

        var after = Path.Combine(_root, "saves-processed", "example.sav");
        File.Move(before, after);

        var resolved = CorpusFileLocator.TryResolve(settings, "example.sav");
        Assert.Equal(after, resolved);
    }

    [Fact]
    public void A_name_absent_from_every_folder_resolves_to_null()
    {
        var settings = LoadSettings();

        Assert.Null(CorpusFileLocator.TryResolve(settings, "missing.sav"));
    }

    [Fact]
    public void A_name_found_in_two_folders_is_reported_naming_both_paths()
    {
        var first = Path.Combine(_root, "saves", "dup.sav");
        var second = Path.Combine(_root, "saves-processed", "dup.sav");
        File.WriteAllBytes(first, new byte[] { 0 });
        File.WriteAllBytes(second, new byte[] { 0 });
        var settings = LoadSettings();

        var ex = Assert.Throws<InvalidOperationException>(() => CorpusFileLocator.TryResolve(settings, "dup.sav"));

        Assert.Contains("dup.sav", ex.Message);
        Assert.Contains(first, ex.Message);
        Assert.Contains(second, ex.Message);
    }

    [Fact]
    public void DiscoverFileNames_reports_every_folder_a_name_is_found_in()
    {
        File.WriteAllBytes(Path.Combine(_root, "saves", "dup.sav"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(_root, "saves-processed", "dup.sav"), new byte[] { 0 });
        var settings = LoadSettings();

        var found = CorpusFileLocator.DiscoverFileNames(settings);

        Assert.Equal(2, found["dup.sav"].Count);
        Assert.Contains("saves", found["dup.sav"]);
        Assert.Contains("saves-processed", found["dup.sav"]);
    }

    [Fact]
    public void DiscoverFileNames_includes_the_dat()
    {
        var settings = LoadSettings();

        var found = CorpusFileLocator.DiscoverFileNames(settings);

        Assert.True(found.ContainsKey(CorpusFileLocator.DatFileName));
        Assert.Equal(new[] { "" }, found[CorpusFileLocator.DatFileName]);
    }

    // ---- #213: releases/<tag>/** joins the same search order ----

    [Fact]
    public void A_save_present_only_under_a_release_tag_is_found_by_TryResolve()
    {
        var releasePath = Path.Combine(_root, "releases", "run-1-rome", "sub", "only-in-release.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(releasePath)!);
        File.WriteAllBytes(releasePath, new byte[] { 1, 2, 3 });
        var settings = LoadSettings();

        var resolved = CorpusFileLocator.TryResolve(settings, "only-in-release.sav");

        Assert.Equal(releasePath, resolved);
    }

    [Fact]
    public void A_save_present_only_under_a_release_tag_is_found_by_DiscoverFileNames()
    {
        var releasePath = Path.Combine(_root, "releases", "run-1-rome", "only-in-release.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(releasePath)!);
        File.WriteAllBytes(releasePath, new byte[] { 1, 2, 3 });
        var settings = LoadSettings();

        var found = CorpusFileLocator.DiscoverFileNames(settings);

        Assert.True(found.ContainsKey("only-in-release.sav"));
        Assert.Equal(new[] { "releases/run-1-rome" }, found["only-in-release.sav"]);
    }

    [Fact]
    public void A_save_present_in_a_plain_folder_and_a_release_with_identical_bytes_resolves_to_the_plain_copy()
    {
        var plainPath = Path.Combine(_root, "saves", "repeated.sav");
        var releasePath = Path.Combine(_root, "releases", "run-1-rome", "repeated.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(releasePath)!);
        File.WriteAllBytes(plainPath, new byte[] { 9, 9, 9 });
        File.WriteAllBytes(releasePath, new byte[] { 9, 9, 9 }); // Byte-identical: a legitimate release cache repeat.
        var settings = LoadSettings();

        var resolved = CorpusFileLocator.TryResolve(settings, "repeated.sav");

        Assert.Equal(plainPath, resolved);
    }

    [Fact]
    public void A_save_present_in_a_plain_folder_and_a_release_with_identical_bytes_is_one_location_in_DiscoverFileNames()
    {
        var plainPath = Path.Combine(_root, "saves", "repeated.sav");
        var releasePath = Path.Combine(_root, "releases", "run-1-rome", "repeated.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(releasePath)!);
        File.WriteAllBytes(plainPath, new byte[] { 9, 9, 9 });
        File.WriteAllBytes(releasePath, new byte[] { 9, 9, 9 });
        var settings = LoadSettings();

        var found = CorpusFileLocator.DiscoverFileNames(settings);

        Assert.Equal(new[] { "saves" }, found["repeated.sav"]);
    }

    [Fact]
    public void A_save_present_in_a_plain_folder_and_a_release_with_different_bytes_throws()
    {
        var plainPath = Path.Combine(_root, "saves", "conflicting.sav");
        var releasePath = Path.Combine(_root, "releases", "run-1-rome", "conflicting.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(releasePath)!);
        File.WriteAllBytes(plainPath, new byte[] { 1 });
        File.WriteAllBytes(releasePath, new byte[] { 2 }); // Different content: a genuine ambiguity.
        var settings = LoadSettings();

        var ex = Assert.Throws<InvalidOperationException>(
            () => CorpusFileLocator.TryResolve(settings, "conflicting.sav"));

        Assert.Contains("conflicting.sav", ex.Message);
        Assert.Contains(plainPath, ex.Message);
        Assert.Contains(releasePath, ex.Message);
    }

    [Fact]
    public void A_duplicate_between_two_plain_folders_still_throws_even_with_identical_bytes()
    {
        // The #213 hazard's "identical bytes count as one file" rule is specific to a release cache
        // repeating a plain-folder save — NOT a general softening of the plain-folder duplicate rule,
        // which stays an error regardless of content (the "move, don't copy" convention means this
        // should never legitimately happen).
        var first = Path.Combine(_root, "saves", "dup.sav");
        var second = Path.Combine(_root, "saves-processed", "dup.sav");
        File.WriteAllBytes(first, new byte[] { 0 });
        File.WriteAllBytes(second, new byte[] { 0 }); // Identical bytes, still two plain folders.
        var settings = LoadSettings();

        Assert.Throws<InvalidOperationException>(() => CorpusFileLocator.TryResolve(settings, "dup.sav"));
    }

    // ---- #213 Done-when: "found by ... FixtureResolver" too, not just CorpusFileLocator ----
    // FixtureResolver.TryResolve(AssetSettings, string) is the internal, settings-parameterised
    // overload FixtureResolver.TryResolve(string) delegates to after substituting LocalAssets.Settings
    // — see FixtureResolver's own remarks. Testing it directly here proves the same "one list, one
    // place" claim CorpusFileLocatorTests already makes, without faking LocalAssets' static state.

    [Fact]
    public void FixtureResolver_also_finds_a_save_present_only_under_a_release_tag()
    {
        var releasePath = Path.Combine(_root, "releases", "run-1-rome", "only-in-release.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(releasePath)!);
        File.WriteAllBytes(releasePath, new byte[] { 1, 2, 3 });
        var settings = LoadSettings();

        var resolved = FixtureResolver.TryResolve(settings, "only-in-release.sav");

        Assert.Equal(releasePath, resolved);
    }

    [Fact]
    public void FixtureResolver_also_finds_a_save_present_only_under_saves_processed_processed()
    {
        var path = Path.Combine(_root, "saves-processed", "processed", "deep.sav");
        File.WriteAllBytes(path, new byte[] { 1 });
        var settings = LoadSettings();

        var resolved = FixtureResolver.TryResolve(settings, "deep.sav");

        Assert.Equal(path, resolved);
    }
}
