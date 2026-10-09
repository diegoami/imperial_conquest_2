using IC2.Engine.Serialization;
using IC2.Engine.Tests.Fixtures;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T155 Done-when 2's corpus half, a top-up under T04's existing contract
/// (<c>tests/fixtures/**</c>, <c>docs/build-process.md</c> §2.4): the capital-or-75% recruitment gate's
/// threshold is transcribed once into the fixtures corpus with its report, and every shipped ruleset
/// carries it at the corpus value with the report and <c>[derived: code]</c> in its
/// <c>_provenance</c>. T04's own four Done-when checks
/// (<see cref="IC2.Engine.Tests.Fixtures.FixturesCorpusTests"/>) re-run generically over the whole
/// corpus and stay green with this entry present; this file spot-checks the specific top-up.
/// </summary>
public sealed class RecruitTownThresholdCorpusTests
{
    private const string ThresholdId = "recruitment.recruitTownMinFortificationPercent";
    private const string Report = "2026-09-29-which-cities-may-recruit-and-troop-amounts.md";

    private static readonly Lazy<GameDataRepository> LazyRepository =
        new(() => GameDataRepository.Load(TestPaths.DataRoot));

    private static GameDataRepository Repository => LazyRepository.Value;

    [Fact]
    public void The_threshold_is_75_citing_the_recruitment_report_as_derived()
    {
        var entry = FixtureCorpus.Get(ThresholdId);
        Assert.Equal(75, entry.AsInt());
        Assert.Equal(Report, entry.Source);
        Assert.Equal("derived", entry.Tag);
    }

    [Fact]
    public void The_known_reports_manifest_includes_the_cited_report()
    {
        Assert.Contains(Report, FixtureCorpus.KnownReportFilenames);
    }

    /// <summary>
    /// Done-when 2: the key is 75 in <c>classical-faithful</c> and <c>improved</c>, and
    /// <c>toy-ruleset.json</c> carries it too — at 75 as well, the value that keeps its existing
    /// scenarios and tests meaningful: every toy-3city city already passes the gate (arx and meridia
    /// are capitals, portus's word 340 carries a pending order), so no toy scenario or golden
    /// changes behaviour, and the 74/75 boundary is testable against the shipped ruleset rather than
    /// a test-only one. Each ruleset's <c>_provenance</c> cites the report and
    /// <c>[derived: code]</c>.
    /// </summary>
    [Theory]
    [InlineData("classical-faithful")]
    [InlineData("improved")]
    [InlineData("toy-ruleset")]
    public void Every_ruleset_carries_the_threshold_at_the_corpus_value_with_its_provenance(string rulesetId)
    {
        var ruleset = Repository.RulesetById(rulesetId)!;
        Assert.Equal(FixtureCorpus.Get(ThresholdId).AsInt(), ruleset.Recruitment.RecruitTownMinFortificationPercent);

        var provenance = ruleset.Recruitment.Provenance?.SourceFor("recruitTownMinFortificationPercent");
        Assert.False(string.IsNullOrWhiteSpace(provenance), $"'{rulesetId}' has no provenance for the threshold");
        Assert.Contains(Report, provenance, StringComparison.Ordinal);
        Assert.Contains("[derived: code]", provenance, StringComparison.Ordinal);
    }

    [Fact]
    public void All_four_T04_checks_stay_green_with_the_topped_up_corpus()
    {
        // The same generic assertions FixturesCorpusTests makes, re-run over the corpus this task's
        // edits produced — the top-up contract's own requirement, not a duplicate of that suite.
        Assert.NotEmpty(FixtureCorpus.All);
        Assert.True(FixtureCorpus.All.All(e => !string.IsNullOrWhiteSpace(e.Id)));
        var known = new HashSet<string>(FixtureCorpus.KnownReportFilenames, StringComparer.Ordinal);
        Assert.True(FixtureCorpus.All.Select(e => e.Source).Distinct().All(known.Contains));
        Assert.True(FixtureCorpus.RequiredIds.All(id => FixtureCorpus.All.Any(e => e.Id == id)));
        Assert.Equal(FixtureCorpus.All.Count, FixtureCorpus.All.Select(e => e.Id).Distinct().Count());
    }
}
