using IC2.Engine.Model;
using IC2.Engine.Victory;
using Xunit;

namespace IC2.Engine.Tests.Victory;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T12 Victory conditions" Note: "which condition is the shipped default
/// is <c>design-audit.md</c> Q5... The task implements all four conditions and reads the default from
/// the ruleset per seat's chosen preset; it does not hardcode either." These tests pin
/// <see cref="VictoryEvaluator.Evaluate"/> reading <see cref="VictoryRules.DefaultCondition"/> off the
/// loaded ruleset when no scenario overrides it, and a scenario's own choice taking priority when one
/// does.
/// </summary>
public sealed class VictoryEvaluatorDispatchTests
{
    [Fact]
    public void NoScenarioGiven_UsesTheRulesetsOwnDefaultCondition_TotalConquest()
    {
        // The shipped toy ruleset's default is totalConquest (data/rulesets/toy-ruleset.json).
        Assert.Equal(VictoryConditionType.TotalConquest, VictoryTestbed.Ruleset.Victory.DefaultCondition);

        var conquered = VictoryTestbed.WithAllCitiesOwnedBy(VictoryTestbed.InitialState(), "north");

        var outcome = VictoryEvaluator.Evaluate(conquered, VictoryTestbed.Ruleset, scenario: null);

        Assert.Equal(VictoryConditionType.TotalConquest, outcome.ConditionType);
        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }

    /// <summary>
    /// Swapping only the ruleset's default (never touching engine code) changes which condition
    /// <see cref="VictoryEvaluator.Evaluate"/> checks — proof the choice is read from data, not a
    /// hardcoded case.
    /// </summary>
    [Fact]
    public void NoScenarioGiven_ADifferentRulesetDefault_ChecksThatConditionInstead()
    {
        var dominationDefault = VictoryTestbed.Ruleset with
        {
            Victory = VictoryTestbed.Ruleset.Victory with { DefaultCondition = VictoryConditionType.Domination },
        };
        var warCode = VictoryTestbed.Ruleset.Diplomacy.StateCodes.War;

        var state = VictoryTestbed.InitialState();
        var atWar = VictoryTestbed.WithRelation(state, "north", "south", warCode);
        var stripped = VictoryTestbed.WithCityOwner(atWar, "meridia", "north");

        var outcome = VictoryEvaluator.Evaluate(stripped, dominationDefault, scenario: null);

        Assert.Equal(VictoryConditionType.Domination, outcome.ConditionType);
        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }

    [Fact]
    public void ScenariosOwnChoice_OverridesTheRulesetDefault()
    {
        Assert.Equal(VictoryConditionType.TotalConquest, VictoryTestbed.Ruleset.Victory.DefaultCondition);

        var scoreScenario = VictoryTestbed.Scenario with
        {
            Victory = new VictoryCondition(VictoryConditionType.ScoreAtTurnLimit),
        };
        var turnLimit = scoreScenario.TurnLimit!.Value;

        var state = VictoryTestbed.WithTurnIndex(VictoryTestbed.InitialState(), turnLimit);
        var withHigherTreasury = VictoryTestbed.WithTreasury(state, "north", 10_000);

        var outcome = VictoryEvaluator.Evaluate(withHigherTreasury, VictoryTestbed.Ruleset, scoreScenario);

        Assert.Equal(VictoryConditionType.ScoreAtTurnLimit, outcome.ConditionType);
        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }
}
