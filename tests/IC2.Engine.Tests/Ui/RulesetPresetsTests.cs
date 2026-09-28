using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// <c>docs/tasks/T24.md</c>: the ruleset chooser's own Godot-free model
/// (<c>godot/UI/RulesetPresets.cs</c>, linked here — see this project's own <c>.csproj</c> remarks).
/// Checks the two claims the chooser screen and Done-when 4's headless check both depend on: the default
/// is Classical Faithful, and each card's own <c>ScenarioId</c> actually resolves, through the real
/// shipped data, to the ruleset the card names.
/// </summary>
public sealed class RulesetPresetsTests
{
    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    [Fact]
    public void The_default_preset_is_classical_faithful()
    {
        Assert.Equal(RulesetPreset.ClassicalFaithful, RulesetPresets.Default);
    }

    [Fact]
    public void Exactly_two_cards_are_offered_classical_faithful_first()
    {
        Assert.Equal(2, RulesetPresets.Cards.Count);
        Assert.Equal(RulesetPreset.ClassicalFaithful, RulesetPresets.Cards[0].Preset);
        Assert.Equal(RulesetPreset.Improved, RulesetPresets.Cards[1].Preset);
    }

    [Theory]
    [InlineData(RulesetPreset.ClassicalFaithful, "classical-faithful", "classical-mediterranean")]
    [InlineData(RulesetPreset.Improved, "improved", "example-classical-improved")]
    public void Each_cards_scenario_actually_resolves_to_the_ruleset_it_names(
        RulesetPreset preset, string expectedRulesetId, string expectedScenarioId)
    {
        var card = RulesetPresets.CardFor(preset);
        Assert.Equal(expectedRulesetId, card.RulesetId);
        Assert.Equal(expectedScenarioId, card.ScenarioId);

        var resolved = Repository.Resolve(card.ScenarioId);

        Assert.Equal(expectedRulesetId, resolved.Ruleset.Id);
        Assert.Equal(expectedScenarioId, resolved.Scenario.Id);
    }

    [Fact]
    public void Every_cards_summary_is_non_empty_and_has_five_lines()
    {
        // docs/tasks/T24.md: "diplomacy, victory condition, human/AI symmetry, bug reproduction, and ...
        // what happens to a defeated army" -- five named dimensions, one line each.
        foreach (var card in RulesetPresets.Cards)
        {
            Assert.Equal(5, card.Summary.Count);
            Assert.All(card.Summary, line => Assert.False(string.IsNullOrWhiteSpace(line)));
        }
    }

    [Fact]
    public void The_two_cards_disagree_on_what_happens_to_a_defeated_army()
    {
        // The one dimension docs/tasks/T24.md calls out as "the one visible in play most often" --
        // pinned here against the real ruleset data (combat.onDefeat), not just each card's own prose.
        var faithfulRuleset = Repository.Resolve(RulesetPresets.ClassicalFaithful.ScenarioId).Ruleset;
        var improvedRuleset = Repository.Resolve(RulesetPresets.Improved.ScenarioId).Ruleset;

        Assert.Equal(DefeatOutcome.Destroyed, faithfulRuleset.Flags.CombatOnDefeat);
        Assert.Equal(DefeatOutcome.Scatter, improvedRuleset.Flags.CombatOnDefeat);
    }
}
