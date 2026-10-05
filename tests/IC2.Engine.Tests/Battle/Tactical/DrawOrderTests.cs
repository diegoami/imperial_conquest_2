using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// One whole battle, both sides human-ordered (so no general), from copy-in to the write-back. Every
/// order is a human's, so every draw is one the design lists.
/// </summary>
/// <remarks>
/// Rome-like attacker (north, army morale 60): LI 9,000 q2 and archers 3,000 q5. Defender (south, army
/// morale 60): HI 300 q4 and LC 2,000 q1. The numbers each draw's bound comes from are worked out in
/// <see cref="DrawOrderTests.A_scripted_human_battle_draws_exactly_the_designs_list_in_order"/>.
/// </remarks>
public static class ScriptedHumanBattle
{
    /// <summary>The scripted values, in the order the design says they are drawn.</summary>
    public static readonly int[] Script =
    {
        5, 10, 0, 0, // copy-in: LI, archers, then HI, LC
        95, 95, 95, 95, 95, 95, 95, 95, // four archer shots on LC; LC stays above 39, so no rout draws
        90, 90, // the LI's shot on LC (morale 45, no rout draw)
        100, 100, 30, 30, // the LI-HI melee; LI holds above 39, HI falls below its floor (no draw)
        50, 50, 20, 20, // an archer shot, then the LC's rout test at morale 36: 40 > 29, it holds
        50, 50, 0, 0, // another shot, then the rout test at morale 33: 0 <= 29, routed; the battle ends
        0, 2, // the promotions: LI, then archers
    };

    public static GameState Game()
    {
        var initial = BattleTestbed.Initial();
        var nations = initial.Nations.Select(n => n with { Control = SeatControl.Human });
        var attacker = BattleTestbed.Army("att", "north", 1, 1, 60, 10, 10,
            BattleTestbed.Unit("light_infantry", 9000, 2, "Legio I"),
            BattleTestbed.Unit("archers", 3000, 5, "Sagittarii"));
        var defender = BattleTestbed.Army("def", "south", 2, 1, 60, 40, 30,
            BattleTestbed.Unit("heavy_infantry", 300, 4, "Guards"),
            BattleTestbed.Unit("light_cavalry", 2000, 1, "Riders"));
        return BattleTestbed.StateWith(new[] { attacker, defender }, nations: nations);
    }

    /// <summary>Plays the battle with <paramref name="draws"/>, through to the write-back.</summary>
    public static TacticalWriteBackResult Play(IBattleDraws draws, IEventSink? events = null)
    {
        var general = NullTacticalGeneral.Instance;
        var start = TacticalCopyIn.Start(Game(), "att", "def", Context, draws);
        var s = start.Battle;

        // Half-round 1: the defender places. Half-round 2: the attacker places.
        s = Ok(TacticalOrders.Place(s, D0, 5, 9, Context));
        s = Ok(TacticalOrders.Place(s, D0 + 1, 6, 9, Context));
        s = Ok(TacticalOrders.EndHalfRound(s, Context, draws, general));
        s = Ok(TacticalOrders.Place(s, 0, 5, 2, Context));
        s = Ok(TacticalOrders.Place(s, 1, 7, 1, Context));
        s = Ok(TacticalOrders.EndHalfRound(s, Context, draws, general));

        // Half-round 3: the attacker advances; human moves draw nothing.
        s = Ok(TacticalOrders.Move(s, 0, 5, 6, Context, draws));
        s = Ok(TacticalOrders.Move(s, 1, 7, 5, Context, draws));
        s = Ok(TacticalOrders.EndHalfRound(s, Context, draws, general));

        // Half-round 4: the defender closes in.
        s = Ok(TacticalOrders.Move(s, D0, 5, 7, Context, draws));
        s = Ok(TacticalOrders.Move(s, D0 + 1, 6, 7, Context, draws));
        s = Ok(TacticalOrders.EndHalfRound(s, Context, draws, general));

        // Half-round 5: four archer shots and an LI shot on the LC, the LI attacks the HI, and the
        // half-round's end fights the melee.
        for (var i = 0; i < 4; i++)
        {
            s = Ok(TacticalOrders.Shoot(s, 1, D0 + 1, Context, draws));
        }

        s = Ok(TacticalOrders.Shoot(s, 0, D0 + 1, Context, draws));
        s = Ok(TacticalOrders.SetTarget(s, 0, D0, Context));
        s = Ok(TacticalOrders.EndHalfRound(s, Context, draws, general));

        // Half-round 6: the defender's LC waits. Half-round 7: two archer shots rout it.
        s = Ok(TacticalOrders.EndHalfRound(s, Context, draws, general));
        s = Ok(TacticalOrders.Shoot(s, 1, D0 + 1, Context, draws));
        s = Ok(TacticalOrders.Shoot(s, 1, D0 + 1, Context, draws));
        Assert.True(s.IsOver);

        return TacticalWriteBack.Apply(start.Game, s, Context, draws, events ?? NullEventSink.Instance);
    }

    private static TacticalBattleState Ok(TacticalOrderResult result)
    {
        Assert.True(result.Accepted, result.Rejection);
        return result.State;
    }
}

/// <summary>
/// The draw order: report <c>2026-10-04-decompiled-tactical-battle-rules.md</c>, "Every <c>Random</c>
/// call, in draw order", and golden-master checks 1, 5, 6, 8 and 10 together;
/// <c>docs/game-design.md</c>, "Every random draw, in order". The generator:
/// <c>2026-10-03-new-game-turn-order-shuffle.md</c> item 3.
/// </summary>
public class DrawOrderTests
{
    [Fact]
    public void A_scripted_human_battle_draws_exactly_the_designs_list_in_order()
    {
        var draws = new ScriptedDraws(ScriptedHumanBattle.Script);
        var result = ScriptedHumanBattle.Play(draws);

        // The bounds, worked out:
        // Copy-in, quality x 4: LI q2 -> 8, archers q5 -> 20, then HI q4 -> 16, LC q1 -> 4. Morale: LI
        // 60 + 5 = 65, archers 70, HI 60, LC 60.
        // Archers 3,000 q5 m70 at LC (vuln 15), distance 2 (not doubled): base = 15,750,000 div
        // 165,000 = 95, n = min(95, min(1,000, troops_LC div 2 >= 715)) + 1 = 96, four times. Losses
        // 190: LC 2,000 -> 1,240, morale 60 -> 48 (-3 each); above 39, so no rout draw.
        // LI 9,000 q2 m65 at LC, distance 1: base = 17,550,000 div 195,000 = 90, n = 91. Loss 180:
        // LC 1,060, morale 45.
        // Melee LI on HI 300 q4 m60, f = 1: A = 4 x 9,000 x 85 div 2,000 + 12 = 1,542; D = 60 x 300 x
        // 100 div 2,000 + 12 = 912; nA = (9,000 x 912 div 1,542) div 12 + 1 = 444; nD = (300 x 1,542
        // div 912) div 10 + 1 = 51. la = 161, ld = 85: HI 215 < 240, removed without a draw; the LC
        // takes -6 (39), the archers +5 (75), the LI +2 then +5 (72).
        // Archers m75 at LC 1,060: base = 16,875,000 div 165,000 = 102, n = min(102, 530) + 1 = 103.
        // Loss 100: morale 36, a rout test, Random(36) twice. Again: n = min(102, 480) + 1 = 103;
        // morale 33, Random(33) twice: 0 + 0 <= 29, routed, the defender is empty.
        // Promotions: Random(4) for the LI (slot 0), then the archers (slot 1).
        var expected = new (int Bound, int Value)[]
        {
            (8, 5), (20, 10), (16, 0), (4, 0),
            (96, 95), (96, 95), (96, 95), (96, 95), (96, 95), (96, 95), (96, 95), (96, 95),
            (91, 90), (91, 90),
            (444, 100), (444, 100), (51, 30), (51, 30),
            (103, 50), (103, 50), (36, 20), (36, 20),
            (103, 50), (103, 50), (33, 0), (33, 0),
            (4, 0), (4, 2),
        };

        Assert.Equal(expected, draws.Calls);
        Assert.Equal(expected, DrawsOn(result.Battle));

        // The trace's exchanges, in order: 5 shots, the melee, the HI's rout, 2 shots, the LC's rout.
        Assert.Equal(
            new[] { "shot", "shot", "shot", "shot", "shot", "melee", "routed", "shot", "shot", "routed" },
            result.Report.ExchangeLog.Select(e => e switch
            {
                TacticalShotEvent => "shot",
                TacticalMeleeEvent => "melee",
                TacticalRoutedEvent => "routed",
                _ => e.GetType().Name,
            }));

        // The write-back: the attacker won; LI q2 -> max(6, 2) = 6, Random(4) = 0 -> 7; archers 5 -> 6.
        var army = result.Game.ArmyById("att")!;
        Assert.Equal(new[] { 7, 6 }, army.Units.Select(u => u.Quality));
        Assert.Equal(new[] { 9000 - 161, 3000 }, army.Units.Select(u => u.Troops));
        Assert.Null(result.Game.ArmyById("def"));
    }

    [Fact]
    public void The_Delphi_generator_replays_the_originals_Random_from_a_seed()
    {
        // RandSeed := RandSeed x $08088405 + 1 (mod 2^32), then Random(n) = (RandSeed x n) shr 32, from
        // RandSeed = 12345 (the probe's normal-build seed):
        //  1. 12,345 x 134,775,813 + 1 = 1,663,807,411,486; mod 2^32 = 1,655,067,934 ($62A6551E);
        //     Random(5) = 8,275,339,670 shr 32 = 1.
        //  2. $4A132197 = 1,242,767,767; Random(16) = 19,884,284,272 shr 32 = 4.
        //  3. $146983F4 = 342,459,380; Random(100) = 34,245,938,000 shr 32 = 7.
        //  4. $8DB963C5; Random(0) = 0, the seed still advanced.
        //  5. $A23886DA = 2,721,613,530; Random(7) = 19,051,294,710 shr 32 = 4.
        //  6. $57730A43 = 1,467,157,059; Random(1000) = 1,467,157,059,000 shr 32 = 341.
        //  7. $70A1BF50 = 1,889,648,464; Random(4) = 7,558,593,856 shr 32 = 1.
        //  8. $144DFC91 = 340,655,249; Random(3) = 1,021,965,747 shr 32 = 0.
        //  9. $0848B2D6 = 138,982,102; Random(25) = 3,474,552,550 shr 32 = 0.
        // 10. $EC51D62F = 3,964,786,223; Random(2) = 7,929,572,446 shr 32 = 1.
        var delphi = new DelphiBattleDraws(12345);
        var bounds = new[] { 5, 16, 100, 0, 7, 1000, 4, 3, 25, 2 };

        Assert.Equal(new[] { 1, 4, 7, 0, 4, 341, 1, 0, 0, 1 }, bounds.Select(delphi.Random));
        Assert.Equal(0xEC51D62Fu, delphi.RandSeed);
    }

    [Fact]
    public void The_game_seam_draws_Random_0_as_0_and_still_consumes_a_draw()
    {
        var withZero = new RngBattleDraws(new SplitMix64Rng(7));
        var reference = new SplitMix64Rng(7);

        Assert.Equal(0, withZero.Random(0));
        reference.NextUInt64();
        Assert.Equal(reference.NextInt(10), withZero.Random(10));
    }

    [Fact]
    public void A_negative_bound_reads_as_unsigned_as_the_originals_MUL_does()
    {
        // From RandSeed 12345 the first seed is 1,655,067,934; Random(-1) = (1,655,067,934 x
        // 4,294,967,295) shr 32 = 1,655,067,933, as Delphi computes with the bound as an unsigned word.
        Assert.Equal(1655067933, new DelphiBattleDraws(12345).Random(-1));
    }
}
