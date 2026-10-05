using IC2.Engine.Battle.Tactical;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// Integer semantics: report <c>2026-10-04-decompiled-tactical-battle-rules.md</c>, "Inferences, kept
/// apart" (the int32 wrap) and §4 (the 16-bit <c>min</c>); <c>docs/game-design.md</c>, "Integer
/// semantics".
/// </summary>
public class IntegerSemanticsTests
{
    [Fact]
    public void The_melees_troops_times_D_wraps_in_32_bits_above_23000_troops_against_a_strong_defender()
    {
        // LI 25,000 q5 m70 attacks HI 20,000 q9 m99. M[LI][HI] = 4, M[HI][LI] = 60.
        // A = 4 x 25,000 x (50 + 70) div 2,000 + 12 = 12,000,000 div 2,000 + 12 = 6,012.
        // D = 60 x 20,000 x (90 + 99) div 2,000 + 12 = 226,800,000 div 2,000 + 12 = 113,412.
        // troops_a x D = 25,000 x 113,412 = 2,835,300,000, above 2^31 - 1 = 2,147,483,647: as a 32-bit
        // integer it wraps to 2,835,300,000 - 4,294,967,296 = -1,459,667,296.
        // nA = (-1,459,667,296 div 6,012) div 12 + 1 = -242,792 div 12 + 1 = -20,232 + 1 = -20,231
        // (Delphi's div truncates toward zero, as C#'s does). In 64 bits it would be
        // (2,835,300,000 div 6,012) div 12 + 1 = 471,606 div 12 + 1 = 39,301.
        // nD = (20,000 x 6,012 div 113,412) div 10 + 1 = 1,060 div 10 + 1 = 107.
        var state = Arena(
            (0, Unit(LI, 3, 3, 25000, quality: 5, morale: 70, target: D0)),
            (D0, Unit(HI, 3, 4, 20000, quality: 9, morale: 99)));

        var draws = new ScriptedDraws(0, 0, 0, 0);
        var after = TacticalMelee.Resolve(state, 0, Context, draws);

        Assert.Equal(-1459667296, unchecked(25000 * 113412));
        var melee = after.Log.OfType<TacticalMeleeEvent>().Single();
        Assert.Equal((6012, 113412), (melee.AttackerPower, melee.DefenderPower));
        Assert.Equal(-20231, melee.AttackerBound);
        Assert.Equal(107, melee.DefenderBound);
        Assert.Equal(new[] { -20231, -20231, 107, 107 }, draws.Bounds);
    }

    [Fact]
    public void The_shot_bounds_min_differs_in_16_and_32_bits()
    {
        // LI 6,000 q7 m80 at LI 70,000: base = 6,000 x 7 x 80 x 18 div 180,000 = 336; troops_t div 2 =
        // 35,000, a signed 16-bit -30,536. 32-bit: min(336, min(2,000, 35,000)) + 1 = 337. 16-bit:
        // min(336, min(2,000, -30,536)) + 1 = -30,535.
        var shooter = Unit(LI, 5, 5, 6000, quality: 7, morale: 80);
        var target = Unit(LI, 5, 6, 70000);

        var bound = TacticalShooting.Bound(shooter, target, 1, Context);

        Assert.Equal(-30535, bound);
        Assert.Equal(337, Math.Min(336, Math.Min(2000, 35000)) + 1);
    }

    [Fact]
    public void Shots_may_go_below_0_since_the_counter_is_signed()
    {
        // The shot routine checks nothing (pass 2 of the computer general relies on it): a unit with 0
        // shots and 0 moves that is made to shoot ends at -1 and -1.
        var state = Arena((0, Unit(LI, 5, 5, 6000, shots: 0, moves: 0)), (D0, Unit(HI, 5, 6, 6000, morale: 99)));

        var after = TacticalShooting.Shoot(state, 0, D0, Context, new ScriptedDraws(0, 0));

        Assert.Equal((-1, -1), (after.Slots[0].Shots, after.Slots[0].Moves));
    }
}
