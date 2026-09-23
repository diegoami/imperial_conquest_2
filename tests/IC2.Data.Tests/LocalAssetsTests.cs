using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T34 #40 item 6: <see cref="LocalAssets.TryLoad"/> must treat exactly the three documented
/// "not configured on this machine" exception types as a skip signal — never a bare
/// <c>catch (Exception)</c> that would also silently swallow a genuinely different failure (a
/// malformed config producing an unexpected exception type, or a real <c>AssetSettings.Load</c>
/// bug). Uses synthetic temp files, never the repo's own <c>assets.local.ini</c>, so no Skip is
/// needed.
/// </summary>
public class LocalAssetsTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "ic2-local-assets-" + Guid.NewGuid().ToString("N"));

    public LocalAssetsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void A_missing_config_file_is_treated_as_not_configured()
    {
        var (settings, isConfigured) = LocalAssets.TryLoad(Path.Combine(_tempDir, "missing.ini"));

        Assert.False(isConfigured);
        Assert.Null(settings);
    }

    [Fact]
    public void A_config_pointing_at_a_missing_directory_is_treated_as_not_configured()
    {
        var configPath = Path.Combine(_tempDir, "assets.local.ini");
        File.WriteAllText(configPath, "[assets]\ndirectory = does-not-exist\n");

        var (settings, isConfigured) = LocalAssets.TryLoad(configPath);

        Assert.False(isConfigured);
        Assert.Null(settings);
    }

    [Fact]
    public void A_malformed_ini_line_is_still_treated_as_not_configured()
    {
        // AssetSettings.Load throws InvalidDataException for this — one of the three documented
        // types — so this regresses the pre-existing "not configured" behaviour for a genuinely
        // malformed ini, confirming the narrowed catch didn't accidentally break it.
        var configPath = Path.Combine(_tempDir, "assets.local.ini");
        File.WriteAllText(configPath, "[assets]\nthis line has no equals sign\n");

        var (settings, isConfigured) = LocalAssets.TryLoad(configPath);

        Assert.False(isConfigured);
        Assert.Null(settings);
    }

    [Fact]
    public void An_exception_type_outside_the_three_documented_ones_propagates_loudly()
    {
        // AssetSettings.Load throws ArgumentException for an empty config path — not one of the
        // three "not configured" types (FileNotFoundException, DirectoryNotFoundException,
        // InvalidDataException). This must propagate rather than being swallowed as "not configured".
        Assert.Throws<ArgumentException>(() => LocalAssets.TryLoad(""));
    }

    // ---- #218 N11: DetermineRequestMode is the pure decision behind WasRequested/IsCiFixtureMode,
    // over every (fixturesDirEnvValue, configFileExists) combination — including N12, a blank
    // IC2_FIXTURES_DIR treated as unset. Mutating either assignment in LocalAssets' static
    // constructor to false must fail one of these (see the PR body for both mutations shown).

    [Theory]
    [InlineData(null, false, false, false)]
    [InlineData(null, true, true, false)]
    [InlineData("", false, false, false)] // N12: blank env var is unset-equivalent, not "requested".
    [InlineData("", true, true, false)]
    [InlineData("   ", false, false, false)] // Whitespace-only is blank too.
    [InlineData("   ", true, true, false)]
    [InlineData(@"C:\fixtures", false, true, true)] // Set (to anything non-blank): CI-fixture mode,
    [InlineData(@"C:\fixtures", true, true, true)]  // wins outright regardless of the ini file.
    public void DetermineRequestMode_covers_every_combination(
        string? fixturesDirEnvValue, bool configFileExists, bool expectedWasRequested, bool expectedIsCiFixtureMode)
    {
        var (wasRequested, isCiFixtureMode) =
            LocalAssets.DetermineRequestMode(fixturesDirEnvValue, configFileExists);

        Assert.Equal(expectedWasRequested, wasRequested);
        Assert.Equal(expectedIsCiFixtureMode, isCiFixtureMode);
    }
}
