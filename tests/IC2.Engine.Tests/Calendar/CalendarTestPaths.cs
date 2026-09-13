namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// Locates this task's own committed test data from a test run's output folder.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IC2.Engine.Tests.Model.TestPaths"/> (T02) and
/// <see cref="IC2.Engine.Tests.Fixtures.FixturePaths"/> (T04): walk up from the test assembly's
/// location to <c>IC2.sln</c> and read the committed file in place, rather than adding a
/// copy-to-output item group to <c>IC2.Engine.Tests.csproj</c> -- that file is T01's, not part of this
/// task's Owns list (<c>src/IC2.Engine/Calendar/**</c>, <c>tests/IC2.Engine.Tests/Calendar/**</c>).
/// </remarks>
public static class CalendarTestPaths
{
    private const string SolutionFileName = "IC2.sln";

    /// <summary>The repository root, found by walking up from the test assembly's location.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The DoD 1 hand-computed expected week/season/year sequence over 48 rounds.</summary>
    public static string ExpectedCalendarSequenceFile { get; } = Path.Combine(
        RepositoryRoot, "tests", "IC2.Engine.Tests", "Calendar", "expected-calendar-sequence.json");

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
