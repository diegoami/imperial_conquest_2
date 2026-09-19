namespace IC2.Data.Tests;

/// <summary>
/// Locates the user's or CI's copy of the original Imperial Conquest 2 files and exposes whether they
/// are configured on this machine — exactly the check T21 and T29's Done-when lines describe ("every
/// test in this task skips with an explicit 'original files not configured' result when assets are
/// absent"). Every test in this project calls <see cref="SkipReason"/> via <c>Skip.IfNot</c> before
/// touching any local file, so a plain worktree (which never has <c>assets.local.ini</c>) reports an
/// explicit Skipped result rather than a silent pass or a hard failure.
/// </summary>
/// <remarks>
/// <para>Two independent sources, checked in this order (T53, issue #204):</para>
/// <list type="number">
/// <item><description><c>IC2_FIXTURES_DIR</c> — set by CI's fixtures-fetch step (a clone of the
/// private <c>ic2-test-fixtures</c> repository: the DAT plus the twelve named saves, flat under
/// <c>saves/</c>, matching the layout <see cref="AssetSettings"/> already expects). When set, this
/// wins outright — a developer who sets it locally to point at a trimmed fixture set gets exactly
/// that, not a silent fall-back to their own <c>assets.local.ini</c>.</description></item>
/// <item><description>The repository-root <c>assets.local.ini</c> — the developer's own full
/// installation, git-ignored, present only in the main checkout.</description></item>
/// </list>
/// <para>Whichever source wins becomes <see cref="Settings"/>'s <see cref="AssetSettings.DirectoryPath"/>,
/// so every existing call site (<c>settings.DatPath</c>, <c>settings.ResolveSavePath(...)</c>) keeps
/// working unchanged, and <see cref="FixtureResolver"/> layers name-based, folder-independent lookup
/// on top of the same <see cref="AssetSettings"/> for the tests that used to hardcode a folder.</para>
/// <para>Mirrors <see cref="IC2.Engine.Tests.Model.TestPaths"/> / <c>FixturePaths</c>'s own approach: walk
/// up from the test assembly's location to <c>IC2.sln</c>, rather than adding a copy-to-output item
/// group for a file this task does not own (the repo root is outside every Owns list).</para>
/// </remarks>
internal static class LocalAssets
{
    private const string SolutionFileName = "IC2.sln";
    private const string ConfigFileName = "assets.local.ini";
    private const string FixturesDirEnvVar = "IC2_FIXTURES_DIR";

    /// <summary>The repository root, found by walking up from the test assembly's location.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Full path to the repo-root <c>assets.local.ini</c>, whether or not it exists.</summary>
    public static string ConfigPath { get; } = Path.Combine(RepositoryRoot, ConfigFileName);

    /// <summary>True once, computed the first time it is touched: whether either source above resolved
    /// to a usable asset directory (one that actually contains the DAT).</summary>
    public static bool IsConfigured { get; }

    /// <summary>True when <see cref="IsConfigured"/> came from <c>IC2_FIXTURES_DIR</c> (CI's narrow,
    /// twelve-fixture clone) rather than a developer's own full <c>assets.local.ini</c> installation.
    /// <see cref="FixtureResolutionTests"/> and diagnostic messages use this to say which source is in
    /// play; it does not change how any other test behaves.</summary>
    public static bool IsCiFixtureMode { get; }

    /// <summary>The loaded settings when <see cref="IsConfigured"/> is true; otherwise null.</summary>
    public static AssetSettings? Settings { get; }

    /// <summary>The reason string every test in this project passes to <c>Skip.IfNot</c>. Distinguishes
    /// (T53 Done-when line 3) "nothing configured" (neither source above is present at all) from
    /// "configured but incomplete" (a source is present but could not be loaded — a missing directory,
    /// a missing DAT, or a malformed ini) — today both produced the identical generic message, which is
    /// how a moved or absent fixture looked identical to a machine nobody had set up.</summary>
    public static string SkipReason { get; }

    static LocalAssets()
    {
        var fixturesDir = Environment.GetEnvironmentVariable(FixturesDirEnvVar);
        if (!string.IsNullOrWhiteSpace(fixturesDir))
        {
            IsCiFixtureMode = true;
            (Settings, IsConfigured, SkipReason) = TryLoadFixturesDir(fixturesDir);
            return;
        }

        IsCiFixtureMode = false;
        var configFileExists = File.Exists(ConfigPath);
        (Settings, IsConfigured) = TryLoad(ConfigPath);
        SkipReason = IsConfigured
            ? "" // never read: no test calls Skip.IfNot(true, ...) down this path
            : configFileExists
                ? $"configured but incomplete: '{ConfigPath}' exists but its [assets] directory or DAT " +
                  "could not be loaded (see AssetSettings.Load's own error for the missing piece)."
                : $"original files not configured: '{ConfigFileName}' was not found at the repository " +
                  $"root ('{ConfigPath}'). Copy assets.example.ini and set [assets] directory.";
    }

    /// <summary>Loads settings from <paramref name="configPath"/>, treating exactly the three
    /// documented "not configured on this machine" exception types as a skip signal — never a bare
    /// <c>catch (Exception)</c> (T34 #40 item 6). <see cref="IC2.Data.AssetSettings.Load"/> throws
    /// <see cref="FileNotFoundException"/>, <see cref="DirectoryNotFoundException"/> or
    /// <see cref="InvalidDataException"/> for every "not configured" case; any other exception type
    /// — a typo'd config producing something unexpected, or a genuine <c>AssetSettings.Load</c> bug —
    /// propagates and fails loudly, rather than silently reporting dozens of skipped tests on a
    /// machine that actually IS configured.</summary>
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

    /// <summary>Points an <see cref="AssetSettings"/> at <paramref name="fixturesDir"/> by writing a
    /// throwaway ini next to it and loading it through the same, unmodified <c>AssetSettings.Load</c>
    /// every other path uses — <c>AssetSettings</c> is outside this task's Owns list, so this reuses it
    /// rather than duplicating or widening it. The fixtures repo's own layout (<c>Imperial Conquest
    /// 2.dat</c> at the root, saves flat under <c>saves/</c>) is exactly what <c>AssetSettings</c>
    /// already expects, so nothing downstream needs to know CI is any different from a developer's own
    /// installation.</summary>
    private static (AssetSettings? Settings, bool IsConfigured, string SkipReason) TryLoadFixturesDir(string fixturesDir)
    {
        var full = Path.GetFullPath(fixturesDir);
        if (!Directory.Exists(full))
            return (null, false,
                $"configured but incomplete: {FixturesDirEnvVar}='{full}' does not exist.");

        var tempIni = Path.Combine(Path.GetTempPath(), "ic2-fixtures-" + Guid.NewGuid().ToString("N") + ".ini");
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
            if (File.Exists(tempIni)) File.Delete(tempIni);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find '{SolutionFileName}' above '{AppContext.BaseDirectory}'.");
    }
}
