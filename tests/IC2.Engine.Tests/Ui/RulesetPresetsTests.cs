using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Victory;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// <c>docs/tasks/T24.md</c>: the ruleset chooser's own Godot-free model
/// (<c>godot/UI/RulesetPresets.cs</c>, linked here — see this project's own <c>.csproj</c> remarks).
/// Checks the two claims the chooser screen and Done-when 4's headless check both depend on (the default
/// is Classical Faithful, and each card's own <c>ScenarioId</c> actually resolves, through the real
/// shipped data, to the ruleset the card names), and — rework round 1, PR #466 review, blocking finding
/// 2 — pins every remaining claim in <see cref="RulesetPresets.ClassicalFaithful"/>/
/// <see cref="RulesetPresets.Improved"/>'s own <c>Summary</c> against the loaded <see cref="Ruleset"/>
/// and, where the review found the field alone was not enough proof (the victory line), against the
/// actual live evaluator the turn pipeline calls.
/// </summary>
public sealed class RulesetPresetsTests
{
    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    private static Ruleset FaithfulRuleset => Repository.Resolve(RulesetPresets.ClassicalFaithful.ScenarioId).Ruleset;

    private static Ruleset ImprovedRuleset => Repository.Resolve(RulesetPresets.Improved.ScenarioId).Ruleset;

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

    // ---- Diplomacy (Summary[0]) ----

    [Fact]
    public void Diplomacy_model_flag_values_match_what_each_card_states()
    {
        Assert.Equal(DiplomacyModel.ConfirmedStateMachine, FaithfulRuleset.Flags.DiplomacyModel);
        Assert.Equal(DiplomacyModel.ConfirmedStateMachineWithOpinionScore, ImprovedRuleset.Flags.DiplomacyModel);
    }

    [Theory]
    [InlineData(RulesetPreset.ClassicalFaithful)]
    [InlineData(RulesetPreset.Improved)]
    public void Neither_cards_diplomacy_line_claims_a_live_behavioural_difference(RulesetPreset preset)
    {
        // Rework round 1, PR #466 review, blocking finding 2: flags.diplomacyModel is declared on
        // RulesetFlags but read by no consumer anywhere in src/IC2.Engine (confirmed by inspection: an
        // opinion-score layer is not implemented). This test cannot grep the engine itself, but it does
        // pin the one claim the corrected card text is allowed to make: that today, both rulesets behave
        // the same on this dimension. A future PR that actually wires an opinion-score system in should
        // fail this test the moment it rewords either line back to claiming a live difference without
        // updating this assertion to match.
        var line = RulesetPresets.CardFor(preset).Summary[0];
        Assert.Contains("diplomacyModel", line, StringComparison.Ordinal);
        Assert.DoesNotContain("your choice", line, StringComparison.Ordinal);
    }

    // ---- Victory (Summary[1]) ----

    [Fact]
    public void Victory_default_condition_values_match_what_each_card_states()
    {
        Assert.Equal(VictoryConditionType.TotalConquest, FaithfulRuleset.Victory.DefaultCondition);
        Assert.Equal(VictoryConditionType.Domination, ImprovedRuleset.Victory.DefaultCondition);
    }

    [Fact]
    public void Faithful_total_conquest_requires_every_city_as_the_card_states()
    {
        Assert.True(FaithfulRuleset.Victory.TotalConquestRequiresEveryCity);
    }

    [Fact]
    public void Improved_card_no_longer_claims_a_turn_limit_choice_that_does_not_exist()
    {
        // Blocking finding 2's own text: "your choice" is false, and the 150-turn score limit is dead
        // for the live pipeline's own fixed 'domination' default.
        var line = RulesetPresets.Improved.Summary[1];
        Assert.DoesNotContain("your choice", line, StringComparison.Ordinal);
        Assert.Contains("domination", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Improved_rulesets_own_turn_limit_is_recorded_but_dead_for_the_live_pipelines_default_condition()
    {
        // Direct behavioural proof, not just a reading of the code: calls VictoryEvaluator.Evaluate the
        // exact way VictoryCheckSystem.Execute calls it in the live turn pipeline (scenario: null) and
        // confirms the outcome's own ConditionType is Domination -- never ScoreAtTurnLimit -- even though
        // DefaultTurnLimit is a real, non-null 150. VictoryEvaluator.Evaluate's own switch is keyed
        // solely off victory.Type, and (scenario is null) sets
        // victory = new VictoryCondition(ruleset.Victory.DefaultCondition) -- so DefaultCondition alone
        // decides which branch runs, structurally, not merely by today's data values.
        var resolved = Repository.Resolve(RulesetPresets.Improved.ScenarioId);
        Assert.NotNull(resolved.Ruleset.Victory.DefaultTurnLimit);
        Assert.Equal(150, resolved.Ruleset.Victory.DefaultTurnLimit);

        var state = GameStateFactory.CreateInitial(resolved.World, resolved.Ruleset, resolved.Scenario);

        var outcome = VictoryEvaluator.Evaluate(state, resolved.Ruleset, scenario: null);

        Assert.Equal(VictoryConditionType.Domination, outcome.ConditionType);
        Assert.NotEqual(VictoryConditionType.ScoreAtTurnLimit, outcome.ConditionType);
    }

    // ---- Human vs AI (Summary[2]) ----

    [Fact]
    public void Seat_asymmetry_flag_values_match_what_each_card_states()
    {
        Assert.Equal(SeatAsymmetryModel.Faithful, FaithfulRuleset.Flags.SeatAsymmetry);
        Assert.Equal(SeatAsymmetryModel.Normalized, ImprovedRuleset.Flags.SeatAsymmetry);
    }

    // ---- Bugs (Summary[3]) ----

    [Fact]
    public void Bug_reproduction_flag_values_match_what_each_card_states()
    {
        Assert.True(FaithfulRuleset.Flags.FaithfulThawColumnBug);
        Assert.Equal(SiegeRatioClampPolicy.Reproduce16BitClamp, FaithfulRuleset.Flags.BugPolicySiegeRatioClamp);

        Assert.False(ImprovedRuleset.Flags.FaithfulThawColumnBug);
        Assert.Equal(SiegeRatioClampPolicy.Clamp32Bit, ImprovedRuleset.Flags.BugPolicySiegeRatioClamp);
    }

    // ---- Defeated-army fate (Summary[4]) ----

    [Fact]
    public void The_two_cards_disagree_on_what_happens_to_a_defeated_army()
    {
        // The one dimension docs/tasks/T24.md calls out as "the one visible in play most often" --
        // pinned here against the real ruleset data (flags.combatOnDefeat), not just each card's own prose.
        Assert.Equal(DefeatOutcome.Destroyed, FaithfulRuleset.Flags.CombatOnDefeat);
        Assert.Equal(DefeatOutcome.Scatter, ImprovedRuleset.Flags.CombatOnDefeat);
    }

    [Theory]
    [InlineData(RulesetPreset.ClassicalFaithful)]
    [InlineData(RulesetPreset.Improved)]
    public void Every_cards_path_labels_name_the_real_flags_object_not_a_nonexistent_combat_object(RulesetPreset preset)
    {
        // Non-blocking finding 3: the field lives at flags.combatOnDefeat, never a top-level
        // combat.onDefeat -- confirmed: FaithfulRuleset/ImprovedRuleset above are only reachable through
        // Ruleset.Flags.CombatOnDefeat, and jq '.combat' against either shipped ruleset file returns null.
        var defeatLine = RulesetPresets.CardFor(preset).Summary[4];
        Assert.Contains("flags.combatOnDefeat", defeatLine, StringComparison.Ordinal);
        Assert.DoesNotContain("combat.onDefeat:", defeatLine, StringComparison.Ordinal);
    }
}
