using IC2.Engine.Battle.Tactical;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// Morale and rout, <c>FUN_00438FB0</c>: report <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §6,
/// golden-master check 8; <c>docs/game-design.md</c>, "Morale and rout", and Combat's "The rout mechanic"
/// (one level deep).
/// </summary>
public class RoutTests
{
    /// <summary>A defender LI at slot D0 with the given troops and morale, and one live unit a side besides.</summary>
    private static TacticalBattleState With(int troops, int morale) =>
        Arena(
            (0, Unit(HI, 3, 3, 5000, morale: 70)),
            (D0, Unit(LI, 3, 4, troops, morale: morale)),
            (D0 + 1, Unit(LI, 8, 8, 5000, morale: 80)));

    [Fact]
    public void Below_the_floor_std_div_25_a_unit_is_removed_without_a_draw()
    {
        var floor = Context.StandardOf(LI) / Rules.RoutTroopsDivisor;
        Assert.Equal(600, floor);

        var below = TacticalRout.Rout(With(floor - 1, 99), D0, Context, NoDraws.Instance);
        Assert.Equal(0, below.Slots[D0].Troops);
        Assert.Equal(TacticalIcon.Empty, GridAt(below, 3, 4));

        var at = TacticalRout.Rout(With(floor, 99), D0, Context, NoDraws.Instance);
        Assert.Equal(floor, at.Slots[D0].Troops);
    }

    [Fact]
    public void At_morale_19_or_less_a_unit_is_removed_without_a_draw()
    {
        var routed = TacticalRout.Rout(With(5000, 19), D0, Context, NoDraws.Instance);
        Assert.Equal(0, routed.Slots[D0].Troops);
        Assert.Contains(new TacticalRoutedEvent(D0), routed.Log);
    }

    [Fact]
    public void Above_39_a_unit_is_safe_and_nothing_is_drawn()
    {
        var safe = TacticalRout.Rout(With(5000, 40), D0, Context, NoDraws.Instance);
        Assert.Equal(5000, safe.Slots[D0].Troops);
        Assert.Empty(safe.Log);
    }

    [Theory]
    [InlineData(20, 10, 10, true)] // 20 is in the band: 10 + 10 = 20 <= 29, removed
    [InlineData(39, 15, 15, false)] // 39 is in the band: 30 > 29, holds
    [InlineData(30, 15, 14, true)] // 29 is not above 29: removed
    [InlineData(30, 15, 15, false)] // 30 > 29: holds
    public void In_the_20_to_39_band_two_draws_of_Random_morale_decide(int morale, int first, int second, bool removed)
    {
        var draws = new ScriptedDraws(first, second);
        var after = TacticalRout.Rout(With(5000, morale), D0, Context, draws);

        Assert.Equal(new[] { morale, morale }, draws.Bounds);
        Assert.Equal(removed, after.Slots[D0].Troops == 0);
    }

    [Fact]
    public void A_rout_costs_friends_6_drags_out_those_below_30_with_no_second_level_and_gives_enemies_5_capped_at_99()
    {
        // The defender at D0 routs (morale 19). Friends: D0+1 at 35 -> 29, removed by the cascade;
        // D0+2 at 36 -> 30, stays (30 is not below 30), and is not hit again by D0+1's removal.
        // Enemies: slot 0 at 97 -> 99 (cap) and targeting D0 -> cleared; slot 1 at 50 -> 55 and
        // targeting D0+1, the cascade's unit, keeps its target.
        var state = Arena(
            (0, Unit(HI, 3, 3, 5000, morale: 97, target: D0)),
            (1, Unit(HI, 4, 3, 5000, morale: 50, target: D0 + 1)),
            (D0, Unit(LI, 3, 4, 5000, morale: 19)),
            (D0 + 1, Unit(LI, 4, 4, 5000, morale: 35)),
            (D0 + 2, Unit(LI, 5, 4, 5000, morale: 36)));

        var after = TacticalRout.Rout(state, D0, Context, NoDraws.Instance);

        Assert.Equal(0, after.Slots[D0].Troops);
        Assert.Equal(0, after.Slots[D0 + 1].Troops);
        Assert.Equal(29, after.Slots[D0 + 1].Morale);
        Assert.Equal(TacticalIcon.Empty, GridAt(after, 4, 4));
        Assert.Equal(5000, after.Slots[D0 + 2].Troops);
        Assert.Equal(30, after.Slots[D0 + 2].Morale);

        Assert.Equal(99, after.Slots[0].Morale);
        Assert.Equal(TacticalSlot.NoTarget, after.Slots[0].Target);
        Assert.Equal(55, after.Slots[1].Morale);
        Assert.Equal(D0 + 1, after.Slots[1].Target);

        Assert.Equal(
            new TacticalEvent[] { new TacticalRoutedEvent(D0), new TacticalCascadeRemovedEvent(D0 + 1) },
            after.Log.ToArray());
        Assert.False(after.IsOver);
    }

    [Fact]
    public void The_battle_ends_when_a_rout_empties_a_side()
    {
        var state = Arena(
            (0, Unit(HI, 3, 3, 5000, morale: 70)),
            (D0, Unit(LI, 3, 4, 5000, morale: 10)));

        var after = TacticalRout.Rout(state, D0, Context, NoDraws.Instance);

        Assert.True(after.IsOver);
        Assert.Equal(new TacticalBattleOverEvent(TacticalBattleState.AttackerSide), after.Log[^1]);
    }

    [Fact]
    public void The_battle_ends_when_the_cascade_empties_a_side()
    {
        var state = Arena(
            (0, Unit(HI, 3, 3, 5000, morale: 70)),
            (D0, Unit(LI, 3, 4, 5000, morale: 10)),
            (D0 + 1, Unit(LI, 4, 4, 5000, morale: 31)));

        var after = TacticalRout.Rout(state, D0, Context, NoDraws.Instance);

        Assert.True(after.IsOver);
        Assert.False(after.HasLiveUnit(TacticalBattleState.DefenderSide));
    }
}
