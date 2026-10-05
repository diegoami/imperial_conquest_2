using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/tasks/T136.md</c>: disbanding a regular unit lowers the nation's mobilisation
/// (<c>MobilizationRate.AfterOrderCancelled</c>); a mercenary's disband changes no nation field.
/// </summary>
public sealed class DisbandUnitMobilizationTests
{
    private static GameState Scripted(int mobilized, int wealth, UnitSlot disbanded)
    {
        var state = InitialState();
        var nation = state.NationById(NorthNationId)!;
        state = WithNation(state, nation with { MobilizedPercent = mobilized, Wealth = wealth });
        var army = Army("t136-army", NorthNationId, 0, 0, [RegularUnit("keep", troops: 500), disbanded]);
        return WithArmies(state, army);
    }

    private static (GameState Before, CommandResult Result) Run(int mobilized, int wealth, UnitSlot disbanded)
    {
        var before = Scripted(mobilized, wealth, disbanded);
        var result = Dispatcher().Dispatch(before, new DisbandUnitCommand(NorthNationId, "t136-army", 1));
        return (before, result);
    }

    [Fact]
    public void RegularUnit_LowersMobilisationByTroopTermPlusStep_AndChangesNothingElseInTheNation()
    {
        var (before, result) = Run(40, 768_000, RegularUnit("gone", troops: 15_000));

        Assert.True(result.IsAccepted, result.ToString());
        var nation = result.State.NationById(NorthNationId)!;
        Assert.Equal(20, nation.MobilizedPercent);
        Assert.Equal(before.NationById(NorthNationId)! with { MobilizedPercent = 20 }, nation);
        Assert.Single(result.State.ArmyById("t136-army")!.Units);
    }

    [Fact]
    public void Floor_IsZero_NotNegative()
    {
        var (_, result) = Run(5, 768_000, RegularUnit("gone", troops: 15_000));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(0, result.State.NationById(NorthNationId)!.MobilizedPercent);
    }

    [Fact]
    public void ZeroWealth_LowersByTheStepAlone()
    {
        var (_, result) = Run(40, 0, RegularUnit("gone", troops: 15_000));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(39, result.State.NationById(NorthNationId)!.MobilizedPercent);
    }

    [Fact]
    public void MercenaryUnit_ChangesNoNationField()
    {
        var (before, result) = Run(40, 768_000, MercenaryUnit("gone", troops: 15_000));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(before.NationById(NorthNationId), result.State.NationById(NorthNationId));
    }

    [Theory]
    [InlineData(40, 15_000, 768_000)]
    [InlineData(90, 2_000, 100_000)]
    [InlineData(12, 700, 3_000)]
    public void NewValue_EqualsMobilizationRateAfterOrderCancelled(int mobilized, int troops, int wealth)
    {
        var (_, result) = Run(mobilized, wealth, RegularUnit("gone", troops: troops));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(
            MobilizationRate.AfterOrderCancelled(mobilized, troops, wealth, ArmiesTestbed.Ruleset.Recruitment),
            result.State.NationById(NorthNationId)!.MobilizedPercent);
    }

    [Fact]
    public void LastUnit_Refusal_LeavesTheStateUntouched()
    {
        var state = WithArmies(
            WithNation(InitialState(), InitialState().NationById(NorthNationId)! with { MobilizedPercent = 40 }),
            Army("t136-army", NorthNationId, 0, 0, [RegularUnit("only", troops: 1_000)]));

        var result = Dispatcher().Dispatch(state, new DisbandUnitCommand(NorthNationId, "t136-army", 0));

        Assert.False(result.IsAccepted);
        Assert.Same(state, result.State);
        Assert.Equal(40, result.State.NationById(NorthNationId)!.MobilizedPercent);
    }

    [Fact]
    public void NotYourArmy_Refusal_LeavesMobilisationUnchanged()
    {
        var before = Scripted(40, 768_000, RegularUnit("gone", troops: 15_000));

        var result = Dispatcher().Dispatch(before, new DisbandUnitCommand(SouthNationId, "t136-army", 1));

        Assert.False(result.IsAccepted);
        Assert.Same(before, result.State);
        Assert.Equal(40, result.State.NationById(NorthNationId)!.MobilizedPercent);
    }
}
