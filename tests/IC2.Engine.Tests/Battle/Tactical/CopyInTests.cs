using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// Copy-in, <c>FUN_00437DE4</c>: report <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §2
/// ("Copy-in") and golden-master check 1; <c>docs/game-design.md</c>, "The tactical battle", "Copy-in".
/// </summary>
public class CopyInTests
{
    private static GameState Game(SeatControl attackerControl, SeatControl defenderControl, ArmyState attacker, ArmyState defender)
    {
        var initial = BattleTestbed.Initial();
        var nations = initial.Nations.Select(n => n.Id switch
        {
            "north" => n with { Control = attackerControl },
            "south" => n with { Control = defenderControl },
            _ => n,
        });
        return BattleTestbed.StateWith(new[] { attacker, defender }, nations: nations);
    }

    private static UnitSlot U(string type, int troops, int quality, string name) => BattleTestbed.Unit(type, troops, quality, name);

    [Fact]
    public void Morale_is_drawn_per_live_slot_attacker_first_clamped_and_Random_0_consumes_a_draw()
    {
        // Attacker (human, army morale 58): LI q0, HI q5, archers q9 -> Random(0), Random(20), Random(36).
        var attacker = BattleTestbed.Army("a", "north", 1, 1, 58, 0, 0,
            U("light_infantry", 9000, 0, "a0"), U("heavy_infantry", 3000, 5, "a1"), U("archers", 2000, 9, "a2"));
        // Defender (computer, army morale 60 -> 63 after the +3); sorted HI, LI, LC, archers (below).
        var defender = BattleTestbed.Army("d", "south", 2, 1, 60, 0, 0,
            U("light_infantry", 4000, 3, "d0"), U("archers", 6000, 2, "d1"),
            U("heavy_infantry", 500, 4, "d2"), U("light_cavalry", 800, 1, "d3"));
        var game = Game(SeatControl.Human, SeatControl.Ai, attacker, defender);

        // Scripted: 7 is consumed by Random(0) (which returns 0); then 19, 35; then the defender's 10, 0, 3, 7.
        var draws = new ScriptedDraws(7, 19, 35, 10, 0, 3, 7);
        var start = TacticalCopyIn.Start(game, "a", "d", Context, draws);
        var battle = start.Battle;

        // q x 4 for each live slot, attacker slots first, then the defender's in its sorted order.
        Assert.Equal(new[] { 0, 20, 36, 16, 12, 4, 8 }, draws.Bounds);
        Assert.Equal(0, draws.Calls[0].Value);
        Assert.Equal(0, draws.Remaining);

        // max(60, min(90, draw + 58)): 0 + 58 -> 60 (floor), 19 + 58 = 77, 35 + 58 = 93 -> 90 (ceiling).
        Assert.Equal(60, battle.Slots[0].Morale);
        Assert.Equal(77, battle.Slots[1].Morale);
        Assert.Equal(90, battle.Slots[2].Morale);

        // The defender's army morale is 63 after the +3: 10 + 63, 0 + 63, 3 + 63, 7 + 63.
        Assert.Equal(73, battle.Slots[D0 + 0].Morale);
        Assert.Equal(63, battle.Slots[D0 + 1].Morale);
        Assert.Equal(66, battle.Slots[D0 + 2].Morale);
        Assert.Equal(70, battle.Slots[D0 + 3].Morale);

        // Copied fields, and shots from the type's stat (LI 7, HI 0, archers 25).
        Assert.Equal((LI, 9000, 0, 7, "a0"), (battle.Slots[0].Type, battle.Slots[0].Troops, battle.Slots[0].Quality, battle.Slots[0].Shots, battle.Slots[0].Name));
        Assert.Equal((HI, 0), (battle.Slots[1].Type, battle.Slots[1].Shots));
        Assert.Equal((AR, 25), (battle.Slots[2].Type, battle.Slots[2].Shots));
        Assert.All(Enumerable.Range(3, D0 - 3), s => Assert.Equal(0, battle.Slots[s].Troops));
        AssertGridMatchesSlots(battle);
    }

    [Fact]
    public void A_computer_sides_army_gains_three_morale_and_is_sorted_by_the_designs_key_with_ties_kept_in_order()
    {
        var attacker = BattleTestbed.Army("a", "north", 1, 1, 58, 0, 0, U("light_infantry", 9000, 1, "a0"));
        // Keys troops x M[type][LI] div M[LI][type]: LI 4000 x 15 / 15 = 4000; archers 6000 x 10 / 20 = 3000;
        // HI 500 x 60 / 4 = 7500; LC 800 x 25 / 5 = 4000 (a tie with LI, which comes first).
        var defender = BattleTestbed.Army("d", "south", 2, 1, 60, 0, 0,
            U("light_infantry", 4000, 3, "d0"), U("archers", 6000, 2, "d1"),
            U("heavy_infantry", 500, 4, "d2"), U("light_cavalry", 800, 1, "d3"));
        var game = Game(SeatControl.Human, SeatControl.Ai, attacker, defender);

        var start = TacticalCopyIn.Start(game, "a", "d", Context, new ScriptedDraws(0, 0, 0, 0, 0));

        // i = 0: HI (7500) swaps up; i = 1: LI (4000) beats archers (3000), LC (4000) does not beat LI
        // under the strict >; i = 2: LC beats archers. Result HI, LI, LC, archers: the tie kept in order.
        var sorted = start.Game.ArmyById("d")!;
        Assert.Equal(new[] { "d2", "d0", "d3", "d1" }, sorted.Units.Select(u => u.Name));
        Assert.Equal(60 + Rules.ComputerArmyMoraleBonus, sorted.Morale);
        Assert.Equal(63, sorted.Morale);

        // The battle's defender slots follow the sorted records.
        Assert.Equal(new[] { "d2", "d0", "d3", "d1" }, Enumerable.Range(D0, 4).Select(s => start.Battle.Slots[s].Name));

        // The human side is unchanged: its morale, and its order.
        Assert.Equal(attacker, start.Game.ArmyById("a"));
        Assert.Equal(new[] { true }, new[] { start.Battle.ComputerControlled[1] });
        Assert.False(start.Battle.ComputerControlled[0]);
    }

    [Fact]
    public void The_sort_key_weights_troops_by_the_matrix_in_integers()
    {
        // LI x1, HI x15, archers x0.5, LC x5, HC x6, multiplied before the division.
        Assert.Equal(1000, TacticalCopyIn.SortKey(U("light_infantry", 1000, 5, "x"), Context));
        Assert.Equal(15000, TacticalCopyIn.SortKey(U("heavy_infantry", 1000, 5, "x"), Context));
        Assert.Equal(500, TacticalCopyIn.SortKey(U("archers", 1001, 5, "x"), Context)); // 1001 x 10 div 20 = 500
        Assert.Equal(5000, TacticalCopyIn.SortKey(U("light_cavalry", 1000, 5, "x"), Context));
        Assert.Equal(6000, TacticalCopyIn.SortKey(U("heavy_cavalry", 1000, 5, "x"), Context));
    }

    [Fact]
    public void A_human_attacker_against_a_computer_defender_leaves_the_attackers_records_untouched_even_out_of_order()
    {
        // The human army is out of key order; it must keep it.
        var attacker = BattleTestbed.Army("a", "north", 1, 1, 58, 0, 0,
            U("archers", 100, 1, "a0"), U("heavy_infantry", 5000, 1, "a1"));
        var defender = BattleTestbed.Army("d", "south", 2, 1, 60, 0, 0, U("light_infantry", 100, 1, "d0"));
        var game = Game(SeatControl.Human, SeatControl.Ai, attacker, defender);

        var start = TacticalCopyIn.Start(game, "a", "d", Context, new ScriptedDraws(0, 0, 0));

        Assert.Equal(new[] { "a0", "a1" }, start.Game.ArmyById("a")!.Units.Select(u => u.Name));
        Assert.Equal(58, start.Game.ArmyById("a")!.Morale);
        Assert.Equal("a0", start.Battle.Slots[0].Name);
    }

    [Fact]
    public void A_computer_attacker_is_sorted_and_raised_too()
    {
        var attacker = BattleTestbed.Army("a", "north", 1, 1, 58, 0, 0,
            U("archers", 100, 1, "a0"), U("heavy_infantry", 5000, 1, "a1"));
        var defender = BattleTestbed.Army("d", "south", 2, 1, 60, 0, 0, U("light_infantry", 100, 1, "d0"));
        var game = Game(SeatControl.Ai, SeatControl.Human, attacker, defender);

        var start = TacticalCopyIn.Start(game, "a", "d", Context, new ScriptedDraws(0, 0, 0));

        Assert.Equal(new[] { "a1", "a0" }, start.Game.ArmyById("a")!.Units.Select(u => u.Name));
        Assert.Equal(61, start.Game.ArmyById("a")!.Morale);
        Assert.Equal(60, start.Game.ArmyById("d")!.Morale);
    }

    [Fact]
    public void Positions_are_s_mod_13_and_s_div_13_for_the_attacker_and_11_minus_s_div_13_for_the_defender()
    {
        var attackerUnits = Enumerable.Range(0, 14).Select(i => U("light_infantry", 1000, 0, $"a{i}")).ToArray();
        var defenderUnits = Enumerable.Range(0, 14).Select(i => U("light_infantry", 1000, 0, $"d{i}")).ToArray();
        var attacker = BattleTestbed.Army("a", "north", 1, 1, 70, 0, 0, attackerUnits);
        var defender = BattleTestbed.Army("d", "south", 2, 1, 70, 0, 0, defenderUnits);
        var game = Game(SeatControl.Human, SeatControl.Human, attacker, defender);

        var draws = new ScriptedDraws(Enumerable.Repeat(0, 28).ToArray());
        var battle = TacticalCopyIn.Start(game, "a", "d", Context, draws).Battle;

        Assert.Equal((0, 0), (battle.Slots[0].X, battle.Slots[0].Y));
        Assert.Equal((12, 0), (battle.Slots[12].X, battle.Slots[12].Y));
        Assert.Equal((0, 1), (battle.Slots[13].X, battle.Slots[13].Y));
        Assert.Equal((0, 11), (battle.Slots[D0].X, battle.Slots[D0].Y));
        Assert.Equal((12, 11), (battle.Slots[D0 + 12].X, battle.Slots[D0 + 12].Y));
        Assert.Equal((0, 10), (battle.Slots[D0 + 13].X, battle.Slots[D0 + 13].Y));
        AssertGridMatchesSlots(battle);
    }

    [Fact]
    public void The_fresh_battle_has_the_defender_to_move_and_its_first_half_round_set_up()
    {
        var attacker = BattleTestbed.Army("a", "north", 1, 1, 70, 0, 0, U("light_infantry", 1000, 1, "a0"));
        var defender = BattleTestbed.Army("d", "south", 2, 1, 70, 0, 0, U("heavy_infantry", 1000, 1, "d0"));
        var game = Game(SeatControl.Human, SeatControl.Human, attacker, defender);

        var battle = TacticalCopyIn.Start(game, "a", "d", Context, new ScriptedDraws(0, 0)).Battle;

        Assert.Equal(TacticalBattleState.DefenderSide, battle.SideToMove);
        Assert.False(battle.Placed);
        Assert.False(battle.IsOver);
        Assert.Equal(1, battle.Counter);
        Assert.Equal(Context.MovesOf(HI), battle.Slots[D0].Moves);
        Assert.IsType<TacticalSetUpEvent>(battle.Log[^1]);
    }
}
