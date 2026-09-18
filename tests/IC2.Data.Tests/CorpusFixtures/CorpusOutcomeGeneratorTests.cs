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
    /// conventions than the one this test process would emit — the content is what must match.</summary>
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
