using IC2.Engine.Victory;
using Xunit;

namespace IC2.Engine.Tests.Victory;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T12 Victory conditions" — <c>docs/game-design.md</c>
/// §"Victory conditions" <strong>[designed]</strong>: "a weighted sum of cities held, treasury, and
/// unity, highest wins." The turn limit itself comes from the shipped toy scenario
/// (<c>turnLimit: 50</c> in <c>data/scenarios/toy-3city.json</c>), never a literal here.
/// </summary>
public sealed class ScoreAtTurnLimitVictoryTests
{
    [Fact]
    public void HighestScoreWins_OnceTheTurnLimitIsReached()
    {
        var state = VictoryTestbed.InitialState();
        var turnLimit = VictoryTestbed.Scenario.TurnLimit;
        Assert.NotNull(turnLimit);

        var atTheLimit = VictoryTestbed.WithTurnIndex(state, turnLimit!.Value);
        var withHigherTreasury = VictoryTestbed.WithTreasury(atTheLimit, "north", 10_000);

        var outcome = VictoryEvaluator.EvaluateScoreAtTurnLimit(withHigherTreasury, turnLimit);

        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }

    [Fact]
    public void NoWinner_BeforeTheTurnLimitIsReached()
    {
        var state = VictoryTestbed.InitialState();
        var turnLimit = VictoryTestbed.Scenario.TurnLimit;
        Assert.NotNull(turnLimit);

        var oneShort = VictoryTestbed.WithTurnIndex(state, turnLimit!.Value - 1);
        var withHigherTreasury = VictoryTestbed.WithTreasury(oneShort, "north", 10_000);

        var outcome = VictoryEvaluator.EvaluateScoreAtTurnLimit(withHigherTreasury, turnLimit);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    [Fact]
    public void NoWinner_WhenTheHighestScoreIsTied()
    {
        var state = VictoryTestbed.InitialState();
        var turnLimit = VictoryTestbed.Scenario.TurnLimit;
        Assert.NotNull(turnLimit);

        var atTheLimit = VictoryTestbed.WithTurnIndex(state, turnLimit!.Value);

        // Leave the toy world's own starting split as is (North holds 2 of 3 cities, South 1), and
        // choose treasury/unity so the two nations' totals land on the same score: North 2 (cities) + 0
        // + 0 = 2; South 1 (city) + 1 + 0 = 2.
        var north = VictoryTestbed.WithUnity(VictoryTestbed.WithTreasury(atTheLimit, "north", 0), "north", 0);
        var evenedOut = VictoryTestbed.WithUnity(VictoryTestbed.WithTreasury(north, "south", 1), "south", 0);

        var outcome = VictoryEvaluator.EvaluateScoreAtTurnLimit(evenedOut, turnLimit);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    [Fact]
    public void NoWinner_WhenNoTurnLimitIsConfigured()
    {
        var state = VictoryTestbed.InitialState();
        var farInTheFuture = VictoryTestbed.WithTurnIndex(state, 10_000);

        var outcome = VictoryEvaluator.EvaluateScoreAtTurnLimit(farInTheFuture, turnLimit: null);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }
}
