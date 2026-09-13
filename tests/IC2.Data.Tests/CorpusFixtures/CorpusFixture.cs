using System.Text.Json;

namespace IC2.Data.Tests.CorpusFixtures;

/// <summary>Loads the committed <see cref="CorpusEntry"/> table from disk. See <see cref="CorpusEntry"/>.</summary>
internal static class CorpusFixture
{
    /// <summary>The committed expected-outcome table, relative to this file's own folder so it is
    /// found the same way whether the test runs from source or from a build output directory.</summary>
    public static string FixtureFile { get; } =
        Path.Combine(LocalAssets.RepositoryRoot, "tests", "IC2.Data.Tests", "CorpusFixtures",
            "expected-corpus-outcomes.json");

    public static IReadOnlyList<CorpusEntry> Entries { get; } = Load();

    private static IReadOnlyList<CorpusEntry> Load()
    {
        var json = File.ReadAllText(FixtureFile);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var entries = JsonSerializer.Deserialize<List<CorpusEntry>>(json, options);
        if (entries is null || entries.Count == 0)
            throw new InvalidOperationException($"'{FixtureFile}' did not deserialize to a nonempty corpus table.");
        return entries;
    }
}
