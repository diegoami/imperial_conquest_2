using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>
/// T124 Done-when 2: pass 1's target choice, <c>FUN_00439E08</c>
/// (<c>2026-10-04-decompiled-tactical-battle-rules.md</c> §7, "Target choice", "The target score";
/// <c>docs/game-design.md</c>, "The computer general", "Pass 1").
/// </summary>
/// <remarks>
/// Scores are derived by hand from the report's §5 matrix, <c>M[attacker][defender]</c>: LI row 15 4 20 5 3,
/// HI 60 5 65 15 8, archers 10 3 18 5 3, LC 25 8 28 15 8, HC 18 12 20 12 8. Every enemy here has quality 5
/// and morale 70, so <c>v = troops × 350 × M[theirs][mine] div 10000</c>.
/// </remarks>
public class TargetChoiceTests
{
    private static TacticalSlot Enemy(int type, int troops) => Unit(type, 10, 10, troops, quality: 5, morale: 70);

    private static int Choose(TacticalBattleState state, int slot = 0) => GeneralTargeting.Choose(state, slot, Context).Target;

    private static TacticalBattleState WithClaims(TacticalBattleState state, int slot, int claims)
    {
        var all = state.GeneralClaims.ToArray();
        all[slot] = claims;
        return state with { GeneralClaims = ValueList.From(all) };
    }

    [Fact]
    public void The_score_is_troops_quality_morale_and_their_matrix_entry_against_mine_div_10000()
    {
        // An enemy light infantry of 5,000 against my heavy infantry: 5000 × 5 × 70 × M[LI][HI] (4) div 10000 = 700.
        var state = Arena((0, Unit(HI, 0, 0, 3000)), (D0, Enemy(LI, 5000)));

        Assert.Equal(700, GeneralTargeting.Score(state, 0, D0, Context));
    }

    [Fact]
    public void Light_infantry_prefers_archers_then_light_infantry_then_any()
    {
        // Against my LI: LC 1,000 → 1000 × 350 × 25 div 10000 = 875; LI 5,000 → × 15 = 2625;
        // archers 3,000 → × 10 = 1050; HC 2,000 → × 18 = 1260.
        var lc = (D0, Enemy(LC, 1000));
        var li = (D0 + 1, Enemy(LI, 5000));
        var ar = (D0 + 2, Enemy(AR, 3000));
        var hc = (D0 + 3, Enemy(HC, 2000));
        var me = (0, Unit(LI, 0, 0, 5000));

        Assert.Equal(D0 + 2, Choose(Arena(me, lc, li, ar, hc)));   // archers, though not the lowest score
        Assert.Equal(D0 + 1, Choose(Arena(me, lc, li, hc)));       // then light infantry
        Assert.Equal(D0, Choose(Arena(me, lc, hc)));               // then any: LC 875 < HC 1260
    }

    [Fact]
    public void Heavy_infantry_and_heavy_cavalry_prefer_heavy_infantry_then_any()
    {
        // My HI: enemy LI 1,000 → 1000 × 350 × 4 div 10000 = 140; enemy HI 4,000 → × 5 = 700.
        // My HC: enemy LI → M[LI][HC] 3 → 105; enemy HI → M[HI][HC] 8 → 1120.
        var li = (D0, Enemy(LI, 1000));
        var hi = (D0 + 1, Enemy(HI, 4000));
        var lc = (D0 + 2, Enemy(LC, 1000));

        Assert.Equal(D0 + 1, Choose(Arena((0, Unit(HI, 0, 0, 3000)), li, hi, lc)));
        Assert.Equal(D0 + 1, Choose(Arena((0, Unit(HC, 0, 0, 1000)), li, hi, lc)));

        // With no heavy infantry, any: my HI scores LI 140 and LC 1000 × 350 × 15 div 10000 = 525.
        Assert.Equal(D0, Choose(Arena((0, Unit(HI, 0, 0, 3000)), li, lc)));
    }

    [Fact]
    public void Archers_and_light_cavalry_take_any_by_the_lowest_score()
    {
        // My archers: HI 4,000 → 4000 × 350 × 65 div 10000 = 9100; LI 1,000 → × 20 = 700.
        // My LC: HI → × 15 = 2100; LI → × 5 = 175.
        var hi = (D0, Enemy(HI, 4000));
        var li = (D0 + 1, Enemy(LI, 1000));

        Assert.Equal(D0 + 1, Choose(Arena((0, Unit(AR, 0, 0, 1000)), hi, li)));
        Assert.Equal(D0 + 1, Choose(Arena((0, Unit(LC, 0, 0, 1000)), hi, li)));
    }

    [Fact]
    public void The_score_is_divided_by_c_plus_1_below_the_claim_limit_and_doubled_at_it()
    {
        // My HI against two enemy HI: A (slot 20) 4,000 → 700; B (slot 21) 6,000 → 1050. Own slot 1, out of
        // the battle (troops 0), targets B: the focus counts every own slot "alive or not".
        var me = (0, Unit(HI, 0, 0, 3000));
        var a = (D0, Enemy(HI, 4000));
        var b = (D0 + 1, Enemy(HI, 6000));
        var deadFocus = (1, Unit(HI, 1, 0, 0, target: D0 + 1));

        Assert.Equal(D0, Choose(Arena(me, a, b)));                                // c = 0: 700 < 1050
        Assert.Equal(1, GeneralTargeting.Focus(Arena(me, a, b, deadFocus), 0, D0 + 1, Context));
        Assert.Equal(D0 + 1, Choose(Arena(me, a, b, deadFocus)));                 // c = 1: 1050 div 2 = 525 < 700
        Assert.Equal(D0 + 1, Choose(WithClaims(Arena(me, a, b, deadFocus), D0 + 1, 2))); // c = 3: 1050 div 4 = 262
        Assert.Equal(D0, Choose(WithClaims(Arena(me, a, b, deadFocus), D0 + 1, 3)));     // c = 4: 1050 × 2 = 2100
        Assert.Equal(D0, Choose(WithClaims(Arena(me, a, b), D0 + 1, 4)));                // claims alone, c = 4
        Assert.Equal(D0 + 1, Choose(WithClaims(Arena(me, a, b), D0 + 1, 1)));            // claims alone, c = 1
    }

    [Fact]
    public void Ties_go_to_the_first_enemy_in_slot_order()
    {
        var state = Arena((0, Unit(HI, 0, 0, 3000)), (D0 + 3, Enemy(HI, 4000)), (D0 + 5, Enemy(HI, 4000)));

        Assert.Equal(D0 + 3, Choose(state));

        // 4000 → 700 and 4002 → 700 too (4002 × 350 × 5 = 7,003,500 div 10000): still a tie, still the first.
        var near = Arena((0, Unit(HI, 0, 0, 3000)), (D0 + 3, Enemy(HI, 4002)), (D0 + 5, Enemy(HI, 4000)));
        Assert.Equal(D0 + 3, Choose(near));
    }

    [Fact]
    public void The_claim_is_recorded_on_the_chosen_enemy_and_steers_the_next_unit()
    {
        // Two own HI choose in turn among enemy HI A (700) and B (6,000 → 1050). The first takes A and
        // claims it; for the second, A scores 700 div 2 = 350, still the lowest, and A's claims reach 2.
        var state = Arena(
            (0, Unit(HI, 0, 0, 3000)), (1, Unit(HI, 1, 0, 3000)),
            (D0, Enemy(HI, 4000)), (D0 + 1, Enemy(HI, 6000)));

        var first = GeneralTargeting.Choose(state, 0, Context);
        Assert.Equal(D0, first.Target);
        Assert.Equal(1, first.State.GeneralClaims[D0]);

        var second = GeneralTargeting.Choose(first.State, 1, Context);
        Assert.Equal(D0, second.Target);
        Assert.Equal(2, second.State.GeneralClaims[D0]);
        Assert.Equal(0, second.State.GeneralClaims[D0 + 1]);
    }

    [Fact]
    public void The_claims_are_cleared_at_the_half_round_setup()
    {
        var state = WithClaims(WithClaims(Arena((0, Unit(HI, 0, 0, 3000)), (D0, Enemy(HI, 4000))), D0, 3), 5, 1);

        var setUp = TacticalHalfRound.Setup(state, Context);

        Assert.All(setUp.GeneralClaims, c => Assert.Equal(0, c));
    }

    [Fact]
    public void With_no_live_enemy_there_is_no_target_and_no_claim()
    {
        var state = Arena((0, Unit(HI, 0, 0, 3000)));

        var choice = GeneralTargeting.Choose(state, 0, Context);

        Assert.Equal(-1, choice.Target);
        Assert.All(choice.State.GeneralClaims, c => Assert.Equal(0, c));
    }
}
