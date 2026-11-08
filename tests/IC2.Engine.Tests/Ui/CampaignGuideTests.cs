using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

public sealed class CampaignGuideTests
{
    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);
    private static readonly CampaignGuideCatalog Catalog = CampaignGuideCatalog.Load(Repository);

    [Fact]
    public void Every_shipped_campaign_choice_has_valid_guidance_and_a_maintained_brief()
    {
        foreach (var card in IC2.Slice.UI.RulesetPresets.CardsFor(Repository))
        {
            var guide = Assert.IsType<CampaignGuide>(Catalog.ForScenario(card.ScenarioId));
            CampaignGuideCatalog.Validate(guide, Repository);
            Assert.Equal("historical", guide.Kind);
            Assert.StartsWith("Provisional", guide.Validation);
            var brief = Path.Combine(Path.GetDirectoryName(Repository.DataRoot)!, guide.Brief);
            Assert.True(File.Exists(brief), brief);
            Assert.Contains($"`{guide.Id}`", File.ReadAllText(brief));
        }
        Assert.Null(Catalog.ForScenario("toy-3city"));
    }

    [Theory]
    [InlineData("deleted-nation")]
    [InlineData("barbarians")]
    public void Advice_cannot_name_missing_or_nonplayable_seats(string nation)
    {
        var guide = Catalog.ForScenario("near-east-1350bc")!;
        var powers = guide.Powers.ToArray();
        powers[0] = powers[0] with { NationId = nation };
        Assert.Throws<InvalidDataException>(() => CampaignGuideCatalog.Validate(guide with { Powers = powers }, Repository));
    }

    [Fact]
    public void Naval_recommendation_requires_an_actual_starting_fleet_in_every_variant()
    {
        var guide = Catalog.ForScenario("classical-mediterranean")!;
        var powers = guide.Powers.ToArray();
        powers[0] = powers[0] with { RequiresStartingFleet = true }; // Rome has none.
        Assert.Throws<InvalidDataException>(() => CampaignGuideCatalog.Validate(guide with { Powers = powers }, Repository));
    }

    [Fact]
    public void Duplicate_ids_and_unsafe_or_missing_provenance_are_rejected()
    {
        var guide = Catalog.ForScenario("classical-mediterranean")!;
        foreach (var invalid in new[]
        {
            guide with { ScenarioIds = new[] { "classical-mediterranean", "classical-mediterranean" } },
            guide with { Powers = new[] { guide.Powers[0], guide.Powers[0] } },
            guide with { Sources = Array.Empty<CampaignGuideSource>() },
            guide with { Sources = new[] { new CampaignGuideSource("unsafe", "file:///private") } },
            guide with { Brief = "../private.md" },
            guide with { Background = new string('x', 901) },
            guide with { SchemaVersion = 2 },
            guide with { Kind = "unclassified" }
        }) Assert.Throws<InvalidDataException>(() => CampaignGuideCatalog.Validate(invalid, Repository));
    }
}
