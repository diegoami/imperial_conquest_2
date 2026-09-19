using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T55 Done-when 7: "The recruitment order raises the mobilization rate, and mobilizing does not touch
/// it" — <c>decompiled-mobilization-and-mercenary-restock.md</c> §5, closing
/// <a href="https://github.com/diegoami/imperial_conquest_2/issues/183">#183</a>.
/// The "mobilizing does not touch it" half is asserted where mobilizing happens, in
/// <see cref="MobilizeRecruitSlotCommandHandlerTests"/> and
/// <see cref="RomeAutumnMobilizationReplayTests"/>.
/// </summary>
public sealed class MobilizationRateTests
{
    private static RecruitmentRules Rules => RecruitmentTestbed.Ruleset.Recruitment;

    /// <summary>
    /// The report's own magnitude check: "Rome at wealth 768,000 ordering a 15,000-troop
    /// light-infantry unit gains <c>1 + 15,000,000/768,000 = 1 + 19 = 20</c> points at once".
    /// </summary>
    [Fact]
    public void The_reports_own_worked_example_reproduces()
    {
        const int wealth = 768_000;
        const int troops = 15_000;

        Assert.Equal(20, MobilizationRate.AfterOrderPlaced(0, troops, wealth, Rules));
        Assert.Equal(50, MobilizationRate.AfterOrderPlaced(30, troops, wealth, Rules));
    }

    /// <summary>
    /// The troop term is an integer division, truncated before the flat step is added — not a rounded
    /// or fractional one. At wealth 768,000 a 15,000-troop order and a 15,767-troop order both give
    /// 19, and 15,768 is the first that gives 20.
    /// </summary>
    [Theory]
    [InlineData(15_000, 20)]
    [InlineData(15_359, 20)]  // 15,359,000 / 768,000 = 19.99...
    [InlineData(15_360, 21)]  // exactly 20
    [InlineData(1, 1)]        // 1,000 / 768,000 truncates to 0: the flat step alone
    [InlineData(767, 1)]
    [InlineData(768, 2)]      // the first order worth a whole point of troop term
    public void The_troop_term_truncates(int troops, int expected) =>
        Assert.Equal(expected, MobilizationRate.AfterOrderPlaced(0, troops, 768_000, Rules));

    /// <summary>The raise is clamped at the cap, and a nation already at the cap stays there.</summary>
    [Fact]
    public void The_raise_is_clamped_at_the_cap()
    {
        var cap = Rules.MobilizationCapPercent;
        Assert.Equal(100, cap);

        Assert.Equal(cap, MobilizationRate.AfterOrderPlaced(95, 15_000, 768_000, Rules));
        Assert.Equal(cap, MobilizationRate.AfterOrderPlaced(cap, 1, 768_000, Rules));
        Assert.Equal(cap - 1, MobilizationRate.AfterOrderPlaced(cap - 2, 1, 768_000, Rules));
    }

    /// <summary>
    /// Cancelling is exactly symmetric, floored at 0 — <c>TArmyRecruits_DisbandUnits</c>'s
    /// <c>max(0, mobilized − 1 − (troops × 1000) / wealth)</c>.
    /// </summary>
    [Fact]
    public void Cancelling_is_the_exact_mirror_of_placing_and_is_floored_at_zero()
    {
        const int wealth = 768_000;
        const int troops = 15_000;

        // Place then cancel the same order from a rate high enough that neither clamp bites: the
        // round trip returns to where it started.
        var after = MobilizationRate.AfterOrderPlaced(40, troops, wealth, Rules);
        Assert.Equal(60, after);
        Assert.Equal(40, MobilizationRate.AfterOrderCancelled(after, troops, wealth, Rules));

        Assert.Equal(0, MobilizationRate.AfterOrderCancelled(5, troops, wealth, Rules));
        Assert.Equal(0, MobilizationRate.AfterOrderCancelled(0, 1, wealth, Rules));
        Assert.Equal(4, MobilizationRate.AfterOrderCancelled(5, 1, wealth, Rules));
    }

    /// <summary>
    /// Zero wealth: the original would divide by zero there, this engine treats the troop term as 0 and
    /// still applies the flat step. The edge <see cref="MobilizationRate"/>'s own <c>[designed]</c>
    /// remark claims, and the only place it is visited — a nation with no cities is eliminated and
    /// issues no commands.
    /// </summary>
    [Fact]
    public void Zero_wealth_leaves_the_flat_step_and_divides_by_nothing()
    {
        Assert.Equal(51, MobilizationRate.AfterOrderPlaced(50, 15_000, 0, Rules));
        Assert.Equal(49, MobilizationRate.AfterOrderCancelled(50, 15_000, 0, Rules));

        // Negative wealth is not a state the engine produces either; it takes the same branch rather
        // than producing a negative troop term that would lower the rate on an order being placed.
        Assert.Equal(51, MobilizationRate.AfterOrderPlaced(50, 15_000, -1, Rules));
    }

    /// <summary>
    /// The term is widened before the multiply, so a troop count large enough to overflow
    /// <c>troops × 1000</c> in 32 bits does not wrap into a negative raise. No army cap allows such an
    /// order, which is exactly why nothing else visits this edge.
    /// </summary>
    [Fact]
    public void A_troop_count_that_would_overflow_the_multiply_still_raises_the_rate()
    {
        const int hugeTroops = 2_147_484; // x 1000 overflows int.MaxValue
        var raised = MobilizationRate.AfterOrderPlaced(0, hugeTroops, 1_000_000, Rules);

        Assert.Equal(Rules.MobilizationCapPercent, raised);
        Assert.True(raised > 0);
    }
}
