using IC2.Inspect;

namespace IC2.Data.Tests.CorpusFixtures;

/// <summary>Loads the committed <see cref="CorpusOutcome"/> table from disk, using the exact same
/// (de)serialization <c>IC2.Inspect --corpus-outcomes</c> uses to write it — see
/// <see cref="CorpusOutcomeGenerator.FromJson"/>.</summary>
internal static class CorpusFixture
{
    /// <summary>The committed expected-outcome table, relative to this file's own folder so it is
    /// found the same way whether the test runs from source or from a build output directory.</summary>
    public static string FixtureFile { get; } =
        Path.Combine(LocalAssets.RepositoryRoot, "tests", "IC2.Data.Tests", "CorpusFixtures",
            "expected-corpus-outcomes.json");

    public static IReadOnlyList<CorpusOutcome> Entries { get; } = Load();

    private static IReadOnlyList<CorpusOutcome> Load() =>
        CorpusOutcomeGenerator.FromJson(File.ReadAllText(FixtureFile));
}
