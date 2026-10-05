using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.General.GeneralTestbed;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>
/// T124 Done-when 4, the report's golden-master check 9 ("The AI path: each unit's destination and path,
/// the detours, and the last-move danger forfeit"): a scripted computer half-round of a mixed army, each
/// unit's destination (the general's journal), path (the trace's moves) and detours, against values
/// derived by hand below.
/// </summary>
public class PathTests
{
    /// <summary>
    /// The attacker is a computer nation; its half-round is set up from counter 11, so there is no slow
    /// advance (the counter, read before its increment, is not below 10) and every unit has its stat moves:
    /// HI 2, HC 5, LC 6, LI 4, archers 4. The defender (rows 10–11) is out of every unit's reach, so nothing
    /// shoots, nothing is adjacent and no last move is forfeited (no enemy in a 3 × 3 around any last step).
    /// </summary>
    /// <remarks>
    /// <para>Board: attacker s0 HI (2,1), s1 HC (6,1), s2 LC (10,1), s3 LI (4,0), s4 archers (8,0), s5 LI
    /// (5,1); defender D0 HI 4,000 (3,11), D0+1 LI 5,000 (7,11), D0+2 archers 1,500 (12,11), D0+3 LC 2,500
    /// (9,10). Every enemy has quality 5 and morale 70, so a score is <c>troops × 350 × M[theirs][mine] div
    /// 10000</c>. The general's order is HI, HC, LC, LI, archers: s0, s1, s2, s3, s5, s4.</para>
    /// <para><b>Pass 1.</b></para>
    /// <list type="bullet">
    /// <item><description>s0 HI prefers HI: D0. Its line (2,2), (2,3), (2,4), (2,5), (3,6) … (3,10) is
    /// clear; 2 moves: (2,2), (2,3).</description></item>
    /// <item><description>s1 HC prefers HI: D0. Line (6,2), (5,3), (5,4), (5,5), (4,6), … clear (s0 now at
    /// (2,3)); 5 moves: (6,2), (5,3), (5,4), (5,5), (4,6).</description></item>
    /// <item><description>s2 LC takes any: D0 4000 × 350 × M[HI][LC] 15 div 10000 = 2100, with c = 2 claims
    /// (s0, s1) → 2100 div 3 = 700; D0+1 × M[LI][LC] 5 = 875; D0+2 1500 × 350 × M[Ar][LC] 5 = 262; D0+3
    /// × M[LC][LC] 15 = 1312. D0+2. Line (10,2), (10,3), (11,4), (11,5), (11,6), (11,7), … clear; 6 moves to
    /// (11,7).</description></item>
    /// <item><description>s3 LI prefers archers: D0+2. Its line starts (5,1), which s5 holds; every cell of
    /// the box around (12,11), that is (11,10), (11,11), (12,10), (13,10) and (13,11), has a line from (4,0)
    /// starting (5,1) too. No move.</description></item>
    /// <item><description>s5 LI prefers archers: D0+2. Line (6,2), (6,3), (7,4), (8,5), … clear; 4 moves to
    /// (8,5).</description></item>
    /// <item><description>s4 archers take any: D0 4000 × 350 × M[HI][Ar] 65 = 9100, c = 2 → 3033; D0+1
    /// × M[LI][Ar] 20 = 3500; D0+2 × M[Ar][Ar] 18 = 945, c = 3 (s2, s3, s5) → 945 div 4 = 236; D0+3 ×
    /// M[LC][Ar] 28 = 2450. D0+2, at distance 11 (no volley). Its line (8,1), (9,2), (9,3), (9,4), (10,5),
    /// (10,6), (11,7) meets s2. The radius-2 box, dx outer, dy inner: (10,9) first, at distance 9 on the
    /// clear line (8,1), (8,2), (9,3), (9,4), (9,5), …; no later cell is nearer than 9. 4 moves: (8,1),
    /// (8,2), (9,3), (9,4).</description></item>
    /// </list>
    /// <para><b>Pass 2.</b> Only s3 has moves and no target; nothing is in its range 1 or adjacent.</para>
    /// <para><b>Pass 3.</b> s3's threat order against LI: D0+2 archers with shots, −(1500 × 350 × 18 div
    /// 157500 = 60, + 1) = −61; D0+3 2500 × 350 × M[LC][LI] 25 = 2187; D0+1 × M[LI][LI] 15 = 2625; D0 ×
    /// M[HI][LI] 60 = 8400. D0+2 first: its line (5,1), (5,2), (6,3), (7,4), (8,5) meets s5. Its box: (11,10)
    /// blocked at (8,5); (11,11) clear at distance 11; (12,10) blocked at (8,5); (13,10) clear at distance
    /// 10, strictly nearer; (13,11) blocked. So (13,10), on the line (5,1), (6,2), (7,3), (8,4), …; 4 moves.
    /// Pass 2, the flank and pass 2 again: every unit's moves are spent, so nothing more, and nothing draws.
    /// </para>
    /// <para><b>Detours.</b> None: the general walks only to a cell whose line it has just found clear, so
    /// each path is a prefix of that Bresenham line, which the test checks step by step.</para>
    /// </remarks>
    [Fact]
    public void A_computer_half_round_of_a_mixed_army_follows_the_derived_destinations_and_paths()
    {
        var state = Arena(
            [
                (0, Unit(HI, 2, 1, 3000)), (1, Unit(HC, 6, 1, 1000)), (2, Unit(LC, 10, 1, 2500)),
                (3, Unit(LI, 4, 0, 5000)), (4, Unit(AR, 8, 0, 1500)), (5, Unit(LI, 5, 1, 5000)),
                (D0, Unit(HI, 3, 11, 4000)), (D0 + 1, Unit(LI, 7, 11, 5000)),
                (D0 + 2, Unit(AR, 12, 11, 1500)), (D0 + 3, Unit(LC, 9, 10, 2500)),
            ],
            counter: 11,
            attackerComputer: true);
        state = TacticalHalfRound.Setup(state, Context);
        var journal = new List<GeneralDecision>();
        var draws = new ScriptedDraws();

        var after = new ComputerGeneral(journal).Move(state, Context, draws);

        Assert.Equal(
            new[]
            {
                new GeneralDecision(0, GeneralStep.TargetChosen, D0, 3, 11),
                new GeneralDecision(0, GeneralStep.ApproachLine, D0, 3, 11),
                new GeneralDecision(1, GeneralStep.TargetChosen, D0, 3, 11),
                new GeneralDecision(1, GeneralStep.ApproachLine, D0, 3, 11),
                new GeneralDecision(2, GeneralStep.TargetChosen, D0 + 2, 12, 11),
                new GeneralDecision(2, GeneralStep.ApproachLine, D0 + 2, 12, 11),
                new GeneralDecision(3, GeneralStep.TargetChosen, D0 + 2, 12, 11),
                new GeneralDecision(5, GeneralStep.TargetChosen, D0 + 2, 12, 11),
                new GeneralDecision(5, GeneralStep.ApproachLine, D0 + 2, 12, 11),
                new GeneralDecision(4, GeneralStep.TargetChosen, D0 + 2, 12, 11),
                new GeneralDecision(4, GeneralStep.ApproachBox, D0 + 2, 10, 9),
                new GeneralDecision(3, GeneralStep.ThreatBox, D0 + 2, 13, 10),
            },
            journal);

        var paths = new Dictionary<int, (int, int)[]>
        {
            [0] = [(2, 2), (2, 3)],
            [1] = [(6, 2), (5, 3), (5, 4), (5, 5), (4, 6)],
            [2] = [(10, 2), (10, 3), (11, 4), (11, 5), (11, 6), (11, 7)],
            [5] = [(6, 2), (6, 3), (7, 4), (8, 5)],
            [4] = [(8, 1), (8, 2), (9, 3), (9, 4)],
            [3] = [(5, 1), (6, 2), (7, 3), (8, 4)],
        };
        foreach (var (slot, path) in paths)
        {
            Assert.Equal(path, Path(after, slot));
            Assert.Equal(0, after.Slots[slot].Moves);
        }

        // The moves come in the general's order, unit by unit, and nothing else happens: no shot, no target,
        // no forfeit, no draw.
        Assert.Equal(
            new[] { 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 5, 5, 5, 5, 4, 4, 4, 4, 3, 3, 3, 3 },
            after.Log.OfType<TacticalMovedEvent>().Select(m => m.Slot).ToArray());
        Assert.Empty(draws.Bounds);
        Assert.DoesNotContain(after.Log.Skip(state.Log.Count), e => e is not TacticalMovedEvent);

        // No detour: each step is the next cell of the unit's own Bresenham line to its destination.
        var destinations = new Dictionary<int, (int X, int Y)>
        {
            [0] = (3, 11), [1] = (3, 11), [2] = (12, 11), [5] = (12, 11), [4] = (10, 9), [3] = (13, 10),
        };
        foreach (var (slot, (x, y)) in destinations)
        {
            var start = state.Slots[slot];
            var line = BresenhamCells(start.X, start.Y, x, y);
            Assert.Equal(line.Take(paths[slot].Length), paths[slot]);
        }

        // The claims recorded: s0 and s1 on D0; s2, s3, s5 and s4 on D0+2.
        Assert.Equal(2, after.GeneralClaims[D0]);
        Assert.Equal(4, after.GeneralClaims[D0 + 2]);
    }

    /// <summary>
    /// The last-move danger forfeit (check 9), a T123 branch the general's walk reaches on a computer side:
    /// before spending its last move, a unit gives it up when the 3 × 3 around the next path cell holds an
    /// enemy at least as strong, <c>S = (troops × quality div 100) × morale × M[mine][theirs]</c>.
    /// </summary>
    [Fact]
    public void A_last_move_next_to_a_stronger_enemy_is_forfeited()
    {
        // HC 1,000 (q 5, m 70) at (0,1), 5 moves, targets the HI 4,000 at (6,7) (it prefers HI). The line:
        // (1,2), (2,3), (3,4), (4,5), (5,6). Four steps reach (4,5) with 1 move left; before the fifth, the
        // 3 × 3 around (5,6) holds the HI, whose S = (4000 × 5 div 100) × 70 × M[HI][HC] 8 = 112,000 is at
        // least the HC's (1000 × 5 div 100) × 70 × M[HC][HI] 12 = 42,000: the move is forfeited, moves 0.
        var state = Arena(
            [(0, Unit(HC, 0, 1, 1000)), (D0, Unit(HI, 6, 7, 4000))],
            counter: 11,
            attackerComputer: true);
        state = TacticalHalfRound.Setup(state, Context);
        var journal = new List<GeneralDecision>();

        var after = new ComputerGeneral(journal).Move(state, Context, new ScriptedDraws());

        Assert.Equal(new GeneralDecision(0, GeneralStep.ApproachLine, D0, 6, 7), journal[1]);
        Assert.Equal(new[] { (1, 2), (2, 3), (3, 4), (4, 5) }, Path(after, 0));
        Assert.Single(after.Log.OfType<TacticalMoveForfeitedEvent>());
        Assert.Equal(0, after.Slots[0].Moves);
    }

    /// <summary>
    /// Detours: a step off the straight line. T123's movement takes one when the path cell is blocked
    /// (<c>FUN_00438A6C</c>; T123's <c>MovementTests</c> pin each case). The general's own walks cannot
    /// take one. It walks only to a cell whose line <see cref="GeneralLine"/> has just found clear, on the
    /// same grid and by the same Bresenham cursor the walk follows, and nothing fills a cell during a walk:
    /// its shots and routs only empty cells.
    /// </summary>
    /// <remarks>
    /// The first part shows the oracle sees a detour: T123's walk from (2,5) to (6,5) with (3,5) held steps
    /// to (3,6) (the row's +1 alternative), which is not the line's (3,5), so the oracle rejects it. The
    /// second part runs 60 whole battles between two computer generals and checks every step of every walk
    /// the general makes: each must be the next cell of the line from where the walk began to the
    /// journal's destination. Not one detours.
    /// </remarks>
    [Fact]
    public void A_blocked_path_detours_but_the_generals_own_walks_never_do()
    {
        // The oracle against a real detour: the straight line (3,5), (4,5) … is blocked at (3,5).
        var blocked = Arena((0, Unit(LC, 2, 5, 5000)), (1, Unit(HI, 3, 5, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        var detoured = TacticalMovement.MoveUnit(blocked, 0, 6, 5, Context, NoDraws.Instance);
        Assert.Equal(new[] { (3, 6), (4, 5), (5, 5), (6, 5) }, Path(detoured, 0));
        var fakeWalk = new GeneralDecision(0, GeneralStep.ApproachLine, -1, 6, 5);
        // The first step leaves the line, so it and every step after it are left over: all 4.
        Assert.Equal(4, Detours(blocked, [fakeWalk], detoured.Log.OfType<TacticalMovedEvent>().ToList()));

        // The general's walks across 60 battles.
        var walks = 0;
        var steps = 0;
        for (uint seed = 1; seed <= 30; seed++)
        {
            foreach (var scale in new[] { 1, 2 })
            {
                var journal = new List<GeneralDecision>();
                var general = new ComputerGeneral(journal);
                var draws = new DelphiBattleDraws(seed * 7919u);
                var state = FreshBattle(Army(1), Army(scale));
                for (var halfRound = 0; halfRound < 400 && !state.IsOver; halfRound++)
                {
                    if (state.Placed)
                    {
                        var before = state;
                        var decided = journal.Count;
                        state = general.Move(state, Context, draws);
                        var moves = state.Log.Skip(before.Log.Count).OfType<TacticalMovedEvent>().ToList();
                        var decisions = journal.Skip(decided).Where(IsWalk).ToList();
                        Assert.Equal(0, Detours(before, decisions, moves));
                        walks += decisions.Count;
                        steps += moves.Count;
                    }
                    else
                    {
                        state = general.Place(state, Context, draws);
                    }

                    state = TacticalHalfRound.End(state, Context, draws);
                }

                Assert.True(state.IsOver);
            }
        }

        Assert.True(walks > 500 && steps > 1000, $"{walks} walks, {steps} steps");
    }

    private static TacticalSlot[] Army(int scale) =>
    [
        Unit(LI, 0, 0, 6000 * scale), Unit(HI, 0, 0, 3000 * scale, quality: 6), Unit(AR, 0, 0, 1500 * scale),
        Unit(LC, 0, 0, 2500 * scale), Unit(HC, 0, 0, 1000 * scale, quality: 6), Unit(HI, 0, 0, 2500 * scale),
        Unit(LI, 0, 0, 4000 * scale, quality: 4), Unit(AR, 0, 0, 1200 * scale, quality: 4),
    ];

    private static bool IsWalk(GeneralDecision d) =>
        d.Step is GeneralStep.ApproachLine or GeneralStep.ApproachBox or GeneralStep.ThreatLine
            or GeneralStep.ThreatBox or GeneralStep.Flank;

    /// <summary>
    /// The steps of <paramref name="moves"/> that are not on their walk's line: each walk decision, in
    /// order, takes the following steps of its unit while each is the next cell of the line from the unit's
    /// cell to the decision's destination. Any step left over means a walk left its line: the count is the
    /// first off-line step and every step after it.
    /// </summary>
    private static int Detours(TacticalBattleState before, IReadOnlyList<GeneralDecision> walks, IReadOnlyList<TacticalMovedEvent> moves)
    {
        var at = before.Slots.Select(s => (s.X, s.Y)).ToArray();
        var next = 0;
        foreach (var walk in walks)
        {
            var line = BresenhamCells(at[walk.Slot].X, at[walk.Slot].Y, walk.X, walk.Y);
            for (var k = 0; k < line.Count && next < moves.Count && moves[next].Slot == walk.Slot
                            && (moves[next].ToX, moves[next].ToY) == line[k]; k++)
            {
                at[walk.Slot] = line[k];
                next++;
            }
        }

        return moves.Count - next;
    }

    /// <summary>The design's line, written out independently: <c>err = 2 × minor − major</c>; while <c>err ≥ 0</c> a minor step, then the major step.</summary>
    private static List<(int, int)> BresenhamCells(int x1, int y1, int x2, int y2)
    {
        int dx = x2 - x1, dy = y2 - y1, sx = Math.Sign(dx), sy = Math.Sign(dy);
        var yMajor = Math.Abs(dy) > Math.Abs(dx);
        int major = yMajor ? Math.Abs(dy) : Math.Abs(dx), minor = yMajor ? Math.Abs(dx) : Math.Abs(dy);
        int err = (2 * minor) - major, px = x1, py = y1;
        var cells = new List<(int, int)>();
        while (px != x2 || py != y2)
        {
            while (err >= 0)
            {
                if (yMajor)
                {
                    px += sx;
                }
                else
                {
                    py += sy;
                }

                err -= 2 * major;
            }

            if (yMajor)
            {
                py += sy;
            }
            else
            {
                px += sx;
            }

            err += 2 * minor;
            cells.Add((px, py));
        }

        return cells;
    }
}
