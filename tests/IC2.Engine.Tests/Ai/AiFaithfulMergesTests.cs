using System.Text.Json;
using IC2.Engine.Ai;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T159 (#959): the AI's army merge (FUN_004509f0) and fleet merge (FUN_00450b30) under classical-faithful, on
/// hand-built states. Every gate has a test that fails when it moves by one; the candidate order, the
/// carry-over, the conservation and the refusals are pinned too.
/// </summary>
public sealed class AiFaithfulMergesTests
{
    private const string Me = "a";
    private const string Foe = "b";
    private const string Li = "light_infantry";

    private static Ruleset Rules(AiEconomyModel model = AiEconomyModel.Original)
    {
        var toy = BattleCommandTestbed.ToyRuleset;
        return toy with { Flags = toy.Flags with { AiEconomy = model } };
    }

    private static CommandDispatcher Dispatcher(Ruleset rules) =>
        new(SystemRegistry.FromEngineAssembly(), rules, BattleCommandTestbed.ToyWorld, NullEventSink.Instance);

    private static UnitSlot U(int troops, string name = "u", int quality = 6) => new(0, Li, troops, quality, name);

    private static ArmyState Army(
        string id,
        int x,
        int y,
        IEnumerable<UnitSlot> units,
        int moves = 0,
        int money = 0,
        int supplies = 0,
        string? aboard = null,
        string nation = Me) =>
        new(id, nation, x, y, moves, 50, money, supplies, 2, aboard, ValueList.From(units));

    private static ArmyState Army(string id, int x, int y, int troops, int moves = 0, int money = 0, int supplies = 0) =>
        Army(id, x, y, new[] { U(troops) }, moves, money, supplies);

    private static FleetState Fleet(
        string id,
        int x,
        int y,
        int ships,
        int money = 0,
        int supplies = 0,
        int moves = 0,
        int condition = 100,
        string? carried = null,
        int? construction = null,
        string nation = Me) =>
        new(id, nation, x, y, moves, ships, condition, money, supplies, construction, construction is null ? null : "c", carried, 2);

    private static GameState Build(
        IEnumerable<ArmyState>? armies = null,
        IEnumerable<FleetState>? fleets = null,
        SeatControl control = SeatControl.Ai,
        int treasury = 1000)
    {
        var mine = CaptureFixtures.Nation(Me, treasury: treasury, capitalCityId: "a-cap") with { Control = control };
        var foe = CaptureFixtures.Nation(Foe, capitalCityId: "b-cap");
        var cities = new[]
        {
            CaptureFixtures.City("a-cap", "a-cap", 1, 1, Me, Me, 80, 0, 10, 100, 10),
            CaptureFixtures.City("b-cap", "b-cap", 90, 90, Foe, Foe, 80, 0, 10, 100, 10),
        };
        return BattleCommandTestbed.StateWith(new[] { mine, foe }, cities, armies, fleets) with
        {
            TurnOrder = ValueList.From(new[] { Me, Foe }),
            ActiveSeatIndex = 0,
        };
    }

    private static (GameState State, int Issued, int Rejected) MergeArmies(GameState state, Ruleset? rules = null)
    {
        rules ??= Rules();
        return AiFaithfulEconomy.MergeArmies(
            state, rules, Dispatcher(rules), NullEventSink.Instance, Me, new List<string>());
    }

    private static (GameState State, int Issued, int Rejected) MergeFleets(GameState state, Ruleset? rules = null)
    {
        rules ??= Rules();
        return AiFaithfulEconomy.MergeFleets(
            state, rules, Dispatcher(rules), NullEventSink.Instance, Me, new List<string>());
    }

    private static bool ArmyMerged(GameState state) => MergeArmies(state) is { Issued: 1, Rejected: 0 };

    private static bool FleetMerged(GameState state) => MergeFleets(state) is { Issued: 1, Rejected: 0 };

    private static IEnumerable<UnitSlot> Slots(int count, int troops = 100) =>
        Enumerable.Range(0, count).Select(i => U(troops, $"s{i}"));

    // ---------------------------------------------------------------- Done-when 1: the army merge's gates

    [Fact]
    public void The_donor_gate_is_troops_below_20000()
    {
        // The 30,000-troop partner is no donor itself (it fails the donor gate), so only "d" can donate.
        Assert.True(ArmyMerged(Build(new[] { Army("d", 0, 0, 19_999), Army("p", 1, 0, 30_000) })));
        Assert.False(ArmyMerged(Build(new[] { Army("d", 0, 0, 20_000), Army("p", 1, 0, 30_000) })));
    }

    [Fact]
    public void The_partner_gate_is_its_own_troops_below_80000()
    {
        Assert.True(ArmyMerged(Build(new[] { Army("d", 0, 0, 5_000), Army("p", 1, 0, 79_999) })));
        Assert.False(ArmyMerged(Build(new[] { Army("d", 0, 0, 5_000), Army("p", 1, 0, 80_000) })));
    }

    [Fact]
    public void The_distance_gate_is_chebyshev_17_merges_and_18_does_not()
    {
        Assert.True(ArmyMerged(Build(new[] { Army("d", 0, 0, 100), Army("p", 17, 17, 100) })));
        Assert.True(ArmyMerged(Build(new[] { Army("d", 0, 0, 100), Army("p", 17, 3, 100) })));
        Assert.False(ArmyMerged(Build(new[] { Army("d", 0, 0, 100), Army("p", 18, 0, 100) })));
        Assert.False(ArmyMerged(Build(new[] { Army("d", 0, 0, 100), Army("p", 5, 18, 100) })));
    }

    [Fact]
    public void The_slot_extent_gate_is_a_combined_extent_below_20()
    {
        Assert.True(ArmyMerged(Build(new[] { Army("d", 0, 0, Slots(10)), Army("p", 1, 0, Slots(9)) })));
        Assert.False(ArmyMerged(Build(new[] { Army("d", 0, 0, Slots(10)), Army("p", 1, 0, Slots(10)) })));
    }

    [Fact]
    public void The_extent_is_the_last_occupied_slot_plus_one_and_not_the_unit_count()
    {
        // The partner lists ten slots but only the first two hold troops: its extent is 2, so 10 + 2 < 20 merges
        // (a count of 10 would make 20 and refuse). The empty slots are not kept.
        var sparse = new[] { U(100, "x0"), U(100, "x1") }.Concat(Enumerable.Range(0, 8).Select(i => U(0, $"z{i}")));
        var state = Build(new[] { Army("d", 0, 0, Slots(10)), Army("p", 1, 0, sparse) });
        var (after, issued, rejected) = MergeArmies(state);
        Assert.Equal((1, 0), (issued, rejected));
        Assert.Equal(12, after.ArmyById("p")!.Units.Count);

        // A single occupied slot at index 18 has extent 19: with a one-slot donor that is 20 and refuses.
        var tail = Enumerable.Range(0, 18).Select(i => U(0, $"z{i}")).Append(U(100, "tail"));
        Assert.False(ArmyMerged(Build(new[] { Army("d", 0, 0, 100), Army("p", 1, 0, tail) })));
    }

    [Fact]
    public void An_army_aboard_a_fleet_on_either_side_blocks_the_merge()
    {
        Assert.False(ArmyMerged(Build(new[] { Army("d", 0, 0, new[] { U(100) }, aboard: "f"), Army("p", 1, 0, 100) })));
        Assert.False(ArmyMerged(Build(new[] { Army("d", 0, 0, 100), Army("p", 1, 0, new[] { U(100) }, aboard: "f") })));
        Assert.True(ArmyMerged(Build(new[] { Army("d", 0, 0, 100), Army("p", 1, 0, 100) })));
    }

    [Fact]
    public void A_donor_larger_than_its_partner_still_merges_and_the_smaller_army_survives()
    {
        // Table order: the big army is first, so it is the donor; the sizes are never compared.
        var (after, issued, _) = MergeArmies(Build(new[] { Army("big", 0, 0, 15_000), Army("small", 1, 0, 1_000) }));
        Assert.Equal(1, issued);
        Assert.Null(after.ArmyById("big"));
        Assert.Equal(16_000, after.ArmyById("small")!.TotalTroops);
    }

    [Fact]
    public void An_enemy_army_is_neither_donor_nor_partner()
    {
        Assert.False(ArmyMerged(Build(new[] { Army("d", 0, 0, 100), Army("e", 1, 0, new[] { U(100) }, nation: Foe) })));
    }

    [Fact]
    public void At_most_one_army_merge_a_call()
    {
        var state = Build(new[] { Army("a1", 0, 0, 100), Army("a2", 1, 0, 100), Army("a3", 2, 0, 100), Army("a4", 3, 0, 100) });
        var (after, issued, rejected) = MergeArmies(state);
        Assert.Equal((1, 0), (issued, rejected));
        Assert.Equal(3, after.Armies.Count);
    }

    [Fact]
    public void The_donor_is_the_first_army_with_a_valid_partner_and_the_partner_the_first_valid_one_in_both_table_orders()
    {
        var alone = Army("alone", 0, 0, 100); // no partner within 17: skipped as a donor
        var x = Army("x", 50, 0, 100);
        var y = Army("y", 52, 0, 100);
        var z = Army("z", 55, 0, 100);

        // [alone, x, y, z]: "alone" has no partner, so x donates; its first valid partner is y.
        var forward = MergeArmies(Build(new[] { alone, x, y, z })).State;
        Assert.Null(forward.ArmyById("x"));
        Assert.Equal(200, forward.ArmyById("y")!.TotalTroops);
        Assert.Equal(4 - 1, forward.Armies.Count);
        Assert.Equal(100, forward.ArmyById("z")!.TotalTroops);

        // [z, y, x, alone]: z donates (it has partners) and its first valid partner in table order is y.
        var reverse = MergeArmies(Build(new[] { z, y, x, alone })).State;
        Assert.Null(reverse.ArmyById("z"));
        Assert.Equal(200, reverse.ArmyById("y")!.TotalTroops);
        Assert.Equal(100, reverse.ArmyById("x")!.TotalTroops);
    }

    [Fact]
    public void An_invalid_first_partner_in_table_order_is_passed_over()
    {
        var d = Army("d", 0, 0, 100);
        var blocked = Army("blocked", 1, 0, new[] { U(100) }, aboard: "f");
        var ok = Army("ok", 2, 0, 100);
        var after = MergeArmies(Build(new[] { d, blocked, ok })).State;
        Assert.NotNull(after.ArmyById("blocked"));
        Assert.Equal(200, after.ArmyById("ok")!.TotalTroops);
    }

    [Fact]
    public void A_donor_with_no_moves_zeroes_the_partners_moves_otherwise_they_stay()
    {
        var zero = MergeArmies(Build(new[] { Army("d", 0, 0, 100, moves: 0), Army("p", 1, 0, 30_000, moves: 5) })).State;
        Assert.Equal(0, zero.ArmyById("p")!.Moves);

        var kept = MergeArmies(Build(new[] { Army("d", 0, 0, 100, moves: 3), Army("p", 1, 0, 30_000, moves: 5) })).State;
        Assert.Equal(5, kept.ArmyById("p")!.Moves);
    }

    [Fact]
    public void The_donors_slots_arrive_in_the_originals_order_first_then_last_then_the_next_last()
    {
        var donor = Army("d", 0, 0, new[] { U(100, "a"), U(100, "b"), U(100, "c") });
        var partner = Army("p", 1, 0, new[] { U(30_000, "p0") });
        var after = MergeArmies(Build(new[] { donor, partner })).State;
        Assert.Equal(new[] { "p0", "a", "c", "b" }, after.ArmyById("p")!.Units.Select(u => u.Name).ToArray());

        var four = Army("d", 0, 0, new[] { U(100, "a"), U(100, "b"), U(100, "c"), U(100, "d") });
        var after4 = MergeArmies(Build(new[] { four, partner })).State;
        Assert.Equal(new[] { "p0", "a", "d", "c", "b" }, after4.ArmyById("p")!.Units.Select(u => u.Name).ToArray());
    }

    [Fact]
    public void The_partner_keeps_its_position_morale_and_every_other_field()
    {
        var partner = Army("p", 3, 4, 30_000) with { Morale = 77, CoveredTileCode = 5 };
        var after = MergeArmies(Build(new[] { Army("d", 0, 0, 100), partner })).State;
        var kept = after.ArmyById("p")!;
        Assert.Equal((3, 4, 77, 5), (kept.X, kept.Y, kept.Morale, kept.CoveredTileCode));
    }

    // ---------------------------------------------------------------- Done-when 2: the fleet merge's gates

    [Fact]
    public void The_survivor_gate_is_ships_below_40()
    {
        // The 60-ship partner is no survivor itself (it fails the 40-ship gate).
        Assert.True(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 39), Fleet("p", 1, 0, 50) })));
        Assert.False(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 40), Fleet("p", 1, 0, 50) })));
    }

    [Fact]
    public void The_distance_gate_is_chebyshev_33_merges_and_34_does_not()
    {
        Assert.True(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 10), Fleet("p", 33, 33, 10) })));
        Assert.False(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 10), Fleet("p", 34, 0, 10) })));
        Assert.False(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 10), Fleet("p", 3, 34, 10) })));
    }

    [Fact]
    public void The_combined_ships_gate_is_below_100()
    {
        // The partner is over 40 so it is no survivor; the survivor holds 39 at most.
        Assert.True(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 39), Fleet("p", 1, 0, 60) })));
        Assert.False(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 39), Fleet("p", 1, 0, 61) })));
    }

    [Fact]
    public void A_carried_army_on_either_fleet_blocks_the_merge()
    {
        Assert.False(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 10, carried: "x"), Fleet("p", 1, 0, 10) })));
        Assert.False(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 10), Fleet("p", 1, 0, 10, carried: "x") })));
    }

    [Fact]
    public void A_fleet_under_construction_on_either_side_blocks_the_merge()
    {
        Assert.False(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 10, construction: 5), Fleet("p", 1, 0, 10) })));
        Assert.False(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 10), Fleet("p", 1, 0, 10, construction: 5) })));
    }

    [Fact]
    public void A_fleets_moves_do_not_matter_there_is_no_destination_gate()
    {
        Assert.True(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 10, moves: 0), Fleet("p", 1, 0, 10, moves: 7) })));
        Assert.True(FleetMerged(Build(fleets: new[] { Fleet("s", 0, 0, 10, moves: 7), Fleet("p", 1, 0, 10, moves: 0) })));
    }

    [Fact]
    public void The_absorbed_fleet_may_hold_40_or_more_ships_and_the_idle_outer_fleet_survives()
    {
        var (after, issued, _) = MergeFleets(Build(fleets: new[] { Fleet("idle", 0, 0, 10), Fleet("big", 1, 0, 70) }));
        Assert.Equal(1, issued);
        Assert.Null(after.FleetById("big"));
        Assert.Equal(80, after.FleetById("idle")!.Ships);
    }

    [Fact]
    public void At_most_one_fleet_merge_a_call()
    {
        var state = Build(fleets: new[] { Fleet("f1", 0, 0, 5), Fleet("f2", 1, 0, 5), Fleet("f3", 2, 0, 5), Fleet("f4", 3, 0, 5) });
        var (after, issued, _) = MergeFleets(state);
        Assert.Equal(1, issued);
        Assert.Equal(3, after.Fleets.Count);
    }

    [Fact]
    public void The_survivor_is_the_first_fleet_with_a_valid_partner_and_the_partner_the_first_valid_one_in_both_table_orders()
    {
        var alone = Fleet("alone", 0, 0, 5);
        var x = Fleet("x", 80, 0, 5);
        var y = Fleet("y", 82, 0, 5);
        var z = Fleet("z", 85, 0, 5);

        // [alone, x, y, z]: alone has no partner within 33, so x survives and absorbs y, the first valid partner.
        var forward = MergeFleets(Build(fleets: new[] { alone, x, y, z })).State;
        Assert.Null(forward.FleetById("y"));
        Assert.Equal(10, forward.FleetById("x")!.Ships);
        Assert.Equal(3, forward.Fleets.Count);

        // [z, y, x, alone]: z survives and absorbs y.
        var reverse = MergeFleets(Build(fleets: new[] { z, y, x, alone })).State;
        Assert.Null(reverse.FleetById("y"));
        Assert.Equal(10, reverse.FleetById("z")!.Ships);
    }

    [Fact]
    public void The_survivor_keeps_its_position_moves_and_condition_and_the_absorbed_ones_are_discarded()
    {
        var survivor = Fleet("s", 2, 3, 10, moves: 4, condition: 60);
        var absorbed = Fleet("p", 3, 3, 10, moves: 9, condition: 100);
        var kept = MergeFleets(Build(fleets: new[] { survivor, absorbed })).State.FleetById("s")!;
        Assert.Equal((2, 3, 4, 60, 20), (kept.X, kept.Y, kept.Moves, kept.ConditionPercent, kept.Ships));
    }

    // ---------------------------------------------------------------- Done-when 3: conservation

    [Fact]
    public void An_army_merge_conserves_troops_units_supplies_and_money_and_leaves_the_treasury()
    {
        var donor = Army("d", 0, 0, new[] { U(100, "a"), U(200, "b", 3) }, money: 300, supplies: 7);
        var partner = Army("p", 1, 0, new[] { U(30_000, "p0") }, money: 400, supplies: 11);
        var other = Army("far-1", 60, 60, 500);
        var other2 = Army("far-2", 90, 0, 600);
        var state = Build(new[] { donor, partner, other, other2 });
        var (after, issued, _) = MergeArmies(state);
        Assert.Equal(1, issued);

        var merged = after.ArmyById("p")!;
        Assert.Equal(donor.TotalTroops + partner.TotalTroops, merged.TotalTroops);
        Assert.Equal(3, merged.Units.Count);
        Assert.Equal(700, merged.Money);
        Assert.Equal(18, merged.SupplyTons);
        Assert.Equal(state.NationById(Me)!.Treasury, after.NationById(Me)!.Treasury);
        Assert.Equal(state.Armies.Sum(a => a.TotalTroops), after.Armies.Sum(a => a.TotalTroops));
        Assert.Equal(state.Armies.Sum(a => a.Money), after.Armies.Sum(a => a.Money));
        Assert.Equal(state.Armies.Sum(a => a.SupplyTons), after.Armies.Sum(a => a.SupplyTons));

        // The absorbed id is nowhere; the unmerged candidates are untouched.
        Assert.Null(after.ArmyById("d"));
        Assert.DoesNotContain(after.Fleets, f => f.CarriedArmyId == "d");
        Assert.DoesNotContain(after.Armies, a => a.AboardFleetId == "d");
        Assert.DoesNotContain("\"d\"", GameJson.Serialize(after));
        Assert.Equal(other, after.ArmyById("far-1"));
        Assert.Equal(other2, after.ArmyById("far-2"));
        AssertRoundTrips(after);
    }

    [Fact]
    public void A_fleet_merge_conserves_ships_supplies_and_money_and_leaves_the_treasury()
    {
        var survivor = Fleet("s", 0, 0, 10, money: 300, supplies: 5);
        var absorbed = Fleet("p", 1, 0, 20, money: 400, supplies: 6);
        var far = Fleet("far", 90, 0, 7);
        var state = Build(fleets: new[] { survivor, absorbed, far });
        var (after, issued, _) = MergeFleets(state);
        Assert.Equal(1, issued);

        var merged = after.FleetById("s")!;
        Assert.Equal((30, 700, 11), (merged.Ships, merged.Money, merged.SupplyTons));
        Assert.Equal(state.NationById(Me)!.Treasury, after.NationById(Me)!.Treasury);
        Assert.Equal(state.Fleets.Sum(f => f.Ships), after.Fleets.Sum(f => f.Ships));
        Assert.Equal(state.Fleets.Sum(f => f.Money), after.Fleets.Sum(f => f.Money));
        Assert.Equal(state.Fleets.Sum(f => f.SupplyTons), after.Fleets.Sum(f => f.SupplyTons));

        Assert.Null(after.FleetById("p"));
        Assert.DoesNotContain(after.Armies, a => a.AboardFleetId == "p");
        Assert.DoesNotContain(after.Fleets, f => f.CarriedArmyId == "p");
        Assert.Equal(far, after.FleetById("far"));
        AssertRoundTrips(after);
    }

    [Fact]
    public void Merged_purses_above_the_field_bound_saturate_and_the_excess_is_discarded_with_the_treasury_unchanged()
    {
        var armies = Build(new[] { Army("d", 0, 0, 100, money: 20_000), Army("p", 1, 0, 30_000, money: 20_000) });
        var armyAfter = MergeArmies(armies).State;
        Assert.Equal(32_767, armyAfter.ArmyById("p")!.Money);
        Assert.Equal(armies.NationById(Me)!.Treasury, armyAfter.NationById(Me)!.Treasury);

        var fleets = Build(fleets: new[] { Fleet("s", 0, 0, 10, money: 20_000), Fleet("p", 1, 0, 10, money: 20_000) });
        var fleetAfter = MergeFleets(fleets).State;
        Assert.Equal(32_767, fleetAfter.FleetById("s")!.Money);
        Assert.Equal(fleets.NationById(Me)!.Treasury, fleetAfter.NationById(Me)!.Treasury);
    }

    private static void AssertRoundTrips(GameState state)
    {
        var json = GameJson.Serialize(state);
        var back = JsonSerializer.Deserialize<GameState>(json, GameJson.Options);
        Assert.NotNull(back);
        Assert.Equal(json, GameJson.Serialize(back));
    }

    // ---------------------------------------------------------------- Done-when 4: only the AI, only classical-faithful

    [Fact]
    public void The_army_merge_is_refused_for_a_human_seat_and_under_improved_each_with_its_code()
    {
        var armies = new[] { Army("d", 0, 0, 100), Army("p", 1, 0, 100) };
        var command = new AiAbsorbArmyCommand(Me, "p", "d");

        var human = Dispatcher(Rules()).Dispatch(Build(armies, control: SeatControl.Human), command);
        Assert.True(human.IsRejected);
        Assert.Equal("armies.ai-absorb-not-ai-seat", human.Rejection!.Code.Value);

        var improved = Dispatcher(Rules(AiEconomyModel.Designed)).Dispatch(Build(armies), command);
        Assert.True(improved.IsRejected);
        Assert.Equal("armies.ai-absorb-not-original-ai", improved.Rejection!.Code.Value);
    }

    [Fact]
    public void The_fleet_merge_is_refused_for_a_human_seat_and_under_improved_each_with_its_code()
    {
        var fleets = new[] { Fleet("s", 0, 0, 10), Fleet("p", 1, 0, 10) };
        var command = new AiAbsorbFleetCommand(Me, "s", "p");

        var human = Dispatcher(Rules()).Dispatch(Build(fleets: fleets, control: SeatControl.Human), command);
        Assert.True(human.IsRejected);
        Assert.Equal("naval.ai-absorb-not-ai-seat", human.Rejection!.Code.Value);

        var improved = Dispatcher(Rules(AiEconomyModel.Designed)).Dispatch(Build(fleets: fleets), command);
        Assert.True(improved.IsRejected);
        Assert.Equal("naval.ai-absorb-not-original-ai", improved.Rejection!.Code.Value);
    }

    [Fact]
    public void The_handlers_re_check_the_gates_a_far_pair_is_refused()
    {
        var far = Build(new[] { Army("d", 0, 0, 100), Army("p", 18, 0, 100) });
        var army = Dispatcher(Rules()).Dispatch(far, new AiAbsorbArmyCommand(Me, "p", "d"));
        Assert.Equal("armies.ai-absorb-gate-failed", army.Rejection!.Code.Value);

        var farFleets = Build(fleets: new[] { Fleet("s", 0, 0, 10), Fleet("p", 34, 0, 10) });
        var fleet = Dispatcher(Rules()).Dispatch(farFleets, new AiAbsorbFleetCommand(Me, "s", "p"));
        Assert.Equal("naval.ai-absorb-gate-failed", fleet.Rejection!.Code.Value);
    }

    [Fact]
    public void A_humans_join_is_still_refused_at_distance_two()
    {
        var state = Build(
            new[] { Army("d", 0, 0, 100), Army("p", 2, 0, 100) },
            new[] { Fleet("s", 0, 5, 10), Fleet("t", 2, 5, 10) },
            control: SeatControl.Human);
        var dispatcher = Dispatcher(Rules());
        var army = dispatcher.Dispatch(state, new JoinArmiesCommand(Me, "p", "d"));
        Assert.Equal("armies.not-adjacent", army.Rejection!.Code.Value);
        var fleet = dispatcher.Dispatch(state, new JoinFleetsCommand(Me, "s", "t"));
        Assert.Equal("naval.not-adjacent", fleet.Rejection!.Code.Value);
    }

    // ---------------------------------------------------------------- the phase wiring

    [Fact]
    public void The_economy_phase_runs_the_army_merge_then_the_fleet_merge_once_each()
    {
        var state = Build(
            new[] { Army("a1", 0, 0, 100), Army("a2", 1, 0, 100), Army("a3", 2, 0, 100) },
            new[] { Fleet("f1", 0, 5, 5), Fleet("f2", 1, 5, 5), Fleet("f3", 2, 5, 5) });
        var rules = Rules();
        var log = new List<string>();
        var result = AiFaithfulEconomy.Run(
            state, rules, BattleCommandTestbed.ToyWorld, Dispatcher(rules), NullEventSink.Instance, new SplitMix64Rng(1), Me, log);
        Assert.Equal(0, result.Rejected);
        Assert.Equal(2, result.State.Armies.Count);
        Assert.Equal(2, result.State.Fleets.Count);
        var army = log.FindIndex(l => l.StartsWith("faithful merge: army", StringComparison.Ordinal));
        var fleet = log.FindIndex(l => l.StartsWith("faithful merge: fleet", StringComparison.Ordinal));
        Assert.True(army >= 0 && fleet > army);
    }
}
