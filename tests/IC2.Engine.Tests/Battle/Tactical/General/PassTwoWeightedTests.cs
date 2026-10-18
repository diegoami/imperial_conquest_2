using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

public sealed class PassTwoWeightedTests
{
    private static TacticalBattleState Issue(bool reverse = false) => Arena(new[]
    {
        (0, Unit(HI, 5, 5, 3000, moves: 1)),
        (D0, reverse ? Unit(HI, 6, 5, 2000, quality: 9, morale: 90) : Unit(LI, 5, 6, 4000, quality: 1, morale: 50, shots: 0)),
        (D0 + 1, reverse ? Unit(LI, 5, 6, 4000, quality: 1, morale: 50, shots: 0) : Unit(HI, 6, 5, 2000, quality: 9, morale: 90))
    }, attackerGeneral: true);

    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 21)]
    public void Production_run_uses_weighted_candidate_odds(bool reverse, int target)
    {
        // Actor-v-LI 630000, enemy 8000; actor-v-HI 52500, enemy 81000.
        var state = Issue(reverse);
        Assert.Equal(8000, GeneralPassTwo.MeleeKey(state, 0, target, Context));
        Assert.Equal(81000, GeneralPassTwo.MeleeKey(state, 0, target == D0 ? D0 + 1 : D0, Context));
        var journal = new List<GeneralDecision>();
        var after = GeneralPassTwo.Run(state, Context, NoDraws.Instance, journal);
        Assert.Equal(target, after.Slots[0].Target);
        Assert.Equal(state.Slots[0] with { Target = target }, after.Slots[0]);
        Assert.Equal(new GeneralDecision(0, GeneralStep.PassTwoTarget, target, 5, 6), Assert.Single(journal));
        Assert.Equal(new TacticalTargetSetEvent(0, target), Assert.Single(after.Log));
    }

    [Fact]
    public void Lower_ineligible_key_does_not_hide_later_eligible_candidate()
    {
        // HI actor strength vs HI 52500: 81000 fails at 54000. LI's higher key 84000
        // passes against actor-v-LI 630000. A global minimum followed by odds would reject both.
        var state = Arena((0, Unit(HI, 5, 5, 3000)),
            (D0, Unit(HI, 5, 6, 2000, quality: 9, morale: 90)),
            (D0 + 1, Unit(LI, 6, 5, 6000, shots: 0)));
        Assert.Equal(81000, GeneralPassTwo.MeleeKey(state, 0, D0, Context));
        Assert.Equal(84000, GeneralPassTwo.MeleeKey(state, 0, D0 + 1, Context));
        Assert.Equal(D0 + 1, GeneralPassTwo.Run(state, Context, NoDraws.Instance).Slots[0].Target);
    }

    [Theory]
    [InlineData(4499, 20)]
    [InlineData(4500, -1)]
    [InlineData(4520, -1)]
    public void Eligibility_is_strict_at_equality(int troops, int target)
    {
        // Same-type HI: actor 52500; enemies 78400,78750,79100 (truncation before morale).
        var state = Arena((0, Unit(HI, 5, 5, 3000)), (D0, Unit(HI, 5, 6, troops)));
        Assert.Equal(target, GeneralPassTwo.Run(state, Context, NoDraws.Instance).Slots[0].Target);
    }

    [Fact]
    public void Equal_weighted_keys_keep_first_slot()
    {
        var state = Arena((0, Unit(HI, 5, 5, 3000)),
            (D0, Unit(HI, 5, 6, 2000)), (D0 + 1, Unit(HI, 6, 5, 2000)));
        Assert.Equal(D0, GeneralPassTwo.Run(state, Context, NoDraws.Instance).Slots[0].Target);
    }

    [Theory]
    [InlineData(1999999, 99999950, 20)]
    [InlineData(2000000, 100000000, -1)]
    [InlineData(2000001, 100000050, -1)]
    public void Initial_minimum_is_one_hundred_million(int troops, long key, int target)
    {
        var state = Arena((0, Unit(HI, 5, 5, 2000000, quality: 100, morale: 100)),
            (D0, Unit(HI, 5, 6, troops, quality: 100, morale: 10)));
        Assert.Equal(key, GeneralPassTwo.MeleeKey(state, 0, D0, Context));
        Assert.Equal(target, GeneralPassTwo.Run(state, Context, NoDraws.Instance).Slots[0].Target);
    }

    [Theory]
    [InlineData(0, 106500)]
    [InlineData(1, 0)]
    [InlineData(2, -106500)]
    [InlineData(19, -1917000)]
    public void Strength_truncates_before_morale_and_counts_all_dead_focus_words(int focus, long key)
    {
        // (10003*3)/100 = 300, *71*5 =106500; postponing division would give 106531.
        var units = Enumerable.Range(1, focus).Select(s => (s, Unit(HI, 0, 0, 0, target: D0)))
            .Append((0, Unit(HI, 5, 5, 3000))).Append((D0, Unit(HI, 5, 6, 10003, quality: 3, morale: 71)));
        var state = Arena(units);
        Assert.Equal(focus, GeneralTargeting.Focus(state, 0, D0, Context));
        Assert.Equal(key, GeneralPassTwo.MeleeKey(state, 0, D0, Context));
        Assert.Equal(focus == 0 ? -1 : D0, GeneralPassTwo.Run(state, Context, NoDraws.Instance).Slots[0].Target);
    }

    [Fact]
    public void Signed_product_wrap_and_division_precede_later_products()
    {
        // 1000000003*3 wraps to -1294967287; signed /100 is -12949672 (toward zero).
        // The final *71*5 wraps to -302166264.
        var state = Arena((0, Unit(HI, 5, 5, 3000)),
            (D0, Unit(HI, 5, 6, 1000000003, quality: 3, morale: 71)));
        Assert.Equal(-302166264, GeneralPassTwo.MeleeKey(state, 0, D0, Context));
        Assert.Equal(D0, GeneralPassTwo.Run(state, Context, NoDraws.Instance).Slots[0].Target);
        var r = ToyRuleset;
        var wide = TacticalContext.From(r with { Combat = r.Combat with { Tactical = r.Combat.Tactical with { WideArithmetic = true } } });
        Assert.Equal(10650000000L, GeneralPassTwo.MeleeKey(state, 0, D0, wide));
        Assert.Equal(-1, GeneralPassTwo.Run(state, wide, NoDraws.Instance).Slots[0].Target);
    }

    [Fact]
    public void Odds_multiply_wraps_before_signed_division()
    {
        // Enemy strength 1.5 billion; 2*S wraps to -1294967296, /3=-431655765.
        // Two dead focus words negate that key to -1.5 billion, below the initial minimum.
        // Doubling the negative key wraps positive, /3=431655765, so it is ineligible.
        var state = Arena((0, Unit(HI, 5, 5, 3000)),
            (1, Unit(HI, 0, 0, 0, target: D0)), (2, Unit(HI, 0, 0, 0, target: D0)),
            (D0, Unit(HI, 5, 6, 3000000, quality: 100, morale: 100)));
        Assert.Equal(-1500000000, GeneralPassTwo.MeleeKey(state, 0, D0, Context));
        Assert.Equal(-1, GeneralPassTwo.Run(state, Context, NoDraws.Instance).Slots[0].Target);
    }

    [Fact]
    public void Nested_pass_three_uses_weighted_choice_without_selector_draws()
    {
        var journal = new List<GeneralDecision>();
        var draws = new DelphiBattleDraws(17);
        var original = Issue();
        // A retained shot with HI range zero prevents Walk auto-targeting or firing;
        // the nested Pass2 must make the choice. Two moves remain at the occupied aim.
        var state = original with { Slots = IC2.Engine.Model.ValueList.From(original.Slots.Select((u, s) => s == 0 ? u with { Shots = 1, Moves = 2 } : u)) };
        var after = GeneralPassThree.Run(state, Context, draws, journal);
        Assert.Equal(D0, after.Slots[0].Target);
        Assert.Contains(new GeneralDecision(0, GeneralStep.PassTwoTarget, D0, 5, 6), journal);
        Assert.Equal(17u, draws.RandSeed);
        Assert.Empty(after.Log.OfType<TacticalDrawEvent>());
        Assert.Equal(after, GeneralPassThree.Run(state, Context, new DelphiBattleDraws(17)));
    }
}
