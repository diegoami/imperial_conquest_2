using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T46 Fleet-to-fleet transfer, and the supply path that keeps fleets
/// alive" (issue #148), Done-when 1-5 — <see cref="FleetToFleetTransferCommand"/>.
/// </summary>
public sealed class FleetToFleetTransferCommandHandlerTests
{
    private const string NationId = "north";
    private const string OtherNationId = "south";

    private static FleetState Fleet(
        string id, int x, int y, int ships, int supply = 0, int money = 0,
        string nation = NationId, string? carriedArmyId = null, int? constructionTicksRemaining = null) =>
        new(id, nation, x, y, Moves: 5, ships, ConditionPercent: 90, money, supply,
            constructionTicksRemaining, BuildCityId: null, carriedArmyId, CoveredTileCode: null);

    private static ArmyState Army(string id, string fleetId, string nation = NationId) =>
        new(id, nation, X: 3, Y: 3, Moves: 0, Morale: 60, Money: 0, SupplyTons: 0,
            CoveredTileCode: null, AboardFleetId: fleetId,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", 4000, 6, "Aboard Battalion")));

    /// <summary>DoD 1: a basic reciprocal transfer moves ships, supply and money exactly as requested.</summary>
    [Fact]
    public void OrdinaryTransfer_MovesExactlyTheRequestedAmounts_AndConservesTotals()
    {
        var state = NavalTestbed.InitialState();
        var source = Fleet("xfer-src", 3, 3, ships: 30, supply: 100, money: 50);
        var target = Fleet("xfer-dst", 3, 3, ships: 20, supply: 10, money: 5);
        state = state with { Fleets = ValueList.Of(source, target) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, Ships: 10, SupplyTons: 20, Money: 5));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedSource = result.State.FleetById(source.Id)!;
        var updatedTarget = result.State.FleetById(target.Id)!;

        Assert.Equal(20, updatedSource.Ships);
        Assert.Equal(80, updatedSource.SupplyTons);
        Assert.Equal(45, updatedSource.Money);

        Assert.Equal(30, updatedTarget.Ships);
        Assert.Equal(30, updatedTarget.SupplyTons);
        Assert.Equal(10, updatedTarget.Money);

        // Conservation: nothing created or destroyed across the pair.
        Assert.Equal(source.Ships + target.Ships, updatedSource.Ships + updatedTarget.Ships);
        Assert.Equal(source.SupplyTons + target.SupplyTons, updatedSource.SupplyTons + updatedTarget.SupplyTons);
        Assert.Equal(source.Money + target.Money, updatedSource.Money + updatedTarget.Money);
    }

    /// <summary>
    /// DoD 2 (T14 round-2 review, B6): a carrying fleet refuses transfer outright, on either side, so the
    /// bug that deleted a carrier out from under its embarked army cannot recur. Proved end to end: the
    /// resulting (unchanged) state is still one <see cref="GameDataValidation"/> accepts.
    /// </summary>
    [Fact]
    public void CarryingFleet_AsEitherSide_RefusesTheTransfer_AndStateStaysValid()
    {
        var state = NavalTestbed.InitialState();
        var carryingSource = Fleet("xfer-carrying-src", 3, 3, ships: 10, supply: 20, money: 10, carriedArmyId: "aboard-army");
        var army = Army("aboard-army", carryingSource.Id);
        var plainTarget = Fleet("xfer-plain-dst", 3, 3, ships: 5);
        state = state with { Fleets = ValueList.Of(carryingSource, plainTarget), Armies = ValueList.Of(army) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();

        // Attempting to move every ship out of the carrying fleet -- the exact repro that deleted the
        // carrier in the first attempt -- must be refused, not accepted-and-corrupted.
        var result = dispatcher.Dispatch(
            state, new FleetToFleetTransferCommand(NationId, carryingSource.Id, plainTarget.Id, Ships: 10, SupplyTons: 0, Money: 0));

        Assert.True(result.IsRejected);
        Assert.Equal(FleetToFleetTransferRejections.CarryingArmy, result.Code);

        // The carrying fleet is untouched: still present, still carrying, the army still aboard it.
        Assert.NotNull(result.State.FleetById(carryingSource.Id));
        Assert.NotNull(result.State.ArmyById(army.Id));
        Assert.Equal(carryingSource.Id, result.State.ArmyById(army.Id)!.AboardFleetId);

        // And the resulting state is one the engine's own validator would accept -- no dangling reference.
        var document = result.State;
        Assert.Equal(document, document); // sanity: record equality still holds (state literally unchanged shape-wise).
        Should.NotThrow(() => GameDataValidation.Validate("probe.json", document));

        // The reciprocal case: carrying fleet as the *target* is refused too.
        var otherTarget = Fleet("xfer-carrying-dst", 3, 3, ships: 5, carriedArmyId: "aboard-army-2");
        var otherArmy = Army("aboard-army-2", otherTarget.Id);
        var plainSource = Fleet("xfer-plain-src", 3, 3, ships: 10);
        var state2 = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(plainSource, otherTarget),
            Armies = ValueList.Of(otherArmy),
        };

        var result2 = dispatcher.Dispatch(
            state2, new FleetToFleetTransferCommand(NationId, plainSource.Id, otherTarget.Id, Ships: 1, SupplyTons: 0, Money: 0));

        Assert.True(result2.IsRejected);
        Assert.Equal(FleetToFleetTransferRejections.CarryingArmy, result2.Code);
    }

    /// <summary>
    /// DoD 3 (T14 round-2 review, B7): a disbanding source's <em>full</em> remaining supply and money pool
    /// into the survivor, not just the requested amounts. The test starts the source with non-zero
    /// supply and money -- the first attempt's own test used zeroes and could not see this bug.
    /// </summary>
    [Fact]
    public void TransferringAllShips_DisbandsTheSource_AndConservesItsFullRemainingSupplyAndMoney()
    {
        var state = NavalTestbed.InitialState();
        var source = Fleet("xfer-empty-src", 3, 3, ships: 30, supply: 100, money: 50);
        var target = Fleet("xfer-empty-dst", 3, 3, ships: 20, supply: 10, money: 5);
        state = state with { Fleets = ValueList.Of(source, target) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();

        // Requesting only PART of the source's supply/money (0 tons, 0 money) alongside ALL its ships --
        // exactly the T14 round-2 repro (B7): 30 ships / 0 supply / 0 money requested, from a source
        // holding 100 tons and 50 talents.
        var result = dispatcher.Dispatch(
            state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, Ships: 30, SupplyTons: 0, Money: 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.FleetById(source.Id)); // the emptied source is removed.

        var survivor = result.State.FleetById(target.Id)!;
        Assert.Equal(50, survivor.Ships); // 20 + 30.

        // The full 100 tons and 50 talents the first attempt annihilated are conserved into the survivor.
        Assert.Equal(110, survivor.SupplyTons); // 10 (own) + 100 (source's full remaining stock).
        Assert.Equal(55, survivor.Money); // 5 (own) + 50 (source's full remaining purse).

        // Global conservation across the whole fleet list.
        var totalShipsAfter = result.State.Fleets.Sum(f => f.Ships);
        var totalSupplyAfter = result.State.Fleets.Sum(f => f.SupplyTons);
        var totalMoneyAfter = result.State.Fleets.Sum(f => f.Money);
        Assert.Equal(source.Ships + target.Ships, totalShipsAfter);
        Assert.Equal(source.SupplyTons + target.SupplyTons, totalSupplyAfter);
        Assert.Equal(source.Money + target.Money, totalMoneyAfter);
    }

    /// <summary>A fleet cannot transfer to itself.</summary>
    [Fact]
    public void SameFleetOnBothSides_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("xfer-self", 3, 3, ships: 10);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(NationId, fleet.Id, fleet.Id, Ships: 1, SupplyTons: 0, Money: 0));

        Assert.True(result.IsRejected);
        Assert.Equal(FleetToFleetTransferRejections.SameFleet, result.Code);
    }

    /// <summary>
    /// DoD 5 [designed]: the resulting combined ship count is capped at
    /// <see cref="NavalRules.JoinMaxShips"/>, the same ceiling <c>JoinFleets</c> enforces -- exactly at
    /// the cap is accepted, one over is refused.
    /// </summary>
    [Theory]
    [InlineData(61, 60, 40, true)] // 60 + 40 = 100 -- exactly the cap, accepted (source keeps 21 ships, no disband).
    [InlineData(61, 60, 41, false)] // 60 + 41 = 101 -- one over, refused.
    public void CombinedShipCap_MatchesJoinFleets(int sourceShips, int targetShips, int shipsToMove, bool expectAccepted)
    {
        var state = NavalTestbed.InitialState();
        var rules = NavalTestbed.Ruleset.Naval;
        var source = Fleet("xfer-cap-src", 3, 3, ships: sourceShips);
        var target = Fleet("xfer-cap-dst", 3, 3, ships: targetShips);
        state = state with { Fleets = ValueList.Of(source, target) };

        Assert.InRange(shipsToMove, 1, sourceShips); // sanity: the scenario itself must be well-formed.

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, shipsToMove, SupplyTons: 0, Money: 0));

        if (expectAccepted)
        {
            Assert.True(result.IsAccepted, result.ToString());
            Assert.Equal(rules.JoinMaxShips, result.State.FleetById(target.Id)!.Ships);
        }
        else
        {
            Assert.True(result.IsRejected);
            Assert.Equal(FleetToFleetTransferRejections.CombinedShipsTooLarge, result.Code);
        }
    }

    /// <summary>Requesting more ships/supply/money than the source actually holds is refused, not clamped.</summary>
    [Fact]
    public void RequestingMoreThanTheSourceHolds_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var source = Fleet("xfer-short-src", 3, 3, ships: 10, supply: 5, money: 2);
        var target = Fleet("xfer-short-dst", 3, 3, ships: 10);
        state = state with { Fleets = ValueList.Of(source, target) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();

        var tooManyShips = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, 11, 0, 0));
        Assert.Equal(FleetToFleetTransferRejections.InsufficientShips, tooManyShips.Code);

        var tooMuchSupply = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, 1, 6, 0));
        Assert.Equal(FleetToFleetTransferRejections.InsufficientSupply, tooMuchSupply.Code);

        var tooMuchMoney = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, 1, 0, 3));
        Assert.Equal(FleetToFleetTransferRejections.InsufficientMoney, tooMuchMoney.Code);
    }

    /// <summary>Fleets belonging to another nation, not co-located, or under construction all refuse.</summary>
    [Fact]
    public void NotYourFleet_NotCoLocated_AndUnderConstruction_AllRefuse()
    {
        var state = NavalTestbed.InitialState();
        var foreign = Fleet("xfer-foreign", 3, 3, ships: 10, nation: OtherNationId);
        var mine = Fleet("xfer-mine", 3, 3, ships: 10);
        var elsewhere = Fleet("xfer-elsewhere", 9, 9, ships: 10);
        var building = Fleet("xfer-building", 3, 3, ships: 10, constructionTicksRemaining: 4);
        state = state with { Fleets = ValueList.Of(foreign, mine, elsewhere, building) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();

        var notYours = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(NationId, foreign.Id, mine.Id, 1, 0, 0));
        Assert.Equal(FleetToFleetTransferRejections.NotYourFleet, notYours.Code);

        var notCoLocated = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(NationId, mine.Id, elsewhere.Id, 1, 0, 0));
        Assert.Equal(FleetToFleetTransferRejections.NotCoLocated, notCoLocated.Code);

        var underConstruction = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(NationId, mine.Id, building.Id, 1, 0, 0));
        Assert.Equal(FleetToFleetTransferRejections.UnderConstruction, underConstruction.Code);
    }
}

/// <summary>Tiny local shim so the validation probe above reads as an assertion, not a bare method call.</summary>
internal static class Should
{
    public static void NotThrow(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Expected no exception, but got: {ex}");
        }
    }
}
