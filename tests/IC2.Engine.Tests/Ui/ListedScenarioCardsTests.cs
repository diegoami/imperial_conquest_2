using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T200: New Game's cards are the two presets plus one per scenario marked <c>"listed": true</c>, and the command
/// table's Nations rows are the live world's nations.
/// </summary>
public sealed class ListedScenarioCardsTests
{
    private static readonly GameDataRepository Shipped = GameDataRepository.Load(ModelTestPaths.DataRoot);

    /// <summary>A temp data root: the shipped tiny-duel world, the toy ruleset, and the given scenarios marked listed.</summary>
    private static GameDataRepository WithListed(params string[] listedIds)
    {
        var root = Path.Combine(Path.GetTempPath(), "ic2-t200-" + Guid.NewGuid().ToString("N"));
        foreach (var dir in new[] { "worlds", "rulesets", "scenarios" })
        {
            Directory.CreateDirectory(Path.Combine(root, dir));
        }

        var shipped = ModelTestPaths.DataRoot;
        File.Copy(Path.Combine(shipped, "worlds", "example-tiny-duel.json"), Path.Combine(root, "worlds", "example-tiny-duel.json"));
        File.Copy(Path.Combine(shipped, "rulesets", "toy-ruleset.json"), Path.Combine(root, "rulesets", "toy-ruleset.json"));
        var scenario = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(shipped, "scenarios", "example-tiny-duel.json")))!.AsObject();
        scenario["listed"] = listedIds.Contains("example-tiny-duel");
        File.WriteAllText(Path.Combine(root, "scenarios", "example-tiny-duel.json"), scenario.ToJsonString());
        try
        {
            return GameDataRepository.Load(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_shipped_data_lists_the_two_presets_then_both_Italy_variants()
    {
        var cards = RulesetPresets.CardsFor(Shipped);

        Assert.Equal(RulesetPresets.Cards, cards.Take(2));
        Assert.Equal(new[] { "italy-500bc", "italy-500bc-barbarians", "near-east-1350bc", "near-east-750bc" }, cards.Skip(2).Select(c => c.ScenarioId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Contains("12 playable nations; barbarian arrivals by land and sea.", cards.Single(c => c.ScenarioId == "italy-500bc-barbarians").Summary);
        Assert.True(Shipped.ScenarioById("classical-mediterranean")!.Listed);
        Assert.True(Shipped.ScenarioById("example-classical-improved")!.Listed);
        Assert.False(Shipped.ScenarioById("toy-3city")!.Listed);
        Assert.False(Shipped.ScenarioById("example-tiny-duel")!.Listed);
    }

    [Fact]
    public void A_listed_scenario_gets_its_own_card_after_the_presets()
    {
        var cards = RulesetPresets.CardsFor(WithListed("example-tiny-duel"));

        Assert.Equal(3, cards.Count);
        Assert.Equal(RulesetPresets.ClassicalFaithful, cards[0]);
        Assert.Equal(RulesetPresets.Improved, cards[1]);
        var card = cards[2];
        Assert.Equal(RulesetPreset.Listed, card.Preset);
        Assert.Equal("example-tiny-duel", card.ScenarioId);
        Assert.Equal("toy-ruleset", card.RulesetId);
        Assert.Equal(Shipped.ScenarioById("example-tiny-duel")!.Name, card.DisplayName);
        Assert.Contains("Win by holding every city on the map.", card.Summary);
        Assert.Contains("2 nations; you choose yours on the next screen.", card.Summary);
    }

    [Fact]
    public void A_selection_holds_a_listed_card_and_resolves_its_scenario()
    {
        var repository = WithListed("example-tiny-duel");
        var card = RulesetPresets.CardsFor(repository)[2];
        var selection = new NewGameSelection { SelectedCard = card, HumanNationId = "north-realm" };

        Assert.Equal(RulesetPreset.Listed, selection.RulesetPreset);
        Assert.Equal("example-tiny-duel", GameSessionFactory.ResolveSelectedScenario(repository, selection).Scenario.Id);
        Assert.Equal("example-tiny-duel", GameSessionFactory.CreateSession(repository, selection).Scenario.Id);

        selection.RulesetPreset = RulesetPreset.Improved;
        Assert.Equal(RulesetPresets.Improved, selection.SelectedCard);
    }

    [Theory]
    [InlineData(VictoryConditionType.Domination, null, "Win by holding every city of every nation still at war with you.")]
    [InlineData(VictoryConditionType.ScoreAtTurnLimit, 80, "Win with the highest score when the turn limit is reached (80 turns).")]
    [InlineData(VictoryConditionType.Custom, null, "Win by meeting this scenario's own goal.")]
    public void The_goal_line_reads_the_scenarios_own_condition(VictoryConditionType type, int? turnLimit, string expected)
    {
        var scenario = Shipped.ScenarioById("toy-3city")! with { Victory = new VictoryCondition(type, "{}"), TurnLimit = turnLimit };

        Assert.Equal(expected, RulesetPresets.VictoryLineFor(scenario));
    }

    [Fact]
    public void The_command_table_takes_its_nation_rows_from_the_world()
    {
        var duel = Shipped.WorldById("example-tiny-duel")!;
        var classical = Shipped.WorldById("classical-mediterranean")!;

        var duelRows = new GameCommandTable(duel).MenuRows.Where(r => r.Menu == "Nations").Select(r => (r.Id, r.Caption)).ToArray();
        Assert.Equal(
            new[] { ("nations.north-realm", "Northern Realm"), ("nations.south-realm", "Southern Realm"), ("nations.all", "All nations") },
            duelRows);

        Assert.Equal(GameCommandTable.Rows, new GameCommandTable(classical).MenuRows);
        Assert.Same(GameCommandTable.Rows, new GameCommandTable().MenuRows);

        var table = new GameCommandTable(duel);
        table.Bind("nations.north-realm", () => { });
        Assert.Throws<ArgumentException>(() => table.Bind("nations.rome", () => { }));
    }
}
