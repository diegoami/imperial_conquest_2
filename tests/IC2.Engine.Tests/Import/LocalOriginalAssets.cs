using IC2.Data;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// Locates the user's or CI's copy of the original Imperial Conquest 2 files for this project's own
/// import tests — the same two-source resolution <c>tests/IC2.Data.Tests/LocalAssets</c> already
/// implements (<c>IC2_FIXTURES_DIR</c> first, then the repo-root <c>assets.local.ini</c>), kept as this
/// project's own copy rather than a cross-test-project reference into an <c>internal</c> type of a
/// different assembly. Every test that touches a real save calls <see cref="SkipReason"/> via
/// <c>Skip.IfNot</c> first, so a worktree with no configured assets reports an explicit Skipped result
/// (Done-when 4), never a silent pass or a hard failure.
/// </summary>
internal static class LocalOriginalAssets
{
    private const string SolutionFileName = "IC2.sln";
    private const string ConfigFileName = "assets.local.ini";
    private const string FixturesDirEnvVar = "IC2_FIXTURES_DIR";

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string ConfigPath { get; } = Path.Combine(RepositoryRoot, ConfigFileName);

    /// <summary>True once, computed the first time it is touched: whether either source resolved to a
    /// usable asset directory (one that actually contains the DAT).</summary>
    public static bool IsConfigured { get; }

    /// <summary>The loaded settings when <see cref="IsConfigured"/> is true; otherwise null.</summary>
    public static AssetSettings? Settings { get; }

    /// <summary>The reason string every test in this project passes to <c>Skip.IfNot</c>.</summary>
    public static string SkipReason { get; }

    static LocalOriginalAssets()
    {
        var fixturesDir = Environment.GetEnvironmentVariable(FixturesDirEnvVar);
        if (!string.IsNullOrWhiteSpace(fixturesDir))
        {
            (Settings, IsConfigured, SkipReason) = TryLoadFixturesDir(fixturesDir);
            return;
        }

        var configFileExists = File.Exists(ConfigPath);
        (Settings, IsConfigured) = TryLoad(ConfigPath);
        SkipReason = IsConfigured
            ? ""
            : configFileExists
                ? $"configured but incomplete: '{ConfigPath}' exists but its [assets] directory or DAT " +
                  "could not be loaded (see AssetSettings.Load's own error for the missing piece)."
                : $"original files not configured: '{ConfigFileName}' was not found at the repository " +
                  $"root ('{ConfigPath}'). Copy assets.example.ini and set [assets] directory.";
    }

    /// <summary>Mirrors <c>LocalAssets.TryLoad</c>: only the three documented "not configured" exception
    /// types are treated as a skip signal, never a bare <c>catch (Exception)</c>.</summary>
    internal static (AssetSettings? Settings, bool IsConfigured) TryLoad(string configPath)
    {
        try
        {
            return (AssetSettings.Load(configPath), true);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
        {
            return (null, false);
        }
    }

    private static (AssetSettings? Settings, bool IsConfigured, string SkipReason) TryLoadFixturesDir(string fixturesDir)
    {
        var full = Path.GetFullPath(fixturesDir);
        if (!Directory.Exists(full))
        {
            return (null, false, $"configured but incomplete: {FixturesDirEnvVar}='{full}' does not exist.");
        }

        var tempIni = Path.Combine(Path.GetTempPath(), "ic2-engine-fixtures-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(tempIni, $"[assets]{Environment.NewLine}directory = {full}{Environment.NewLine}");
            var (settings, ok) = TryLoad(tempIni);
            return ok
                ? (settings, true, "")
                : (null, false,
                    $"configured but incomplete: {FixturesDirEnvVar}='{full}' does not contain " +
                    "'Imperial Conquest 2.dat'.");
        }
        finally
        {
            if (File.Exists(tempIni))
            {
                File.Delete(tempIni);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find '{SolutionFileName}' above '{AppContext.BaseDirectory}'.");
    }
}
