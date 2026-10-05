using IC2.Engine.Battle.Tactical;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// A shot, <c>FUN_0043910C</c>: report <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §4, and §6
/// for the morale term, golden-master check 5; <c>docs/game-design.md</c>, "Shooting".
/// </summary>
public class ShootingTests
{
    [Fact]
    public void The_loss_is_two_draws_from_n_and_the_shooters_shots_and_moves_drop_and_its_target_clears()
    {
        // LI 6,000, q7, m80 shoots heavy cavalry (vuln 4) of 2,000 at distance 1:
        // base = 6,000 x 7 x 80 x 4 div (6,000 x 5 + 150,000) = 13,440,000 div 180,000 = 74;
        // distance 1 is not below LI's range 1, so no doubling; n = min(74, min(2,000, 1,000)) + 1 = 75.
        var state = Arena(
            (0, Unit(LI, 5, 5, 6000, quality: 7, morale: 80, target: D0 + 1)),
            (D0, Unit(HC, 5, 6, 2000, morale: 70)),
            (D0 + 1, Unit(LI, 6, 6, 5000)));

        var draws = new ScriptedDraws(74, 73);
        var shot = TacticalShooting.Shoot(state, 0, D0, Context, draws);

        Assert.Equal(new[] { 75, 75 }, draws.Bounds);
        var shooter = shot.Slots[0];
        Assert.Equal((6, 3, TacticalSlot.NoTarget), (shooter.Shots, shooter.Moves, shooter.Target));

        // loss 74 + 73 = 147; morale -= min(3, 147 x 35 div 2,001) = 2.
        var target = shot.Slots[D0];
        Assert.Equal(2000 - 147, target.Troops);
        Assert.Equal(68, target.Morale);
        Assert.Equal(new TacticalShotEvent(0, D0, 75, 147, 2, 1853, 68), shot.Log.OfType<TacticalShotEvent>().Single());
    }

    [Fact]
    public void The_morale_term_divides_by_the_troops_before_the_loss()
    {
        // Target HC of 300: n = min(74, min(2,000, 150)) + 1 = 151. Loss 9 + 8 = 17:
        // 17 x 35 = 595 div (300 + 1) = 1, where the troops after the loss would give 595 div 284 = 2.
        var state = Arena(
            (0, Unit(LI, 5, 5, 6000, quality: 7, morale: 80)),
            (D0, Unit(HC, 5, 6, 300, morale: 70)));

        var shot = TacticalShooting.Shoot(state, 0, D0, Context, new ScriptedDraws(9, 8));

        Assert.Equal(69, shot.Slots[D0].Morale);
        Assert.Equal(283, shot.Slots[D0].Troops);
    }

    [Fact]
    public void The_morale_loss_is_capped_at_3()
    {
        // Target HC of 1,000: n = min(74, 500) + 1 = 75; loss 148; 148 x 35 div 1,001 = 5, capped at 3.
        var state = Arena(
            (0, Unit(LI, 5, 5, 6000, quality: 7, morale: 80)),
            (D0, Unit(HC, 5, 6, 1000, morale: 70)));

        var shot = TacticalShooting.Shoot(state, 0, D0, Context, new ScriptedDraws(74, 74));

        Assert.Equal(67, shot.Slots[D0].Morale);
        Assert.Equal(Rules.ShotMoraleCap, 70 - shot.Slots[D0].Morale);
    }

    [Theory]
    // Archers (range 2): base 3,000 x 9 x 99 x 18 div 165,000 = 291, doubled to 582 only at distance 1.
    [InlineData(AR, 1, 583)]
    [InlineData(AR, 2, 292)]
    // Light infantry (range 1) at distance 1 is not below its range: no doubling.
    [InlineData(LI, 1, 292)]
    public void Only_archers_at_distance_1_double_the_base(int shooterType, int distance, int expectedBound)
    {
        var state = Arena(
            (0, Unit(shooterType, 5, 5, 3000, quality: 9, morale: 99)),
            (D0, Unit(LI, 5, 5 + distance, 10000, morale: 99)));

        var draws = new ScriptedDraws(0, 0);
        TacticalShooting.Shoot(state, 0, D0, Context, draws);

        Assert.Equal(new[] { expectedBound, expectedBound }, draws.Bounds);
    }

    [Fact]
    public void The_shot_bounds_min_is_taken_in_16_bits()
    {
        // LI 6,000, q7, m80 at LI: base = 6,000 x 7 x 80 x 18 div 180,000 = 336. The target holds 70,000,
        // so troops_t div 2 = 35,000, which as a signed 16-bit word is 35,000 - 65,536 = -30,536.
        // In 32 bits: n = min(336, min(2,000, 35,000)) + 1 = 337.
        // In 16 bits: min(2,000, -30,536) = -30,536; min(336, -30,536) = -30,536; n = -30,535.
        var shooter = Unit(LI, 5, 5, 6000, quality: 7, morale: 80);
        var target = Unit(LI, 5, 6, 70000, morale: 80);

        Assert.Equal(-30535, TacticalShooting.Bound(shooter, target, 1, Context));
        Assert.NotEqual(337, TacticalShooting.Bound(shooter, target, 1, Context));
        Assert.Equal(-30536, TacticalShooting.Min16(336, TacticalShooting.Min16(2000, 35000)));

        var draws = new ScriptedDraws(5, 6);
        var shot = TacticalShooting.Shoot(Arena((0, shooter), (D0, target)), 0, D0, Context, draws);
        Assert.Equal(new[] { -30535, -30535 }, draws.Bounds);
        Assert.Equal(70000 - 11, shot.Slots[D0].Troops);
    }

    [Fact]
    public void A_shot_below_16_bits_is_the_plain_min()
    {
        Assert.Equal(12, TacticalShooting.Min16(12, 30000));
        Assert.Equal(-1, TacticalShooting.Min16(-1, 5));
    }
}
