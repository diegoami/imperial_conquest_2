using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class HumanSupportTimingTests
{
    private static GameSession Build(bool cargo = false, bool hotseat = false, int armyMoves = 8,
        bool adjacent = false, bool improvedLegacy = false)
    {
        var data = ToyFixtures.Toy;
        var world = data.World with
        {
            Width = 8, Height = 6,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength,
                ValueList.Of(new TerrainRun(2, 32), new TerrainRun(0, 16))),
            Cities = ValueList.From(data.World.Cities.Select(c => c.Id switch
            {
                "arx" => c with { X = 3, Y = 3 },
                "portus" => c with { X = 5, Y = 0 },
                _ => c with { X = 7, Y = 0 },
            })),
        };
        var rules = data.Ruleset with { Flags = data.Ruleset.Flags with
        {
            EconomyPurses = improvedLegacy ? EconomyPurseModel.CentralTreasury : EconomyPurseModel.PerUnitPurses,
            HumanMoveResupply = improvedLegacy ? HumanMoveResupplyPolicy.AgainstNonHostileCity : HumanMoveResupplyPolicy.Never,
            StrategicStep = StrategicStepPolicy.StraightLine,
        } };
        var state = GameStateFactory.CreateInitial(world, rules, data.Scenario);
        var fleet = state.FleetById("north-fleet-1")! with
        {
            X = adjacent ? 2 : 0, Y = 4, Ships = 20, SupplyTons = 0, Money = 0,
            ConditionPercent = 70, Moves = 8, ConstructionTicksRemaining = null, BuildCityId = null,
            CarriedArmyId = cargo ? "north-army-1" : null, CoveredTileCode = 0,
        };
        var army = state.ArmyById("north-army-1")! with
        {
            X = cargo ? fleet.X : adjacent ? 2 : 0, Y = cargo ? 4 : adjacent ? 3 : 2,
            SupplyTons = 0, Money = 0, Moves = armyMoves,
            AboardFleetId = cargo ? fleet.Id : null, CoveredTileCode = cargo ? null : 2,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 10000, 6, "Regulars")),
        };
        state = state with
        {
            Armies = hotseat ? ValueList.Of(army, army with
                { Id = "south-army-1", Nation = "south", X = 6, Y = 0, AboardFleetId = null, CoveredTileCode = 2 })
                : ValueList.Of(army),
            Fleets = ValueList.Of(fleet),
            Cities = ValueList.From(state.Cities.Select(c => c with
                { SupplyTons = c.Id == "arx" || (hotseat && c.Id == "meridia") ? 1000 : 0 })),
            Nations = ValueList.From(state.Nations.Select(n => n with
                { Control = hotseat || n.Id == "north" ? SeatControl.Human : SeatControl.Ai, Treasury = 10000, Unity = 900 })),
            Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.Alliance),
        };
        var save = new SaveGame(state.SchemaVersion, "support-timing", "support-timing", state.ScenarioId,
            state.WorldId, state.RulesetId, state);
        return new GameSession(world, rules, data.Scenario, save);
    }

    [Fact]
    public void Passing_an_intermediate_town_supplies_an_army_that_finishes_far_away()
    {
        var session = Build();
        session.ConfigureHumanSupport(new(SupplyArmies: true));
        Assert.Equal(0, session.State.Armies[0].SupplyTons);
        var response = session.Submit("move north-army-1 7 2");
        Assert.DoesNotContain(response.Lines, line => line.Contains("rejected", StringComparison.Ordinal));
        var army = session.State.ArmyById("north-army-1")!;
        var target = SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, session.Ruleset);
        Assert.Equal((7, 2), (army.X, army.Y));
        Assert.Equal(target, army.SupplyTons);
        Assert.Equal(1000 - target, session.State.CityById("arx")!.SupplyTons);
    }

    [Fact]
    public void The_last_affordable_entered_tile_can_supply_with_zero_moves_left()
    {
        var session = Build(armyMoves: 2);
        session.ConfigureHumanSupport(new(SupplyArmies: true));
        session.Submit("move north-army-1 7 2");
        var army = session.State.Armies[0];
        Assert.Equal((2, 2, 0), (army.X, army.Y, army.Moves));
        Assert.True(army.SupplyTons > 0);
    }

    [Fact]
    public void A_carrying_fleet_supplies_its_cargo_and_itself_at_an_intermediate_port_without_repair()
    {
        var session = Build(cargo: true);
        session.ConfigureHumanSupport(new(SupplyArmies: true, SupplyFleets: true));
        session.Submit("move-fleet north-fleet-1 7 4");
        var fleet = session.State.Fleets[0];
        var army = session.State.Armies[0];
        Assert.Equal((7, 4), (fleet.X, fleet.Y));
        Assert.Equal((fleet.X, fleet.Y, fleet.Id), (army.X, army.Y, army.AboardFleetId));
        Assert.Equal(army.Id, fleet.CarriedArmyId);
        Assert.Equal(SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, session.Ruleset), army.SupplyTons);
        Assert.Equal(SupplyCapacity.FleetCapacityTons(fleet.Ships, session.Ruleset), fleet.SupplyTons);
        Assert.Equal(1000, session.State.CityById("arx")!.SupplyTons + army.SupplyTons + fleet.SupplyTons);
        Assert.Equal(70, fleet.ConditionPercent);
        Assert.True(fleet.Moves > 0);
    }

    [Fact]
    public void Disabled_policy_preserves_the_existing_move_state_and_rng_exactly()
    {
        var baseline = Build();
        var disabled = Build();
        var before = disabled.State;
        disabled.ConfigureHumanSupport(new());
        Assert.Same(before, disabled.State);
        Assert.Equal(baseline.Submit("move north-army-1 7 2").Lines,
            disabled.Submit("move north-army-1 7 2").Lines);
        Assert.Equal(baseline.State, disabled.State);
        Assert.Equal(0, disabled.State.Armies[0].SupplyTons);
    }

    [Fact]
    public void Rejected_movement_does_not_supply_or_advance_state()
    {
        var session = Build();
        session.ConfigureHumanSupport(new(SupplyArmies: true));
        var before = session.State;
        Assert.Contains(session.Submit("move north-army-1 99 2").Lines, line => line.Contains("rejected", StringComparison.Ordinal));
        Assert.Same(before, session.State);
    }

    [Fact]
    public void Selected_automation_does_not_lose_the_dialog_capacity_bonus_to_legacy_improved_resupply()
    {
        var session = Build(improvedLegacy: true);
        session.ConfigureHumanSupport(new(SupplyArmies: true));
        session.Submit("move north-army-1 3 2");
        var army = session.State.Armies[0];
        var target = SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, session.Ruleset);
        Assert.Equal(target, army.SupplyTons);
        Assert.Equal(1000 - target, session.State.CityById("arx")!.SupplyTons);
    }

    [Fact]
    public void Unselected_legacy_improved_supply_still_runs_when_only_funding_is_selected()
    {
        var session = Build(improvedLegacy: true);
        session.ConfigureHumanSupport(new(FundArmies: true));
        session.Submit("move north-army-1 3 2");
        var army = session.State.Armies[0];
        Assert.Equal(SupplyCapacity.ArmyCapacityTons(army.TotalTroops, session.Ruleset), army.SupplyTons);
        Assert.Equal(HumanAutoSupport.ArmyPurseTarget(army, session.Ruleset), army.Money);
    }

    [Fact]
    public void Initial_support_and_identical_configuration_are_guarded_but_the_next_turn_refills_again()
    {
        var session = Build(adjacent: true);
        var options = new HumanSupportOptions(SupplyArmies: true);
        session.ConfigureHumanSupport(options);
        var target = session.State.Armies[0].SupplyTons;
        Assert.True(target > 50);
        session.Submit("buy-fleet-supply north-fleet-1 army north-army-1 50");
        Assert.Equal(target - 50, session.State.Armies[0].SupplyTons);
        var drained = session.State;
        session.ConfigureHumanSupport(options with { });
        Assert.Same(drained, session.State);
        session.Submit("end");
        Assert.Equal("north", session.State.ActiveNationId);
        Assert.Equal(target, session.State.ArmyById("north-army-1")!.SupplyTons);
    }

    [Fact]
    public void Incoming_hotseat_gets_support_before_its_first_order()
    {
        var session = Build(hotseat: true);
        session.ConfigureHumanSupport(new(SupplyArmies: true));
        Assert.Equal(0, session.State.ArmyById("south-army-1")!.SupplyTons);
        session.Submit("end");
        Assert.Equal("south", session.State.ActiveNationId);
        Assert.Equal(SupplyCapacity.ArmyDialogCapacityTons(10000, session.Ruleset),
            session.State.ArmyById("south-army-1")!.SupplyTons);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Per_unit_recovery_supplies_intermediate_towns_even_with_global_automation_off(bool fleet)
    {
        var session = Build();
        var id = fleet ? "north-fleet-1" : "north-army-1";
        session.SetRetreatSupplyPlan(new(id, "north", fleet));
        session.Submit(fleet ? "move-fleet north-fleet-1 7 4" : "move north-army-1 7 2");
        Assert.True((fleet ? session.State.Fleets[0].SupplyTons : session.State.Armies[0].SupplyTons) > 0);
        Assert.True((fleet ? session.State.Fleets[0].Money : session.State.Armies[0].Money) > 0);
        Assert.Equal(70, session.State.Fleets[0].ConditionPercent);
    }
}
