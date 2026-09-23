using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// <c>docs/tasks/T20.md</c> Done-when 1: "A mid-game state after N turns (N &gt;= 20, all systems
/// registered) round-trips to an equal state hash."
/// </summary>
public sealed class SaveRoundTripTests
{
    private const int TurnCount = 23;

    [Fact]
    public void A_state_reached_by_playing_at_least_20_turns_round_trips_to_an_equal_state_hash()
    {
        var toy = PersistenceTestbed.Toy;
        var original = PersistenceTestbed.PlayTurns(TurnCount);

        // Proves the fixture is actually mid-game, not a scenario that stalled at turn 0 — a hash
        // equality test over two copies of the untouched initial state would pass for the wrong reason.
        Assert.True(original.Calendar.TurnIndex > 0);

        var save = new SaveGame(
            SchemaVersion: original.SchemaVersion,
            Id: "toy-3city-playthrough",
            Label: $"Toy playthrough, {TurnCount} turns",
            ScenarioId: original.ScenarioId,
            WorldId: original.WorldId,
            RulesetId: original.RulesetId,
            State: original);

        var text = SaveManager.Serialize(save);
        var reloaded = SaveManager.Load("toy-3city-playthrough.json", text, toy.World, toy.Ruleset);

        Assert.Equal(GameStateHash.Compute(original), GameStateHash.Compute(reloaded.State));

        // The hash is the contract, but a byte-for-byte check on top of it means a hash collision (or a
        // field the hash happens not to cover) still cannot hide a real divergence here.
        Assert.Equal(original, reloaded.State);
    }

    [Fact]
    public void A_save_records_its_world_and_ruleset_ids()
    {
        var toy = PersistenceTestbed.Toy;
        var state = PersistenceTestbed.PlayTurns(TurnCount);

        var save = new SaveGame(
            SchemaVersion: state.SchemaVersion,
            Id: "toy-3city-playthrough",
            Label: "Toy playthrough",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);

        Assert.Equal(toy.World.Id, save.WorldId);
        Assert.Equal(toy.Ruleset.Id, save.RulesetId);
    }
}
