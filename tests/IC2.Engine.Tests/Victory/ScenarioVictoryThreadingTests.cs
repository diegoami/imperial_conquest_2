using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Victory;

/// <summary>
/// T199 (#121): the scenario's own victory condition and turn limit reach the round tick, so a game is decided by
/// them through real <c>end</c> commands, after a save and resume, and in the all-AI runner; total conquest keeps
/// the original's own fall path.
/// </summary>
public sealed class ScenarioVictoryThreadingTests : IDisposable
{
    private const int MaxEnds = 40;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ic2-t199-" + Guid.NewGuid().ToString("N"));

    public ScenarioVictoryThreadingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leaked temp directory is a nuisance, not a test failure.
        }
    }

    private static ResolvedScenario Toy => CoreTestbed.Toy;

    /// <summary>The toy scenario, won by <c>north</c> (its human seat) holding its own <c>arx</c> from turn <paramref name="turn"/>.</summary>
    private static Scenario NorthHoldsArxFrom(int turn) => Toy.Scenario with
    {
        Victory = new VictoryCondition(
            VictoryConditionType.Custom, $"{{\"nation\":\"north\",\"holdCities\":[\"arx\"],\"turnAtOrAfter\":{turn}}}"),
    };

    private static (int Ends, List<SeatFall> Falls) EndUntilOver(GameSession session)
    {
        var falls = new List<SeatFall>();
        for (var ends = 1; ends <= MaxEnds; ends++)
        {
            var output = session.Submit("end");
            falls.AddRange(output.SeatFalls ?? Array.Empty<SeatFall>());
            if (output.GameOver)
            {
                return (ends, falls);
            }
        }

        return (-1, falls);
    }

    [Fact]
    public void A_custom_goal_ends_the_session_through_real_round_ticks()
    {
        var session = new GameSession(Toy.World, Toy.Ruleset, NorthHoldsArxFrom(3));

        var (ends, falls) = EndUntilOver(session);

        Assert.True(ends > 0, "The custom goal never ended the game.");
        Assert.True(session.State.Calendar.TurnIndex >= 3);
        var fall = Assert.Single(falls);
        Assert.Equal("north", fall.NationId);
        Assert.Equal(SeatFallReason.ScenarioWon, fall.Reason);
        Assert.Equal(SeatControl.Human, session.State.NationById("north")!.Control);
        Assert.True(session.IsGameOver);
        Assert.Contains("The game is over. No further commands are accepted.", session.Submit("status").Lines);
    }

    [Fact]
    public void The_same_number_of_rounds_under_the_shipped_total_conquest_scenario_does_not_end_the_game()
    {
        var decided = new GameSession(Toy.World, Toy.Ruleset, NorthHoldsArxFrom(3));
        var (ends, _) = EndUntilOver(decided);

        var shipped = new GameSession(Toy.World, Toy.Ruleset, Toy.Scenario);
        for (var i = 0; i < ends; i++)
        {
            Assert.False(shipped.Submit("end").GameOver);
        }
    }

    [Fact]
    public void A_scenario_turn_limit_decides_score_at_turn_limit_through_real_round_ticks()
    {
        const int limit = 4;
        var scenario = Toy.Scenario with
        {
            Victory = new VictoryCondition(VictoryConditionType.ScoreAtTurnLimit),
            TurnLimit = limit,
        };
        var session = new GameSession(Toy.World, Toy.Ruleset, scenario);

        var (ends, falls) = EndUntilOver(session);

        Assert.True(ends > 0, "The scenario's turn limit never ended the game.");
        Assert.True(session.State.Calendar.TurnIndex >= limit);
        var fall = Assert.Single(falls);
        Assert.Equal("north", fall.NationId);
        Assert.Contains(fall.Reason, new[] { SeatFallReason.ScenarioWon, SeatFallReason.ScenarioLost });
    }

    [Fact]
    public void The_scenario_condition_still_decides_after_save_and_resume()
    {
        var scenario = NorthHoldsArxFrom(3);
        var session = new GameSession(Toy.World, Toy.Ruleset, scenario);
        Assert.False(session.Submit("end").GameOver);

        var path = Path.Combine(_directory, "t199.json");
        session.Submit($"save {path}");
        var resumed = new GameSession(Toy.World, Toy.Ruleset, scenario, SaveManager.LoadFile(path, Toy.World, Toy.Ruleset));

        var (ends, falls) = EndUntilOver(resumed);

        Assert.True(ends > 0, "The resumed game was never decided by the scenario's goal.");
        Assert.Equal(SeatFallReason.ScenarioWon, Assert.Single(falls).Reason);
    }

    [Fact]
    public void The_AI_runner_is_decided_by_the_scenario_condition()
    {
        var result = AiGameRunner.Run(Toy.World, Toy.Ruleset, NorthHoldsArxFrom(2), seed: 1, turnCap: 60);

        Assert.Equal(AiGameEnding.Won, result.Ending);
        Assert.Equal("north", result.WinningNationId);
    }

    [Fact]
    public void The_shipped_presets_keep_their_advertised_conditions()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var classical = repository.ScenarioById("classical-mediterranean")!;
        var improved = repository.ScenarioById("example-classical-improved")!;

        Assert.Equal(VictoryConditionType.TotalConquest, classical.Victory.Type);
        Assert.Equal(repository.RulesetById(classical.RulesetId)!.Victory.DefaultCondition, classical.Victory.Type);
        Assert.Equal(VictoryConditionType.Domination, improved.Victory.Type);
        Assert.Equal(repository.RulesetById(improved.RulesetId)!.Victory.DefaultCondition, improved.Victory.Type);
    }
}
