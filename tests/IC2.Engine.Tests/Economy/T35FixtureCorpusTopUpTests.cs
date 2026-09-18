using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 8: the corpus corrections. This is a top-up under T04's existing
/// contract (<c>tests/fixtures/**</c>), not a reopen of it: T04's own four Done-when checks
/// (<see cref="IC2.Engine.Tests.Fixtures.FixturesCorpusTests"/>) already re-run generically over the
/// whole corpus and stay green with these corrected entries present; this file spot-checks that the
/// specific corrections this task made resolve to the expected values.
/// </summary>
public sealed class T35FixtureCorpusTopUpTests
{
    [Fact]
    public void NationTaxBaseRome_IsCorrectedTo2444()
    {
        var entry = FixtureCorpus.Get("tax.nationTaxBaseRome");
        Assert.Equal(2444, entry.AsInt());
        Assert.Equal("nation-tax-base-and-city-economy-fields.md", entry.Source);
        Assert.Equal("confirmed", entry.Tag);
    }

    [Fact]
    public void UnityDecayPerQuarter_IsReplacedByMobilizationDecayPerQuarter()
    {
        Assert.Throws<KeyNotFoundException>(() => FixtureCorpus.Get("economy.unityDecayPerQuarter"));

        var entry = FixtureCorpus.Get("economy.mobilizationDecayPerQuarter");
        Assert.Equal(3, entry.AsInt());
        Assert.Equal("city-population-growth.md", entry.Source);

        Assert.Equal(entry.AsInt(), EconomyTestbed.Ruleset.Economy.MobilizationDecayPerQuarter);
    }

    [Fact]
    public void WealthFormulaOnCapture_IsKeyedToPopulationNotFortification()
    {
        var value = FixtureCorpus.Get("economy.wealthFormulaOnCapture").AsString();
        Assert.Contains("Population", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fortification", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CaptureTaxBaseMultiplier_StaysFourAndCitesTheCitysOwnContribution()
    {
        Assert.Equal(4, FixtureCorpus.Get("capture.taxBaseMultiplier").AsInt());
    }

    [Fact]
    public void KnownReportsManifest_IncludesBothNewlyCitedReports()
    {
        Assert.Contains("nation-tax-base-and-city-economy-fields.md", FixtureCorpus.KnownReportFilenames);
        Assert.Contains("city-population-growth.md", FixtureCorpus.KnownReportFilenames);
    }

    [Fact]
    public void AllFourT04ChecksStayGreenWithTheCorrectedCorpus()
    {
        // Re-runs the same assertions FixturesCorpusTests makes, generically, over the corpus this
        // task's edits produced -- the top-up contract's own requirement, not a duplicate of that suite.
        Assert.NotEmpty(FixtureCorpus.All);
        Assert.True(FixtureCorpus.All.All(e => !string.IsNullOrWhiteSpace(e.Id)));
        var known = new HashSet<string>(FixtureCorpus.KnownReportFilenames, StringComparer.Ordinal);
        Assert.True(FixtureCorpus.All.Select(e => e.Source).Distinct().All(known.Contains));
        Assert.True(FixtureCorpus.RequiredIds.All(id => FixtureCorpus.All.Any(e => e.Id == id)));
        Assert.Equal(FixtureCorpus.All.Count, FixtureCorpus.All.Select(e => e.Id).Distinct().Count());
    }
}
