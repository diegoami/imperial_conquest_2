using IC2.Engine.Model;
using IC2.Engine.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// <see cref="SaveManager.PeekSummary"/>: the reason <c>docs/tasks/T20.md</c>'s envelope carries a
/// <c>turnIndex</c> field at all (<see cref="SaveFormat.CurrentVersion"/>'s remarks) — a save picker can
/// read a save's listing metadata without deserializing or validating its whole <see cref="GameState"/>.
/// </summary>
public sealed class SaveSummaryTests
{
    [Fact]
    public void Peeking_a_current_format_save_reads_its_metadata_without_a_world_or_ruleset_on_hand()
    {
        var toy = PersistenceTestbed.Toy;
        var state = PersistenceTestbed.PlayTurns(21);
        var save = new SaveGame(
            SchemaVersion: state.SchemaVersion,
            Id: "toy-3city-summary",
            Label: "Toy summary fixture",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);

        var text = SaveManager.Serialize(save);
        var summary = SaveManager.PeekSummary("save.json", text);

        Assert.Equal("toy-3city-summary", summary.Id);
        Assert.Equal("Toy summary fixture", summary.Label);
        Assert.Equal(toy.Scenario.Id, summary.ScenarioId);
        Assert.Equal(toy.World.Id, summary.WorldId);
        Assert.Equal(toy.Ruleset.Id, summary.RulesetId);
        Assert.Equal(state.Calendar.TurnIndex, summary.TurnIndex);
    }

    [Fact]
    public void Peeking_a_save_whose_nested_state_would_fail_full_validation_still_reads_the_summary()
    {
        // A save with a dangling reference (an army owned by a nation that does not exist) fails
        // GameDataValidation -- proving Load would reject it, and that PeekSummary genuinely never
        // reaches that check, not merely that it happens not to trip it on a well-formed fixture.
        var toy = PersistenceTestbed.Toy;
        var state = PersistenceTestbed.PlayTurns(20);
        var broken = state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a with { Nation = "no-such-nation" })),
        };
        var save = new SaveGame(
            SchemaVersion: broken.SchemaVersion,
            Id: "toy-3city-broken",
            Label: "Toy broken fixture",
            ScenarioId: broken.ScenarioId,
            WorldId: broken.WorldId,
            RulesetId: broken.RulesetId,
            State: broken);

        var text = SaveManager.Serialize(save);

        Assert.Throws<IC2.Engine.Serialization.UnresolvedReferenceException>(
            () => SaveManager.Load("broken-save.json", text, toy.World, toy.Ruleset));

        var summary = SaveManager.PeekSummary("broken-save.json", text);
        Assert.Equal("toy-3city-broken", summary.Id);
        Assert.Equal(state.Calendar.TurnIndex, summary.TurnIndex);
    }
}
