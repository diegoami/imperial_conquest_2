using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// Locates this task's own committed test data from a test run's output folder.
/// </summary>
/// <remarks>
/// T06 review follow-up #43 / T32 DoD 3: this used to carry its own third copy of the repository-root
/// walk, alongside <see cref="IC2.Engine.Tests.Model.TestPaths"/> (T02) and
/// <see cref="IC2.Engine.Tests.Fixtures.FixturePaths"/> (T04). Both of those are reachable from this
/// assembly without editing either task's Owns list, so this now calls
/// <see cref="IC2.Engine.Tests.Model.TestPaths.RepositoryRoot"/> directly rather than re-implementing the
/// walk a third time.
/// </remarks>
public static class CalendarTestPaths
{
    /// <summary>The repository root, found by walking up from the test assembly's location.</summary>
    public static string RepositoryRoot => ModelTestPaths.RepositoryRoot;

    /// <summary>The DoD 1 hand-computed expected week/season/year sequence over 48 rounds.</summary>
    public static string ExpectedCalendarSequenceFile { get; } = Path.Combine(
        RepositoryRoot, "tests", "IC2.Engine.Tests", "Calendar", "expected-calendar-sequence.json");
}
