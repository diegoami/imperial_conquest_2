using IC2.Inspect;
using Xunit;

namespace IC2.Data.Tests.CorpusFixtures;

/// <summary>
/// T34 Done-when line 3: <c>IC2.Inspect --corpus-outcomes &lt;out.json&gt;</c> regenerates the fixture
/// from the configured directory, and a re-run over an unchanged corpus is byte-identical. The exact
/// file count is intentionally NOT asserted here (it belongs in the PR body, not hard-coded in a
/// test) — the corpus grows over time as /process-evidence adds and moves saves.
/// </summary>
public class CorpusOutcomeGeneratorTests
{
    private const string RegenerateCommand =
        "dotnet run --project src/IC2.Inspect -- --corpus-outcomes " +
        "tests/IC2.Data.Tests/CorpusFixtures/expected-corpus-outcomes.json";

    [SkippableFact]
    public void Regenerating_the_corpus_outcomes_twice_is_byte_identical()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;

        var first = CorpusOutcomeGenerator.ToJson(CorpusOutcomeGenerator.GenerateAll(settings));
        var second = CorpusOutcomeGenerator.ToJson(CorpusOutcomeGenerator.GenerateAll(settings));

        Assert.Equal(first, second);
    }

    /// <summary>The committed fixture IS a corpus-outcomes regeneration (this task's Done-when line
    /// 3: "the committed fixture is regenerated with it").
    ///
    /// T53 (issue #204): this used to skip specifically under <see cref="LocalAssets.IsCiFixtureMode"/>,
    /// because CI's fixtures repo originally held only the twelve saves the tests name as literals, and
    /// this comparison is against the full committed table — a guaranteed, not-a-bug mismatch. The
    /// fixtures repo now holds the whole corpus (issue #207), so that particular mismatch no longer
    /// occurs — confirmed by removing the guard and rerunning against a real download: it passes.
    ///
    /// T64 (#301) revisits this once more: the committed table is regenerated over whichever machine
    /// ran the regeneration, and a developer's own configured directory can legitimately hold saves
    /// the fixtures repo has not received yet (the 2026-09-20 <c>IP*.sav</c> batch, present locally,
    /// absent from CI's 54-save clone until that repository is updated) — the "two corpora" hazard.
    /// Requiring every committed row to also exist on THIS run's disk (the direction the old full-list
    /// <c>Assert.Equal</c> enforced) would make CI fail the moment the committed table gets ahead of
    /// the fixtures repo, exactly the situation this task creates. So <b>that one</b> direction is
    /// relaxed: a committed row for a file this run's corpus doesn't have is not compared at all here,
    /// the same "not a bug, not urgent" treatment
    /// <see cref="CorpusSweepTests.Fixture_covers_every_file_currently_in_the_configured_corpus"/>
    /// already gives the opposite direction (a disk file the table doesn't cover) via a named Skip —
    /// so both directions of "the two corpora disagree" are handled the same way, just in the two
    /// tests that each already own one direction of the coverage check.
    ///
    /// <b>The other direction stays a hard requirement</b> (T64 rework round 1, N1): every file this
    /// run's corpus actually has must be represented in the committed table, and parse to exactly what
    /// that row says — a regeneration this task's own Done-when 7 requires, and a real, failing signal
    /// no other test in this project gives (a local save present on disk with no row at all, meaning
    /// the table was never regenerated after that save was added). Silently skipping an unmatched
    /// regenerated file, as an earlier revision of this test did, would remove exactly that
    /// signal — nothing in any of the three run shapes (with `assets.local.ini`, unconfigured, or
    /// CI-shaped) needs it relaxed, since the committed table this PR ships already covers every file
    /// in every shape's corpus.</summary>
    [SkippableFact]
    public void The_committed_fixture_matches_a_fresh_regeneration_over_the_configured_corpus()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;

        var regenerated = CorpusOutcomeGenerator.GenerateAll(settings);
        var committedByName = CorpusFixture.Entries.ToDictionary(e => e.FileName, StringComparer.Ordinal);

        Assert.NotEmpty(regenerated);
        foreach (var actual in regenerated)
        {
            Assert.True(committedByName.TryGetValue(actual.FileName, out var expected),
                $"{actual.FileName} is in the configured corpus but has no row in the committed " +
                $"fixture. Regenerate it with: {RegenerateCommand}");
            Assert.Equal(expected, actual);
        }
    }
}
