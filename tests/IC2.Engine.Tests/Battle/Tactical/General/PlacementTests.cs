using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.General.GeneralTestbed;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>
/// T124 Done-when 1, the report's golden-master check 2: the computer general's placement,
/// <c>FUN_004381A4</c> (<c>2026-10-04-decompiled-tactical-battle-rules.md</c> §2, "AI placement";
/// <c>docs/game-design.md</c>, "The computer general", "Placement"), under scripted draws.
/// </summary>
/// <remarks>
/// The expected cells are written out here from the design's <c>Form</c> table, independently of the
/// ruleset the engine reads: block <c>g</c> covers columns <c>3g + 1 … 3g + 3</c>; the attacker's base row
/// is 2 stepping −1 (rows 2, 1, 0), the defender's 9 stepping +1 (rows 9, 10, 11); after the third row the
/// row is held, so a block of more than nine units stacks.
/// </remarks>
public class PlacementTests
{
    /// <summary>The design's <c>Form</c> table, the type in each block (heavy infantry standing for "HI + Ar").</summary>
    private static readonly int[][] Form =
    [
        [HC, HI, LI, LC],
        [LI, HI, HC, LC],
        [LC, HC, LI, HI],
        [LC, LI, HI, HC],
        [HC, LC, HI, LI],
    ];

    private static readonly int[] AttackerRows = [2, 1, 0];
    private static readonly int[] DefenderRows = [9, 10, 11];

    private static int BlockColumn(int g, int i) => (3 * g) + 1 + i;

    /// <summary>One unit of each type, in slot order LI, HI, archers, LC, HC, scattered off the home rows.</summary>
    private static TacticalBattleState OneOfEach(int side) =>
        Arena(
            Enumerable.Range(0, 5).Select(t => (side * D0 + t, Unit(t, 13 - t, 5 + (t % 2), 3000))),
            sideToMove: side,
            placed: false,
            counter: 1,
            attackerComputer: true,
            defenderComputer: true);

    public static TheoryData<int, int> RowsAndSides
    {
        get
        {
            var data = new TheoryData<int, int>();
            for (var r = 0; r < 5; r++)
            {
                data.Add(r, TacticalBattleState.AttackerSide);
                data.Add(r, TacticalBattleState.DefenderSide);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RowsAndSides))]
    public void Each_type_lands_in_its_formation_block(int r, int side)
    {
        var draws = new ScriptedDraws(r);
        var placed = GeneralPlacement.Place(OneOfEach(side), Context, draws);

        Assert.Equal(new[] { 5 }, draws.Bounds);
        var row = side == TacticalBattleState.AttackerSide ? AttackerRows[0] : DefenderRows[0];
        for (var g = 0; g < 4; g++)
        {
            var type = Form[r][g];
            Assert.Equal((BlockColumn(g, 0), row), (placed.Slots[side * D0 + type].X, placed.Slots[side * D0 + type].Y));
            if (type == HI)
            {
                // The archers follow the heavy infantry in its block, in the next column.
                Assert.Equal((BlockColumn(g, 1), row), (placed.Slots[side * D0 + AR].X, placed.Slots[side * D0 + AR].Y));
            }
        }

        // The old cells were cleared and every new cell painted: the grid holds exactly the five icons.
        AssertGridMatchesSlots(placed);
        Assert.Equal(5, placed.Grid.Count(code => code != TacticalIcon.Empty));
        Assert.Equal(5, placed.Log.OfType<TacticalPlacedEvent>().Count());
    }

    [Fact]
    public void Heavy_infantry_and_archers_share_a_block_continuing_the_same_counters()
    {
        // r = 0: heavy infantry's block is g = 1, columns 4–6. Slots: HI 0, Ar 1, HI 2, Ar 3, HI 4, Ar 5, HI 6.
        // Heavy infantry first, in slot order: 0 → (4,2), 2 → (5,2), 4 → (6,2), 6 → (4,1); then the archers
        // continue: 1 → (5,1), 3 → (6,1), 5 → (4,0).
        var units = new[] { HI, AR, HI, AR, HI, AR, HI }.Select((t, s) => (s, Unit(t, s, 6, 2000)));
        var state = Arena(units, sideToMove: 0, placed: false, counter: 1, attackerComputer: true);

        var placed = GeneralPlacement.Place(state, Context, new ScriptedDraws(0));

        Assert.Equal(
            new[] { (4, 2), (5, 1), (5, 2), (6, 1), (6, 2), (4, 0), (4, 1) },
            Enumerable.Range(0, 7).Select(s => (placed.Slots[s].X, placed.Slots[s].Y)).ToArray());
        Assert.Equal(new[] { 0, 2, 4, 6, 1, 3, 5 }, placed.Log.OfType<TacticalPlacedEvent>().Select(p => p.Slot).ToArray());
        AssertGridMatchesSlots(placed);
    }

    [Fact]
    public void The_archers_join_the_heavy_infantry_block_whatever_the_slow_advance_exempts()
    {
        // A ruleset copy whose slow advance exempts light cavalry instead: the HI + Ar block is the
        // formations' heavy_infantry entry (T122's Done-when 1; T124's entry as amended by plan PR #686), not
        // a type borrowed from slowAdvanceExemptType. r = 0: HI in g = 1, so the archers land at (5,2), not
        // after the light cavalry in g = 3 (11,2).
        var ruleset = ToyRuleset with
        {
            Combat = ToyRuleset.Combat with
            {
                Tactical = ToyRuleset.Combat.Tactical with { SlowAdvanceExemptType = "light_cavalry" },
            },
        };
        var context = TacticalContext.From(ruleset);
        Assert.Equal(LC, context.SlowAdvanceExemptType);

        var placed = GeneralPlacement.Place(OneOfEach(TacticalBattleState.AttackerSide), context, new ScriptedDraws(0));

        Assert.Equal((4, 2), (placed.Slots[HI].X, placed.Slots[HI].Y));
        Assert.Equal((5, 2), (placed.Slots[AR].X, placed.Slots[AR].Y));
        Assert.Equal((10, 2), (placed.Slots[LC].X, placed.Slots[LC].Y));
    }

    [Fact]
    public void Ten_or_more_heavy_infantry_and_archers_stack_on_the_third_row()
    {
        // Defender, r = 2: heavy infantry's block is g = 3, columns 10–12, rows 9, 10, 11. Seven heavy
        // infantry (slots 20–26) and five archers (slots 27–31), twelve units:
        //   1–3 → row 9, 4–6 → row 10, 7–9 → row 11, then the row is held: 10–12 → row 11 again.
        // So the 7th–9th units (HI 26, Ar 27, Ar 28) and the 10th–12th (Ar 29, 30, 31) share cells
        // (10,11), (11,11), (12,11), and each shared cell shows the later-placed unit's icon.
        var units = Enumerable.Range(0, 12).Select(i => (D0 + i, Unit(i < 7 ? HI : AR, i, 3, 2000)));
        var state = Arena(units, sideToMove: 1, placed: false, counter: 1, defenderComputer: true);

        var placed = GeneralPlacement.Place(state, Context, new ScriptedDraws(2));

        var expected = new[]
        {
            (10, 9), (11, 9), (12, 9), (10, 10), (11, 10), (12, 10), (10, 11),
            (11, 11), (12, 11), (10, 11), (11, 11), (12, 11),
        };
        Assert.Equal(expected, Enumerable.Range(0, 12).Select(i => (placed.Slots[D0 + i].X, placed.Slots[D0 + i].Y)).ToArray());

        for (var i = 9; i < 12; i++)
        {
            var (x, y) = expected[i];
            Assert.Equal(IconOf(D0 + i, placed.Slots[D0 + i]), GridAt(placed, x, y));
        }

        // Nine distinct cells hold the twelve units.
        Assert.Equal(9, placed.Grid.Count(code => code != TacticalIcon.Empty));
    }

    [Fact]
    public void A_block_of_more_than_nine_of_one_type_stacks_too()
    {
        // Attacker, r = 4: light infantry's block is g = 3, columns 10–12, rows 2, 1, 0. Eleven light
        // infantry: units 10 and 11 land on (10,0) and (11,0), on top of units 7 and 8.
        var units = Enumerable.Range(0, 11).Select(i => (i, Unit(LI, i, 5, 1000)));
        var state = Arena(units, sideToMove: 0, placed: false, counter: 1, attackerComputer: true);

        var placed = GeneralPlacement.Place(state, Context, new ScriptedDraws(4));

        Assert.Equal((10, 0), (placed.Slots[9].X, placed.Slots[9].Y));
        Assert.Equal((10, 0), (placed.Slots[6].X, placed.Slots[6].Y));
        Assert.Equal((11, 0), (placed.Slots[10].X, placed.Slots[10].Y));
        Assert.Equal((11, 0), (placed.Slots[7].X, placed.Slots[7].Y));
    }

    [Fact]
    public void One_Random_5_per_computer_side_defender_first()
    {
        // The two placement half-rounds of a fresh battle, both sides computer: the defender places with
        // the first draw (r = 3: heavy infantry in g = 2, columns 7–9) and the attacker with the second
        // (r = 1: heavy infantry in g = 1, columns 4–6). Nothing else draws.
        var start = FreshBattle([Unit(HI, 0, 0, 3000)], [Unit(HI, 0, 0, 3000)]);
        var draws = new ScriptedDraws(3, 1);

        var (placed, halfRounds) = RunBounded(start, draws, new ComputerGeneral(), maxHalfRounds: 2);

        Assert.Equal(2, halfRounds);
        Assert.Equal(new[] { 5, 5 }, draws.Bounds);
        Assert.True(placed.Placed);
        Assert.Equal(TacticalBattleState.AttackerSide, placed.SideToMove);
        Assert.Equal((7, 9), (placed.Slots[D0].X, placed.Slots[D0].Y));
        Assert.Equal((4, 2), (placed.Slots[0].X, placed.Slots[0].Y));
        Assert.Equal(new[] { D0, 0 }, placed.Log.OfType<TacticalPlacedEvent>().Select(p => p.Slot).ToArray());
    }

    [Fact]
    public void A_human_side_draws_nothing_for_placement()
    {
        // Only the defender is computer: one Random(5), then the driver waits for the human attacker.
        var start = FreshBattle([Unit(HI, 0, 0, 3000)], [Unit(HI, 0, 0, 3000)], attackerComputer: false);
        var draws = new ScriptedDraws(0);

        var after = TacticalDriver.Run(start, Context, draws, new ComputerGeneral());

        Assert.Equal(new[] { 5 }, draws.Bounds);
        Assert.Equal(TacticalBattleState.AttackerSide, after.SideToMove);
        Assert.False(after.Placed);
        Assert.Equal((0, 0), (after.Slots[0].X, after.Slots[0].Y));
    }
}
