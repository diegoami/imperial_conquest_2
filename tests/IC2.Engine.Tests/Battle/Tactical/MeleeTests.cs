using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// The melee, <c>FUN_004393EC</c>: report <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §5 (the
/// formula and the real matrix), golden-master checks 6 and 7; <c>docs/game-design.md</c>, "Melee", and
/// Combat's "Read by the tactical battle" (the matrix).
/// </summary>
public class MeleeTests
{
    /// <summary>
    /// Heavy infantry 5,000, q5, m70 against the same, with <paramref name="focus"/> attacker slots
    /// targeting the defender: slot 0 live, the others empty (a dead slot's target still counts).
    /// A = D = 5 x 5,000 x 120 div 2,000 + 12 = 1,512; nA = 5,000 div 12 + 1 = 417; nD = 5,000 div 10 + 1 = 501.
    /// </summary>
    private static TacticalBattleState HiAgainstHi(int focus, int attackerMorale = 70)
    {
        var units = new List<(int, TacticalSlot)>
        {
            (0, Unit(HI, 3, 3, 5000, quality: 5, morale: attackerMorale, target: D0)),
            (D0, Unit(HI, 3, 4, 5000, quality: 5, morale: 70)),
        };
        for (var s = 1; s < focus; s++)
        {
            units.Add((s, TacticalSlot.Empty with { Target = D0 }));
        }

        return Arena(units);
    }

    [Theory]
    // f = 1: la = 800 x 4 div 5 = 640 + 1; ld = 1,000 x 7 div 5 = 1,400 + 1.
    [InlineData(1, 1, 641, 1401)]
    // f = 2: la = 800 x 3 div 5 = 480 + 1; ld = 1,000 x 9 div 5 = 1,800 + 1.
    [InlineData(2, 2, 481, 1801)]
    // f = 4: la = 800 x 1 div 5 = 160 + 1; ld = 1,000 x 13 div 5 = 2,600, capped at 40 % of 5,000 = 2,000, + 1.
    [InlineData(4, 4, 161, 2001)]
    // f = 5 targeting slots is capped at 4: the same as f = 4.
    [InlineData(5, 4, 161, 2001)]
    public void The_focus_scales_both_losses_and_is_capped_at_4(int targeting, int focus, int attackerLoss, int defenderLoss)
    {
        var draws = new ScriptedDraws(400, 400, 500, 500);
        var after = TacticalMelee.Resolve(HiAgainstHi(targeting), 0, Context, draws);

        // Four draws: attacker, attacker, defender, defender.
        Assert.Equal(new[] { 417, 417, 501, 501 }, draws.Bounds);
        var melee = after.Log.OfType<TacticalMeleeEvent>().Single();
        Assert.Equal((focus, 1512, 1512, 417, 501), (melee.Focus, melee.AttackerPower, melee.DefenderPower, melee.AttackerBound, melee.DefenderBound));
        Assert.Equal((attackerLoss, defenderLoss), (melee.AttackerLoss, melee.DefenderLoss));
        Assert.Equal(5000 - attackerLoss, after.Slots[0].Troops);
        Assert.Equal(5000 - defenderLoss, after.Slots[D0].Troops);
    }

    [Fact]
    public void The_side_with_the_smaller_troops_div_loss_takes_minus_3_and_the_other_plus_2_capped_at_99()
    {
        // Attacker morale 98: A = 5 x 5,000 x 148 div 2,000 + 12 = 1,862, so nA = (5,000 x 1,512 div 1,862)
        // div 12 + 1 = 339 and nD = (5,000 x 1,862 div 1,512) div 10 + 1 = 616. f = 1, draws 300, 300,
        // 500, 500: la = 480 + 1 = 481, ld = 1,400 + 1 = 1,401. 5,000 div 1,401 = 3 < 5,000 div 481 = 10:
        // the defender lost the larger fraction. Attacker 98 + 2 -> 99 (cap), defender 70 - 3 = 67.
        var draws = new ScriptedDraws(300, 300, 500, 500);
        var after = TacticalMelee.Resolve(HiAgainstHi(1, attackerMorale: 98), 0, Context, draws);

        Assert.Equal(new[] { 339, 339, 616, 616 }, draws.Bounds);

        Assert.Equal(99, after.Slots[0].Morale);
        Assert.Equal(67, after.Slots[D0].Morale);
    }

    [Fact]
    public void A_tie_in_troops_div_loss_goes_against_the_attacker()
    {
        // f = 1. la = (416 + 416) x 4 div 5 = 665, + 1 = 666; ld = (238 + 237) x 7 div 5 = 665, + 1 = 666.
        // 5,000 div 666 = 7 on both sides: the attacker takes -3, the defender +2.
        var after = TacticalMelee.Resolve(HiAgainstHi(1), 0, Context, new ScriptedDraws(416, 416, 238, 237));

        var melee = after.Log.OfType<TacticalMeleeEvent>().Single();
        Assert.Equal((666, 666), (melee.AttackerLoss, melee.DefenderLoss));
        Assert.Equal(67, after.Slots[0].Morale);
        Assert.Equal(72, after.Slots[D0].Morale);
    }

    [Fact]
    public void The_attackers_loss_reverses_the_morale_when_it_lost_the_larger_fraction()
    {
        // f = 1. la = 832 x 4 div 5 = 665 + 1 = 666 -> 5,000 div 666 = 7; ld = 0 + 1 = 1 -> 5,000.
        // 5,000 < 7 is false: the attacker -3, the defender +2.
        var after = TacticalMelee.Resolve(HiAgainstHi(1), 0, Context, new ScriptedDraws(416, 416, 0, 0));

        Assert.Equal(67, after.Slots[0].Morale);
        Assert.Equal(72, after.Slots[D0].Morale);
    }

    [Fact]
    public void The_defenders_loss_is_capped_at_30000_before_the_40_percent_cap_and_its_plus_1()
    {
        // HI 10,000 (q0, m20) attacks LI 80,000 (q0, m20); M[HI][LI] = 60, M[LI][HI] = 4.
        // A = 60 x 10,000 x 20 div 2,000 + 12 = 6,012; D = 4 x 80,000 x 20 div 2,000 + 12 = 3,212.
        // nA = (10,000 x 3,212 div 6,012) div 12 + 1 = 5,342 div 12 + 1 = 446.
        // nD = (80,000 x 6,012 div 3,212) div 10 + 1 = 149,738 div 10 + 1 = 14,974.
        // ld = min(30,000, (14,973 + 14,973) x 7 div 5 = 41,924) = 30,000; 40 % of 80,000 = 32,000; + 1.
        var state = Arena(
            (0, Unit(HI, 3, 3, 10000, quality: 0, morale: 20, target: D0)),
            (D0, Unit(LI, 3, 4, 80000, quality: 0, morale: 20)),
            (D0 + 1, Unit(LI, 9, 9, 5000, morale: 99)));

        // The four exchange draws; then Rout(attacker) at morale 22 draws twice (21 + 21 > 29: it holds);
        // Rout(defender) at morale 17 removes it without a draw.
        var draws = new ScriptedDraws(0, 0, 14973, 14973, 21, 21);
        var after = TacticalMelee.Resolve(state, 0, Context, draws);

        var melee = after.Log.OfType<TacticalMeleeEvent>().Single();
        Assert.Equal((6012, 3212, 446, 14974), (melee.AttackerPower, melee.DefenderPower, melee.AttackerBound, melee.DefenderBound));
        Assert.Equal((1, 30001), (melee.AttackerLoss, melee.DefenderLoss));
        Assert.Equal(Context.Melee.MeleeLossHardCap + Context.Melee.MeleeLossCapOffset, melee.DefenderLoss);
        Assert.Equal(new[] { 446, 446, 14974, 14974, 22, 22 }, draws.Bounds);
    }

    [Fact]
    public void The_attackers_loss_is_capped_at_30000_too()
    {
        // LI 100,000 (q0, m20) attacks HI 16,000 (q0, m20): A = 4 x 100,000 x 20 div 2,000 + 12 = 4,012;
        // D = 60 x 16,000 x 20 div 2,000 + 12 = 9,612. nA = (100,000 x 9,612 div 4,012) div 12 + 1
        // = 239,581 div 12 + 1 = 19,966. la = min(30,000, 39,930 x 4 div 5 = 31,944) = 30,000; 40 % is
        // 40,000; + 1 = 30,001.
        var state = Arena(
            (0, Unit(LI, 3, 3, 100000, quality: 0, morale: 20, target: D0)),
            (D0, Unit(HI, 3, 4, 16000, quality: 0, morale: 20)));

        var draws = new ScriptedDraws(19965, 19965, 0, 0, 0, 0);
        var after = TacticalMelee.Resolve(state, 0, Context, draws);

        var melee = after.Log.OfType<TacticalMeleeEvent>().First();
        Assert.Equal(19966, melee.AttackerBound);
        Assert.Equal(30001, melee.AttackerLoss);
    }

    [Fact]
    public void The_real_matrix_makes_a_light_infantry_attack_on_heavy_infantry_hit_the_attackers_cap()
    {
        // Report §5: M[LI][HI] = 4, M[HI][LI] = 60. LI 5,000 q5 m70 against HI 5,000 q5 m70:
        // A = 4 x 5,000 x 120 div 2,000 + 12 = 1,212; D = 60 x 5,000 x 120 div 2,000 + 12 = 18,012;
        // nA = (5,000 x 18,012 div 1,212) div 12 + 1 = 74,306 div 12 + 1 = 6,193.
        // At the top of the range, la = (6,192 + 6,192) x 4 div 5 = 9,907, capped at 40 % = 2,000, + 1.
        Assert.Equal(4, Context.Matrix(LI, HI));
        Assert.Equal(60, Context.Matrix(HI, LI));

        var state = Arena(
            (0, Unit(LI, 3, 3, 5000, quality: 5, morale: 70, target: D0)),
            (D0, Unit(HI, 3, 4, 5000, quality: 5, morale: 70)));

        var draws = new ScriptedDraws(6192, 6192, 0, 0);
        var after = TacticalMelee.Resolve(state, 0, Context, draws);

        var melee = after.Log.OfType<TacticalMeleeEvent>().Single();
        Assert.Equal((1212, 18012, 6193), (melee.AttackerPower, melee.DefenderPower, melee.AttackerBound));
        Assert.Equal(2001, melee.AttackerLoss);
        Assert.Equal(5000 * Context.Melee.MeleeLossCapPercent / 100 + Context.Melee.MeleeLossCapOffset, melee.AttackerLoss);
    }

    [Fact]
    public void The_matrix_is_read_from_the_ruleset_so_two_matrices_give_different_losses()
    {
        var state = Arena(
            (0, Unit(LI, 3, 3, 5000, quality: 5, morale: 70, target: D0)),
            (D0, Unit(HI, 3, 4, 5000, quality: 5, morale: 70)));

        // A second ruleset whose matrix is flat 10s: A = D = 10 x 5,000 x 120 div 2,000 + 12 = 3,012.
        var flat = ValueList.From(Enumerable.Range(0, 5).Select(_ => ValueList.From(Enumerable.Repeat(10, 5))));
        var other = ToyRuleset with
        {
            Combat = ToyRuleset.Combat with
            {
                DetailedResolver = ToyRuleset.Combat.DetailedResolver with { TypeEffectiveness = flat },
            },
        };
        var otherContext = TacticalContext.From(other);

        var real = TacticalMelee.Resolve(state, 0, Context, new DelphiBattleDraws(12345)).Log.OfType<TacticalMeleeEvent>().Single();
        var changed = TacticalMelee.Resolve(state, 0, otherContext, new DelphiBattleDraws(12345)).Log.OfType<TacticalMeleeEvent>().Single();

        Assert.Equal((3012, 3012), (changed.AttackerPower, changed.DefenderPower));
        Assert.NotEqual((real.AttackerLoss, real.DefenderLoss), (changed.AttackerLoss, changed.DefenderLoss));
    }

    [Fact]
    public void Only_slots_with_troops_a_target_and_a_live_target_fight_in_slot_order()
    {
        // Slot 0: no target. Slot 1: a dead target. Slot 2: fights D0. Slot 3: fights D0 + 2.
        var state = Arena(
            (0, Unit(HI, 0, 3, 5000)),
            (1, Unit(HI, 1, 3, 5000, target: D0 + 1)),
            (2, Unit(HI, 2, 3, 5000, target: D0)),
            (3, Unit(HI, 3, 3, 5000, target: D0 + 2)),
            (D0, Unit(HI, 2, 4, 5000)),
            (D0 + 2, Unit(HI, 3, 4, 5000)));

        var after = TacticalMelee.Resolve(state, 0, Context, new ScriptedDraws(0, 0, 0, 0, 0, 0, 0, 0));

        Assert.Equal(new[] { (2, D0), (3, D0 + 2) }, after.Log.OfType<TacticalMeleeEvent>().Select(m => (m.Attacker, m.Defender)));
    }
}
