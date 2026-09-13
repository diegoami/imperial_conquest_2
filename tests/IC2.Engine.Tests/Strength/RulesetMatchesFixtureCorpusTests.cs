using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Strength;

/// <summary>
/// Cross-checks that the ruleset values <c>ArmyPower</c>/<c>FleetPower</c>/<c>SiegeStrength</c> actually
/// read at runtime agree with the T04 fixtures corpus's own transcription of the same reports — the
/// task rule "every gameplay constant comes from the Ruleset or fixtures corpus... pull them from
/// there, don't hardcode" cuts both ways: production code reads <see cref="Model.Ruleset"/> (as every
/// other merged task's production code does), but this test makes sure that ruleset data was not
/// itself quietly hand-typed out of step with the corpus.
/// </summary>
public sealed class RulesetMatchesFixtureCorpusTests
{
    [Theory]
    [InlineData("light_infantry", "unitType.lightInfantry.powerWeight")]
    [InlineData("heavy_infantry", "unitType.heavyInfantry.powerWeight")]
    [InlineData("archers", "unitType.archers.powerWeight")]
    [InlineData("light_cavalry", "unitType.lightCavalry.powerWeight")]
    [InlineData("heavy_cavalry", "unitType.heavyCavalry.powerWeight")]
    public void CombatPowerWeight_MatchesCorpus(string unitTypeId, string fixtureId)
    {
        var ruleset = StrengthTestbed.Ruleset;
        var unitType = ruleset.UnitTypeById(unitTypeId);
        Assert.NotNull(unitType);

        var expected = FixtureCorpus.Get(fixtureId).AsInt();
        Assert.Equal(expected, unitType!.CombatPowerWeight);
    }

    [Fact]
    public void ArcherStrengthMultiplier_MatchesCorpus()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var expected = FixtureCorpus.Get("capture.attackerSiegeStrengthArcherMultiplier").AsInt();
        Assert.Equal(expected, ruleset.Siege.ArcherStrengthMultiplier);
    }
}
