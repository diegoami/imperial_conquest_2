using IC2.Engine.Model;
using IC2.Engine.Victory;
using Xunit;

namespace IC2.Engine.Tests.Victory;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T12 Victory conditions": "a scenario-custom goal defined purely in
/// scenario JSON fires." Every goal below is authored as the scenario's own <c>victory.goal</c> JSON
/// text (<see cref="CustomVictoryGoal"/>'s small parsed shape) — nothing about which nation or which
/// cities matters is a C# literal inside the evaluator itself.
/// </summary>
public sealed class CustomVictoryGoalTests
{
    private const string HoldAllThreeCitiesFromTurn50 =
        "{\"nation\":\"north\",\"holdCities\":[\"arx\",\"portus\",\"meridia\"],\"turnAtOrAfter\":50}";

    [Fact]
    public void Fires_WhenTheNamedNationHoldsEveryNamedCityAtOrAfterTheStatedTurn()
    {
        var goal = new VictoryCondition(VictoryConditionType.Custom, HoldAllThreeCitiesFromTurn50);
        var state = VictoryTestbed.WithTurnIndex(
            VictoryTestbed.WithAllCitiesOwnedBy(VictoryTestbed.InitialState(), "north"),
            turnIndex: 50);

        var outcome = CustomVictoryGoal.Evaluate(state, goal);

        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }

    [Fact]
    public void DoesNotFire_WhenOneNamedCityIsStillHeldByAnotherNation()
    {
        var goal = new VictoryCondition(VictoryConditionType.Custom, HoldAllThreeCitiesFromTurn50);

        // North holds arx and portus (its starting cities) but not meridia -- the goal names all three.
        var state = VictoryTestbed.WithTurnIndex(VictoryTestbed.InitialState(), turnIndex: 50);

        var outcome = CustomVictoryGoal.Evaluate(state, goal);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    [Fact]
    public void DoesNotFire_BeforeTheStatedTurn_EvenIfEveryCityIsAlreadyHeld()
    {
        var goal = new VictoryCondition(VictoryConditionType.Custom, HoldAllThreeCitiesFromTurn50);
        var state = VictoryTestbed.WithTurnIndex(
            VictoryTestbed.WithAllCitiesOwnedBy(VictoryTestbed.InitialState(), "north"),
            turnIndex: 49);

        var outcome = CustomVictoryGoal.Evaluate(state, goal);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    /// <summary>The full dispatcher, over an actual <see cref="Scenario"/> whose goal is scenario JSON text.</summary>
    [Fact]
    public void EvaluatorDispatch_FiresTheScenarioJsonGoal_ThroughTheTopLevelEvaluate()
    {
        var scenario = VictoryTestbed.Scenario with
        {
            Victory = new VictoryCondition(VictoryConditionType.Custom, HoldAllThreeCitiesFromTurn50),
            TurnLimit = null,
        };
        var state = VictoryTestbed.WithTurnIndex(
            VictoryTestbed.WithAllCitiesOwnedBy(VictoryTestbed.InitialState(), "north"),
            turnIndex: 50);

        var outcome = VictoryEvaluator.Evaluate(state, VictoryTestbed.Ruleset, scenario);

        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal(VictoryConditionType.Custom, outcome.ConditionType);
        Assert.Equal("north", outcome.WinningNationId);
    }

    [Fact]
    public void Evaluate_WrongConditionType_Throws()
    {
        var totalConquest = new VictoryCondition(VictoryConditionType.TotalConquest);

        Assert.Throws<ArgumentException>(
            () => CustomVictoryGoal.Evaluate(VictoryTestbed.InitialState(), totalConquest));
    }

    [Fact]
    public void Evaluate_EmptyGoal_Throws()
    {
        var noGoal = new VictoryCondition(VictoryConditionType.Custom, Goal: null);

        Assert.Throws<InvalidOperationException>(
            () => CustomVictoryGoal.Evaluate(VictoryTestbed.InitialState(), noGoal));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"holdCities\":[\"arx\"]}")]
    [InlineData("{\"nation\":\"north\"}")]
    public void Evaluate_MalformedGoal_Throws(string malformed)
    {
        var goal = new VictoryCondition(VictoryConditionType.Custom, malformed);

        Assert.Throws<FormatException>(() => CustomVictoryGoal.Evaluate(VictoryTestbed.InitialState(), goal));
    }

    [Fact]
    public void DoesNotFire_WhenTheNamedNationDoesNotExist()
    {
        var goal = new VictoryCondition(
            VictoryConditionType.Custom, "{\"nation\":\"atlantis\",\"holdCities\":[\"arx\"]}");

        var outcome = CustomVictoryGoal.Evaluate(VictoryTestbed.InitialState(), goal);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }
}
