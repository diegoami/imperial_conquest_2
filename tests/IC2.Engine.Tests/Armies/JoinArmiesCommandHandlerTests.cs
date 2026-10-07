using IC2.Engine.Armies.Commands;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T15 Army and unit management", Done-when 1 (join caps at 20 units /
/// 100,000 troops, refuses an embarked army, zeroes the survivor's moves, pools money and supplies
/// exactly) and Done-when 6 (the army-join seam of the <c>MaxUnitsPerArmy</c> cap, issue #181): a
/// 19-unit-combined join accepts, a 20-unit-combined one does too (the cap is inclusive), and 21 refuses.
/// </summary>
public sealed class JoinArmiesCommandHandlerTests
{
    private static IEnumerable<UnitSlot> Units(int count, string prefix = "u") =>
        Enumerable.Range(0, count).Select(i => RegularUnit($"{prefix}{i}", troops: 100));

    [Fact]
    public void Join_CombinedUnitsAtExactlyNineteen_IsAccepted()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-a", NorthNationId, 3, 3, Units(18)),
            Army("join-b", NorthNationId, 4, 3, Units(1, "v")));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-a", "join-b"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(19, result.State.ArmyById("join-a")!.Units.Count);
        Assert.Null(result.State.ArmyById("join-b"));
    }

    [Fact]
    public void Join_CombinedUnitsAtExactlyTwenty_IsAccepted()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-c", NorthNationId, 3, 3, Units(19)),
            Army("join-d", NorthNationId, 4, 3, Units(1, "v")));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-c", "join-d"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(20, result.State.ArmyById("join-c")!.Units.Count);
    }

    [Fact]
    public void Join_CombinedUnitsAtTwentyOne_IsRejected()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-e", NorthNationId, 3, 3, Units(20)),
            Army("join-f", NorthNationId, 4, 3, Units(1, "v")));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-e", "join-f"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinArmiesRejections.CombinedUnitsTooLarge, result.Code);
        // Nothing changed on a rejection: both armies still exist.
        Assert.NotNull(result.State.ArmyById("join-e"));
        Assert.NotNull(result.State.ArmyById("join-f"));
    }

    [Fact]
    public void Join_CombinedTroopsAboveOneHundredThousand_IsRejected()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-g", NorthNationId, 3, 3, new[] { RegularUnit("g0", troops: 60_000) }),
            Army("join-h", NorthNationId, 4, 3, new[] { RegularUnit("h0", troops: 40_001) }));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-g", "join-h"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinArmiesRejections.CombinedTroopsTooLarge, result.Code);
    }

    [Fact]
    public void Join_CombinedTroopsAtExactlyOneHundredThousand_IsAccepted()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-i", NorthNationId, 3, 3, new[] { RegularUnit("i0", troops: 60_000) }),
            Army("join-j", NorthNationId, 4, 3, new[] { RegularUnit("j0", troops: 40_000) }));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-i", "join-j"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(100_000, result.State.ArmyById("join-i")!.TotalTroops);
    }

    [Fact]
    public void Join_EitherArmyEmbarked_IsRejected()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-k", NorthNationId, 3, 3, Units(1), coveredTileCode: null, aboardFleetId: "some-fleet"),
            Army("join-l", NorthNationId, 3, 3, Units(1, "v")));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-k", "join-l"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinArmiesRejections.ArmyEmbarked, result.Code);
    }

    [Fact]
    public void Join_NotAdjacent_IsRejected()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-m", NorthNationId, 3, 3, Units(1)),
            Army("join-n", NorthNationId, 5, 5, Units(1, "v")));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-m", "join-n"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinArmiesRejections.NotAdjacent, result.Code);
    }

    [Fact]
    public void Join_PoolsMoneyAndSuppliesExactlyAndZeroesSurvivorMoves()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-o", NorthNationId, 3, 3, Units(1), moves: 5, money: 156, supplyTons: 40),
            Army("join-p", NorthNationId, 4, 3, Units(1, "v"), moves: 5, money: 100, supplyTons: 12));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-o", "join-p"));

        Assert.True(result.IsAccepted, result.ToString());
        var survivor = result.State.ArmyById("join-o")!;
        Assert.Equal(256, survivor.Money); // 156 + 100, under the 1,000 purse cap: conserved exactly.
        Assert.Equal(52, survivor.SupplyTons); // 40 + 12, no cap on supply.
        Assert.Equal(0, survivor.Moves);
        Assert.Null(result.State.ArmyById("join-p"));
    }

    /// <summary>
    /// T72 (bug #315), Done-when 3: a join of 800 + 600 gives 1,400 — the pooled purse adds UNCAPPED.
    /// Row 7 of <c>2026-10-05-army-purse-writes-and-the-1000-cap.md</c> reads <c>TUnitMap_JoinArmies</c>
    /// :46992-46993 as <c>kept.purse += partner.purse</c> [derived], 1,000 + 1,000 gave 2,000 in play
    /// [Wine candidates <c>Q1_05_before_join.SAV</c> → <c>Q1_06_after_join.SAV</c>], and no treasury is
    /// touched — the pre-T72 behaviour (clamp at 1,000, excess 800 to the treasury) fails this test, and
    /// was the bug: it cut IP016-style over-1,000 purses on the next join.
    /// </summary>
    [Fact]
    public void Join_PooledMoneyAboveOneThousand_AddsUncapped_AndNoTreasuryIsTouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-q", NorthNationId, 3, 3, Units(1), money: 800),
            Army("join-r", NorthNationId, 4, 3, Units(1, "v"), money: 600));
        var treasuryBefore = state.NationById(NorthNationId)!.Treasury;

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-q", "join-r"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(1400, result.State.ArmyById("join-q")!.Money); // 800 + 600, not cut to 1,000.
        Assert.Equal(treasuryBefore, result.State.NationById(NorthNationId)!.Treasury); // the join moves money between the two army records only.
    }

    /// <summary>
    /// The same uncapped pool with two purses the pre-T72 test put at 900 each: 1,800 rides the survivor,
    /// the treasury unmoved (T72 recomputes the pre-T72 pin "clamped, excess 800 credited to the treasury"
    /// from the path's real rule, row 7 — the old pin was evidence of the bug, not of correct behaviour).
    /// </summary>
    [Fact]
    public void Join_PooledMoneyAtNinetyEach_AddsUncappedToEighteenHundred()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-s", NorthNationId, 3, 3, Units(1), money: 900),
            Army("join-t", NorthNationId, 4, 3, Units(1, "v"), money: 900));
        var treasuryBefore = state.NationById(NorthNationId)!.Treasury;

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-s", "join-t"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(1800, result.State.ArmyById("join-s")!.Money);
        Assert.Equal(treasuryBefore, result.State.NationById(NorthNationId)!.Treasury);
    }

    /// <summary>
    /// Done-when 4: no purse leaves the field's range. The original's join is a 16-bit add that wraps
    /// above 32,767, possibly to a negative purse (row 7 [derived: code, not played]); the clone enforces
    /// <c>0 … 32,767</c> instead [designed: the user's 2026-10-05 choice, PR #758's R2 resolution] — the
    /// sum lands at 32,767 and the excess goes NOWHERE (this path touches no treasury in the original,
    /// so there is nothing to credit it to; see <c>JoinArmiesCommandHandler</c>'s remark).
    /// </summary>
    [Fact]
    public void Join_PooledMoneyAboveTheFieldRange_LandsAtThirtyTwoThousandSevenHundredSixtySeven()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-u", NorthNationId, 3, 3, Units(1), money: 32000),
            Army("join-v", NorthNationId, 4, 3, Units(1, "w"), money: 2000));
        var treasuryBefore = state.NationById(NorthNationId)!.Treasury;

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-u", "join-v"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(32767, result.State.ArmyById("join-u")!.Money); // 34,000 clamped to the signed 16-bit field, no wrap.
        Assert.Equal(treasuryBefore, result.State.NationById(NorthNationId)!.Treasury); // the 233-talent excess goes nowhere.
    }

    /// <summary>
    /// The two-entity probe (<c>docs/build-process.md</c> §4.2 gate 5): a third, uninvolved army must
    /// survive a join completely untouched — an over-broad clear would fail this, not a single-entity
    /// fixture.
    /// </summary>
    [Fact]
    public void Join_AThirdUninvolvedArmy_IsUntouched()
    {
        var bystander = Army("join-bystander", NorthNationId, 9, 9, Units(3, "b"), money: 77, supplyTons: 11, moves: 4);
        var state = WithArmies(
            InitialState(),
            Army("join-s", NorthNationId, 3, 3, Units(1)),
            Army("join-t", NorthNationId, 4, 3, Units(1, "v")),
            bystander);

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-s", "join-t"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(bystander, result.State.ArmyById("join-bystander"));
    }

    [Fact]
    public void Join_NotYourArmy_IsRejected()
    {
        var state = WithArmies(
            InitialState(),
            Army("join-u", NorthNationId, 3, 3, Units(1)),
            Army("join-v", SouthNationId, 4, 3, Units(1, "v")));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-u", "join-v"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinArmiesRejections.NotYourArmy, result.Code);
    }

    [Fact]
    public void Join_UnknownArmy_IsRejected()
    {
        var state = WithArmies(InitialState(), Army("join-w", NorthNationId, 3, 3, Units(1)));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-w", "no-such-army"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinArmiesRejections.UnknownArmy, result.Code);
    }

    [Fact]
    public void Join_SameArmyTwice_IsRejected()
    {
        var state = WithArmies(InitialState(), Army("join-x", NorthNationId, 3, 3, Units(1)));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-x", "join-x"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinArmiesRejections.SameArmy, result.Code);
    }
}
