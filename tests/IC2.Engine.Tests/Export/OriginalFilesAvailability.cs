namespace IC2.Engine.Tests.Export;

/// <summary>
/// Whether the original <c>Imperial Conquest 2.dat</c> is available on this machine, so this
/// project's export-reproducibility tests can decide, at runtime, whether to actually re-run
/// <c>scripts/export-classical-world.cs</c> or report that the original files are not configured
/// (<c>docs/task-catalogue.md</c> T29 DoD 6).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this duplicates a sliver of <c>IC2.Data.Tests.LocalAssets</c>/<c>AssetSettings</c>
/// rather than reusing them:</strong> this task's Owns list is <c>tests/IC2.Engine.Tests/Export/**</c>
/// plus, as of the widened grant that added <c>Xunit.SkippableFact</c>, one package reference in
/// <c>IC2.Engine.Tests.csproj</c> — it does not extend to a <c>ProjectReference</c> on
/// <c>IC2.Data</c>. T30 added that reference to <c>IC2.Data.Tests.csproj</c> for the identical
/// reason, but that file belongs to whichever task owns this project's scaffolding, not this one.
/// This class therefore re-derives only the minimal "is a DAT reachable" signal — an existence
/// check, not a parse — entirely from files this task is allowed to read.
/// </para>
/// <para>
/// <c>Xunit.SkippableFact</c> (the package this task's widened Owns grant added) is what lets
/// <see cref="ExportScriptReproducibilityTests"/> report a genuine xunit <c>Skipped</c> result via
/// <c>Skip.IfNot(IsConfigured, SkipReason)</c>, the same call <c>IC2.Data.Tests</c> already makes
/// against <c>LocalAssets.IsConfigured</c>/<c>LocalAssets.SkipReason</c> — mirrored here rather than
/// reused, for the same "no <c>IC2.Data</c> reference" reason above.
/// </para>
/// </remarks>
internal static class OriginalFilesAvailability
{
    private const string FixturesDirEnvVar = "IC2_FIXTURES_DIR";
    private const string DatFileName = "Imperial Conquest 2.dat";

    /// <summary>
    /// True when either <c>IC2_FIXTURES_DIR</c> (CI's fixtures clone) or the repository-root
    /// <c>assets.local.ini</c> (a developer's own installation) resolves to a directory that
    /// actually contains <c>Imperial Conquest 2.dat</c>.
    /// </summary>
    public static bool IsConfigured { get; } = TryResolveDatPath() is not null;

    /// <summary>The resolved path to the DAT, or <see langword="null"/> when nothing is configured.</summary>
    public static string? DatPath { get; } = TryResolveDatPath();

    /// <summary>The reason every dependent test passes to <c>Skip.IfNot</c> when not configured.</summary>
    public const string SkipReason =
        "original files not configured: neither IC2_FIXTURES_DIR nor a usable repository-root "
        + "assets.local.ini was found.";

    private static string? TryResolveDatPath()
    {
        var fixturesDir = Environment.GetEnvironmentVariable(FixturesDirEnvVar);
        if (!string.IsNullOrWhiteSpace(fixturesDir) && Directory.Exists(fixturesDir))
        {
            var candidate = Path.Combine(fixturesDir, DatFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (!File.Exists(ExportedDataPaths.AssetsIniPath))
        {
            return null;
        }

        var directory = ReadAssetsDirectory(ExportedDataPaths.AssetsIniPath);
        if (directory is null)
        {
            return null;
        }

        var datPath = Path.Combine(directory, DatFileName);
        return File.Exists(datPath) ? datPath : null;
    }

    /// <summary>
    /// A minimal read of <c>[assets] directory = ...</c> from an ini file, deliberately no more
    /// capable than that one line -- <see cref="IC2.Data.AssetSettings.Load"/> (outside this task's
    /// reach, see the class remarks) is the authoritative parser every other task uses.
    /// </summary>
    private static string? ReadAssetsDirectory(string configPath)
    {
        var inAssets = false;
        string? directory = null;
        foreach (var raw in File.ReadLines(configPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inAssets = string.Equals(line[1..^1].Trim(), "assets", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inAssets)
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals < 0 || !string.Equals(line[..equals].Trim(), "directory", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            directory = line[(equals + 1)..].Trim().Trim('"');
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(directory);
        var basePath = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var resolved = Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(basePath, expanded));
        return Directory.Exists(resolved) ? resolved : null;
    }
}
