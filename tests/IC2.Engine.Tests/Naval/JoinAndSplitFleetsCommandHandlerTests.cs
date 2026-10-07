using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 6: join caps at 100 combined ships; split
/// requires at least 20 ships.
/// </summary>
public sealed class JoinAndSplitFleetsCommandHandlerTests
{
    private const string NationId = "north";

    private static FleetState Fleet(string id, int x, int y, int ships, int supply = 0, int money = 0) =>
        new(id, NationId, x, y, Moves: 5, ships, ConditionPercent: 90, money, supply,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    [Fact]
    public void Join_CombinedShipsAtExactlyOneHundred_IsAccepted()
    {
        var state = NavalTestbed.InitialState();
        var a = Fleet("join-a", 3, 3, 60, supply: 10, money: 20);
        var b = Fleet("join-b", 4, 3, 40, supply: 5, money: 8);
        state = state with { Fleets = ValueList.Of(a, b) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new JoinFleetsCommand(NationId, a.Id, b.Id));

        Assert.True(result.IsAccepted, result.ToString());
        var survivor = result.State.FleetById(a.Id)!;
        Assert.Equal(100, survivor.Ships);
        Assert.Equal(15, survivor.SupplyTons);
        Assert.Equal(28, survivor.Money);
        Assert.Equal(0, survivor.Moves);
        Assert.Null(result.State.FleetById(b.Id));
    }

    [Fact]
    public void Join_CombinedShipsAboveOneHundred_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var a = Fleet("join-c", 3, 3, 60);
        var b = Fleet("join-d", 4, 3, 41);
        state = state with { Fleets = ValueList.Of(a, b) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new JoinFleetsCommand(NationId, a.Id, b.Id));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinFleetsRejections.CombinedShipsTooLarge, result.Code);
    }

    /// <summary>
    /// T72 (bug #315), Done-when 3: a fleet join adds the purses UNCAPPED -- 900 + 900 leaves the survivor
    /// at 1,800 and touches no treasury. T72's research check (Done-when 1) settled the path on the
    /// <c>decompiled-unit-map-orders-and-record-fields.md</c> Fleet-orders row: <c>TUnitMap_JoinFleets</c>
    /// (<c>0x00447A48</c>) -- "ships, supplies and money add", its <c>&lt; 100</c>-ships bound named, no
    /// money cap named [derived; the 2026-10-05 purse report left this fleet path not-settled and the live
    /// 2026-10-02 join run had 0-purse fleets], the naval twin of the army join's row 7 (1,000 + 1,000 =
    /// 2,000 [Wine candidates Q1_05/Q1_06]). The pre-T72 pin (survivor at the 1,000 cap, excess 800 to the
    /// treasury -- T50 Done-when 4, resting on T08's "every path" wording that the 2026-10-05 report
    /// corrects) was evidence of the bug, not of correct behaviour, and is recomputed here from the path's
    /// real rule.
    /// </summary>
    [Fact]
    public void Join_PooledMoneyAboveOneThousand_AddsUncapped_AndNoTreasuryIsTouched()
    {
        var state = NavalTestbed.InitialState();
        var treasuryBefore = state.NationById(NationId)!.Treasury;
        var a = Fleet("join-purse-a", 3, 3, 10, money: 900);
        var b = Fleet("join-purse-b", 4, 3, 10, money: 900);
        state = state with { Fleets = ValueList.Of(a, b) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new JoinFleetsCommand(NationId, a.Id, b.Id));

        Assert.True(result.IsAccepted, result.ToString());
        var survivor = result.State.FleetById(a.Id)!;
        Assert.Equal(1800, survivor.Money); // 900 + 900, not cut to 1,000.
        Assert.Null(result.State.FleetById(b.Id));

        Assert.Equal(treasuryBefore, result.State.NationById(NationId)!.Treasury); // no diversion: the join moves money between the two fleet records only.
    }

    /// <summary>Done-when 3: the fleet join of 800 + 600 gives 1,400, matching the army join's case.</summary>
    [Fact]
    public void Join_PooledMoneyAtEightHundredAndSixHundred_GivesFourteenHundred()
    {
        var state = NavalTestbed.InitialState();
        var a = Fleet("join-purse-c", 3, 3, 10, money: 800);
        var b = Fleet("join-purse-d", 4, 3, 10, money: 600);
        state = state with { Fleets = ValueList.Of(a, b) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new JoinFleetsCommand(NationId, a.Id, b.Id));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(1400, result.State.FleetById(a.Id)!.Money);
    }

    /// <summary>
    /// Done-when 4: the fleet purse is the same signed 16-bit field (the sweep bounds both army and fleet
    /// at 0..32,767); a sum above it lands at 32,767 and the excess goes NOWHERE -- the original's wrap is
    /// deliberately not reproduced [designed: the user's 2026-10-05 choice, PR #758's R2 resolution].
    /// </summary>
    [Fact]
    public void Join_PooledMoneyAboveTheFieldRange_LandsAtThirtyTwoThousandSevenHundredSixtySeven()
    {
        var state = NavalTestbed.InitialState();
        var treasuryBefore = state.NationById(NationId)!.Treasury;
        var a = Fleet("join-purse-e", 3, 3, 10, money: 32000);
        var b = Fleet("join-purse-f", 4, 3, 10, money: 2000);
        state = state with { Fleets = ValueList.Of(a, b) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new JoinFleetsCommand(NationId, a.Id, b.Id));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(32767, result.State.FleetById(a.Id)!.Money); // 34,000 clamped, no wrap.
        Assert.Equal(treasuryBefore, result.State.NationById(NationId)!.Treasury); // the excess goes nowhere.
    }

    [Fact]
    public void Split_FleetWithExactlyTwentyShips_IsAccepted()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("split-a", 3, 5, 20, supply: 8, money: 4);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new SplitFleetCommand(NationId, fleet.Id, "split-a-new", ShipsToNewFleet: 5));

        Assert.True(result.IsAccepted, result.ToString());
        var remaining = result.State.FleetById(fleet.Id)!;
        var created = result.State.FleetById("split-a-new")!;
        Assert.Equal(15, remaining.Ships);
        Assert.Equal(5, created.Ships);
        Assert.Equal(20, remaining.Ships + created.Ships); // ships conserve exactly.
    }

    [Fact]
    public void Split_FleetWithFewerThanTwentyShips_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("split-b", 3, 3, 19);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new SplitFleetCommand(NationId, fleet.Id, "split-b-new", ShipsToNewFleet: 5));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitFleetRejections.TooFewShipsToSplit, result.Code);
    }
}
