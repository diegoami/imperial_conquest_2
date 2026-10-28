using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Naval;
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
    private const string EngineNationId = "north";
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
        string? carriedArmyId = null,
        string nation = NationId) => new(
        id, nation, x, y, Moves: 8, Ships: ships, ConditionPercent: condition, Money: money,
        SupplyTons: supply, ConstructionTicksRemaining: null, BuildCityId: null,
        CarriedArmyId: carriedArmyId, CoveredTileCode: null);

    private static CityState City(int fortificationCode = 40, int population = 100) => new(
        CityId, "T112 city", X: 4, Y: 2, Owner: NationId, Allegiance: NationId, Loyalty: 90,
        SupplyTons: 330, FortificationCode: fortificationCode, PopulationThousands: population,
        MaxPopulationThousands: 100, Tribute: 0, Garrison: ValueList<UnitSlot>.Empty);

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
            // The fixture's city belongs to "rome", which the toy scenario's matrix does not cover; the
            // hostile-army-adjacent predicate reads the matrix, so it must (everyone at peace).
            Relations = DiplomaticRelations.Uniform(
                ValueList.From(initial.Nations.Select(n => n.Id).Append(NationId)),
                ClassicalRuleset.Diplomacy.StateCodes.Peace),
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

    // ---- Done-when 1: Transfer ships refuses the selected fleet's last ship (T112 R2) ----

    [Fact]
    public void Transfer_ships_allows_either_empty_donor()
    {
        var ruleset = ClassicalRuleset;
        var selected = Fleet(ships: 30);
        var partner = Fleet(PartnerId, x: 3, y: 3, ships: 20);
        var state = State(selected, partner);
        var model = FleetTransferModel.ForFleets(state.FleetById(FleetId)!, state.FleetById(PartnerId)!, ruleset);

        // Every one of A's ships toward B is the engine's disband trigger (remainingShips == 0): the model
        // refuses it and composes nothing.
        model.AdjustShips(30);
        Assert.Equal(30, model.ShipsNet);
        Assert.True(model.MovesEverySelectedShip);
        Assert.Null(model.RefusalMessage);
        Assert.Equal($"fleet-transfer {FleetId} {PartnerId} 30 0 0", Assert.Single(model.ComposeOk()));

        // One short of every ship is accepted: the partial A -> B line composes as before.
        model.AdjustShips(-1);
        Assert.False(model.MovesEverySelectedShip);
        Assert.Null(model.RefusalMessage);
        Assert.Equal($"fleet-transfer {FleetId} {PartnerId} 29 0 0", Assert.Single(model.ComposeOk()));
    }

    [Fact]
    public void Transfer_ships_completes_reverse_resources_before_deleting_donor()
    {
        var state = CoreTestbed.InitialState();
        var selected = Fleet(FleetId, x: 3, y: 3, ships: 30, supply: 300, money: 100, nation: EngineNationId);
        var partner = Fleet(PartnerId, x: 4, y: 3, ships: 20, supply: 200, money: 50, nation: EngineNationId);
        state = state with { Fleets = ValueList.Of(selected, partner) };
        var ruleset = NavalTestbed.Ruleset;
        var model = FleetTransferModel.ForFleets(selected, partner, ruleset);
        model.AdjustShips(30);
        model.AdjustMoney(-50);
        Assert.Null(model.RefusalMessage);
        Assert.Equal(new[] { $"fleet-transfer {PartnerId} {FleetId} 0 0 50", $"fleet-transfer {FleetId} {PartnerId} 30 0 0" }, model.ComposeOk());
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var reverse = dispatcher.Dispatch(state, new FleetToFleetTransferCommand(EngineNationId, PartnerId, FleetId, 0, 0, 50));
        Assert.True(reverse.IsAccepted);
        var merged = dispatcher.Dispatch(reverse.State, new FleetToFleetTransferCommand(EngineNationId, FleetId, PartnerId, 30, 0, 0));
        Assert.True(merged.IsAccepted);
        Assert.Null(merged.State.FleetById(FleetId));
        var survivor = merged.State.FleetById(PartnerId)!;
        Assert.Equal(50, survivor.Ships);
        Assert.Equal(500, survivor.SupplyTons);
        Assert.Equal(150, survivor.Money);
    }

    // ---- Done-when 1: Transfer ships refuses a carrying fleet (T112 R1) ----

    [Fact]
    public void Transfer_ships_is_refused_while_either_fleet_carries_an_army()
    {
        var ruleset = ClassicalRuleset;

        // The selected fleet carries, the partner is empty: the engine's own
        // source.IsCarryingArmy || target.IsCarryingArmy predicate refuses.
        var carryingSelected = State(
            Fleet(ships: 30, supply: 300, money: 100, carriedArmyId: "t112-aboard"),
            Fleet(PartnerId, x: 3, y: 3, ships: 20, supply: 200, money: 50));
        var model = FleetTransferModel.ForFleets(
            carryingSelected.FleetById(FleetId)!, carryingSelected.FleetById(PartnerId)!, ruleset);

        Assert.True(model.EitherCarryingArmy);
        Assert.Equal(FleetCityDialogModels.TransferCarryingArmyMessage, model.RefusalMessage);
        Assert.Equal("Neither fleet may carry an army to transfer.", model.RefusalMessage);

        // No arrow stages anything and OK composes nothing.
        Assert.Equal(0, model.MaxShipsToPartner);
        Assert.Equal(0, model.MaxSupplyToPartner);
        Assert.Equal(0, model.MaxMoneyToPartner);
        model.AdjustShips(5);
        model.AdjustSupply(20);
        model.AdjustMoney(10);
        Assert.Empty(model.ComposeOk());

        // The engine refuses whichever side carries: the partner carrying alone refuses too.
        var carryingPartner = State(
            Fleet(ships: 30, supply: 300, money: 100),
            Fleet(PartnerId, x: 3, y: 3, ships: 20, supply: 200, money: 50, carriedArmyId: "t112-aboard-b"));
        var back = FleetTransferModel.ForFleets(
            carryingPartner.FleetById(FleetId)!, carryingPartner.FleetById(PartnerId)!, ruleset);

        Assert.True(back.EitherCarryingArmy);
        Assert.Equal(FleetCityDialogModels.TransferCarryingArmyMessage, back.RefusalMessage);
        back.AdjustShips(5);
        Assert.Empty(back.ComposeOk());
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

        // T67 (#304): "under siege" is the hostile-army-adjacent predicate. Built by play: the other
        // nation, at war with the city's owner, walks an army beside the city with a real MoveArmyCommand.
        var calm = State(Fleet(), city: City());
        var calmModel = FortifyCityModel.ForCity(calm, calm.CityById(CityId)!, ruleset);
        Assert.False(calmModel.UnderSiege);
        Assert.True(calmModel.CanFortify);

        var besieged = HostileArmyMovedBesideCity(calm, ruleset);
        var besiegedModel = FortifyCityModel.ForCity(besieged, besieged.CityById(CityId)!, ruleset);
        Assert.True(besiegedModel.UnderSiege);
        Assert.Equal("You cannot fortify a city which is under siege.", besiegedModel.RefusalMessage);
        if (rule.RefusedWhileUnderSiege)
        {
            Assert.False(besiegedModel.CanFortify);
            Assert.Null(besiegedModel.ComposeOk(1));
        }
    }

    // Bug #1064: the siege refusal (R22) comes before the maximum (R23) and a pending order (R24).
    [Fact]
    public void Fortify_gives_the_siege_refusal_first_for_a_besieged_city_at_the_maximum_or_with_an_order_pending()
    {
        var ruleset = ClassicalRuleset;
        var rule = ruleset.CityOrders.Orders.Single(order => order.Id == FortifyCityModel.FortifyOrderId);
        const string siege = "You cannot fortify a city which is under siege.";

        var calm = State(Fleet(), city: City());
        var besiegedBase = HostileArmyMovedBesideCity(calm, ruleset);
        foreach (var code in new[] { rule.MaxPercent, 40 + rule.InProgressEncodingRadix })
        {
            var city = besiegedBase.CityById(CityId)! with { FortificationCode = code };
            var state = besiegedBase with
            {
                Cities = ValueList.From(besiegedBase.Cities.Select(c => c.Id == CityId ? city : c)),
            };
            var model = FortifyCityModel.ForCity(state, state.CityById(CityId)!, ruleset);
            Assert.True(model.UnderSiege);
            Assert.Equal(siege, model.RefusalMessage);
        }
    }

    // Bug #1067: with no siege the maximum (R23) is checked before a pending order (R24), as the engine does.
    // The shipped ruleset (max 100, radix 100) cannot hold both, so the rule is loosened to max 50 < radix 100.
    [Fact]
    public void Fortify_gives_the_maximum_refusal_before_the_pending_order_refusal()
    {
        var shipped = ClassicalRuleset;
        var loosened = shipped with
        {
            CityOrders = shipped.CityOrders with
            {
                Orders = ValueList.From(shipped.CityOrders.Orders.Select(
                    order => order.Id == FortifyCityModel.FortifyOrderId ? order with { MaxPercent = 50 } : order)),
            },
        };
        var rule = loosened.CityOrders.Orders.Single(order => order.Id == FortifyCityModel.FortifyOrderId);

        string Refusal(int code)
        {
            var state = State(Fleet(), city: City(fortificationCode: code));
            return FortifyCityModel.ForCity(state, state.CityById(CityId)!, loosened).RefusalMessage!;
        }

        // Finished 50 (= max) with 1 point pending: both conditions hold; the engine says "any further".
        Assert.Equal("This city cannot be fortified any further.", Refusal(50 + rule.InProgressEncodingRadix));
        Assert.Equal("This city cannot be fortified any further.", Refusal(50));
        Assert.Equal("This city is already being fortified.", Refusal(20 + rule.InProgressEncodingRadix));
    }

    private static GameState HostileArmyMovedBesideCity(GameState state, Ruleset ruleset)
    {
        var city = state.CityById(CityId)!;
        var owner = NationId;
        var enemy = state.Nations.First(n => n.Id != owner).Id;
        var armyId = state.Armies.First(a => a.Nation == enemy).Id;
        var armed = state with
        {
            Relations = state.Relations.WithRelation(owner, enemy, ruleset.Diplomacy.StateCodes.War),
            Armies = ValueList.From(state.Armies.Select(a => a.Id == armyId ? a with { Moves = 30 } : a)),
            ActiveSeatIndex = state.TurnOrder.ToList().IndexOf(enemy),
        };
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);
        var result = dispatcher.Dispatch(armed, new IC2.Engine.Movement.Commands.MoveArmyCommand(enemy, armyId, city.X, city.Y + 1));
        Assert.True(result.IsAccepted, result.Rejection?.ToString());
        return result.State with { ActiveSeatIndex = state.ActiveSeatIndex };
    }
}
