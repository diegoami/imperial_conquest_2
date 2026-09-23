using IC2.Engine.Model;
using IC2.Engine.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// <c>docs/tasks/T20.md</c> Done-when 4: a save records which World and Ruleset it started from, and
/// reloading with a different ruleset id — or world id, per <c>game-design.md</c>
/// §"Original-save compatibility"'s "a different Ruleset <em>or World</em>" — is rejected with a clear
/// message, never silently reinterpreted.
/// </summary>
public sealed class SaveContextGuardTests
{
    private static SaveGame BuildToySave()
    {
        var toy = PersistenceTestbed.Toy;
        var state = PersistenceTestbed.PlayTurns(5);
        return new SaveGame(
            SchemaVersion: state.SchemaVersion,
            Id: "toy-3city-guard",
            Label: "Toy guard fixture",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);
    }

    [Fact]
    public void Loading_under_a_different_ruleset_id_is_rejected_with_a_clear_message()
    {
        var toy = PersistenceTestbed.Toy;
        var text = SaveManager.Serialize(BuildToySave());
        var otherRuleset = toy.Ruleset with { Id = "some-other-ruleset" };

        var ex = Assert.Throws<SaveContextMismatchException>(
            () => SaveManager.Load("save.json", text, toy.World, otherRuleset));

        Assert.Equal("ruleset", ex.Kind);
        Assert.Equal("some-other-ruleset", ex.ExpectedId);
        Assert.Equal(toy.Ruleset.Id, ex.FoundId);
        Assert.Contains(toy.Ruleset.Id, ex.Message, StringComparison.Ordinal);
        Assert.Contains("some-other-ruleset", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_under_a_different_world_id_is_rejected_with_a_clear_message()
    {
        var toy = PersistenceTestbed.Toy;
        var text = SaveManager.Serialize(BuildToySave());
        var otherWorld = toy.World with { Id = "some-other-world" };

        var ex = Assert.Throws<SaveContextMismatchException>(
            () => SaveManager.Load("save.json", text, otherWorld, toy.Ruleset));

        Assert.Equal("world", ex.Kind);
        Assert.Equal("some-other-world", ex.ExpectedId);
        Assert.Equal(toy.World.Id, ex.FoundId);
    }

    [Fact]
    public void Loading_under_the_matching_world_and_ruleset_succeeds()
    {
        var toy = PersistenceTestbed.Toy;
        var text = SaveManager.Serialize(BuildToySave());

        var reloaded = SaveManager.Load("save.json", text, toy.World, toy.Ruleset);

        Assert.Equal(toy.World.Id, reloaded.WorldId);
        Assert.Equal(toy.Ruleset.Id, reloaded.RulesetId);
    }
}
