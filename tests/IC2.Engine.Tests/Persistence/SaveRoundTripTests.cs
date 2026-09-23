using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// <c>docs/tasks/T20.md</c> Done-when 1: "A mid-game state after N turns (N &gt;= 20, all systems
/// registered) round-trips to an equal state hash."
/// </summary>
/// <remarks>
/// "N turns" means <see cref="CalendarState.TurnIndex"/> — a full calendar turn, documented as "how many
/// turn cycles have elapsed" and advanced once per round by <c>CalendarSystem</c> — not a call to
/// <see cref="TurnCoordinator.RunTurn"/>, which runs one <em>seat's</em> turn. Review round 1 (B1) found
/// the original version of this test played 23 seat-turns (calendar turn 11 on this two-seat toy
/// scenario) and only guarded <c>TurnIndex &gt; 0</c>, so it round-tripped a state Done-when 1 does not
/// describe without failing. <see cref="PersistenceTestbed.PlayUntilTurnIndex"/> plays until the calendar
/// turn itself reaches the target.
/// </remarks>
public sealed class SaveRoundTripTests
{
    private const int MinTurnIndex = 20;

    [Fact]
    public void A_state_reached_by_playing_at_least_20_calendar_turns_round_trips_to_an_equal_state_hash()
    {
        var toy = PersistenceTestbed.Toy;
        var original = PersistenceTestbed.PlayUntilTurnIndex(MinTurnIndex);

        // The guarantee Done-when 1 actually asks for: not just "the game moved", but "at least N
        // calendar turns", checked on the field the design and the engine both call a turn.
        Assert.True(
            original.Calendar.TurnIndex >= MinTurnIndex,
            $"expected calendar turn >= {MinTurnIndex}, got {original.Calendar.TurnIndex}");

        var save = new SaveGame(
            SchemaVersion: original.SchemaVersion,
            Id: "toy-3city-playthrough",
            Label: $"Toy playthrough, calendar turn {original.Calendar.TurnIndex}",
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
    public void A_state_with_a_siege_an_embarked_army_a_pending_offer_and_a_mercenary_pool_round_trips()
    {
        // Review round 1 (N1): no played run of the toy scenario reaches a siege, an embarked/carried
        // army, a pending diplomatic offer, a fleet under construction or an occupied mercenary pool
        // within a reasonable seat-turn budget, so the played round trip above never exercises the
        // cross-referencing fields (CarriedArmyId/AboardFleetId/BuildCityId) that build-process.md §4.2's
        // "a delete that leaves something behind" class is about. ToyFixtures.NonTrivialState() (T02)
        // already builds exactly that state; round-tripping it here closes the gap without inventing a
        // second state-construction path.
        var toy = PersistenceTestbed.Toy;
        var original = ToyFixtures.NonTrivialState();

        var save = new SaveGame(
            SchemaVersion: original.SchemaVersion,
            Id: "toy-3city-nontrivial",
            Label: "Toy non-trivial state (siege, embark, offer, mercenaries)",
            ScenarioId: original.ScenarioId,
            WorldId: original.WorldId,
            RulesetId: original.RulesetId,
            State: original);

        var text = SaveManager.Serialize(save);
        var reloaded = SaveManager.Load("toy-3city-nontrivial.json", text, toy.World, toy.Ruleset);

        Assert.Equal(GameStateHash.Compute(original), GameStateHash.Compute(reloaded.State));
        Assert.Equal(original, reloaded.State);

        // Review round 2 (R3): a "continue N more seat-turns from the original and from the reloaded
        // state, then compare" check used to follow here. The engine is a pure function of GameState --
        // TurnCoordinator.RunTurn takes a state and returns one, holding no game state itself
        // (TurnCoordinator's own class remarks; round 3, S4, removed this comment's earlier, incorrect
        // attribution of that framing to docs/build-process.md) -- so once
        // Assert.Equal(original, reloaded.State) above holds, running the same deterministic steps from
        // two equal states cannot produce anything but equal results -- the continuation could not catch
        // a divergence the record-equality assert above had not already caught.
        // Actual continuation-after-load coverage (a different concern: whether play started fresh from
        // a save diverges from play that was never saved at all) is what the reviewer's own round-1
        // probes established over hundreds of seat-turns across multiple scenarios; see the PR body's
        // "RNG decision" section.
    }
}
