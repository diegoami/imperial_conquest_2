using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Battle.Tactical.Improved;

public sealed class QuirkSwitchTests
{
    private static Ruleset Classical => GameDataLoader.LoadFile<Ruleset>(Path.Combine(ModelTestPaths.DataRoot, "rulesets", "classical-faithful.json"));
    private static TacticalContext Changed(Func<TacticalBattleRules, TacticalBattleRules> change)
    {
        var r = Classical;
        return TacticalContext.From(r with { Combat = r.Combat with { Tactical = change(r.Combat.Tactical) } });
    }
    private static TacticalContext Original => TacticalContext.From(Classical);

    [Fact]
    public void Only_shotsNeverNegative_stops_the_general_at_zero_and_preserves_the_first_exchange()
    {
        var state = Arena((0, Unit(LI, 3, 3, 6000, shots: 1, moves: 4)), (D0, Unit(HI, 3, 4, 6000, morale: 99)));
        var oldDraws = new Zeros();
        var newDraws = new Zeros();
        var old = GeneralPassTwo.Run(state, Original, oldDraws);
        var next = GeneralPassTwo.Run(state, Changed(t => t with { ShotsNeverNegative = true }), newDraws);
        Assert.Equal(-3, old.Slots[0].Shots);
        Assert.Equal(0, next.Slots[0].Shots);
        Assert.Equal(0, old.Slots[0].Moves);
        Assert.Equal(3, next.Slots[0].Moves);
        Assert.Equal(4, old.Log.OfType<TacticalShotEvent>().Count());
        Assert.Single(next.Log.OfType<TacticalShotEvent>());
        Assert.Equal(old.Log.OfType<TacticalShotEvent>().First(), next.Log.OfType<TacticalShotEvent>().Single());
        Assert.Equal(oldDraws.Bounds.Take(2), newDraws.Bounds);
        Assert.Equal(old.Slots[D0], next.Slots[D0]);
    }

    [Fact]
    public void Only_wideArithmetic_prevents_the_melee_product_wrap_without_changing_zero_draw_losses()
    {
        var state = Arena((0, Unit(LI, 3, 3, 25000, quality: 5, morale: 70, target: D0)),
            (D0, Unit(HI, 3, 4, 20000, quality: 9, morale: 99)));
        var oldDraws = new Zeros();
        var newDraws = new Zeros();
        var old = TacticalMelee.Resolve(state, 0, Original, oldDraws);
        var next = TacticalMelee.Resolve(state, 0, Changed(t => t with { WideArithmetic = true }), newDraws);
        var oldExchange = Assert.Single(old.Log.OfType<TacticalMeleeEvent>());
        var newExchange = Assert.Single(next.Log.OfType<TacticalMeleeEvent>());
        Assert.Equal(-20231, oldExchange.AttackerBound);
        Assert.Equal(39301, newExchange.AttackerBound);
        Assert.Equal(new[] { -20231, -20231, 107, 107 }, oldDraws.Bounds);
        Assert.Equal(new[] { 39301, 39301, 107, 107 }, newDraws.Bounds);
        Assert.Equal(oldExchange with { AttackerBound = 39301 }, newExchange);
        Assert.Equal(old with { Log = ValueList<TacticalEvent>.Empty }, next with { Log = ValueList<TacticalEvent>.Empty });
    }

    [Fact]
    public void Wide_shot_bound_uses_a_32_bit_minimum_and_wide_numerator()
    {
        var shooter = Unit(LI, 5, 5, 6000, quality: 7, morale: 80);
        var target = Unit(LI, 5, 6, 70000);
        Assert.Equal(-30535, TacticalShooting.Bound(shooter, target, 1, Original));
        Assert.Equal(337, TacticalShooting.Bound(shooter, target, 1, Changed(t => t with { WideArithmetic = true })));
        shooter = shooter with { Troops = 60000, Quality = 9, Morale = 99 };
        var expected = ((long)60000 * 9 * 99 * 18) / (60000 * 5 + 150000);
        Assert.Equal(expected + 1, TacticalShooting.Bound(shooter, target, 1, Changed(t => t with { WideArithmetic = true })));
    }

    [Fact]
    public void Wide_general_scores_and_focus_products_do_not_wrap()
    {
        var state = Arena((0, Unit(LI, 3, 3, 1000)), (D0, Unit(HI, 3, 4, 60000, quality: 9, morale: 99)));
        var wide = Changed(t => t with { WideArithmetic = true });
        var old = GeneralTargeting.Score(state, 0, D0, Original);
        var next = GeneralTargeting.Score(state, 0, D0, wide);
        Assert.True(old < 0);
        Assert.Equal(320760, next);
        Assert.Equal(next, GeneralPassThree.Threat(state, 0, D0, wide));
        var focused = Arena(Enumerable.Range(0, 4).Select(i => (i, Unit(LI, i, 3, 1000, target: D0)))
            .Append((D0, Unit(HI, 3, 4, 800000000))));
        Assert.Equal(-1488838464L, GeneralPassTwo.MeleeKey(focused, 0, D0, Original));
        Assert.Equal(-504000000000L, GeneralPassTwo.MeleeKey(focused, 0, D0, wide));
        var armyUnit = new UnitSlot(0, "heavy_infantry", 100000000, 5, "large");
        Assert.NotEqual(TacticalCopyIn.SortKey(armyUnit, Original), TacticalCopyIn.SortKey(armyUnit, wide));
        Assert.Equal(1500000000L, TacticalCopyIn.SortKey(armyUnit, wide));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Only_placementStacks_moves_overflow_to_empty_home_cells_without_an_extra_draw(int side)
    {
        var first = side * D0;
        var state = Arena(Enumerable.Range(0, 12).Select(i => (first + i, Unit(i < 7 ? HI : AR, i, 5, 2000))),
            sideToMove: side, placed: false, counter: 1);
        var oldDraws = new Zeros();
        var newDraws = new Zeros();
        var old = GeneralPlacement.Place(state, Original, oldDraws);
        var next = GeneralPlacement.Place(state, Changed(t => t with { PlacementStacks = false }), newDraws);
        Assert.Equal(9, old.Slots.Where(s => s.IsLive).Select(s => (s.X, s.Y)).Distinct().Count());
        Assert.Equal(12, next.Slots.Where(s => s.IsLive).Select(s => (s.X, s.Y)).Distinct().Count());
        Assert.Equal(old.Slots.Skip(first).Take(9), next.Slots.Skip(first).Take(9));
        var firstRow = side == 0 ? 0 : 9;
        Assert.Equal(new[] { (0, firstRow), (1, firstRow), (2, firstRow) }, next.Slots.Skip(first + 9).Take(3).Select(s => (s.X, s.Y)));
        Assert.Equal(oldDraws.Bounds, newDraws.Bounds);
        Assert.Equal(new[] { 5 }, newDraws.Bounds);
    }

    [Fact]
    public void With_no_empty_home_cell_the_overflow_unit_keeps_its_copy_in_position()
    {
        var state = Arena(Enumerable.Range(0, 15).Select(i => (i, Unit(HI, i % 14, 4, 2000))), placed: false);
        var next = GeneralPlacement.Place(state, Changed(t => t with { PlacementStacks = false, HomeRows = 1 }), new Zeros());
        Assert.Equal((state.Slots[14].X, state.Slots[14].Y), (next.Slots[14].X, next.Slots[14].Y));
        Assert.Equal(14, next.Slots.Take(14).Select(s => (s.X, s.Y)).Distinct().Count());
    }

    [Fact]
    public void Only_slowAdvance_changes_the_computers_initial_moves_at_long_distance()
    {
        var state = Arena((0, Unit(LI, 0, 0, 6000)), (D0, Unit(HI, 13, 11, 6000)))
            with { ComputerControlled = ValueList.Of(true, false), Counter = 2 };
        var old = TacticalHalfRound.Setup(state, Original);
        var next = TacticalHalfRound.Setup(state, Changed(t => t with { SlowAdvance = false }));
        Assert.Equal(1, old.Slots[0].Moves);
        Assert.Equal(Original.MovesOf(LI), next.Slots[0].Moves);
        Assert.True(next.Slots[0].Moves > old.Slots[0].Moves);
        Assert.Equal(old.Log, next.Log);
        Assert.Equal(old.Grid, next.Grid);
        Assert.Equal(old.Counter, next.Counter);
    }

    private sealed class Zeros : IBattleDraws
    {
        public List<int> Bounds { get; } = new();
        public int Random(int n) { Bounds.Add(n); return 0; }
    }
}
