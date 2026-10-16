using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.SerializationTests;

public sealed class MobilizationGateValidationTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void Gate_parameters_reject_nonpositive_values(int value, bool distance)
    {
        var rules = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        var changed = rules with { Ai = distance
            ? rules.Ai with { FaithfulMobilizeArmyDistanceBelow = value }
            : rules.Ai with { FaithfulMobilizeReadyCountAtLeast = value } };
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate("gate.json", changed));
        Assert.Contains(distance ? "ai.faithfulMobilizeArmyDistanceBelow" : "ai.faithfulMobilizeReadyCountAtLeast", ex.Message);
    }

    [Fact]
    public void Toy_data_contains_the_cited_original_gate_parameters()
    {
        var rules = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        GameDataValidation.Validate(TestPaths.ToyRulesetFile, rules);
        Assert.Equal(6, rules.Ai.FaithfulMobilizeArmyDistanceBelow);
        Assert.Equal(4, rules.Ai.FaithfulMobilizeReadyCountAtLeast);
    }
}
