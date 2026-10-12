using IC2.Engine.Armies.Commands;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T15 Army and unit management", Done-when 4: unit join requires the same
/// type, regulars only (the mercenary marker blocks it), merged troops at most the type's battalion size,
/// and merged quality is the arithmetic mean.
/// </summary>
public sealed class JoinUnitsCommandHandlerTests
{
    [Fact]
    public void Join_TwoRegularUnitsOfTheSameType_MergesTroopsAndKeepsTheFirstUnitsIdentity()
    {
        var first = RegularUnit("1st Foot Battalion", troops: 4000, quality: 6);
        var second = RegularUnit("2nd Foot Battalion", troops: 3000, quality: 8);
        var army = Army("join-units-a", NorthNationId, 3, 3, new[] { first, second });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-a", 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        var updated = result.State.ArmyById("join-units-a")!;
        Assert.Single(updated.Units);
        Assert.Equal("1st Foot Battalion", updated.Units[0].Name); // survivor keeps its own name.
        Assert.Equal(7000, updated.Units[0].Troops); // 4,000 + 3,000, conserved exactly.
        Assert.Equal(7, updated.Units[0].Quality); // (6 + 8) / 2 = 7, exact mean.
    }

    [Fact]
    public void Join_QualityMeanThatDoesNotDivideEvenly_TruncatesTowardZero()
    {
        var first = RegularUnit("a", quality: 6);
        var second = RegularUnit("b", quality: 7);
        var army = Army("join-units-b", NorthNationId, 3, 3, new[] { first, second });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-b", 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(6, result.State.ArmyById("join-units-b")!.Units[0].Quality); // (6+7)/2 = 6, truncated.
    }

    [Fact]
    public void Join_EitherUnitIsAMercenary_IsRejected()
    {
        var regular = RegularUnit("a");
        var mercenary = MercenaryUnit("b");
        var army = Army("join-units-c", NorthNationId, 3, 3, new[] { regular, mercenary });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-c", 0, 1));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinUnitsRejections.MercenaryUnit, result.Code);
    }

    [Fact]
    public void Join_DifferentUnitTypes_IsRejected()
    {
        var infantry = RegularUnit("a", unitTypeId: "light_infantry");
        var cavalry = RegularUnit("b", unitTypeId: "light_cavalry");
        var army = Army("join-units-d", NorthNationId, 3, 3, new[] { infantry, cavalry });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-d", 0, 1));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinUnitsRejections.DifferentUnitTypes, result.Code);
    }

    [Fact]
    public void Join_CombinedTroopsAtExactlyTheBattalionSize_IsAccepted()
    {
        // light_infantry's standardBattalionSize is 15,000 (data/rulesets/toy-ruleset.json).
        var first = RegularUnit("a", unitTypeId: "light_infantry", troops: 9000);
        var second = RegularUnit("b", unitTypeId: "light_infantry", troops: 6000);
        var army = Army("join-units-e", NorthNationId, 3, 3, new[] { first, second });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-e", 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(15_000, result.State.ArmyById("join-units-e")!.Units[0].Troops);
    }

    [Fact]
    public void Join_CombinedTroopsOverTheBattalionSize_IsRejected()
    {
        var first = RegularUnit("a", unitTypeId: "light_infantry", troops: 9001);
        var second = RegularUnit("b", unitTypeId: "light_infantry", troops: 6000);
        var army = Army("join-units-f", NorthNationId, 3, 3, new[] { first, second });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-f", 0, 1));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinUnitsRejections.OverBattalionSize, result.Code);
    }

    [Theory]
    [InlineData(0, 0)] // same index twice.
    [InlineData(0, 5)] // out of range.
    [InlineData(-1, 1)] // negative.
    public void Join_InvalidUnitIndex_IsRejected(int first, int second)
    {
        var army = Army("join-units-g", NorthNationId, 3, 3, new[] { RegularUnit("a"), RegularUnit("b") });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-g", first, second));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinUnitsRejections.InvalidUnitIndex, result.Code);
    }

    [Fact]
    public void Join_NotYourArmy_IsRejected()
    {
        var army = Army("join-units-h", SouthNationId, 3, 3, new[] { RegularUnit("a"), RegularUnit("b") });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-h", 0, 1));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinUnitsRejections.NotYourArmy, result.Code);
    }

    /// <summary>
    /// The two-entity probe (<c>docs/build-process.md</c> §4.2 gate 5), applied at unit scope: a third
    /// unit slot in the same army, uninvolved in the merge, must survive completely untouched.
    /// </summary>
    [Fact]
    public void Join_AThirdUninvolvedUnitInTheSameArmy_IsUntouched()
    {
        var bystander = RegularUnit("bystander unit", troops: 555, quality: 9);
        var army = Army("join-units-i", NorthNationId, 3, 3,
            new[] { RegularUnit("a", troops: 100), RegularUnit("b", troops: 200), bystander });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-i", 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        var updated = result.State.ArmyById("join-units-i")!;
        Assert.Equal(2, updated.Units.Count); // the merged survivor, plus the bystander.
        Assert.Contains(updated.Units, u => u == bystander);
    }

    private static UnitSlot Li(string name, int troops, int quality = 6) =>
        RegularUnit(name, unitTypeId: "light_infantry", troops: troops, quality: quality);

    [Fact]
    public void Join_ThreeSelectedUnits_SumsAllIntoTheFirstSelectedSlot()
    {
        var army = Army("join-units-j", NorthNationId, 3, 3,
            new[] { Li("a", 1000), Li("b", 1200), Li("c", 1500) });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-j", new[] { 0, 1, 2 }));

        Assert.True(result.IsAccepted, result.ToString());
        var units = result.State.ArmyById("join-units-j")!.Units;
        Assert.Single(units);
        Assert.Equal(3700, units[0].Troops);
        Assert.Equal("a", units[0].Name);
    }

    [Fact]
    public void Join_FourSelectedUnits_SumsAll()
    {
        var army = Army("join-units-k", NorthNationId, 3, 3,
            new[] { Li("a", 1000), Li("b", 1200), Li("c", 1500), Li("d", 1700) });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-k", new[] { 0, 1, 2, 3 }));

        Assert.True(result.IsAccepted, result.ToString());
        var units = result.State.ArmyById("join-units-k")!.Units;
        Assert.Single(units);
        Assert.Equal(5400, units[0].Troops);
    }

    [Fact]
    public void Join_TwoOfThreeSelected_LeavesTheThirdUntouched()
    {
        var third = Li("c", 1500);
        var army = Army("join-units-l", NorthNationId, 3, 3, new[] { Li("a", 1000), Li("b", 1200), third });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-l", 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        var units = result.State.ArmyById("join-units-l")!.Units;
        Assert.Equal(2, units.Count);
        Assert.Equal(2200, units[0].Troops);
        Assert.Equal(third, units[1]);
    }

    [Fact]
    public void Join_FirstSelectedSlotSurvives_EvenWhenItIsNotTheLowestIndex()
    {
        var army = Army("join-units-m", NorthNationId, 3, 3,
            new[] { Li("a", 100), Li("b", 200), Li("c", 300), Li("d", 400) });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-m", new[] { 2, 0, 3 }));

        Assert.True(result.IsAccepted, result.ToString());
        var units = result.State.ArmyById("join-units-m")!.Units;
        Assert.Equal(new[] { "b", "c" }, units.Select(u => u.Name).ToArray());
        Assert.Equal(800, units[1].Troops);
    }

    [Fact]
    public void Join_ThreeUnitsWhoseQualityDoesNotDivideEvenly_TruncatesTheAverage()
    {
        var army = Army("join-units-n", NorthNationId, 3, 3,
            new[] { Li("a", 100, 4), Li("b", 100, 5), Li("c", 100, 8) });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-n", new[] { 0, 1, 2 }));

        Assert.True(result.IsAccepted, result.ToString());
        var units = result.State.ArmyById("join-units-n")!.Units;
        Assert.Single(units);
        // 17 / 3 = 5 truncated: not 6 (rounded to nearest), not 4 (the first two alone, (4 + 5) / 2).
        Assert.Equal(5, units[0].Quality);
    }

    [Fact]
    public void Join_ThreeSelectedWithOneMercenary_IsRefusedWholeAndLeavesTheArmyUnchanged()
    {
        var army = Army("join-units-o", NorthNationId, 3, 3,
            new[] { Li("a", 100), Li("b", 100), MercenaryUnit("m", unitTypeId: "light_infantry") });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-o", new[] { 0, 1, 2 }));

        Assert.False(result.IsAccepted);
        Assert.Equal(JoinUnitsRejections.MercenaryUnit, result.Code);
        Assert.Equal(army, result.State.ArmyById("join-units-o"));
    }

    [Fact]
    public void Join_ThreeSelectedOfMixedTypes_IsRefusedWholeAndLeavesTheArmyUnchanged()
    {
        var army = Army("join-units-p", NorthNationId, 3, 3,
            new[] { Li("a", 100), Li("b", 100), RegularUnit("c", unitTypeId: "light_cavalry", troops: 100) });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-p", new[] { 0, 1, 2 }));

        Assert.False(result.IsAccepted);
        Assert.Equal(JoinUnitsRejections.DifferentUnitTypes, result.Code);
        Assert.Equal(army, result.State.ArmyById("join-units-p"));
    }

    [Fact]
    public void Join_ThreeSelectedWhoseTotalExceedsTheBattalionSize_IsRefusedWholeAndLeavesTheArmyUnchanged()
    {
        // light_infantry's standardBattalionSize is 15,000 (data/rulesets/toy-ruleset.json).
        var army = Army("join-units-q", NorthNationId, 3, 3,
            new[] { Li("a", 5000), Li("b", 5000), Li("c", 5001) });
        var state = WithArmies(InitialState(), army);

        var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-q", new[] { 0, 1, 2 }));

        Assert.False(result.IsAccepted);
        Assert.Equal(JoinUnitsRejections.OverBattalionSize, result.Code);
        Assert.Equal(army, result.State.ArmyById("join-units-q"));
    }

    [Fact]
    public void Join_FewerThanTwoOrRepeatedIndexes_AreInvalid()
    {
        var army = Army("join-units-r", NorthNationId, 3, 3, new[] { Li("a", 100), Li("b", 100), Li("c", 100) });
        var state = WithArmies(InitialState(), army);

        foreach (var indexes in new[] { new[] { 0 }, new[] { 0, 1, 0 }, new[] { 0, 1, 3 } })
        {
            var result = Dispatcher().Dispatch(state, new JoinUnitsCommand(NorthNationId, "join-units-r", indexes));
            Assert.Equal(JoinUnitsRejections.InvalidUnitIndex, result.Code);
        }
    }

    [Fact]
    public void Join_Commands_WithTheSameIndexes_AreEqualByValue()
    {
        Assert.Equal(
            new JoinUnitsCommand(NorthNationId, "x", 0, 1),
            new JoinUnitsCommand(NorthNationId, "x", new[] { 0, 1 }));
    }
}
