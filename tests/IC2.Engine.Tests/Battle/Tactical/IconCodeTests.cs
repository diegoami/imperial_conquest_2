using IC2.Engine.Battle.Tactical;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// The grid's icon code, <c>FUN_00437C40</c> / <c>TBattleMap_PrintSquare</c>: report
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §1 ("Grid") and §10 (the size thresholds),
/// golden-master check 3; <c>docs/game-design.md</c>, "The board and the battle state".
/// </summary>
public class IconCodeTests
{
    [Theory]
    // Light infantry, std 15,000: thresholds 5,000 and 10,000.
    [InlineData(LI, 4999, 0)]
    [InlineData(LI, 5000, 1)]
    [InlineData(LI, 9999, 1)]
    [InlineData(LI, 10000, 2)]
    // Heavy infantry, std 6,000: 2,000 and 4,000.
    [InlineData(HI, 1999, 3)]
    [InlineData(HI, 2000, 4)]
    [InlineData(HI, 3999, 4)]
    [InlineData(HI, 4000, 5)]
    // Archers, std 3,500 (div 3 = 1,166): 1,166 and 2,332.
    [InlineData(AR, 1165, 6)]
    [InlineData(AR, 1166, 7)]
    [InlineData(AR, 2331, 7)]
    [InlineData(AR, 2332, 8)]
    // Light cavalry, std 7,000 (div 3 = 2,333): 2,333 and 4,666.
    [InlineData(LC, 2332, 9)]
    [InlineData(LC, 2333, 10)]
    [InlineData(LC, 4665, 10)]
    [InlineData(LC, 4666, 11)]
    // Heavy cavalry, std 2,500 (div 3 = 833): 833 and 1,666.
    [InlineData(HC, 832, 12)]
    [InlineData(HC, 833, 13)]
    [InlineData(HC, 1665, 13)]
    [InlineData(HC, 1666, 14)]
    // A large unit stays at size 2.
    [InlineData(LI, 30000, 2)]
    public void The_code_is_type_times_3_plus_the_size_and_the_thresholds_hold_at_their_edges(int type, int troops, int attackerCode)
    {
        var std = Context.StandardOf(type);
        Assert.Equal(attackerCode, TacticalIcon.Code(TacticalBattleState.AttackerSide, type, troops, std));
        Assert.Equal(attackerCode + 20, TacticalIcon.Code(TacticalBattleState.DefenderSide, type, troops, std));
    }

    [Fact]
    public void An_empty_cell_is_50()
    {
        var battle = Arena((0, Unit(LI, 3, 3, 9000)));
        Assert.Equal(50, TacticalIcon.Empty);
        Assert.Equal(50, GridAt(battle, 4, 4));
    }

    [Fact]
    public void The_code_is_recomputed_after_every_shot_and_every_melee()
    {
        // Archers (slot 0) shoot a defender LI of 10,000 (size 2) at distance 2, and LC (slot 1) holds a
        // melee target on a defender HI of 4,000 (size 2). Both losses drop a size.
        var battle = Arena(
            (0, Unit(AR, 5, 5, 3000, quality: 9, morale: 99)),
            (1, Unit(LC, 1, 1, 6000, quality: 9, morale: 99, target: D0 + 1)),
            (D0, Unit(LI, 5, 7, 10000, morale: 99)),
            (D0 + 1, Unit(HI, 1, 2, 4000, morale: 99)));
        Assert.Equal(2, GridAt(battle, 5, 7) - 20);
        Assert.Equal(5, GridAt(battle, 1, 2) - 20);

        // Archers 3,000 x 9 x 99 x 18 = 48,114,000 div (15,000 + 150,000) = 291; distance 2 is not below
        // range 2, so no doubling; n = min(291, min(1,000, 5,000)) + 1 = 292. Loss 291 + 290 = 581.
        var draws = new ScriptedDraws(291, 290);
        var shot = TacticalShooting.Shoot(battle, 0, D0, Context, draws);
        Assert.Equal(10000 - 581, shot.Slots[D0].Troops);
        Assert.Equal(21, GridAt(shot, 5, 7)); // size 1 now, + 20
        AssertGridMatchesSlots(shot);

        // The melee of side 0, f = 1. A = 8 x 6,000 x 189 div 2,000 + 12 = 4,548; D = 15 x 4,000 x 149
        // div 2,000 + 12 = 4,482; nD = (4,000 x 4,548 div 4,482) div 10 + 1 = 406. With 405 + 405 the
        // defender loses min(30,000, 810 x 7 div 5) = 1,134, + 1 = 1,135, leaving 2,865: HI size 1, 3 + 1 + 20 = 24.
        var melee = TacticalMelee.Resolve(shot, 0, Context, new ScriptedDraws(0, 0, 405, 405));
        Assert.Equal(2865, melee.Slots[D0 + 1].Troops);
        Assert.Equal(24, GridAt(melee, 1, 2));
        AssertGridMatchesSlots(melee);
    }

    [Fact]
    public void A_routed_units_cell_becomes_empty()
    {
        // A defender LI of 600 troops shot to below the floor 15,000 div 25 = 600 is removed.
        var battle = Arena(
            (0, Unit(LI, 5, 5, 3000, quality: 9, morale: 99)),
            (D0, Unit(LI, 5, 6, 600, morale: 99)),
            (D0 + 1, Unit(LI, 9, 9, 5000, morale: 99)));
        var shot = TacticalShooting.Shoot(battle, 0, D0, Context, new ScriptedDraws(1, 0));
        Assert.Equal(0, shot.Slots[D0].Troops);
        Assert.Equal(TacticalIcon.Empty, GridAt(shot, 5, 6));
    }
}
