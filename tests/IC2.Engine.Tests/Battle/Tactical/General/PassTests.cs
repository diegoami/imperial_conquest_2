using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.General.GeneralTestbed;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>
/// T124 Done-when 3, passes 2 and 3: <c>FUN_0043A544</c> and <c>FUN_0043ABB4</c>
/// (<c>2026-10-04-decompiled-tactical-battle-rules.md</c> §7; <c>docs/game-design.md</c>, "The computer
/// general", "Pass 2" and "Pass 3").
/// </summary>
/// <remarks>
/// As in <see cref="EngageTests"/>, the attacker is a human nation with <em>Computer general</em> on and
/// its shots draw 0. Shot bounds are derived by hand from report §4: <c>base = troops × quality × morale ×
/// vuln[target] div (troops × 5 + 150000)</c>, <c>vuln</c> 18/2/18/15/4, doubled below the shooter's
/// range, then <c>min(base, min(troops div 3, theirs div 2)) + 1</c>.
/// </remarks>
public class PassTests
{
    private static TacticalBattleState General(params (int Slot, TacticalSlot Unit)[] units) =>
        Arena(units, attackerGeneral: true);

    private static TacticalBattleState GeneralAt(int counter, params (int Slot, TacticalSlot Unit)[] units) =>
        Arena(units, counter: counter, attackerGeneral: true);

    // ---- Pass 2 ----

    [Fact]
    public void Pass_2_shoots_the_enemy_with_the_smallest_troops_div_s_counting_s_div_4_for_a_shooter()
    {
        // Light infantry 3,000 at (5,5), adjacent to two light infantry. base = 3000 × 350 × 18 div 165000 = 114.
        //   slot 20, 1,300 troops, no shots: s = min(114, min(1000, 650)) + 1 = 115; key 1300 div 115 = 11.
        //   slot 21, 1,500 troops, 7 shots: s = 115, + 115 div 4 = 143; key 1500 div 143 = 10.
        // Slot 21 wins. Without the shooter's bonus it would score 1500 div 115 = 13 and lose; with the
        // bonus added to the key instead of to s, 13 + 28 = 41, and lose too.
        var state = General(
            (0, Unit(LI, 5, 5, 3000)),
            (D0, Unit(LI, 5, 6, 1300, shots: 0)),
            (D0 + 1, Unit(LI, 6, 5, 1500)));

        Assert.Equal(11, GeneralPassTwo.ShotKey(state, 0, D0, Context));
        Assert.Equal(10, GeneralPassTwo.ShotKey(state, 0, D0 + 1, Context));

        var journal = new List<GeneralDecision>();
        var after = GeneralPassTwo.Run(state, Context, new ZeroDraws(), journal);

        // It fires until its 4 moves are spent.
        Assert.Equal(Enumerable.Repeat((0, D0 + 1), 4), Shots(after));
        Assert.Equal(0, after.Slots[0].Moves);
        Assert.Equal(new GeneralDecision(0, GeneralStep.PassTwoShots, D0 + 1, 6, 5), journal[0]);
    }

    [Fact]
    public void Pass_2_ignores_enemies_out_of_range()
    {
        // The better key is at distance 2, beyond light infantry's range 1.
        var state = General(
            (0, Unit(LI, 5, 5, 3000)),
            (D0, Unit(LI, 5, 6, 1300, shots: 0)),
            (D0 + 1, Unit(LI, 7, 5, 1500)));

        var after = GeneralPassTwo.Run(state, Context, new ZeroDraws());

        Assert.All(Shots(after), s => Assert.Equal((0, D0), s));
    }

    [Fact]
    public void Pass_2s_shooting_loop_does_not_check_shots_so_they_go_below_0()
    {
        // One shot left and four moves: the unit qualifies (it has shots), then fires four times.
        var state = General((0, Unit(LI, 5, 5, 3000, shots: 1)), (D0, Unit(HI, 5, 6, 4000)));

        var after = GeneralPassTwo.Run(state, Context, new ZeroDraws());

        Assert.Equal(4, Shots(after).Length);
        Assert.Equal(-3, after.Slots[0].Shots);
        Assert.Equal(0, after.Slots[0].Moves);
    }

    [Fact]
    public void Pass_2_stops_firing_when_the_target_is_removed()
    {
        var state = General(
            (0, Unit(LI, 5, 5, 3000)),
            (D0, Unit(LI, 5, 6, 100, shots: 0)),
            (D0 + 1, Unit(HI, 13, 11, 4000)));

        var after = GeneralPassTwo.Run(state, Context, new ZeroDraws());

        Assert.Single(Shots(after));
        Assert.Equal(3, after.Slots[0].Moves);
    }

    [Fact]
    public void Pass_2_skips_a_unit_with_a_target_or_no_moves()
    {
        var state = General(
            (0, Unit(LI, 5, 5, 3000, target: D0)),
            (1, Unit(LI, 4, 5, 3000, moves: 0)),
            (D0, Unit(HI, 5, 6, 4000)));

        var after = GeneralPassTwo.Run(state, Context, new ZeroDraws());

        Assert.Empty(Shots(after));
        Assert.Equal(TacticalSlot.NoTarget, after.Slots[1].Target);
    }

    [Fact]
    public void Pass_2s_melee_target_needs_two_thirds_of_theirs_below_mine()
    {
        // Heavy infantry 3,000 (no shots) next to one enemy, focus 0: theirs' = theirs.
        //   4,000: 2 × 4000 div 3 = 2666 < 3000 → target.   4,500: 3000 < 3000 is false → none.
        var weak = GeneralPassTwo.Run(General((0, Unit(HI, 5, 5, 3000)), (D0, Unit(HI, 5, 6, 4000))), Context, NoDraws.Instance);
        var strong = GeneralPassTwo.Run(General((0, Unit(HI, 5, 5, 3000)), (D0, Unit(HI, 5, 6, 4500))), Context, NoDraws.Instance);

        Assert.Equal(D0, weak.Slots[0].Target);
        Assert.Equal(TacticalSlot.NoTarget, strong.Slots[0].Target);
    }

    [Fact]
    public void Pass_2s_melee_target_minimises_theirs_minus_focus_times_theirs()
    {
        // Two adjacent enemies: A (slot 20) 4,000 with focus 0 → 4000; B (slot 21) 9,000 targeted by own
        // slot 1 (which has its target, so pass 2 skips it): focus 1 → 9000 − 9000 = 0. B wins, and
        // 2 × 0 div 3 = 0 < 3000. With focus 2 it is 9000 − 18000 = −9000, smaller still.
        var one = General(
            (0, Unit(HI, 5, 5, 3000)), (1, Unit(HI, 7, 6, 3000, target: D0 + 1)),
            (D0, Unit(HI, 5, 6, 4000)), (D0 + 1, Unit(HI, 6, 6, 9000)));
        var journal = new List<GeneralDecision>();

        var after = GeneralPassTwo.Run(one, Context, NoDraws.Instance, journal);

        Assert.Equal(D0 + 1, after.Slots[0].Target);
        Assert.Equal(new GeneralDecision(0, GeneralStep.PassTwoTarget, D0 + 1, 6, 6), Assert.Single(journal));

        // Without the focus, A's 4,000 is the smaller.
        var none = General((0, Unit(HI, 5, 5, 3000)), (D0, Unit(HI, 5, 6, 4000)), (D0 + 1, Unit(HI, 6, 6, 9000)));
        Assert.Equal(D0, GeneralPassTwo.Run(none, Context, NoDraws.Instance).Slots[0].Target);
    }

    [Fact]
    public void Pass_2_computes_theirs_minus_focus_times_theirs_in_32_bits_at_focus_4()
    {
        // Enemy B, 30,000 troops, already targeted by four own slots (out of the battle, so pass 2 skips
        // them): 30000 − 4 × 30000 = −90,000 in 32 bits (the clone's integer semantics, T123), well below A's
        // 4,000, and 2 × −90000 div 3 = −60,000 < 3000.
        var state = General(
            (0, Unit(HI, 5, 5, 3000)),
            (1, Unit(HI, 0, 0, 0, target: D0 + 1)), (2, Unit(HI, 0, 1, 0, target: D0 + 1)),
            (3, Unit(HI, 0, 2, 0, target: D0 + 1)), (4, Unit(HI, 0, 3, 0, target: D0 + 1)),
            (D0, Unit(HI, 5, 6, 4000)), (D0 + 1, Unit(HI, 6, 6, 30000)));

        Assert.Equal(4, GeneralTargeting.Focus(state, 0, D0 + 1, Context));
        Assert.Equal(D0 + 1, GeneralPassTwo.Run(state, Context, NoDraws.Instance).Slots[0].Target);
    }

    [Fact]
    public void Pass_2_after_firing_still_takes_an_adjacent_melee_target()
    {
        // The report gives pass 2's melee step after the shooting with no second moves check: a light
        // infantry that spent its moves shooting still takes an adjacent target that passes the odds test.
        var state = General((0, Unit(LI, 5, 5, 3000)), (D0, Unit(HI, 5, 6, 4000)));

        var after = GeneralPassTwo.Run(state, Context, new ZeroDraws());

        Assert.Equal(0, after.Slots[0].Moves);
        Assert.Equal(D0, after.Slots[0].Target);
    }

    // ---- Pass 3: the threat move ----

    [Fact]
    public void Pass_3s_threat_order_puts_archers_with_shots_first_by_minus_their_shot_bound()
    {
        // Against my HI 3,000: LI 1,000 → 1000 × 350 × 4 div 10000 = 140; archers 1,500 with shots → their
        // shot at me, 1500 × 350 × 2 div 157500 = 6, + 1 = 7, so −7; HI 4,000 → 700; archers 1,000 with no
        // shots → M[Ar][HI] 3 → 105. Ascending: −7, 105, 140, 700.
        var state = General(
            (0, Unit(HI, 0, 0, 3000)),
            (D0, Unit(LI, 10, 10, 1000)),
            (D0 + 1, Unit(AR, 11, 10, 1500)),
            (D0 + 2, Unit(HI, 12, 10, 4000)),
            (D0 + 3, Unit(AR, 13, 10, 1000, shots: 0)));

        Assert.Equal(-7, GeneralPassThree.Threat(state, 0, D0 + 1, Context));
        Assert.Equal(new[] { D0 + 1, D0 + 3, D0, D0 + 2 }, GeneralPassThree.ThreatOrder(state, 0, Context));
    }

    [Fact]
    public void Pass_3s_threat_sort_is_the_originals_selection_sort_not_a_stable_one()
    {
        // Threats a 700, b 350, c 700, d 350 (HI 4,000 / 2,000 against my HI). The exchange sort:
        // i = 0: b < a swap → b a c d; i = 1: d < a swap → b d c a; i = 2: a < c no. So b, d, c, a,
        // where a stable sort gives b, d, a, c.
        var state = General(
            (0, Unit(HI, 0, 0, 3000)),
            (D0, Unit(HI, 10, 10, 4000)),
            (D0 + 1, Unit(HI, 11, 10, 2000)),
            (D0 + 2, Unit(HI, 12, 10, 4000)),
            (D0 + 3, Unit(HI, 13, 10, 2000)));

        Assert.Equal(new[] { D0 + 1, D0 + 3, D0 + 2, D0 }, GeneralPassThree.ThreatOrder(state, 0, Context));
    }

    [Fact]
    public void Pass_3_moves_toward_the_first_enemy_by_threat_with_a_clear_line_or_a_box_cell()
    {
        // HI at (13,0). By threat: the corner (13,11) 1,000 → 175, then (12,10) 2,000 → 350, (12,11)
        // 3,000 → 525, (13,10) 4,000 → 700. The corner's line passes (13,10) and its box has no empty
        // cell, so it is passed over; (12,10)'s line, (13,1) … (13,4), (12,5) … (12,9), is clear. The walk
        // takes heavy infantry's 2 moves: (13,1), (13,2).
        var state = General(
            (0, Unit(HI, 13, 0, 3000)),
            (D0, Unit(HI, 13, 11, 1000)), (D0 + 1, Unit(HI, 12, 10, 2000)),
            (D0 + 2, Unit(HI, 12, 11, 3000)), (D0 + 3, Unit(HI, 13, 10, 4000)));
        var journal = new List<GeneralDecision>();

        var after = GeneralPassThree.ThreatMove(state, Context, NoDraws.Instance, journal);

        Assert.Equal(new GeneralDecision(0, GeneralStep.ThreatLine, D0 + 1, 12, 10), Assert.Single(journal));
        Assert.Equal(new[] { (13, 1), (13, 2) }, Path(after, 0));
    }

    [Fact]
    public void Pass_3_takes_a_box_cell_when_the_first_enemys_line_is_blocked()
    {
        // The engage box board: HI at (0,0), own unit at (4,4), the only enemy at (5,5): its box gives (4,5).
        var state = General((0, Unit(HI, 0, 0, 3000)), (1, Unit(HI, 4, 4, 3000, moves: 0)), (D0, Unit(HI, 5, 5, 4000)));
        var journal = new List<GeneralDecision>();

        var after = GeneralPassThree.ThreatMove(state, Context, NoDraws.Instance, journal);

        Assert.Equal(new GeneralDecision(0, GeneralStep.ThreatBox, D0, 4, 5), Assert.Single(journal));
        Assert.Equal(new[] { (1, 1), (2, 2) }, Path(after, 0));
    }

    // ---- Pass 3: the flank ----

    private static (TacticalBattleState State, List<GeneralDecision> Journal, ScriptedDraws Draws) Flank(
        TacticalBattleState state, params int[] draws)
    {
        var journal = new List<GeneralDecision>();
        var scripted = new ScriptedDraws(draws);
        return (GeneralPassThree.Flank(state, Context, scripted, journal), journal, scripted);
    }

    [Fact]
    public void The_flank_goes_right_when_the_enemy_stands_right_and_up_when_the_vertical_count_ties()
    {
        // HC (5 moves, untouched) at (6,5); enemies at (8,8) and (10,2): minX 8 ≤ 13 − 10 = 3 is false, so
        // h = +1; Random(3) = 1 keeps it. One enemy below (y 8), one above (y 2): 0 → v = −1. The first try
        // is (6 + 5, 5 − 5) = (11,0), empty, on the clear diagonal (7,4) … (10,1).
        var state = General((0, Unit(HC, 6, 5, 1000)), (D0, Unit(HI, 8, 8, 4000)), (D0 + 1, Unit(HI, 10, 2, 4000)));

        var (after, journal, draws) = Flank(state, 1);

        Assert.Equal(new[] { 3 }, draws.Bounds);
        Assert.Equal(new GeneralDecision(0, GeneralStep.Flank, -1, 11, 0), Assert.Single(journal));
        Assert.Equal(new[] { (7, 4), (8, 3), (9, 2), (10, 1), (11, 0) }, Path(after, 0));
    }

    [Fact]
    public void Random_3_equal_to_0_flips_the_flank()
    {
        // The same board, Random(3) = 0: h = −1, so (6 − 5, 0) = (1,0).
        var state = General((0, Unit(HC, 6, 5, 1000)), (D0, Unit(HI, 8, 8, 4000)), (D0 + 1, Unit(HI, 10, 2, 4000)));

        var (after, journal, _) = Flank(state, 0);

        Assert.Equal(new GeneralDecision(0, GeneralStep.Flank, -1, 1, 0), Assert.Single(journal));
        Assert.Equal((1, 0), (after.Slots[0].X, after.Slots[0].Y));
    }

    [Fact]
    public void The_flank_ties_minX_to_minus_1_and_follows_the_majority_below()
    {
        // Enemies at (3,8) and (10,9): minX 3 ≤ 13 − 10 = 3, so h = −1 (the tie goes left). Both below:
        // v = +1. HC 5 moves at (7,5): (2,10), the line (6,6), (5,7), (4,8), (3,9) clear.
        var state = General((0, Unit(HC, 7, 5, 1000)), (D0, Unit(HI, 3, 8, 4000)), (D0 + 1, Unit(HI, 10, 9, 4000)));

        var (_, journal, _) = Flank(state, 2);

        Assert.Equal(new GeneralDecision(0, GeneralStep.Flank, -1, 2, 10), Assert.Single(journal));
    }

    [Fact]
    public void The_flank_clamps_to_the_board()
    {
        // HC at (2,5); enemies at (5,8) and (6,9): minX 5 ≤ 13 − 6 = 7 → h = −1; both below → v = +1.
        // (2 − 5, 5 + 5) = (−3, 10), clamped to (0,10); the walk: (2,6), (1,7), (1,8), (0,9), (0,10).
        var state = General((0, Unit(HC, 2, 5, 1000)), (D0, Unit(HI, 5, 8, 4000)), (D0 + 1, Unit(HI, 6, 9, 4000)));

        var (after, journal, _) = Flank(state, 1);

        Assert.Equal(new GeneralDecision(0, GeneralStep.Flank, -1, 0, 10), Assert.Single(journal));
        Assert.Equal(new[] { (2, 6), (1, 7), (1, 8), (0, 9), (0, 10) }, Path(after, 0));
    }

    [Fact]
    public void The_flank_tries_the_row_when_the_diagonal_cell_is_taken()
    {
        // HC at (10,5); enemies at (2,8) and (3,2): h = −1 (2 ≤ 10), v = −1 (one below, one above). The
        // first try (5,0) holds an own unit, so the second, (5,5), along the row: (9,5) … (5,5).
        var state = General(
            (0, Unit(HC, 10, 5, 1000)), (1, Unit(HI, 5, 0, 3000, moves: 0)),
            (D0, Unit(HI, 2, 8, 4000)), (D0 + 1, Unit(HI, 3, 2, 4000)));

        var (after, journal, _) = Flank(state, 1);

        Assert.Equal(new GeneralDecision(0, GeneralStep.Flank, -1, 5, 5), Assert.Single(journal));
        Assert.Equal(new[] { (9, 5), (8, 5), (7, 5), (6, 5), (5, 5) }, Path(after, 0));
    }

    [Fact]
    public void The_flank_draws_even_when_neither_try_qualifies()
    {
        // Both tries blocked: (5,0) taken, and (5,5)'s row line passes an own unit at (7,5).
        var state = General(
            (0, Unit(HC, 10, 5, 1000)), (1, Unit(HI, 5, 0, 3000, moves: 0)), (2, Unit(HI, 7, 5, 3000, moves: 0)),
            (D0, Unit(HI, 2, 8, 4000)), (D0 + 1, Unit(HI, 3, 2, 4000)));

        var (after, journal, draws) = Flank(state, 1);

        Assert.Equal(new[] { 3 }, draws.Bounds);
        Assert.Empty(journal);
        Assert.Empty(Path(after, 0));
    }

    [Fact]
    public void Only_a_unit_with_untouched_moves_or_the_slow_advances_1_flanks()
    {
        var enemies = new[] { (D0, Unit(HI, 2, 8, 4000)), (D0 + 1, Unit(HI, 3, 2, 4000)) };

        // HC with 3 of its 5 moves: no flank, no draw.
        Assert.Empty(Flank(General([(0, Unit(HC, 10, 5, 1000, moves: 3)), .. enemies]), []).Draws.Bounds);

        // LC with the slow advance's 1 move, counter 3 (below 10): it flanks.
        Assert.Equal(new[] { 3 }, Flank(GeneralAt(3, [(0, Unit(LC, 10, 5, 2500, moves: 1)), .. enemies]), 1).Draws.Bounds);

        // The same at counter 10: no.
        Assert.Empty(Flank(GeneralAt(10, [(0, Unit(LC, 10, 5, 2500, moves: 1)), .. enemies]), []).Draws.Bounds);

        // Heavy infantry with 1 of its 2 moves: no, whatever the counter.
        Assert.Empty(Flank(GeneralAt(3, [(0, Unit(HI, 10, 5, 3000, moves: 1)), .. enemies]), []).Draws.Bounds);

        // A unit with a target: no.
        Assert.Empty(Flank(General([(0, Unit(HC, 10, 5, 1000, target: D0)), .. enemies]), []).Draws.Bounds);
    }

    [Fact]
    public void Pass_3_runs_the_threat_move_then_pass_2_then_the_flank_then_pass_2()
    {
        // HC 3,000 (5 moves) at (0,5), one enemy HI 4,000 at (4,9). The threat move walks toward it,
        // (1,6), (2,7), (3,8), stopping where the path reaches the enemy's cell, with 2 moves left. Pass 2
        // then finds it adjacent: 2 × 4000 div 3 = 2666 < 3000, so it takes the target. The flank skips it
        // (a target) and draws nothing; the second pass 2 skips it too.
        var state = General((0, Unit(HC, 0, 5, 3000)), (D0, Unit(HI, 4, 9, 4000)));
        var journal = new List<GeneralDecision>();
        var draws = new ScriptedDraws();

        var after = GeneralPassThree.Run(state, Context, draws, journal);

        Assert.Equal(new[] { GeneralStep.ThreatLine, GeneralStep.PassTwoTarget }, journal.Select(j => j.Step).ToArray());
        Assert.Equal(new[] { (1, 6), (2, 7), (3, 8) }, Path(after, 0));
        Assert.Equal(2, after.Slots[0].Moves);
        Assert.Equal(D0, after.Slots[0].Target);
        Assert.Empty(draws.Bounds);
    }

    [Fact]
    public void Pass_3_flanks_a_unit_its_threat_move_could_not_move()
    {
        // HC at (13,0), the only enemy in the corner (13,11) fenced by own units at (12,10), (12,11) and
        // (13,10): no clear line and no box cell, so the threat move passes. Pass 2: nothing in reach. The
        // flank: minX 13 ≤ 0 is false → h = +1, Random(3) = 1 keeps it; the enemy is below → v = +1:
        // (18, 5) clamps to (13,5), on the clear column (13,1) … (13,4). Then pass 2 again.
        var state = General(
            (0, Unit(HC, 13, 0, 1000)),
            (1, Unit(HI, 12, 10, 3000, moves: 0)), (2, Unit(HI, 12, 11, 3000, moves: 0)), (3, Unit(HI, 13, 10, 3000, moves: 0)),
            (D0, Unit(HI, 13, 11, 4000)));
        var journal = new List<GeneralDecision>();
        var draws = new ScriptedDraws(1);

        var after = GeneralPassThree.Run(state, Context, draws, journal);

        Assert.Equal(new[] { 3 }, draws.Bounds);
        Assert.Equal(new GeneralDecision(0, GeneralStep.Flank, -1, 13, 5), Assert.Single(journal));
        Assert.Equal(new[] { (13, 1), (13, 2), (13, 3), (13, 4), (13, 5) }, Path(after, 0));
    }
}
