using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries", Done-when 2: the Felsina hire
/// (6,438 troops, "very good") costs <c>(troops × quarterlyPrice[type]) / 1000 × quality</c> exactly.
/// </summary>
public sealed class MercenaryHireCostTests
{
    private static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    /// <summary>
    /// mercenary-pool-record.md's confirmed Felsina slot: 6,438 "very good" (quality 8) light infantry.
    /// <c>(6438 * 1) / 1000 * 8 = 6 * 8 = 48</c> talents. No report independently observed a concrete
    /// hire-cost figure to check the total against (the 51-talent Felsina number in the corpus is the
    /// recurring quarterly upkeep, not this one-time cost — see <c>mercenary.felsina.quarterlyCostTalents</c>'s
    /// own note); this asserts the confirmed formula shape reproduces the arithmetic by hand.
    /// </summary>
    [Fact]
    public void Compute_reproduces_the_Felsina_hire_exactly()
    {
        var troops = FixtureCorpus.Get("mercenary.felsina.troops").AsInt();
        var quality = FixtureCorpus.Get("mercenary.felsina.qualityCode").AsInt();

        var cost = MercenaryHireCost.Compute(troops, "light_infantry", quality, Ruleset);

        Assert.Equal(48, cost);
        Assert.Equal((troops * Ruleset.UnitTypeById("light_infantry")!.QuarterlyPrice)
            / Ruleset.Recruitment.MercenaryHireTroopDivisor * quality, cost);
    }

    [Fact]
    public void Unknown_unit_type_throws()
    {
        Assert.Throws<ArgumentException>(() => MercenaryHireCost.Compute(1000, "no-such-type", 8, Ruleset));
    }
}
