using IC2.Engine.Battle;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// Surrender and the end of the battle: report <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §6
/// ("Surrender") and §8 (the end, the winner), golden-master check 10's winner; <c>docs/game-design.md</c>,
/// "Surrender and the end".
/// </summary>
public class SurrenderTests
{
    [Fact]
    public void Surrender_removes_the_sides_units_with_no_cascade_ends_the_battle_and_the_half_round()
    {
        var state = Arena(
            (0, Unit(HI, 3, 3, 5000, morale: 50)),
            (1, Unit(LI, 4, 3, 5000, morale: 31)),
            (D0, Unit(LI, 3, 4, 5000, morale: 97, target: 0)));

        var after = TacticalSurrender.Surrender(state, Context, NoDraws.Instance);

        Assert.False(after.HasLiveUnit(TacticalBattleState.AttackerSide));
        Assert.Equal(TacticalIcon.Empty, GridAt(after, 3, 3));
        // No cascade: the defender gains nothing and keeps its target; the surrendering units' morale is untouched.
        Assert.Equal(97, after.Slots[D0].Morale);
        Assert.Equal(0, after.Slots[D0].Target);
        Assert.Equal(31, after.Slots[1].Morale);
        Assert.True(after.IsOver);
        Assert.Equal(TacticalBattleState.DefenderSide, TacticalSurrender.Winner(after));
        Assert.Contains(after.Log, e => e is TacticalHalfRoundEndedEvent);
        Assert.Equal(1, after.SideToMove);
        Assert.Equal(state.Counter, after.Counter);
        Assert.DoesNotContain(after.Log, e => e is TacticalRoutedEvent);
    }

    [Fact]
    public void The_winner_is_the_attacker_while_any_attacker_slot_has_troops_and_the_defender_otherwise()
    {
        Assert.Equal(0, TacticalSurrender.Winner(Arena((5, Unit(LI, 0, 0, 1)))));
        Assert.Equal(0, TacticalSurrender.Winner(Arena((5, Unit(LI, 0, 0, 1)), (D0, Unit(LI, 0, 11, 9000)))));
        Assert.Equal(1, TacticalSurrender.Winner(Arena((D0, Unit(LI, 0, 11, 1)))));

        // Both sides emptied: the defender "wins" with no units.
        Assert.Equal(1, TacticalSurrender.Winner(Arena()));
    }

    [Fact]
    public void Nothing_but_an_emptied_side_ends_the_battle()
    {
        // Many half-rounds of two units that never meet: no limit ends it.
        var state = Arena((0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        for (var i = 0; i < 50; i++)
        {
            state = TacticalHalfRound.End(state, Context, NoDraws.Instance);
        }

        Assert.False(state.IsOver);
        Assert.Equal(53, state.Counter);
    }
}

/// <summary>
/// The write-back, steps 1–5 of <c>TBattleOver_OK</c>: report
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §9, golden-master check 10;
/// <c>docs/game-design.md</c>, "Write-back".
/// </summary>
public class WriteBackTests
{
    private static GameState Game(int winnerUnity = 980, int loserUnity = 400)
    {
        var initial = BattleTestbed.Initial();
        var attacker = BattleTestbed.Army("att", "north", 1, 1, 63, 100, 20,
            BattleTestbed.Unit("light_infantry", 3000, 4, "a0"),
            BattleTestbed.Unit("heavy_infantry", 2500, 5, "a1"),
            BattleTestbed.Unit("archers", 2000, 9, "a2"),
            BattleTestbed.Unit("light_cavalry", 1000, 7, "a3"));
        var defender = BattleTestbed.Army("def", "south", 2, 1, 60, 250, 50,
            BattleTestbed.Unit("heavy_infantry", 4000, 6, "d0"));
        var other = BattleTestbed.Army("other", "south", 7, 7, 60, 0, 0, BattleTestbed.Unit("light_infantry", 100, 6, "o0"));
        var fleet = BattleTestbed.Fleet("f1", "south", 9, 9, 10, 100, carriedArmyId: "def");
        var nations = initial.Nations.Select(n => n.Id switch
        {
            "north" => n with { Unity = winnerUnity },
            "south" => n with { Unity = loserUnity },
            _ => n,
        });
        return BattleTestbed.StateWith(new[] { attacker, defender, other }, new[] { fleet }, nations: nations);
    }

    /// <summary>The attacker won: slots 0, 2 and 3 survive (slot 1, the HI, routed); the defender is empty.</summary>
    private static TacticalBattleState AttackerWon() =>
        Arena(
            (0, Unit(LI, 0, 0, 2900, quality: 4) with { Origin = 11, Name = "a0" }),
            (2, Unit(AR, 2, 0, 1900, quality: 9) with { Name = "a2" }),
            (3, Unit(LC, 3, 0, 1200, quality: 7) with { Name = "a3" })) with { IsOver = true };

    [Fact]
    public void Survivors_are_compacted_in_slot_order_with_max_6_then_a_Random_4_promotion_capped_at_9()
    {
        var draws = new ScriptedDraws(0, 0, 3);
        var result = TacticalWriteBack.Apply(Game(), AttackerWon(), Context, draws, NullEventSink.Instance);

        // One Random(4) per survivor, in slot order.
        Assert.Equal(new[] { 4, 4, 4 }, draws.Bounds);

        var units = result.Game.ArmyById("att")!.Units;
        Assert.Equal(new[] { "a0", "a2", "a3" }, units.Select(u => u.Name));
        // a0: max(6, 4) = 6, then 0 -> 7. a2: max(6, 9) = 9, then 0 -> min(9, 10) = 9. a3: 7, then 3 -> 7.
        Assert.Equal(new[] { 7, 9, 7 }, units.Select(u => u.Quality));
        Assert.Equal(new[] { 2900, 1900, 1200 }, units.Select(u => u.Troops));
        Assert.Equal(new[] { "light_infantry", "archers", "light_cavalry" }, units.Select(u => u.UnitTypeId));
        Assert.Equal(11, units[0].MercenaryLabel);

        Assert.Equal(
            new[] { new TacticalPromotionEvent(0, 0, 4, 7), new TacticalPromotionEvent(2, 1, 9, 9), new TacticalPromotionEvent(3, 2, 7, 7) },
            result.Report.Promotions.ToArray());
        Assert.Equal(new[] { (4, 0), (4, 0), (4, 3) }, DrawsOn(result.Battle));
    }

    [Fact]
    public void Money_moves_supplies_are_capped_at_troops_div_100_the_loser_is_deleted_and_unity_swings()
    {
        var sink = new RecordingEventSink();
        var result = TacticalWriteBack.Apply(Game(), AttackerWon(), Context, new ScriptedDraws(1, 1, 1), sink);
        var game = result.Game;
        var winner = game.ArmyById("att")!;

        Assert.Equal(100 + 250, winner.Money);
        // 20 + 50 = 70, capped at 6,000 div 100 = 60.
        Assert.Equal(60, winner.SupplyTons);
        Assert.Equal(winner.TotalTroops / Context.Combat.AbsorbedSupplyTroopDivisor, winner.SupplyTons);

        // The loser army is gone and nothing points at it; the loser's other army is untouched.
        Assert.Null(game.ArmyById("def"));
        Assert.NotNull(game.ArmyById("other"));
        Assert.Null(game.FleetById("f1")!.CarriedArmyId);

        // Unity: 980 + 25 capped at 990; 400 - 25.
        Assert.Equal(990, game.NationById("north")!.Unity);
        Assert.Equal(375, game.NationById("south")!.Unity);

        // The news line's event, "W destroys army of L.".
        var news = Assert.IsType<BattleArmyDestroyed>(Assert.Single(sink.Events));
        Assert.Equal(("Northern League", "Southern League"), (news.Winner, news.Loser));
    }

    [Fact]
    public void Supplies_under_the_cap_are_summed_and_the_unity_cap_does_not_bite_below_it()
    {
        var game = Game(winnerUnity: 500, loserUnity: 10);
        game = game with
        {
            Armies = ValueList.From(game.Armies.Select(a => a.Id == "def" ? a with { SupplyTons = 5 } : a)),
        };

        var result = TacticalWriteBack.Apply(game, AttackerWon(), Context, new ScriptedDraws(1, 1, 1), NullEventSink.Instance);

        Assert.Equal(25, result.Game.ArmyById("att")!.SupplyTons);
        Assert.Equal(525, result.Game.NationById("north")!.Unity);
        Assert.Equal(-15, result.Game.NationById("south")!.Unity);
    }

    [Fact]
    public void The_report_carries_the_battle_ended_windows_contents()
    {
        var battle = AttackerWon() with
        {
            Log = ValueList.From(new TacticalEvent[]
            {
                new TacticalDrawEvent(5, 1),
                new TacticalShotEvent(0, D0, 5, 1, 0, 3999, 70),
                new TacticalMovedEvent(0, 0, 0, 0, 1, 3),
                new TacticalRoutedEvent(1),
            }),
        };

        var report = TacticalWriteBack.Apply(Game(), battle, Context, new ScriptedDraws(1, 1, 1), NullEventSink.Instance).Report;

        Assert.Equal((0, "att", "def", "north", "south"), (report.WinnerSide, report.WinnerArmyId, report.LoserArmyId, report.WinnerNationId, report.LoserNationId));
        Assert.Equal(new[] { 3000, 2500, 2000, 1000, 0 }, report.WinnerStartByType);
        Assert.Equal(8500, report.WinnerStartTotal);
        Assert.Equal(new[] { 2900, 0, 1900, 1200, 0 }, report.WinnerFinishByType);
        Assert.Equal(6000, report.WinnerFinishTotal);
        Assert.Equal(new[] { 0, 4000, 0, 0, 0 }, report.LoserStartByType);
        Assert.Equal(4000, report.LoserStartTotal);
        Assert.Equal((250, 50), (report.CapturedMoney, report.CapturedSupplyTons));
        Assert.Equal(new[] { typeof(TacticalShotEvent), typeof(TacticalRoutedEvent) }, report.ExchangeLog.Select(e => e.GetType()));
    }

    [Fact]
    public void When_both_sides_are_emptied_the_defender_wins_with_no_units_and_the_attacker_is_deleted()
    {
        var empty = Arena() with { IsOver = true };

        var result = TacticalWriteBack.Apply(Game(), empty, Context, NoDraws.Instance, NullEventSink.Instance);

        Assert.Equal(TacticalBattleState.DefenderSide, result.Report.WinnerSide);
        Assert.Null(result.Game.ArmyById("att"));
        var defender = result.Game.ArmyById("def")!;
        Assert.Empty(defender.Units);
        Assert.Equal(250 + 100, defender.Money);
        Assert.Equal(0, defender.SupplyTons);
        Assert.Equal(400 + 25, result.Game.NationById("south")!.Unity);
        Assert.Equal(980 - 25, result.Game.NationById("north")!.Unity);
    }

    [Fact]
    public void A_battle_still_running_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            TacticalWriteBack.Apply(Game(), AttackerWon() with { IsOver = false }, Context, NoDraws.Instance, NullEventSink.Instance));
    }
}
