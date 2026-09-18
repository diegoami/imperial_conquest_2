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
    [InlineData("{\"nation\":\"north\",\"holdCities\":[]}")]
    [InlineData("{\"nation\":\"north\",\"holdCities\":[\"arx\",null]}")]
    [InlineData("{\"nation\":\"north\",\"holdCities\":[\"arx\",\"  \"]}")]
    [InlineData("{\"nation\":\"north\",\"holdCities\":[\"arx\"],\"turnAtOrAfter\":-1}")]
    public void Evaluate_MalformedGoal_Throws(string malformed)
    {
        var goal = new VictoryCondition(VictoryConditionType.Custom, malformed);

        Assert.Throws<FormatException>(() => CustomVictoryGoal.Evaluate(VictoryTestbed.InitialState(), goal));
    }

    /// <summary>
    /// Review round 1, blocking finding 1: <c>{"nation":"north","holdCities":[]}</c> used to pass
    /// <c>Parse</c> (only a <em>null</em> <c>holdCities</c> was rejected), so the hold-check loop ran
    /// zero times and any live named nation won instantly, holding nothing — for a goal with no
    /// <c>turnAtOrAfter</c> at all, on turn 0. This pins the fix directly against the exact repro from
    /// the review, rather than only through the <see cref="Evaluate_MalformedGoal_Throws"/> theory.
    /// </summary>
    [Fact]
    public void EmptyHoldCities_NoLongerWinsInstantly_ItIsRejectedAtParseTime()
    {
        var instantWinAttempt = new VictoryCondition(
            VictoryConditionType.Custom, "{\"nation\":\"north\",\"holdCities\":[]}");

        // Turn 0, nobody has done anything -- this must never resolve to a win.
        var ex = Assert.Throws<FormatException>(
            () => CustomVictoryGoal.Evaluate(VictoryTestbed.InitialState(), instantWinAttempt));
        Assert.Contains("holdCities", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotFire_WhenTheNamedNationDoesNotExist()
    {
        var goal = new VictoryCondition(
            VictoryConditionType.Custom, "{\"nation\":\"atlantis\",\"holdCities\":[\"arx\"]}");

        var outcome = CustomVictoryGoal.Evaluate(VictoryTestbed.InitialState(), goal);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    /// <summary>
    /// A city id absent from the world is not a parse-time rejection (<c>Parse</c> never sees a
    /// <see cref="GameState"/>) but documented, tested runtime behaviour: the goal simply never becomes
    /// true, the same friendly failure mode as an unknown nation id.
    /// </summary>
    [Fact]
    public void DoesNotFire_WhenANamedCityDoesNotExistInTheWorld()
    {
        var goal = new VictoryCondition(
            VictoryConditionType.Custom, "{\"nation\":\"north\",\"holdCities\":[\"arx\",\"atlantis-city\"]}");
        var state = VictoryTestbed.WithAllCitiesOwnedBy(VictoryTestbed.InitialState(), "north");

        var outcome = CustomVictoryGoal.Evaluate(state, goal);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    /// <summary>
    /// A repeated city id is accepted, not rejected: the hold check is idempotent, so naming the same
    /// city twice changes nothing about whether the goal fires.
    /// </summary>
    [Fact]
    public void Fires_EvenWhenACityIdIsRepeated_TheDuplicateChangesNothing()
    {
        var goal = new VictoryCondition(
            VictoryConditionType.Custom, "{\"nation\":\"north\",\"holdCities\":[\"arx\",\"arx\",\"portus\"]}");
        var state = VictoryTestbed.InitialState(); // North already holds arx and portus.

        var outcome = CustomVictoryGoal.Evaluate(state, goal);

        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }
}
