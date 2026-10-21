using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Serialization;

/// <summary>
/// T199 (#106, #1028): the checks that need a scenario, its world and its ruleset together, run by
/// <see cref="GameDataRepository.Resolve"/>.
/// </summary>
public sealed class ResolvedScenarioValidationTests
{
    private static ResolvedScenario Toy => CoreTestbed.Toy;

    private static Scenario WithGoal(string goal) => Toy.Scenario with
    {
        Victory = new VictoryCondition(VictoryConditionType.Custom, goal),
    };

    [Fact]
    public void Every_shipped_scenario_still_resolves()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);

        foreach (var scenario in repository.Scenarios)
        {
            var resolved = repository.Resolve(scenario.Id);
            Assert.Equal(scenario.Id, resolved.Scenario.Id);
        }
    }

    [Fact]
    public void A_custom_goal_naming_an_unknown_nation_is_refused_naming_the_field()
    {
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.ValidateResolved(
            WithGoal("{\"nation\":\"atlantis\",\"holdCities\":[\"arx\"]}"), Toy.World, Toy.Ruleset));

        Assert.Contains("'victory.goal.nation'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("atlantis", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_goal_naming_an_unknown_city_is_refused_naming_the_field()
    {
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.ValidateResolved(
            WithGoal("{\"nation\":\"north\",\"holdCities\":[\"arx\",\"nowhere\"]}"), Toy.World, Toy.Ruleset));

        Assert.Contains("'victory.goal.holdCities'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("nowhere", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_custom_goal_is_refused_at_resolve_rather_than_at_the_first_round_end()
    {
        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.ValidateResolved(
            WithGoal("{\"nation\":\"north\",\"holdCities\":[]}"), Toy.World, Toy.Ruleset));

        Assert.Contains("'victory.goal'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_custom_goal_is_accepted()
    {
        GameDataValidation.ValidateResolved(
            WithGoal("{\"nation\":\"north\",\"holdCities\":[\"arx\",\"portus\"],\"turnAtOrAfter\":3}"), Toy.World, Toy.Ruleset);
    }

    [Fact]
    public void A_template_draw_count_above_the_worlds_table_is_refused_naming_the_field()
    {
        var classical = GameDataRepository.Load(TestPaths.DataRoot).WorldById("classical-mediterranean")!;
        var world = Toy.World with { MercenaryTemplates = ValueList.From(classical.MercenaryTemplates!.Take(3)) };
        var ruleset = Toy.Ruleset with
        {
            Recruitment = Toy.Ruleset.Recruitment with { MercenaryRestockTemplateDrawCount = 4 },
        };

        var ex = Assert.Throws<MalformedGameDataException>(
            () => GameDataValidation.ValidateResolved(Toy.Scenario, world, ruleset));

        Assert.Contains("'recruitment.mercenaryRestockTemplateDrawCount'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("3 mercenary templates", ex.Message, StringComparison.Ordinal);

        // At the bound it is fine, and a world with no table draws nothing.
        GameDataValidation.ValidateResolved(
            Toy.Scenario, world, ruleset with { Recruitment = ruleset.Recruitment with { MercenaryRestockTemplateDrawCount = 3 } });
        GameDataValidation.ValidateResolved(Toy.Scenario, Toy.World, ruleset);
    }
}
