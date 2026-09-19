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
    /// 3: "the committed fixture is regenerated with it"). Comparing the two structurally, rather than
    /// requiring byte-identical JSON text, tolerates the file being saved with different newline
    /// conventions than the one this test process would emit — the content is what must match.
    ///
    /// T53 (issue #204): this used to skip specifically under <see cref="LocalAssets.IsCiFixtureMode"/>,
    /// because CI's fixtures repo originally held only the twelve saves the tests name as literals, and
    /// this comparison is against the full 55-entry committed table — a guaranteed, not-a-bug mismatch.
    /// The fixtures repo now holds the whole corpus (issue #207), so that mismatch no longer occurs —
    /// confirmed by removing the guard and rerunning against a real download: it passes. A guard for a
    /// condition that can no longer arise is a dead branch, so it is removed rather than kept "just in
    /// case": <c>IsCiFixtureMode</c> only ever means "the fixtures repo, whatever it currently holds",
    /// and that repo's own contract (its README) is now "the whole corpus", not a named subset — so
    /// this invariant is expected to hold identically whether the configured directory came from
    /// <c>IC2_FIXTURES_DIR</c> or a developer's own <c>assets.local.ini</c>. If the two sources are ever
    /// deliberately allowed to diverge again, the guard would need to come back — until then, keeping it
    /// would only hide a real corpus-fixture regeneration miss in CI, which is worse than not having it.</summary>
    [SkippableFact]
    public void The_committed_fixture_matches_a_fresh_regeneration_over_the_configured_corpus()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;

        var regenerated = CorpusOutcomeGenerator.GenerateAll(settings);
        var committed = CorpusFixture.Entries;

        Assert.Equal(committed, regenerated);
    }
}
