using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// T75 Done-when 1 and 4: a world with no starting data still opens at uniform peace with an empty
/// log, and the shipped classical world opens with the DAT's own relations and 27-line news seed --
/// which then actually drives a round's own news header and the attack-legality gate.
/// </summary>
public class GameStateFactoryStartingDataTests
{
    private static ResolvedScenario Toy() => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("toy-3city");

    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    /// <summary>
    /// Done-when 1: the shipped toy world carries neither field, and the state it opens is exactly the
    /// uniform-peace, empty-log shape <see cref="GameStateFactory"/> always produced before this task --
    /// the existing suite built on the toy world is this claim's own proof (it is unmodified), and this
    /// test states it directly for the one field this task actually touches.
    /// </summary>
    [Fact]
    public void A_world_with_no_starting_data_opens_at_uniform_peace_with_an_empty_log()
    {
        var toy = Toy();
        Assert.Null(toy.World.StartingRelations);
        Assert.Null(toy.World.StartingNews);

        var state = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);

        Assert.Equal(NewsLog.Empty, state.NewsLog);
        var peace = toy.Ruleset.Diplomacy.StateCodes.Peace;
        foreach (var row in state.Relations.Matrix)
        {
            foreach (var value in row)
            {
                Assert.Equal(peace, value);
            }
        }
    }

    /// <summary>
    /// Done-when 4: the classical world's DAT relations and 27-line log reach <see cref="GameState"/>
    /// unchanged -- Rome-Gaul at war and Rome-Macedonia trading (the same pair
    /// <c>ExportedWorldTests</c> checks on the committed file), and slots 0 and 26 by their exact text.
    /// </summary>
    [Fact]
    public void The_classical_world_opens_with_the_DATs_relations_and_news_log()
    {
        var classical = Classical();
        var state = GameStateFactory.CreateInitial(classical.World, classical.Ruleset, classical.Scenario);

        Assert.Equal(27, state.NewsLog.Slots.Count);
        Assert.Equal(26, state.NewsLog.MostRecentSlot);
        Assert.Equal("272 BC", state.NewsLog.Slots[0].Text);
        Assert.Equal("Week 1      Spring      270 BC", state.NewsLog.Slots[26].Text);

        Assert.Equal(classical.Ruleset.Diplomacy.StateCodes.War, state.Relations.Get("rome", "gaul"));
        Assert.Equal(classical.Ruleset.Diplomacy.StateCodes.Trade, state.Relations.Get("rome", "macedonia"));
    }

    /// <summary>
    /// Done-when 4: ending one full round appends the round's own blank-line-and-header pair after
    /// slot 26 without duplicating it -- slot 26's own text is untouched and appears exactly once, and
    /// the new header differs from it (a later week, rendered in the engine's own format, not the DAT's
    /// single-spaced one -- T75 Hazards).
    /// </summary>
    /// <remarks>
    /// Uses <see cref="TurnCoordinator.RunRoundTick"/> directly, the round-scoped phases on their own,
    /// rather than 16 <see cref="TurnCoordinator.RunTurn"/> calls: the classical world's 16 AI seats
    /// generate real diplomacy that round (measured: 13 alliance lines from one seeded run), which would
    /// overflow the 40-slot ring buffer and evict slot 26 itself before the round even ends -- a fact
    /// about this seed's AI, not about whether the round-end write correctly follows what was already in
    /// the log, which is what this test checks. <see cref="TurnCoordinator"/>'s own remarks document
    /// <c>RunRoundTick</c> as exactly "the original's global weekly tick", callable on its own.
    /// </remarks>
    [Fact]
    public void Ending_one_full_round_appends_a_header_after_slot_26_without_duplicating_it()
    {
        var classical = Classical();
        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, classical.Ruleset, classical.World, NullEventSink.Instance);
        var coordinator = new TurnCoordinator(
            registry, classical.Ruleset, classical.World, NullEventSink.Instance, dispatcher);

        var state = GameStateFactory.CreateInitial(classical.World, classical.Ruleset, classical.Scenario);
        var originalSlot26 = state.NewsLog.Slots[26].Text;

        var result = coordinator.RunRoundTick(state);
        state = result.State;

        Assert.True(result.RoundTickRan);
        Assert.Equal(29, state.NewsLog.Slots.Count);
        Assert.Equal(28, state.NewsLog.MostRecentSlot);

        // Slot 26 itself: untouched, and appears exactly once -- the round-end write followed it
        // rather than overwriting or duplicating it.
        Assert.Equal(originalSlot26, state.NewsLog.Slots[26].Text);
        Assert.Equal(1, state.NewsLog.Slots.Count(s => s.Text == originalSlot26));

        // The mandatory blank-line-and-header pair (news-log-format-and-messages.md Q3) follows it
        // directly, at 27 and 28, and the header is a genuinely new entry, not slot 26 repeated.
        Assert.Equal(" ", state.NewsLog.Slots[27].Text);
        var newHeader = state.NewsLog.Slots[28].Text;
        Assert.NotEqual(originalSlot26, newHeader);
        Assert.StartsWith("Week", newHeader, StringComparison.Ordinal);
    }

    /// <summary>
    /// Done-when 4: a war in the starting matrix is a war to the attack-legality check -- not merely a
    /// value <see cref="DiplomaticRelations.Get"/> happens to return, but the exact state
    /// <see cref="AttackLegality.Check(GameState, Ruleset, AttackArmyCommand)"/> reads before refusing an
    /// attack as <see cref="AttackArmyRejections.NotAtWar"/>.
    /// </summary>
    [Fact]
    public void A_war_in_the_starting_matrix_is_a_war_to_the_attack_legality_check()
    {
        var classical = Classical();
        var ruleset = classical.Ruleset;
        var state = GameStateFactory.CreateInitial(classical.World, ruleset, classical.Scenario);

        Assert.Equal(ruleset.Diplomacy.StateCodes.War, state.Relations.Get("rome", "gaul"));

        // Two synthetic, adjacent armies standing in for Rome and Gaul: the classical world's own DAT
        // starting armies are not guaranteed to be adjacent to each other, and this test's only interest
        // is whether the war state GameStateFactory wired in actually gates AttackLegality, not geography.
        var romeArmy = new ArmyState(
            Id: "test-rome-army", Nation: "rome", X: 10, Y: 10, Moves: 1, Morale: 50, Money: 0, SupplyTons: 0,
            CoveredTileCode: 2, AboardFleetId: null, Units: ValueList<UnitSlot>.Empty);
        var gaulArmy = new ArmyState(
            Id: "test-gaul-army", Nation: "gaul", X: 11, Y: 10, Moves: 1, Morale: 50, Money: 0, SupplyTons: 0,
            CoveredTileCode: 2, AboardFleetId: null, Units: ValueList<UnitSlot>.Empty);
        var warState = state with { Armies = ValueList.From(state.Armies.Append(romeArmy).Append(gaulArmy)) };

        var command = new AttackArmyCommand("rome", "test-rome-army", "test-gaul-army");
        var warRejection = AttackLegality.Check(warState, ruleset, command);

        Assert.True(
            warRejection is null || warRejection.Code != AttackArmyRejections.NotAtWar,
            $"Expected no NotAtWar refusal while at war; got: {warRejection?.Code.Value} - {warRejection?.Message}");

        // The mirror: peace between the same two nations is refused specifically as NotAtWar, proving
        // the gate actually reads state.Relations rather than always accepting.
        var peaceState = warState with
        {
            Relations = warState.Relations.WithRelation("rome", "gaul", ruleset.Diplomacy.StateCodes.Peace),
        };
        var peaceRejection = AttackLegality.Check(peaceState, ruleset, command);
        Assert.Equal(AttackArmyRejections.NotAtWar, peaceRejection?.Code);
    }
}
