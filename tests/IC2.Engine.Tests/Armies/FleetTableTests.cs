using IC2.Engine.Ai;
using IC2.Engine.Armies;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Persistence;
using IC2.Engine.Tests.Battle.Commands;
using IC2.Engine.Tests.Core;
using Xunit;
using Xunit.Abstractions;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// T244 (#1606): under <c>flags.unitTableCompaction = swapRemoveSweep</c> a removed fleet leaves a dead slot until the
/// original's sweep, which <c>FUN_0044adb0</c> runs over the fleet table (<c>FUN_0044ad7c</c>) right after the army
/// table, when a human ends the turn and before each AI seat. Under <c>inOrder</c> nothing changes. The cases mirror
/// T240's <see cref="ArmyTableTests"/>.
/// </summary>
public sealed class FleetTableTests
{
    private readonly ITestOutputHelper _output;

    public FleetTableTests(ITestOutputHelper output) => _output = output;

    private static Ruleset Toy => CoreTestbed.Toy.Ruleset;

    private static Ruleset Swept => Toy with
    {
        Flags = Toy.Flags with { UnitTableCompaction = UnitTableCompactionPolicy.SwapRemoveSweep },
    };

    private static string Id(int i) => $"f{i:00}";

    private static string[] Ids(GameState state) => state.Fleets.Select(f => f.Id).ToArray();

    private static string[] Ids(params int[] indices) => indices.Select(Id).ToArray();

    private static FleetState Fleet(string id, int x, int y, string? carried = null) =>
        new(id, NorthNationId, x, y, Moves: 5, Ships: 10, ConditionPercent: 100, Money: 0, SupplyTons: 0,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: carried, CoveredTileCode: null);

    /// <summary>
    /// <paramref name="count"/> launched fleets of the active human seat, <c>f00</c>…, all beside its city <c>arx</c>
    /// so any of them may be scuttled, and no armies.
    /// </summary>
    private static GameState Table(int count)
    {
        var state = HumanNorthFirst(InitialState());
        var arx = state.CityById("arx")!;
        return state with
        {
            Armies = ValueList<ArmyState>.Empty,
            Fleets = ValueList.From(Enumerable.Range(0, count).Select(i => Fleet(Id(i), arx.X + 1, arx.Y))),
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

    private static GameState Scuttle(GameState state, int index, Ruleset? rules = null)
    {
        var result = DispatcherWithRuleset(rules ?? Swept).Dispatch(state, new ScuttleFleetCommand(NorthNationId, Id(index)));
        Assert.True(result.IsAccepted, result.ToString());
        return result.State;
    }

    [Fact]
    public void A_scuttled_fleet_leaves_a_dead_slot_and_the_survivors_keep_their_order_until_the_sweep()
    {
        var state = Scuttle(Table(5), 1);

        Assert.Equal(ValueList.Of(1), state.DeadFleetSlots);
        Assert.Null(state.DeadArmySlots);
        Assert.Equal(Ids(0, 2, 3, 4), Ids(state));
        Assert.Equal(new FleetState?[] { state.Fleets[0], null, state.Fleets[1], state.Fleets[2], state.Fleets[3] },
            ArmyTable.FleetSlots(state));
    }

    [Fact]
    public void The_sweep_moves_the_last_fleet_into_the_dead_slot()
    {
        // The issue's storm30 example: [0,1,2,3,4] loses 2 and becomes [0,1,4,3].
        Assert.Equal(Ids(0, 1, 4, 3), Ids(ArmyTable.Sweep(Scuttle(Table(5), 2))));
    }

    [Fact]
    public void A_death_at_slot_0_brings_the_last_fleet_to_the_front()
    {
        Assert.Equal(Ids(4, 1, 2, 3), Ids(ArmyTable.Sweep(Scuttle(Table(5), 0))));
    }

    [Fact]
    public void One_sweep_over_two_dead_slots_works_from_the_last_slot_down()
    {
        // Slot 3 is met first and takes 5; slot 1 then takes 4, now the last record.
        var state = Scuttle(Scuttle(Table(6), 1), 3);
        Assert.Equal(ValueList.Of(1, 3), state.DeadFleetSlots);

        var swept = ArmyTable.Sweep(state);

        Assert.Equal(Ids(0, 4, 2, 5), Ids(swept));
        Assert.Null(swept.DeadFleetSlots);
    }

    [Fact]
    public void A_fleet_ordered_between_a_loss_and_the_sweep_moves_into_the_dead_slot()
    {
        var state = Scuttle(Table(4), 1);
        var ordered = DispatcherWithRuleset(Swept).Dispatch(
            state, new OrderFleetCommand(NorthNationId, "arx", Ships: 10, NewFleetId: "new"));
        Assert.True(ordered.IsAccepted, ordered.ToString());

        // The new fleet is appended after every slot, the dead one included...
        Assert.Equal(new[] { Id(0), Id(2), Id(3), "new" }, Ids(ordered.State));
        Assert.Equal(ValueList.Of(1), ordered.State.DeadFleetSlots);

        // ...and, being last, the sweep moves it into the dead slot.
        Assert.Equal(new[] { Id(0), "new", Id(2), Id(3) }, Ids(ArmyTable.Sweep(ordered.State)));
    }

    [Fact]
    public void The_swap_keeps_the_carried_army_link_because_both_sides_hold_ids()
    {
        var state = Table(4);
        var arx = state.CityById("arx")!;
        var carrier = Fleet(Id(3), arx.X + 1, arx.Y, carried: "rider");
        var rider = Army("rider", NorthNationId, arx.X + 1, arx.Y, new[] { RegularUnit("r") },
            coveredTileCode: null, aboardFleetId: Id(3));
        state = state with
        {
            Fleets = ValueList.From(state.Fleets.Take(3).Append(carrier)),
            Armies = ValueList.Of(rider),
        };

        var swept = ArmyTable.Sweep(Scuttle(state, 0));

        Assert.Equal(Ids(3, 1, 2), Ids(swept));
        Assert.Equal("rider", swept.Fleets[0].CarriedArmyId);
        Assert.Equal(Id(3), swept.ArmyById("rider")!.AboardFleetId);
    }

    [Fact]
    public void A_loss_in_a_human_turn_is_swept_after_the_armies_when_the_human_ends_the_turn()
    {
        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, Swept, CoreTestbed.Toy.World, NullEventSink.Instance);
        var coordinator = new TurnCoordinator(registry, Swept, CoreTestbed.Toy.World, NullEventSink.Instance, dispatcher);

        var died = dispatcher.Dispatch(Table(5), new ScuttleFleetCommand(NorthNationId, Id(1)));
        Assert.True(died.IsAccepted, died.ToString());
        Assert.Equal(ValueList.Of(1), died.State.DeadFleetSlots);

        var ended = coordinator.RunTurn(died.State);

        Assert.Equal(Ids(0, 4, 2, 3), Ids(ended.State));
        Assert.Null(ended.State.DeadFleetSlots);
        Assert.Equal("armies.table-sweep", ended.Trace.Select(t => t.SystemId).First());
    }

    [Fact]
    public void Dead_fleet_slots_survive_a_save_and_load_and_give_the_same_sweep()
    {
        var state = Scuttle(Scuttle(Table(6), 4), 1);
        var save = new SaveGame(state.SchemaVersion, "dead-fleets", "dead-fleets", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var json = SaveManager.Serialize(save);
        Assert.Contains("\"deadFleetSlots\"", json, StringComparison.Ordinal);

        var loaded = SaveManager.Load("dead-fleets.json", json, CoreTestbed.Toy.World, Swept).State;

        Assert.Equal(state, loaded);
        Assert.Equal(ValueList.Of(1, 4), loaded.DeadFleetSlots);
        Assert.Equal(Ids(0, 5, 2, 3), Ids(ArmyTable.Sweep(loaded)));
    }

    [Fact]
    public void A_state_without_dead_fleet_slots_saves_without_the_field_and_an_older_save_loads_with_none()
    {
        var state = Table(3);
        var json = SaveManager.Serialize(
            new SaveGame(state.SchemaVersion, "none", "none", state.ScenarioId, state.WorldId, state.RulesetId, state));

        Assert.DoesNotContain("deadFleetSlots", json, StringComparison.Ordinal);
        Assert.Null(SaveManager.Load("none.json", json, CoreTestbed.Toy.World, Swept).State.DeadFleetSlots);
    }

    [Fact]
    public void In_order_rulesets_close_the_fleet_gap_at_once_and_keep_the_survivors_in_order()
    {
        Assert.Equal(UnitTableCompactionPolicy.InOrder, Toy.Flags.UnitTableCompaction);
        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, Toy, CoreTestbed.Toy.World, NullEventSink.Instance);
        var coordinator = new TurnCoordinator(registry, Toy, CoreTestbed.Toy.World, NullEventSink.Instance, dispatcher);

        var died = dispatcher.Dispatch(Table(5), new ScuttleFleetCommand(NorthNationId, Id(1)));
        Assert.True(died.IsAccepted, died.ToString());
        Assert.Null(died.State.DeadFleetSlots);

        var ended = coordinator.RunTurn(died.State);
        Assert.Equal(Ids(0, 2, 3, 4), Ids(ended.State));
        Assert.Null(ended.State.DeadFleetSlots);
    }

    // ------------------------------------------------------------ the faithful AI (FUN_00450b30, DAT_004a0326)

    private const string Me = "a";

    private static Ruleset FaithfulSwept
    {
        get
        {
            var toy = BattleCommandTestbed.ToyRuleset;
            return toy with
            {
                Flags = toy.Flags with
                {
                    AiEconomy = AiEconomyModel.Original,
                    UnitTableCompaction = UnitTableCompactionPolicy.SwapRemoveSweep,
                },
            };
        }
    }

    /// <summary>Row 0 is sea; every other row is plain, so a city at y = 1 is coastal (as T159's fleet tests).</summary>
    private static World SeaStripWorld() => BattleCommandTestbed.ToyWorld with
    {
        Width = 40,
        Height = 20,
        Terrain = new TerrainGrid(
            TerrainEncoding.RunLength,
            ValueList.From(new[] { new TerrainRun(0, 40), new TerrainRun(2, 760) })),
    };

    private static GameState FaithfulState(IEnumerable<FleetState> fleets)
    {
        var nation = CaptureFixtures.Nation(Me, treasury: 5_000, unity: 600, wealth: 1_000_000, capitalCityId: "a-cap")
            with { Control = SeatControl.Ai };
        var foe = CaptureFixtures.Nation("b", capitalCityId: "b-cap");
        var cities = new[]
        {
            CaptureFixtures.City("a-cap", "a-cap", 20, 4, Me, Me, 80, 0, 10, 100, 10),
            CaptureFixtures.City("p1", "p1", 24, 1, Me, Me, 80, 0, 10, 100, 10),
            CaptureFixtures.City("f000", "f000", 0, 8, Me, Me, 80, 0, 10, 100, 10),
            CaptureFixtures.City("b-cap", "b-cap", 39, 19, "b", "b", 80, 0, 10, 100, 10),
        };
        return BattleCommandTestbed.StateWith(new[] { nation, foe }, cities, null, fleets) with
        {
            TurnOrder = ValueList.Of(Me, "b"),
            ActiveSeatIndex = 0,
        };
    }

    private static FleetState AiFleet(string id, int x, int y) =>
        new(id, Me, x, y, 0, 5, 100, 0, 0, null, null, null, null);

    [Fact]
    public void The_faithful_fleet_merge_picks_its_survivor_and_partner_in_the_swept_table_order()
    {
        // Two same-nation fleets, first and second in table order, behind a fleet of the same nation that is lost.
        // Without the sweep the first survives; the sweep brings the last to the front, so the pair swaps (the #187
        // Carthage fleet case in spirit).
        var rules = FaithfulSwept;
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), rules, BattleCommandTestbed.ToyWorld, NullEventSink.Instance);
        var state = FaithfulState(new[] { AiFleet("gone", 1, 2), AiFleet("first", 3, 3), AiFleet("second", 4, 3) });

        var lost = ArmyTable.Record(state, state with { Fleets = ValueList.From(state.Fleets.Skip(1)) }, rules);
        Assert.Equal(ValueList.Of(0), lost.DeadFleetSlots);

        var unswept = AiFaithfulEconomy.MergeFleets(lost, rules, dispatcher, NullEventSink.Instance, Me, new List<string>());
        Assert.Equal((1, 0), (unswept.Issued, unswept.Rejected));
        Assert.Null(unswept.State.FleetById("second"));

        var swept = ArmyTable.Sweep(lost);
        Assert.Equal(new[] { "second", "first" }, Ids(swept));

        var log = new List<string>();
        var merged = AiFaithfulEconomy.MergeFleets(swept, rules, dispatcher, NullEventSink.Instance, Me, log);
        foreach (var line in log)
        {
            _output.WriteLine(line);
        }

        Assert.Equal((1, 0), (merged.Issued, merged.Rejected));
        Assert.Null(merged.State.FleetById("first"));
        Assert.Equal(10, merged.State.FleetById("second")!.Ships);
    }

    [Theory]
    [InlineData(UnitTableCompactionPolicy.SwapRemoveSweep, 0)]
    [InlineData(UnitTableCompactionPolicy.InOrder, 1)]
    public void The_99_fleet_gate_counts_dead_fleet_slots_until_the_sweep(UnitTableCompactionPolicy policy, int expectedOrders)
    {
        // 98 live foreign fleets and one dead slot: the original's table holds 99 records until the sweep.
        var rules = FaithfulSwept with { Flags = FaithfulSwept.Flags with { UnitTableCompaction = policy } };
        var world = SeaStripWorld();
        var foreign = Enumerable.Range(0, 99)
            .Select(i => new FleetState($"o{i}", "b", 39, 19, 0, 20, 100, 0, 0, null, null, null, null)).ToArray();
        var full = FaithfulState(foreign);
        var state = ArmyTable.Record(full, full with { Fleets = ValueList.From(full.Fleets.Skip(1)) }, rules);
        Assert.Equal(98, state.Fleets.Count);
        Assert.Equal(policy == UnitTableCompactionPolicy.SwapRemoveSweep ? 99 : 98, ArmyTable.FleetTableCount(state));

        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, NullEventSink.Instance);
        var log = new List<string>();
        var ordered = AiFaithfulEconomy.OrderFleet(state, rules, world, dispatcher, NullEventSink.Instance, Me, log);
        foreach (var line in log)
        {
            _output.WriteLine(line);
        }

        Assert.Equal(expectedOrders, ordered.Issued);
    }
}
