using IC2.Engine.Ai;
using IC2.Engine.Armies;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Tests.Battle.Commands;
using IC2.Engine.Tests.Core;
using Xunit;
using Xunit.Abstractions;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// T240 (#1601): under <c>flags.armyTableCompaction = swapRemoveSweep</c> a removed army leaves a dead slot until the
/// original's sweep (<c>FUN_0044adb0</c>/<c>FUN_0044abe0</c>), which runs when a human ends the turn and before each
/// AI seat. Under <c>inOrder</c> nothing changes. The research's worked cases
/// (2026-10-30-army-table-order-after-deaths117.md, research 150d95d) are driven through the real dispatcher and
/// coordinator.
/// </summary>
public sealed class ArmyTableTests
{
    private readonly ITestOutputHelper _output;

    public ArmyTableTests(ITestOutputHelper output) => _output = output;

    /// <summary>The toy ruleset with the original's army table.</summary>
    private static Ruleset Toy => CoreTestbed.Toy.Ruleset;

    private static Ruleset Swept => Toy with
    {
        Flags = Toy.Flags with { ArmyTableCompaction = ArmyTableCompactionPolicy.SwapRemoveSweep },
    };

    /// <summary>
    /// <paramref name="count"/> armies of the active human seat, <c>a00</c>…, all beside its city <c>arx</c> so any
    /// of them may disband.
    /// </summary>
    private static GameState Table(int count)
    {
        var state = HumanNorthFirst(InitialState());
        var arx = state.CityById("arx")!;
        return state with
        {
            Armies = ValueList.From(Enumerable.Range(0, count)
                .Select(i => Army(Id(i), NorthNationId, arx.X + 1, arx.Y, new[] { RegularUnit($"u{i}") }))),
        };
    }

    private static GameState HumanNorthFirst(GameState state) => state with
    {
        TurnOrder = ValueList.Of(NorthNationId, SouthNationId),
        ActiveSeatIndex = 0,
        Nations = ValueList.From(state.Nations.Select(n => n with
        {
            Control = n.Id == NorthNationId ? SeatControl.Human : SeatControl.Ai,
        })),
    };

    private static string Id(int i) => $"a{i:00}";

    private static string[] Ids(GameState state) => state.Armies.Select(a => a.Id).ToArray();

    private static string[] Ids(params int[] indices) => indices.Select(Id).ToArray();

    private static GameState Disband(GameState state, int index, Ruleset? rules = null)
    {
        var result = DispatcherWithRuleset(rules ?? Swept).Dispatch(state, new DisbandArmyCommand(NorthNationId, Id(index)));
        Assert.True(result.IsAccepted, result.ToString());
        return result.State;
    }

    [Fact]
    public void A_disbanded_army_leaves_a_dead_slot_and_the_survivors_keep_their_order_until_the_sweep()
    {
        var state = Disband(Table(14), 9);

        Assert.Equal(ValueList.Of(9), state.DeadArmySlots);
        Assert.Equal(Ids(0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13), Ids(state));
    }

    [Fact]
    public void Deaths_at_slots_9_and_10_of_14_in_successive_sweeps_give_8_13_12_11()
    {
        // The research's worked case: record 9 dies and is swept (13 moves into slot 9), then record 10 dies and is
        // swept (12, now last, moves into slot 10).
        var state = ArmyTable.Sweep(Disband(Table(14), 9));
        Assert.Null(state.DeadArmySlots);
        state = ArmyTable.Sweep(Disband(state, 10));

        Assert.Equal(Ids(0, 1, 2, 3, 4, 5, 6, 7, 8, 13, 12, 11), Ids(state));
        Assert.Null(state.DeadArmySlots);
    }

    [Fact]
    public void One_sweep_over_dead_slots_9_and_10_works_from_the_last_slot_down()
    {
        // FUN_0044adb0 meets slot 10 first: 13 moves into it; then slot 9 takes 12, now the last record.
        var state = Disband(Disband(Table(14), 9), 10);
        Assert.Equal(ValueList.Of(9, 10), state.DeadArmySlots);

        Assert.Equal(Ids(0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 13, 11), Ids(ArmyTable.Sweep(state)));
    }

    [Fact]
    public void A_death_at_slot_0_brings_the_last_army_to_the_front()
    {
        var swept = ArmyTable.Sweep(Disband(Table(5), 0));

        Assert.Equal(Ids(4, 1, 2, 3), Ids(swept));
    }

    [Fact]
    public void An_army_created_between_a_death_and_the_sweep_moves_into_the_dead_slot()
    {
        var state = Table(4) with { };
        var parent = Army(Id(0), NorthNationId, 3, 3, new[] { RegularUnit("kept"), RegularUnit("moved") });
        state = state with { Armies = ValueList.From(state.Armies.Select(a => a.Id == parent.Id ? parent : a)) };

        state = Disband(state, 1);
        var split = DispatcherWithRuleset(Swept).Dispatch(
            state, new SplitArmyCommand(NorthNationId, Id(0), "new", ValueList.Of(1)));
        Assert.True(split.IsAccepted, split.ToString());

        // The new army is appended after every slot, the dead one included...
        Assert.Equal(new[] { Id(0), Id(2), Id(3), "new" }, Ids(split.State));
        Assert.Equal(ValueList.Of(1), split.State.DeadArmySlots);

        // ...and, being last, the sweep moves it into the dead slot.
        Assert.Equal(new[] { Id(0), "new", Id(2), Id(3) }, Ids(ArmyTable.Sweep(split.State)));
    }

    [Fact]
    public void A_death_in_a_human_turn_is_swept_when_the_human_ends_the_turn_before_the_ai_seat_acts()
    {
        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, Swept, CoreTestbed.Toy.World, NullEventSink.Instance);
        var coordinator = new TurnCoordinator(registry, Swept, CoreTestbed.Toy.World, NullEventSink.Instance, dispatcher);

        var state = Table(5);
        var died = dispatcher.Dispatch(state, new DisbandArmyCommand(NorthNationId, Id(1)));
        Assert.True(died.IsAccepted, died.ToString());
        Assert.Equal(Ids(0, 2, 3, 4), Ids(died.State));
        Assert.Equal(ValueList.Of(1), died.State.DeadArmySlots);

        // The human's End turn: the seat's pipeline run starts with the sweep.
        var ended = coordinator.RunTurn(died.State);

        Assert.Equal(Ids(0, 4, 2, 3), Ids(ended.State));
        Assert.Null(ended.State.DeadArmySlots);
        Assert.Equal(SouthNationId, ended.State.ActiveNationId);
        var trace = ended.Trace.Select(t => t.SystemId).ToList();
        Assert.Equal("armies.table-sweep", trace[0]);

        // The AI seat's own run sweeps again before its orders.
        var aiTurn = coordinator.RunTurn(ended.State);
        var aiTrace = aiTurn.Trace.Select(t => t.SystemId).ToList();
        Assert.True(aiTrace.IndexOf("armies.table-sweep") < aiTrace.IndexOf("ai.turn"));
    }

    [Fact]
    public void A_dead_slot_left_before_an_ai_seat_is_swept_before_that_seat_acts()
    {
        var registry = SystemRegistry.FromEngineAssembly();
        var seen = new List<string[]>();
        var dispatcher = new SpyDispatch(new CommandDispatcher(registry, Swept, CoreTestbed.Toy.World, NullEventSink.Instance), seen);
        var coordinator = new TurnCoordinator(registry, Swept, CoreTestbed.Toy.World, NullEventSink.Instance, dispatcher);

        // The AI seat (south) is active with north's dead slot 1 still in the table, left by north's own turn.
        var state = Disband(Table(5), 1) with { ActiveSeatIndex = 1 };
        Assert.Equal(ValueList.Of(1), state.DeadArmySlots);

        var result = coordinator.RunTurn(state);

        // Every order the AI placed saw the swept table.
        Assert.NotEmpty(seen);
        Assert.All(seen, ids => Assert.Equal(Ids(0, 4, 2, 3), ids));
        Assert.Null(result.State.DeadArmySlots);
    }

    [Fact]
    public void Dead_slots_survive_a_save_and_load_and_give_the_same_sweep()
    {
        var state = Disband(Disband(Table(6), 4), 1);
        var save = new SaveGame(state.SchemaVersion, "dead-slots", "dead-slots", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var json = SaveManager.Serialize(save);
        Assert.Contains("\"deadArmySlots\"", json, StringComparison.Ordinal);

        var loaded = SaveManager.Load("dead-slots.json", json, CoreTestbed.Toy.World, Swept).State;

        Assert.Equal(state, loaded);
        Assert.Equal(ValueList.Of(1, 4), loaded.DeadArmySlots);
        Assert.Equal(Ids(ArmyTable.Sweep(state)), Ids(ArmyTable.Sweep(loaded)));
        Assert.Equal(Ids(0, 5, 2, 3), Ids(ArmyTable.Sweep(loaded)));
    }

    [Fact]
    public void A_state_without_dead_slots_saves_without_the_field_and_an_older_save_loads_with_none()
    {
        var state = Table(3);
        var json = SaveManager.Serialize(
            new SaveGame(state.SchemaVersion, "none", "none", state.ScenarioId, state.WorldId, state.RulesetId, state));

        Assert.DoesNotContain("deadArmySlots", json, StringComparison.Ordinal);
        Assert.Null(SaveManager.Load("none.json", json, CoreTestbed.Toy.World, Swept).State.DeadArmySlots);
    }

    [Fact]
    public void In_order_rulesets_close_the_gap_at_once_and_keep_the_survivors_in_order()
    {
        Assert.Equal(ArmyTableCompactionPolicy.InOrder, Toy.Flags.ArmyTableCompaction);
        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, Toy, CoreTestbed.Toy.World, NullEventSink.Instance);
        var coordinator = new TurnCoordinator(registry, Toy, CoreTestbed.Toy.World, NullEventSink.Instance, dispatcher);

        var died = dispatcher.Dispatch(Table(5), new DisbandArmyCommand(NorthNationId, Id(1)));
        Assert.True(died.IsAccepted, died.ToString());
        Assert.Null(died.State.DeadArmySlots);

        var ended = coordinator.RunTurn(died.State);
        Assert.Equal(Ids(0, 2, 3, 4), Ids(ended.State));
        Assert.Null(ended.State.DeadArmySlots);
    }

    [Theory]
    [InlineData("classical-faithful", ArmyTableCompactionPolicy.SwapRemoveSweep)]
    [InlineData("improved", ArmyTableCompactionPolicy.InOrder)]
    [InlineData("italy-500bc", ArmyTableCompactionPolicy.InOrder)]
    [InlineData("toy-ruleset", ArmyTableCompactionPolicy.InOrder)]
    public void The_shipped_rulesets_carry_the_owners_choice(string rulesetId, ArmyTableCompactionPolicy expected)
    {
        var repository = IC2.Engine.Serialization.GameDataRepository.Load(IC2.Engine.Tests.Model.TestPaths.DataRoot);
        Assert.Equal(expected, repository.RulesetById(rulesetId)!.Flags.ArmyTableCompaction);
    }

    // ------------------------------------------------------------ the faithful small-army merge (FUN_004509f0)

    [Fact]
    public void The_faithful_merge_picks_its_donor_and_receiver_in_the_swept_table_order()
    {
        // Two same-nation armies, first and second in table order, behind an army of the same nation that
        // disbands. Without the sweep the first is the donor; the sweep brings the last to the front, so the
        // pair swaps (the #187 Carthage case in spirit).
        const string me = "a";
        var toy = BattleCommandTestbed.ToyRuleset;
        var rules = toy with
        {
            Flags = toy.Flags with
            {
                AiEconomy = AiEconomyModel.Original,
                ArmyTableCompaction = ArmyTableCompactionPolicy.SwapRemoveSweep,
            },
        };
        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, rules, BattleCommandTestbed.ToyWorld, NullEventSink.Instance);

        static ArmyState Of(string id, int x, int y) =>
            new(id, "a", x, y, 0, 50, 0, 0, 2, null, ValueList.Of(new UnitSlot(0, "light_infantry", 5_000, 6, id)));
        var nation = CaptureFixtures.Nation(me, treasury: 1000, capitalCityId: "a-cap") with { Control = SeatControl.Ai };
        var foe = CaptureFixtures.Nation("b", capitalCityId: "b-cap");
        var cities = new[]
        {
            CaptureFixtures.City("a-cap", "a-cap", 1, 1, me, me, 80, 0, 10, 100, 10),
            CaptureFixtures.City("b-cap", "b-cap", 90, 90, "b", "b", 80, 0, 10, 100, 10),
        };
        var state = BattleCommandTestbed.StateWith(new[] { nation, foe }, cities, new[]
        {
            Of("gone", 1, 1), Of("first", 3, 3), Of("second", 4, 3),
        }) with { TurnOrder = ValueList.Of(me, "b"), ActiveSeatIndex = 0 };

        var disbanded = dispatcher.Dispatch(state, new DisbandArmyCommand(me, "gone"));
        Assert.True(disbanded.IsAccepted, disbanded.ToString());
        var swept = ArmyTable.Sweep(disbanded.State);
        Assert.Equal(new[] { "second", "first" }, Ids(swept));

        var log = new List<string>();
        var merged = AiFaithfulEconomy.MergeArmies(swept, rules, dispatcher, NullEventSink.Instance, me, log);
        foreach (var line in log)
        {
            _output.WriteLine(line);
        }

        Assert.Equal((1, 0), (merged.Issued, merged.Rejected));
        Assert.Null(merged.State.ArmyById("second"));
        Assert.Equal(10_000, merged.State.ArmyById("first")!.TotalTroops);
    }

    // ------------------------------------------------------------ every removal and creation fits the table

    [Fact]
    public void A_seeded_classical_ai_game_removes_and_creates_armies_only_in_ways_the_table_records()
    {
        var violations = new List<string>();
        var deaths = 0;
        ArmyTable.OrderViolationObserver = violations.Add;
        ArmyTable.SweepObserver = closed => deaths += closed;
        try
        {
            var classical = IC2.Engine.Tests.Ai.AiTestbed.Classical;
            Assert.Equal(ArmyTableCompactionPolicy.SwapRemoveSweep, classical.Ruleset.Flags.ArmyTableCompaction);
            AiGameRunner.Run(classical.World, classical.Ruleset, classical.Scenario, seed: 3, turnCap: 160);
        }
        finally
        {
            ArmyTable.OrderViolationObserver = null;
            ArmyTable.SweepObserver = null;
        }

        foreach (var violation in violations.Distinct())
        {
            _output.WriteLine(violation);
        }

        _output.WriteLine($"dead slots swept: {deaths}");
        Assert.Empty(violations);
        Assert.True(deaths > 0, "the run must remove armies for the check to mean anything");
    }

    /// <summary>Records the army order every dispatched command saw.</summary>
    private sealed class SpyDispatch : ICommandDispatch
    {
        private readonly ICommandDispatch _inner;
        private readonly List<string[]> _seen;

        public SpyDispatch(ICommandDispatch inner, List<string[]> seen)
        {
            _inner = inner;
            _seen = seen;
        }

        public CommandResult Dispatch(GameState state, ICommand command)
        {
            _seen.Add(state.Armies.Where(a => a.Nation == NorthNationId).Select(a => a.Id).ToArray());
            return _inner.Dispatch(state, command);
        }

        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            _seen.Add(state.Armies.Where(a => a.Nation == NorthNationId).Select(a => a.Id).ToArray());
            return _inner.Dispatch(state, command, events);
        }
    }
}
