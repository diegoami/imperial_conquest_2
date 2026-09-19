namespace IC2.Engine.Tests.Export;

/// <summary>
/// Locates the committed classical-mediterranean world/ruleset/scenario and the export script that
/// produced them, from a test run's output folder. Mirrors
/// <see cref="IC2.Engine.Tests.Model.TestPaths"/> and <see cref="IC2.Engine.Tests.Fixtures.FixturePaths"/>
/// (T02): walk up to <c>IC2.sln</c> and read the committed files in place.
/// </summary>
internal static class ExportedDataPaths
{
    private const string SolutionFileName = "IC2.sln";

    /// <summary>The repository root, found by walking up from the test assembly's location.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The committed, exported world file.</summary>
    public static string WorldFile { get; } = Path.Combine(RepositoryRoot, "data", "worlds", "classical-mediterranean.json");

    /// <summary>The committed, exported ruleset file.</summary>
    public static string RulesetFile { get; } = Path.Combine(RepositoryRoot, "data", "rulesets", "classical-faithful.json");

    /// <summary>The committed, exported scenario file.</summary>
    public static string ScenarioFile { get; } = Path.Combine(RepositoryRoot, "data", "scenarios", "classical-mediterranean.json");

    /// <summary>The one-shot export script that produced the three files above.</summary>
    public static string ExportScript { get; } = Path.Combine(RepositoryRoot, "scripts", "export-classical-world.cs");

    /// <summary>The repository-root <c>assets.local.ini</c>, whether or not it exists.</summary>
    public static string AssetsIniPath { get; } = Path.Combine(RepositoryRoot, "assets.local.ini");

    /// <summary>The T04 fixtures corpus, <c>tests/fixtures/corpus.json</c>.</summary>
    public static string CorpusFile { get; } = Path.Combine(RepositoryRoot, "tests", "fixtures", "corpus.json");

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
