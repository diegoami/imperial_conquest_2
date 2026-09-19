using IC2.Engine.Armies.Commands;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T15 Army and unit management", Done-when 2: split requires ≥ 2 units,
/// enforces the 198-army cap, gives the new army morale 59 and <c>seatAsymmetry</c>-gated starting moves,
/// conserves troops and units exactly (the real 75,536/16 → 37,081+38,455/9+7 split), and takes a
/// requested money/supply allocation (the real 256 → 156/100 split, and the 0-by-default case,
/// asserted separately per the entry's own instruction).
/// </summary>
public sealed class SplitArmyCommandHandlerTests
{
    /// <summary>
    /// The exact published split (<c>pending-offer-block-army-split-and-naupactus.md</c>): Rome's army 0,
    /// 75,536 troops across 16 units, becomes armies 0 and 13 — 37,081/9 units and 38,455/7 units. The
    /// per-unit troop breakdown itself is not published (only the two group totals are), so this fixture
    /// uses synthetic per-unit troop counts that sum exactly to the two confirmed totals.
    /// </summary>
    private static ArmyState BuildSixteenUnitArmy()
    {
        // Nine units kept with the parent, summing to exactly 37,081.
        var kept = Enumerable.Range(0, 8).Select(i => RegularUnit($"kept{i}", troops: 4000))
            .Append(RegularUnit("kept8", troops: 5081))
            .ToList();
        Assert.Equal(37_081, kept.Sum(u => u.Troops));

        // Seven units moving to the new army, summing to exactly 38,455.
        var moved = Enumerable.Range(0, 6).Select(i => RegularUnit($"moved{i}", troops: 5000))
            .Append(RegularUnit("moved6", troops: 8455))
            .ToList();
        Assert.Equal(38_455, moved.Sum(u => u.Troops));

        var allUnits = kept.Concat(moved).ToList();
        Assert.Equal(75_536, allUnits.Sum(u => u.Troops));
        Assert.Equal(16, allUnits.Count);

        return Army("split-parent", NorthNationId, 88, 26, allUnits, moves: 5, morale: 66);
    }

    [Fact]
    public void Split_TheRealSeventyFiveThousandFiveHundredThirtySixTroopSixteenUnitSplit_ConservesTroopsAndUnitsExactly()
    {
        var parent = BuildSixteenUnitArmy();
        var state = WithArmies(InitialState(), parent);

        // Indices 9..15 (the seven "moved*" units) move to the new army; 0..8 ("kept*") stay.
        var movedIndices = ValueList.Of(9, 10, 11, 12, 13, 14, 15);
        var command = new SplitArmyCommand(NorthNationId, "split-parent", "split-new", movedIndices);

        var result = Dispatcher().Dispatch(state, command);

        Assert.True(result.IsAccepted, result.ToString());
        var remaining = result.State.ArmyById("split-parent")!;
        var created = result.State.ArmyById("split-new")!;

        Assert.Equal(9, remaining.Units.Count);
        Assert.Equal(37_081, remaining.TotalTroops);
        Assert.Equal(7, created.Units.Count);
        Assert.Equal(38_455, created.TotalTroops);

        // Exact conservation, asserted on the totals, not just the parts.
        Assert.Equal(16, remaining.Units.Count + created.Units.Count);
        Assert.Equal(75_536, remaining.TotalTroops + created.TotalTroops);

        // Morale 59 for the new army; the parent's own morale is untouched by the split itself.
        Assert.Equal(59, created.Morale);
        Assert.Equal(66, remaining.Morale);
    }

    [Fact]
    public void Split_TheReal256TalentSplit_ConservesMoneyExactlyAs156And100()
    {
        var parent = Army("split-money", NorthNationId, 3, 3,
            new[] { RegularUnit("a"), RegularUnit("b") }, money: 256);
        var state = WithArmies(InitialState(), parent);

        var command = new SplitArmyCommand(
            NorthNationId, "split-money", "split-money-new", ValueList.Of(1), MoneyToNewArmy: 100);

        var result = Dispatcher().Dispatch(state, command);

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(156, result.State.ArmyById("split-money")!.Money);
        Assert.Equal(100, result.State.ArmyById("split-money-new")!.Money);
        // Conserved exactly: nothing created, nothing destroyed.
        Assert.Equal(256, result.State.ArmyById("split-money")!.Money + result.State.ArmyById("split-money-new")!.Money);
    }

    [Fact]
    public void Split_WithNoRequestedAllocation_DefaultsToZeroForTheNewArmy()
    {
        var parent = Army("split-default", NorthNationId, 3, 3,
            new[] { RegularUnit("a"), RegularUnit("b") }, money: 256, supplyTons: 40);
        var state = WithArmies(InitialState(), parent);

        // MoneyToNewArmy / SupplyTonsToNewArmy omitted -- the command's own declared defaults.
        var command = new SplitArmyCommand(NorthNationId, "split-default", "split-default-new", ValueList.Of(1));

        var result = Dispatcher().Dispatch(state, command);

        Assert.True(result.IsAccepted, result.ToString());
        var created = result.State.ArmyById("split-default-new")!;
        Assert.Equal(0, created.Money);
        Assert.Equal(0, created.SupplyTons);
        Assert.Equal(256, result.State.ArmyById("split-default")!.Money);
        Assert.Equal(40, result.State.ArmyById("split-default")!.SupplyTons);
    }

    [Theory]
    [InlineData(NorthNationId, SeatAsymmetryModel.Faithful, 0)]
    [InlineData(SouthNationId, SeatAsymmetryModel.Faithful, 1)]
    [InlineData(NorthNationId, SeatAsymmetryModel.Normalized, 1)]
    [InlineData(SouthNationId, SeatAsymmetryModel.Normalized, 1)]
    public void Split_NewArmyMoves_AreSeatAsymmetryGated(string nationId, SeatAsymmetryModel model, int expectedMoves)
    {
        var parent = Army("split-moves", nationId, 3, 3, new[] { RegularUnit("a"), RegularUnit("b") });
        var baseState = WithArmies(InitialState(), parent);
        var state = baseState with { ActiveSeatIndex = string.Equals(nationId, SouthNationId, StringComparison.Ordinal) ? 1 : 0 };
        var ruleset = ArmiesTestbed.Ruleset with { Flags = ArmiesTestbed.Ruleset.Flags with { SeatAsymmetry = model } };

        var result = DispatcherWithRuleset(ruleset)
            .Dispatch(state, new SplitArmyCommand(nationId, "split-moves", "split-moves-new", ValueList.Of(1)));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(expectedMoves, result.State.ArmyById("split-moves-new")!.Moves);
    }

    [Fact]
    public void Split_ArmyWithExactlyTwoUnits_IsAccepted()
    {
        var parent = Army("split-min", NorthNationId, 3, 3, new[] { RegularUnit("a"), RegularUnit("b") });
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, "split-min", "split-min-new", ValueList.Of(1)));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Single(result.State.ArmyById("split-min")!.Units);
        Assert.Single(result.State.ArmyById("split-min-new")!.Units);
    }

    [Fact]
    public void Split_ArmyWithOnlyOneUnit_IsRejected()
    {
        var parent = Army("split-one", NorthNationId, 3, 3, new[] { RegularUnit("a") });
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, "split-one", "split-one-new", ValueList<int>.Empty));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.TooFewUnitsToSplit, result.Code);
    }

    [Theory]
    [MemberData(nameof(InvalidSelections))]
    public void Split_InvalidUnitSelection_IsRejected(ValueList<int> selection)
    {
        var parent = Army("split-invalid", NorthNationId, 3, 3,
            new[] { RegularUnit("a"), RegularUnit("b"), RegularUnit("c") });
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, "split-invalid", "split-invalid-new", selection));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.InvalidUnitSelection, result.Code);
    }

    public static IEnumerable<object[]> InvalidSelections()
    {
        yield return new object[] { ValueList<int>.Empty }; // nothing selected.
        yield return new object[] { ValueList.Of(0, 1, 2) }; // all units selected -- parent would be empty.
        yield return new object[] { ValueList.Of(0, 0) }; // duplicate index.
        yield return new object[] { ValueList.Of(5) }; // out of range.
        yield return new object[] { ValueList.Of(-1) }; // negative.
    }

    [Fact]
    public void Split_DuplicateNewArmyId_IsRejected()
    {
        var parent = Army("split-dup", NorthNationId, 3, 3, new[] { RegularUnit("a"), RegularUnit("b") });
        var existing = Army("already-exists", NorthNationId, 9, 9, new[] { RegularUnit("z") });
        var state = WithArmies(InitialState(), parent, existing);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, "split-dup", "already-exists", ValueList.Of(1)));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.DuplicateArmyId, result.Code);
    }

    [Fact]
    public void Split_EmbarkedArmy_IsRejected()
    {
        var parent = Army("split-embarked", NorthNationId, 3, 3,
            new[] { RegularUnit("a"), RegularUnit("b") }, coveredTileCode: null, aboardFleetId: "some-fleet");
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, "split-embarked", "split-embarked-new", ValueList.Of(1)));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.ArmyEmbarked, result.Code);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(300, 0)] // more than the parent's 256.
    public void Split_InvalidMoneyAllocation_IsRejected(int money, int supply)
    {
        var parent = Army("split-badmoney", NorthNationId, 3, 3,
            new[] { RegularUnit("a"), RegularUnit("b") }, money: 256, supplyTons: 10);
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state,
            new SplitArmyCommand(NorthNationId, "split-badmoney", "split-badmoney-new", ValueList.Of(1), MoneyToNewArmy: money, SupplyTonsToNewArmy: supply));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.InvalidMoneyAllocation, result.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(50)] // more than the parent's 10.
    public void Split_InvalidSupplyAllocation_IsRejected(int supply)
    {
        var parent = Army("split-badsupply", NorthNationId, 3, 3,
            new[] { RegularUnit("a"), RegularUnit("b") }, money: 5, supplyTons: 10);
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state,
            new SplitArmyCommand(NorthNationId, "split-badsupply", "split-badsupply-new", ValueList.Of(1), SupplyTonsToNewArmy: supply));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.InvalidSupplyAllocation, result.Code);
    }

    private static ArmyState[] Filler(int count) =>
        Enumerable.Range(0, count)
            .Select(i => Army($"filler-{i}", NorthNationId, 0, 0, new[] { RegularUnit("f") }))
            .ToArray();

    [Fact]
    public void Split_AtOneHundredNinetySevenArmiesTotal_CreatingTheOneHundredNinetyEighthIsAccepted()
    {
        var parent = Army("split-cap-ok", NorthNationId, 3, 3, new[] { RegularUnit("a"), RegularUnit("b") });
        var state = WithArmies(InitialState(), new[] { parent }.Concat(Filler(196)).ToArray());
        Assert.Equal(197, state.Armies.Count);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, "split-cap-ok", "split-cap-ok-new", ValueList.Of(1)));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(198, result.State.Armies.Count);
    }

    [Fact]
    public void Split_AtOneHundredNinetyEightArmiesTotal_IsRejected()
    {
        var parent = Army("split-cap-bad", NorthNationId, 3, 3, new[] { RegularUnit("a"), RegularUnit("b") });
        var state = WithArmies(InitialState(), new[] { parent }.Concat(Filler(197)).ToArray());
        Assert.Equal(198, state.Armies.Count);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, "split-cap-bad", "split-cap-bad-new", ValueList.Of(1)));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.TooManyArmies, result.Code);
    }

    /// <summary>
    /// The two-entity probe (<c>docs/build-process.md</c> §4.2 gate 5): a third, uninvolved army must
    /// survive a split completely untouched.
    /// </summary>
    [Fact]
    public void Split_AThirdUninvolvedArmy_IsUntouched()
    {
        var bystander = Army("split-bystander", NorthNationId, 9, 9, new[] { RegularUnit("z") }, money: 42, supplyTons: 3);
        var parent = Army("split-w-bystander", NorthNationId, 3, 3, new[] { RegularUnit("a"), RegularUnit("b") });
        var state = WithArmies(InitialState(), parent, bystander);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, "split-w-bystander", "split-w-bystander-new", ValueList.Of(1)));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(bystander, result.State.ArmyById("split-bystander"));
    }
}
