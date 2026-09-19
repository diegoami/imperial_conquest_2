using IC2.Engine.Armies.Commands;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T15 Army and unit management", Done-when 3: disband is refused away
/// from an owned city; money moves to the treasury and supplies to that city, both conserved exactly.
/// Uses the toy scenario's real cities (<c>arx</c>, <c>portus</c> owned by <c>north</c>; <c>meridia</c>
/// owned by <c>south</c>) rather than fabricated ones.
/// </summary>
public sealed class DisbandArmyCommandHandlerTests
{
    [Fact]
    public void Disband_AtAnOwnedCity_ConservesMoneyAndSuppliesExactly()
    {
        var state = InitialState();
        var arx = state.CityById("arx")!; // (2, 1), owned by north.
        var army = Army("disband-a", NorthNationId, arx.X, arx.Y, new[] { RegularUnit("a") }, money: 90, supplyTons: 30);
        state = WithArmies(state, army);

        var treasuryBefore = state.NationById(NorthNationId)!.Treasury;
        var citySupplyBefore = arx.SupplyTons;

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-a"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.ArmyById("disband-a"));
        Assert.Equal(treasuryBefore + 90, result.State.NationById(NorthNationId)!.Treasury);
        Assert.Equal(citySupplyBefore + 30, result.State.CityById("arx")!.SupplyTons);
    }

    [Fact]
    public void Disband_AwayFromAnyCity_IsRejected()
    {
        var state = InitialState();
        var army = Army("disband-b", NorthNationId, 99, 99, new[] { RegularUnit("a") });
        state = WithArmies(state, army);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-b"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.NotNearOwnCity, result.Code);
        Assert.NotNull(result.State.ArmyById("disband-b")); // nothing changed on rejection.
    }

    [Fact]
    public void Disband_AtAForeignCity_IsRejected()
    {
        var state = InitialState();
        var meridia = state.CityById("meridia")!; // (3, 4), owned by south.
        var army = Army("disband-c", NorthNationId, meridia.X, meridia.Y, new[] { RegularUnit("a") });
        state = WithArmies(state, army);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-c"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.NotNearOwnCity, result.Code);
    }

    [Fact]
    public void Disband_EmbarkedArmy_IsRejected()
    {
        var state = InitialState();
        var arx = state.CityById("arx")!;
        var army = Army("disband-d", NorthNationId, arx.X, arx.Y, new[] { RegularUnit("a") },
            coveredTileCode: null, aboardFleetId: "some-fleet");
        state = WithArmies(state, army);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-d"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.ArmyEmbarked, result.Code);
    }

    [Fact]
    public void Disband_NotYourArmy_IsRejected()
    {
        var state = InitialState();
        var arx = state.CityById("arx")!;
        var army = Army("disband-e", SouthNationId, arx.X, arx.Y, new[] { RegularUnit("a") });
        state = WithArmies(state, army);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-e"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.NotYourArmy, result.Code);
    }

    [Fact]
    public void Disband_UnknownArmy_IsRejected()
    {
        var result = Dispatcher().Dispatch(InitialState(), new DisbandArmyCommand(NorthNationId, "no-such-army"));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.UnknownArmy, result.Code);
    }

    /// <summary>
    /// The two-entity probe (<c>docs/build-process.md</c> §4.2 gate 5): a second army at the very same
    /// city must survive a disband completely untouched — an over-broad clear would fail this, not a
    /// single-entity fixture.
    /// </summary>
    [Fact]
    public void Disband_ASecondArmyAtTheSameCity_IsUntouched()
    {
        var state = InitialState();
        var arx = state.CityById("arx")!;
        var toDisband = Army("disband-f", NorthNationId, arx.X, arx.Y, new[] { RegularUnit("a") }, money: 20, supplyTons: 5);
        var bystander = Army("disband-bystander", NorthNationId, arx.X, arx.Y, new[] { RegularUnit("z") }, money: 60, supplyTons: 8);
        state = WithArmies(state, toDisband, bystander);

        var result = Dispatcher().Dispatch(state, new DisbandArmyCommand(NorthNationId, "disband-f"));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(bystander, result.State.ArmyById("disband-bystander"));
    }
}
