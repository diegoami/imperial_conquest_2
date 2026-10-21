using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>#1289: the "How to play" page covers what a new player needs and states the rules correctly.</summary>
public sealed class HowToPlayTests
{
    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    [Fact]
    public void It_covers_the_goal_a_turn_moving_attacking_battles_supply_diplomacy_and_how_a_reign_ends()
    {
        var titles = HowToPlay.Sections(null).Select(s => s.Title).ToArray();
        Assert.Equal(
            new[] { "The goal", "A turn", "Moving", "Attacking and sieges", "Field battles", "Supply and money", "Diplomacy", "How a reign ends" },
            titles);
        Assert.All(HowToPlay.Sections(null), s => Assert.NotEmpty(s.Lines));
    }

    [Theory]
    [InlineData("classical-mediterranean", "hold every city on the map")]
    [InlineData("example-classical-improved", "every city of every nation still at war with you")]
    public void The_goal_states_the_games_own_victory_condition(string scenario, string expected)
    {
        var ruleset = Repository.Resolve(scenario).Ruleset;
        Assert.Contains(expected, HowToPlay.Sections(ruleset)[0].Lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_terrain_costs_it_quotes_are_the_rulesets()
    {
        var ruleset = Repository.Resolve("classical-mediterranean").Ruleset;
        int Cost(string id) => ruleset.Terrain.MoveCosts.Single(c => c.TileTypeId == id).MoveCost;
        var line = HowToPlay.Sections(ruleset).Single(s => s.Title == "Moving").Lines[1];

        Assert.Contains($"forest {Cost("forest")}", line, StringComparison.Ordinal);
        Assert.Contains($"mountains and rivers {Cost("mountains")}", line, StringComparison.Ordinal);
        Assert.Equal(Cost("mountains"), Cost("river_1"));
        Assert.Contains($"rough sea {Cost("sea_rough")}", line, StringComparison.Ordinal);
        Assert.Contains("everything else 1", line, StringComparison.Ordinal);
        Assert.All(new[] { "plain", "desert", "sea_calm" }, id => Assert.Equal(1, Cost(id)));
    }

    [Fact]
    public void Every_victory_condition_has_a_line() =>
        Assert.All(Enum.GetValues<VictoryConditionType>(), condition =>
            Assert.StartsWith("To win", HowToPlay.VictoryLine(Repository.Resolve("classical-mediterranean").Ruleset with
            {
                Victory = Repository.Resolve("classical-mediterranean").Ruleset.Victory with { DefaultCondition = condition },
            }), StringComparison.Ordinal));
}
