namespace IC2.Data.Tests;

/// <summary>
/// Locates the repository's <c>assets.local.ini</c> (repo root) and exposes whether the user's
/// original Imperial Conquest 2 files are configured on this machine — exactly the check T21 and
/// T29's Done-when lines describe ("every test in this task skips with an explicit 'original files
/// not configured' result when assets.local.ini is absent"). Every test in this task
/// (docs/build-orchestration-plan.md, T30, Done-when line 8) calls <see cref="SkipReason"/> via
/// <c>Skip.IfNot</c> before touching any local file, so CI (which never has assets.local.ini) reports
/// an explicit Skipped result rather than a silent pass or a hard failure.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IC2.Engine.Tests.Model.TestPaths"/> / <c>FixturePaths</c>'s own approach: walk
/// up from the test assembly's location to <c>IC2.sln</c>, rather than adding a copy-to-output item
/// group for a file this task does not own (the repo root is outside every Owns list).
/// </remarks>
internal static class LocalAssets
{
    private const string SolutionFileName = "IC2.sln";
    private const string ConfigFileName = "assets.local.ini";

    /// <summary>The repository root, found by walking up from the test assembly's location.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Full path to the repo-root <c>assets.local.ini</c>, whether or not it exists.</summary>
    public static string ConfigPath { get; } = Path.Combine(RepositoryRoot, ConfigFileName);

    /// <summary>True once, computed the first time it is touched: whether <c>assets.local.ini</c>
    /// exists and points at a directory that actually contains the DAT.</summary>
    public static bool IsConfigured { get; }

    /// <summary>The loaded settings when <see cref="IsConfigured"/> is true; otherwise null.</summary>
    public static AssetSettings? Settings { get; }

    /// <summary>The reason string every test in this task passes to <c>Skip.IfNot</c>.</summary>
    public const string SkipReason = "original files not configured";

    static LocalAssets()
    {
        (Settings, IsConfigured) = TryLoad(ConfigPath);
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
