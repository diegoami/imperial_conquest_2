namespace IC2.Engine.Tests.Fixtures;

/// <summary>
/// Locates the repository's shipped <c>tests/fixtures/</c> directory from a test run's output
/// folder.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IC2.Engine.Tests.Model.TestPaths"/>'s approach (T02): walk up from the test
/// assembly's location to <c>IC2.sln</c> and read the committed files in place, rather than adding
/// a copy-to-output item group to <c>IC2.Engine.Tests.csproj</c> — that file is T01's, not part of
/// this task's Owns list (<c>tests/fixtures/**</c>, <c>tests/IC2.Engine.Tests/Fixtures/**</c>).
/// </remarks>
public static class FixturePaths
{
    private const string SolutionFileName = "IC2.sln";

    /// <summary>The repository root, found by walking up from the test assembly's location.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The shipped <c>tests/fixtures/</c> directory.</summary>
    public static string FixturesRoot { get; } = Path.Combine(RepositoryRoot, "tests", "fixtures");

    /// <summary>The fixtures corpus: every exact number transcribed from the research repo's reports.</summary>
    public static string CorpusFile { get; } = Path.Combine(FixturesRoot, "corpus.json");

    /// <summary>The committed manifest of the research repo's real <c>docs/reports/</c> filenames.</summary>
    public static string KnownReportsFile { get; } = Path.Combine(FixturesRoot, "known-reports.json");

    /// <summary>The committed list of fixture ids every later task's Done-when checks may rely on.</summary>
    public static string RequiredIdsFile { get; } = Path.Combine(FixturesRoot, "required-ids.json");

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
