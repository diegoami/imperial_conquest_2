using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T112 Done-when 1: the Unit map's Fleet and City dialogs' Godot-free models pin each dialog's limits
/// and the command lines it composes. Every limit is read from the ruleset (the shipped
/// <c>classical-faithful</c> one), never a literal.
/// </summary>
public sealed class FleetCityDialogModelsTests
{
    private const string NationId = "rome";
    private const string FleetId = "t112-fleet";
    private const string PartnerId = "t112-partner";
    private const string NewFleetId = "t112-fleet-split";
    private const string CityId = "t112-city";

    private static readonly Lazy<Ruleset> LazyClassicalRuleset = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean").Ruleset);

    private static Ruleset ClassicalRuleset => LazyClassicalRuleset.Value;

    private static FleetState Fleet(
        string id = FleetId,
        int x = 3,
        int y = 2,
        int ships = 30,
        int condition = 70,
        int supply = 300,
        int money = 100,
        string? carriedArmyId = null) => new(
        id, NationId, x, y, Moves: 8, Ships: ships, ConditionPercent: condition, Money: money,
        SupplyTons: supply, ConstructionTicksRemaining: null, BuildCityId: null,
        CarriedArmyId: carriedArmyId, CoveredTileCode: null);

    private static CityState City(int fortificationCode = 40, int population = 100, bool underSiege = false) => new(
        CityId, "T112 city", X: 4, Y: 2, Owner: NationId, Allegiance: NationId, Loyalty: 90,
        SupplyTons: 330, FortificationCode: fortificationCode, PopulationThousands: population,
        MaxPopulationThousands: 100, Tribute: 0, UnderSiege: underSiege, Garrison: ValueList<UnitSlot>.Empty);

    private static GameState State(FleetState fleet, FleetState? partner = null, CityState? city = null)
    {
        var initial = CoreTestbed.InitialState();
        var fleets = partner is null
            ? ValueList.Of(new[] { fleet })
            : ValueList.Of(new[] { fleet, partner });
        return initial with
        {
            Fleets = fleets,
            Cities = ValueList.Of(new[] { city ?? City() }),
        };
    }

    // ---- Done-when 1: the repair cost ----

    [Fact]
    public void Repair_reads_the_cost_divisor_from_the_ruleset()
    {
        var ruleset = ClassicalRuleset;
        var state = State(Fleet(condition: 70));
        var model = RepairFleetModel.ForFleet(state, state.FleetById(FleetId)!, ruleset);

        Assert.True(model.CanRepair);
        Assert.Null(model.RefusalMessage);

        // The report's own example: 3 points on 30 ships cost 30 × 3 / 5 = 18.
        Assert.Equal(30 * 3 / ruleset.Naval.RepairCostDivisor, model.CostFor(3));
        Assert.Equal(18, model.CostFor(3));
        Assert.Equal(73, model.NewConditionFor(3));
        Assert.Equal($"repair-fleet {FleetId} 3", model.ComposeOk(3));
        Assert.Null(model.Cancel());
    }

    [Fact]
    public void Repair_reads_the_purse_and_clamps_to_the_room()
    {
        var ruleset = ClassicalRuleset with
        {
            Naval = ClassicalRuleset.Naval with { RepairCostDivisor = 3 },
        };
        var state = State(Fleet(condition: 90));
        var model = RepairFleetModel.ForFleet(state, state.FleetById(FleetId)!, ruleset);

        Assert.Equal(10, model.MaxPoints);
        Assert.Equal(30 * 10 / 3, model.CostFor(1_000));
        Assert.Equal($"repair-fleet {FleetId} 10", model.ComposeOk(1_000));
    }

    [Fact]
    public void Repair_is_refused_away_from_an_own_city_and_while_carrying_an_army()
    {
        var ruleset = ClassicalRuleset;
        var away = State(Fleet(x: 50, y: 50), city: City());
        var awayModel = RepairFleetModel.ForFleet(away, away.FleetById(FleetId)!, ruleset);
        Assert.False(awayModel.CanRepair);
        Assert.Equal(FleetCityDialogModels.RepairNotAtCityMessage, awayModel.RefusalMessage);
        Assert.Null(awayModel.ComposeOk(3));

        var carrying = State(Fleet(carriedArmyId: "t112-aboard"));
        var carryingModel = RepairFleetModel.ForFleet(carrying, carrying.FleetById(FleetId)!, ruleset);
        Assert.False(carryingModel.CanRepair);
        Assert.Equal(FleetCityDialogModels.RepairCarryingArmyMessage, carryingModel.RefusalMessage);
    }

    // ---- Done-when 1: Split fleet ----

    [Fact]
    public void Split_fleet_is_refused_under_the_minimum_and_with_an_army_aboard()
    {
        var ruleset = ClassicalRuleset;
        var small = State(Fleet(ships: ruleset.Naval.SplitMinShips - 1));
        var smallModel = SplitFleetModel.ForFleet(state: small, small.FleetById(FleetId)!, ruleset, NewFleetId);
        Assert.False(smallModel.CanSplit);
        Assert.Equal(FleetCityDialogModels.SplitTooFewShipsMessage(ruleset), smallModel.RefusalMessage);
        Assert.Null(smallModel.ComposeOk());

        var carrying = State(Fleet(ships: ruleset.Naval.SplitMinShips, carriedArmyId: "t112-aboard"));
        var carryingModel = SplitFleetModel.ForFleet(
            state: carrying, carrying.FleetById(FleetId)!, ruleset, NewFleetId);
        Assert.False(carryingModel.CanSplit);
        Assert.Equal(FleetCityDialogModels.SplitCarryingArmyMessage, carryingModel.RefusalMessage);
        Assert.Null(carryingModel.ComposeOk());
    }

    [Fact]
    public void Split_fleet_composes_exactly_one_split_with_ships_supply_and_money_and_nothing_for_cancel()
    {
        var ruleset = ClassicalRuleset;
        var state = State(Fleet(ships: 30, supply: 300, money: 100));
        var model = SplitFleetModel.ForFleet(state, state.FleetById(FleetId)!, ruleset, NewFleetId);

        Assert.True(model.CanSplit);
        Assert.Null(model.ComposeOk()); // nothing staged yet.

        model.AdjustShips(10);
        model.AdjustSupply(40);
        model.AdjustMoney(30);

        Assert.Equal($"split-fleet {FleetId} {NewFleetId} 10 supply=40 money=30", model.ComposeOk());
        Assert.Null(model.Cancel());

        // The selected fleet keeps at least one ship: the last one cannot be staged.
        model.AdjustShips(1_000);
        Assert.Equal(29, model.Ships);
    }

    // ---- Done-when 1: Join fleets and Transfer ships with no partner, and the cap ----

    [Fact]
    public void Join_fleets_composes_nothing_without_a_partner_and_returns_the_no_partner_message()
    {
        var ruleset = ClassicalRuleset;
        var state = State(Fleet(ships: 10));
        var model = FleetJoinModel.ForFleet(state.FleetById(FleetId)!, partner: null, ruleset);

        Assert.False(model.CanJoin);
        Assert.Equal(FleetCityDialogModels.NoPartnerMessage, model.RefusalMessage);
        Assert.Null(model.ComposeOk());
    }

    [Fact]
    public void Join_fleets_and_transfer_ships_are_refused_at_101_ships_combined()
    {
        var ruleset = ClassicalRuleset;
        var over = ruleset.Naval.JoinMaxShips + 1; // 101.
        var selected = Fleet(ships: 60);
        var partner = Fleet(PartnerId, x: 3, y: 3, ships: over - 60);
        var state = State(selected, partner);
        var refreshedSelected = state.FleetById(FleetId)!;
        var refreshedPartner = state.FleetById(PartnerId)!;

        var join = FleetJoinModel.ForFleet(refreshedSelected, refreshedPartner, ruleset);
        Assert.False(join.CanJoin);
        Assert.Equal(FleetCityDialogModels.CombinedShipsTooLargeMessage(ruleset), join.RefusalMessage);
        Assert.Null(join.ComposeOk());

        var transfer = FleetTransferModel.ForFleets(refreshedSelected, refreshedPartner, ruleset);
        Assert.False(transfer.CombinedShipsWithinCap);
        Assert.Equal(FleetCityDialogModels.CombinedShipsTooLargeMessage(ruleset), transfer.RefusalMessage);
        transfer.AdjustShips(1);
        transfer.AdjustSupply(1);
        Assert.Empty(transfer.ComposeOk());
        Assert.Empty(transfer.Cancel());
    }

    [Fact]
    public void Transfer_ships_composes_one_command_each_way_with_no_partner_composing_nothing()
    {
        var ruleset = ClassicalRuleset;
        var loner = State(Fleet(ships: 30));
        var noPartner = FleetTransferModel.ForFleets(loner.FleetById(FleetId)!, partner: null, ruleset);
        Assert.Equal(FleetCityDialogModels.NoPartnerMessage, noPartner.RefusalMessage);
        Assert.Empty(noPartner.ComposeOk());

        var selected = Fleet(ships: 30, supply: 300, money: 100);
        var partner = Fleet(PartnerId, x: 3, y: 3, ships: 20, supply: 200, money: 50);
        var state = State(selected, partner);
        var model = FleetTransferModel.ForFleets(state.FleetById(FleetId)!, state.FleetById(PartnerId)!, ruleset);

        model.AdjustShips(10);
        model.AdjustSupply(40);
        model.AdjustMoney(30);
        Assert.Equal(
            $"fleet-transfer {FleetId} {PartnerId} 10 40 30",
            Assert.Single(model.ComposeOk()));

        // A mixed staging composes one command per direction, the fleet twin of the original's two-way
        // steppers (the clone's fleet-transfer is one-directional per line).
        var twoWay = FleetTransferModel.ForFleets(state.FleetById(FleetId)!, state.FleetById(PartnerId)!, ruleset);
        twoWay.AdjustShips(5);   // A -> B
        twoWay.AdjustMoney(-20); // B -> A
        Assert.Equal(
            new[]
            {
                $"fleet-transfer {FleetId} {PartnerId} 5 0 0",
                $"fleet-transfer {PartnerId} {FleetId} 0 0 20",
            },
            twoWay.ComposeOk().ToArray());
    }

    [Fact]
    public void Transfer_ships_never_moves_more_into_the_partner_than_the_join_cap_allows()
    {
        var ruleset = ClassicalRuleset;
        var selected = Fleet(ships: 5);
        var partner = Fleet(PartnerId, x: 3, y: 3, ships: 95);
        var state = State(selected, partner);
        var model = FleetTransferModel.ForFleets(state.FleetById(FleetId)!, state.FleetById(PartnerId)!, ruleset);

        // The partner may take exactly five more ships before the cap.
        Assert.True(model.CombinedShipsWithinCap);
        Assert.Equal(5, model.MaxShipsToPartner);
        model.AdjustShips(10);
        Assert.Equal(5, model.ShipsNet);
    }

    // ---- Done-when 1: Fortify city's range and cost ----

    [Fact]
    public void Fortify_orders_zero_to_max_minus_current_at_the_population_cost()
    {
        var ruleset = ClassicalRuleset;
        var rule = ruleset.CityOrders.Orders.Single(order => order.Id == FortifyCityModel.FortifyOrderId);
        var state = State(Fleet(), city: City(fortificationCode: 40, population: 100));
        var model = FortifyCityModel.ForCity(state, state.CityById(CityId)!, ruleset);

        Assert.True(model.CanFortify);
        Assert.Equal(0, model.MinPoints);
        Assert.Equal(rule.MaxPercent - 40, model.MaxPoints);
        Assert.Equal(60, model.MaxPoints);
        Assert.Equal(rule.CostPerPointPerPopulationThousand * 10 * 100, model.CostFor(10));
        Assert.Equal($"order-city {CityId} fortify 10", model.ComposeOk(10));
        Assert.Null(model.ComposeOk(0));
        Assert.Null(model.Cancel());
    }

    [Fact]
    public void Fortify_is_refused_at_the_maximum_with_an_order_pending_and_under_siege()
    {
        var ruleset = ClassicalRuleset;
        var rule = ruleset.CityOrders.Orders.Single(order => order.Id == FortifyCityModel.FortifyOrderId);

        var atMax = State(Fleet(), city: City(fortificationCode: rule.MaxPercent));
        var atMaxModel = FortifyCityModel.ForCity(atMax, atMax.CityById(CityId)!, ruleset);
        Assert.False(atMaxModel.CanFortify);
        Assert.Equal(0, atMaxModel.MaxPoints);
        Assert.Null(atMaxModel.ComposeOk(1));

        var pending = State(Fleet(), city: City(fortificationCode: 40 + rule.InProgressEncodingRadix));
        var pendingModel = FortifyCityModel.ForCity(pending, pending.CityById(CityId)!, ruleset);
        Assert.False(pendingModel.CanFortify);
        Assert.Null(pendingModel.ComposeOk(1));

        var besieged = State(Fleet(), city: City(underSiege: true));
        var besiegedModel = FortifyCityModel.ForCity(besieged, besieged.CityById(CityId)!, ruleset);
        if (rule.RefusedWhileUnderSiege)
        {
            Assert.False(besiegedModel.CanFortify);
            Assert.Null(besiegedModel.ComposeOk(1));
        }
    }
}
