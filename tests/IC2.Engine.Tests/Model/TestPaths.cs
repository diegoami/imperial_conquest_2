namespace IC2.Engine.Tests.Model;

/// <summary>
/// Locates the repository's shipped <c>data/</c> directory from a test run's output folder.
/// </summary>
/// <remarks>
/// The data files are not copied into the test output: the task's Owns list covers
/// <c>tests/IC2.Engine.Tests/Model/**</c> but not the test project file, so adding a copy-to-output item
/// group would be an out-of-scope change. Walking up to the solution file is both in scope and
/// honest — the tests read exactly the files that are committed, not a copy of them.
/// </remarks>
public static class TestPaths
{
    private const string SolutionFileName = "IC2.sln";

    /// <summary>The repository root, found by walking up from the test assembly's location.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The shipped <c>data/</c> directory.</summary>
    public static string DataRoot { get; } = Path.Combine(RepositoryRoot, "data");

    /// <summary>The toy world file this task ships.</summary>
    public static string ToyWorldFile { get; } = Path.Combine(DataRoot, "worlds", "toy-3city.json");

    /// <summary>The toy ruleset file this task ships.</summary>
    public static string ToyRulesetFile { get; } = Path.Combine(DataRoot, "rulesets", "toy-ruleset.json");

    /// <summary>The toy scenario file this task ships.</summary>
    public static string ToyScenarioFile { get; } = Path.Combine(DataRoot, "scenarios", "toy-3city.json");

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
