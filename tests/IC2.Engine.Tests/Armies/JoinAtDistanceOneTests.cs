using IC2.Engine.Armies;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Tests.Naval;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/tasks/T114.md</c> Done-when 1–4: Join armies, Join fleets and Transfer ships all take their
/// partner at Chebyshev distance exactly 1 — the original's <c>FUN_00449D64</c> (armies) and
/// <c>FUN_00449DD8</c> (fleets), through <c>FUN_004492A0</c>'s <c>distance == 1</c>. Distance 0 and
/// distance 2 are both refused; a diagonal neighbour is at distance 1.
/// </summary>
/// <remarks>
/// The toy map is 8×6. The ships orders never check terrain, so a fleet fixture may stand on any cell;
/// the positions here are chosen only to make an orthogonal and a diagonal pair each distance exactly 1
/// and a third unit distance 2 away, which is what the gate reads.
/// </remarks>
public sealed class JoinAtDistanceOneTests
{
    private const string NationId = "north";

    private static FleetState Fleet(string id, int x, int y, int ships = 10, int supply = 0, int money = 0) =>
        new(id, NationId, x, y, Moves: 5, ships, ConditionPercent: 90, money, supply,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    private static ArmyState OneUnitArmy(string id, int x, int y, int supply = 0, int money = 0) =>
        Army(id, NorthNationId, x, y, new[] { RegularUnit($"{id}-unit") }, moves: 5, money: money, supplyTons: supply);

    // ---- Done-when 1: join-armies ----

    [Theory]
    [InlineData(4, 3)] // orthogonal neighbour
    [InlineData(4, 4)] // diagonal neighbour
    public void JoinArmies_AtDistanceOne_JoinsAndPoolsLikeTheFormerCoLocatedPair(int partnerX, int partnerY)
    {
        var state = WithArmies(
            InitialState(),
            OneUnitArmy("join-1a", 3, 3, supply: 40, money: 156),
            OneUnitArmy("join-1b", partnerX, partnerY, supply: 12, money: 100));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-1a", "join-1b"));

        Assert.True(result.IsAccepted, result.ToString());
        var survivor = result.State.ArmyById("join-1a")!;
        Assert.Equal(2, survivor.Units.Count);
        Assert.Equal(256, survivor.Money);
        Assert.Equal(52, survivor.SupplyTons);
        Assert.Equal(0, survivor.Moves);
        // The survivor keeps its own tile; the absorbed army is removed.
        Assert.Equal(3, survivor.X);
        Assert.Equal(3, survivor.Y);
        Assert.Null(result.State.ArmyById("join-1b"));
    }

    [Fact]
    public void JoinArmies_AtDistanceZero_IsRejectedAndTheStateIsUnchanged()
    {
        var state = WithArmies(
            InitialState(),
            OneUnitArmy("join-0a", 3, 3),
            OneUnitArmy("join-0b", 3, 3));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-0a", "join-0b"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinArmiesRejections.NotAdjacent, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void JoinArmies_AtDistanceTwo_IsRejectedAndTheStateIsUnchanged()
    {
        var state = WithArmies(
            InitialState(),
            OneUnitArmy("join-2a", 3, 3),
            OneUnitArmy("join-2b", 5, 3));

        var result = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "join-2a", "join-2b"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinArmiesRejections.NotAdjacent, result.Code);
        Assert.Same(state, result.State);
    }

    // ---- Done-when 2: join-fleets ----

    [Theory]
    [InlineData(3, 5, 4, 5)] // orthogonal: both sea
    [InlineData(1, 4, 2, 5)] // diagonal: both sea
    public void JoinFleets_AtDistanceOne_JoinsAndPoolsLikeTheFormerCoLocatedPair(
        int primaryX, int primaryY, int partnerX, int partnerY)
    {
        var state = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(
                Fleet("jf-1a", primaryX, primaryY, ships: 10, supply: 10, money: 20),
                Fleet("jf-1b", partnerX, partnerY, ships: 5, supply: 5, money: 8)),
        };

        var result = NavalTestbed.RealEngineDispatcher()
            .Dispatch(state, new JoinFleetsCommand(NationId, "jf-1a", "jf-1b"));

        Assert.True(result.IsAccepted, result.ToString());
        var survivor = result.State.FleetById("jf-1a")!;
        Assert.Equal(15, survivor.Ships);
        Assert.Equal(15, survivor.SupplyTons);
        Assert.Equal(28, survivor.Money);
        Assert.Equal(0, survivor.Moves);
        Assert.Null(result.State.FleetById("jf-1b"));
    }

    [Fact]
    public void JoinFleets_AtDistanceZero_IsRejectedAndTheStateIsUnchanged()
    {
        var state = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(Fleet("jf-0a", 3, 5), Fleet("jf-0b", 3, 5)),
        };

        var result = NavalTestbed.RealEngineDispatcher()
            .Dispatch(state, new JoinFleetsCommand(NationId, "jf-0a", "jf-0b"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinFleetsRejections.NotAdjacent, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void JoinFleets_AtDistanceTwo_IsRejectedAndTheStateIsUnchanged()
    {
        var state = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(Fleet("jf-2a", 3, 5), Fleet("jf-2b", 5, 5)),
        };

        var result = NavalTestbed.RealEngineDispatcher()
            .Dispatch(state, new JoinFleetsCommand(NationId, "jf-2a", "jf-2b"));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinFleetsRejections.NotAdjacent, result.Code);
        Assert.Same(state, result.State);
    }

    // ---- Done-when 3: fleet-transfer ----

    [Theory]
    [InlineData(3, 5, 4, 5)] // orthogonal
    [InlineData(1, 4, 2, 5)] // diagonal
    public void FleetTransfer_AtDistanceOne_MovesExactlyTheRequestedAmounts(
        int sourceX, int sourceY, int targetX, int targetY)
    {
        var state = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(
                Fleet("xf-1a", sourceX, sourceY, ships: 30, supply: 100, money: 50),
                Fleet("xf-1b", targetX, targetY, ships: 20, supply: 10, money: 5)),
        };

        var result = NavalTestbed.RealEngineDispatcher().Dispatch(
            state, new FleetToFleetTransferCommand(NationId, "xf-1a", "xf-1b", Ships: 10, SupplyTons: 20, Money: 5));

        Assert.True(result.IsAccepted, result.ToString());
        var source = result.State.FleetById("xf-1a")!;
        var target = result.State.FleetById("xf-1b")!;
        Assert.Equal(20, source.Ships);
        Assert.Equal(80, source.SupplyTons);
        Assert.Equal(45, source.Money);
        Assert.Equal(30, target.Ships);
        Assert.Equal(30, target.SupplyTons);
        Assert.Equal(10, target.Money);
    }

    [Fact]
    public void FleetTransfer_AtDistanceZero_IsRejectedAndTheStateIsUnchanged()
    {
        var state = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(Fleet("xf-0a", 3, 5, ships: 10), Fleet("xf-0b", 3, 5, ships: 5)),
        };

        var result = NavalTestbed.RealEngineDispatcher().Dispatch(
            state, new FleetToFleetTransferCommand(NationId, "xf-0a", "xf-0b", 1, 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(FleetToFleetTransferRejections.NotAdjacent, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void FleetTransfer_AtDistanceTwo_IsRejectedAndTheStateIsUnchanged()
    {
        var state = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(Fleet("xf-2a", 3, 5, ships: 10), Fleet("xf-2b", 5, 5, ships: 5)),
        };

        var result = NavalTestbed.RealEngineDispatcher().Dispatch(
            state, new FleetToFleetTransferCommand(NationId, "xf-2a", "xf-2b", 1, 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(FleetToFleetTransferRejections.NotAdjacent, result.Code);
        Assert.Same(state, result.State);
    }

    // ---- Done-when 4: agreement with AdjacentPartner ----

    [Fact]
    public void JoinArmies_AcceptsAdjacentPartnersPickAndTheOtherUnitAtDistanceOne_RejectsTheUnitAtDistanceTwo()
    {
        var state = WithArmies(
            InitialState(),
            OneUnitArmy("ag-a", 3, 3),
            OneUnitArmy("ag-b", 4, 3), // distance 1
            OneUnitArmy("ag-c", 4, 4), // distance 1
            OneUnitArmy("ag-d", 5, 5)); // distance 2

        var pick = AdjacentPartner.Army(state, "ag-a");
        Assert.NotNull(pick);
        Assert.Equal("ag-c", pick!.Id); // the last adjacent own army in state order

        var namingPick = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "ag-a", pick.Id));
        Assert.True(namingPick.IsAccepted, namingPick.ToString());

        var namingOther = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "ag-a", "ag-b"));
        Assert.True(namingOther.IsAccepted, namingOther.ToString());

        var namingFar = Dispatcher().Dispatch(state, new JoinArmiesCommand(NorthNationId, "ag-a", "ag-d"));
        Assert.True(namingFar.IsRejected);
        Assert.Equal(JoinArmiesRejections.NotAdjacent, namingFar.Code);
    }

    [Fact]
    public void JoinFleets_AcceptsAdjacentPartnersPickAndTheOtherUnitAtDistanceOne_RejectsTheUnitAtDistanceTwo()
    {
        var state = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(
                Fleet("fg-a", 3, 5),
                Fleet("fg-b", 4, 5), // distance 1
                Fleet("fg-c", 2, 4), // distance 1
                Fleet("fg-d", 5, 5)), // distance 2
        };

        var pick = AdjacentPartner.Fleet(state, "fg-a");
        Assert.NotNull(pick);
        Assert.Equal("fg-c", pick!.Id); // the last adjacent own launched fleet in state order

        var namingPick = NavalTestbed.RealEngineDispatcher()
            .Dispatch(state, new JoinFleetsCommand(NationId, "fg-a", pick.Id));
        Assert.True(namingPick.IsAccepted, namingPick.ToString());

        var namingOther = NavalTestbed.RealEngineDispatcher()
            .Dispatch(state, new JoinFleetsCommand(NationId, "fg-a", "fg-b"));
        Assert.True(namingOther.IsAccepted, namingOther.ToString());

        var namingFar = NavalTestbed.RealEngineDispatcher()
            .Dispatch(state, new JoinFleetsCommand(NationId, "fg-a", "fg-d"));
        Assert.True(namingFar.IsRejected);
        Assert.Equal(JoinFleetsRejections.NotAdjacent, namingFar.Code);
    }

    [Fact]
    public void FleetTransfer_AcceptsAdjacentPartnersPickAndTheOtherUnitAtDistanceOne_RejectsTheUnitAtDistanceTwo()
    {
        var state = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(
                Fleet("tg-a", 3, 5, ships: 10),
                Fleet("tg-b", 4, 5, ships: 5), // distance 1
                Fleet("tg-c", 2, 4, ships: 5), // distance 1
                Fleet("tg-d", 5, 5, ships: 5)), // distance 2
        };

        var pick = AdjacentPartner.Fleet(state, "tg-a");
        Assert.NotNull(pick);
        Assert.Equal("tg-c", pick!.Id); // the last adjacent own launched fleet in state order

        var namingPick = NavalTestbed.RealEngineDispatcher().Dispatch(
            state, new FleetToFleetTransferCommand(NationId, "tg-a", pick.Id, 1, 0, 0));
        Assert.True(namingPick.IsAccepted, namingPick.ToString());

        var namingOther = NavalTestbed.RealEngineDispatcher().Dispatch(
            state, new FleetToFleetTransferCommand(NationId, "tg-a", "tg-b", 1, 0, 0));
        Assert.True(namingOther.IsAccepted, namingOther.ToString());

        var namingFar = NavalTestbed.RealEngineDispatcher().Dispatch(
            state, new FleetToFleetTransferCommand(NationId, "tg-a", "tg-d", 1, 0, 0));
        Assert.True(namingFar.IsRejected);
        Assert.Equal(FleetToFleetTransferRejections.NotAdjacent, namingFar.Code);
    }
}
