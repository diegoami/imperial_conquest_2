using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

public sealed class HumanAutoSupportTests
{
    private static (World World, Ruleset Rules, GameState State) Fixture()
    {
        var data = ToyFixtures.Toy;
        var rules = data.Ruleset with { Flags = data.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.PerUnitPurses } };
        var state = GameStateFactory.CreateInitial(data.World, rules, data.Scenario);
        var army = state.ArmyById("north-army-1")! with
        {
            X = 3, Y = 2, SupplyTons = 0, Money = 0,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 10000, 6, "Regulars")),
        };
        var fleet = state.FleetById("north-fleet-1")! with
        {
            X = 2, Y = 2, Ships = 20, SupplyTons = 0, Money = 0, ConditionPercent = 70,
            ConstructionTicksRemaining = null, BuildCityId = null,
        };
        state = state with
        {
            ActiveSeatIndex = Array.IndexOf(state.TurnOrder.ToArray(), "north"),
            Nations = ValueList.From(state.Nations.Select(n => n with
            {
                Control = n.Id == "north" ? SeatControl.Human : SeatControl.Ai,
                Treasury = n.Id == "north" ? 10000 : n.Treasury,
            })),
            Cities = ValueList.From(state.Cities.Select(c => c with { SupplyTons = c.Id == "arx" ? 1000 : 0 })),
            Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet),
        };
        return (data.World, rules, state);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Null_or_disabled_policy_is_reference_identical_and_does_not_draw(bool explicitDisabled)
    {
        var (world, rules, state) = Fixture();
        var result = HumanAutoSupport.ForNation(state, rules, world,
            explicitDisabled ? new HumanSupportOptions() : null, new NoDraws());
        Assert.Same(state, result.State);
        Assert.Empty(result.Events);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Active_human_receives_maximum_supply_and_quarterly_purses_with_conserved_accounts()
    {
        var (world, rules, state) = Fixture();
        var beforeArmy = state.Armies[0];
        var beforeFleet = state.Fleets[0];
        var armySupply = SupplyCapacity.ArmyDialogCapacityTons(beforeArmy.TotalTroops, rules);
        var fleetSupply = SupplyCapacity.FleetCapacityTons(beforeFleet.Ships, rules);
        var armyPurse = Math.Min(ArmyUpkeep.Compute(beforeArmy.Units, rules), rules.Economy.PurseCapPerUnit);
        var fleetPurse = Math.Min(ShipUpkeep.Compute(beforeFleet.Ships, rules), rules.Economy.PurseCapPerUnit);
        var options = new HumanSupportOptions(true, true, true, true);
        var result = HumanAutoSupport.ForNation(state, rules, world, options, new NoDraws());
        Assert.Equal(armySupply, result.State.Armies[0].SupplyTons);
        Assert.Equal(fleetSupply, result.State.Fleets[0].SupplyTons);
        Assert.Equal(1000 - armySupply - fleetSupply, result.State.CityById("arx")!.SupplyTons);
        Assert.Equal(armyPurse, result.State.Armies[0].Money);
        Assert.Equal(fleetPurse, result.State.Fleets[0].Money);
        Assert.Equal(10000 - armyPurse - fleetPurse, result.State.NationById("north")!.Treasury);
        Assert.Equal(beforeFleet.ConditionPercent, result.State.Fleets[0].ConditionPercent);
        Assert.Equal(beforeFleet.Moves, result.State.Fleets[0].Moves);
        Assert.Equal(state.RandomSeed, result.State.RandomSeed);
        Assert.NotEmpty(result.Events);
        Assert.Empty(result.Failures);
        var again = HumanAutoSupport.ForNation(result.State, rules, world, options, new NoDraws());
        Assert.Same(result.State, again.State);
        Assert.Empty(again.Events);
    }

    [Theory]
    [InlineData(EconomyPurseModel.PerUnitPurses)]
    [InlineData(EconomyPurseModel.CentralTreasury)]
    public void Funding_reuses_the_existing_debt_policy_for_both_armies_and_fleets(EconomyPurseModel model)
    {
        var (world, rules, state) = Fixture();
        rules = rules with { Flags = rules.Flags with { EconomyPurses = model } };
        state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "north"
            ? n with { Treasury = -500 } : n)) };
        var result = HumanAutoSupport.ForNation(state, rules, world,
            new(FundArmies: true, FundFleets: true), new NoDraws());
        if (model == EconomyPurseModel.PerUnitPurses)
        {
            Assert.True(result.State.Armies[0].Money > 0);
            Assert.True(result.State.Fleets[0].Money > 0);
            Assert.Equal(-500, result.State.NationById("north")!.Treasury
                + result.State.Armies[0].Money + result.State.Fleets[0].Money);
        }
        else Assert.Same(state, result.State);
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Army_can_take_free_stock_from_an_own_fleet_or_army(bool fromArmy)
    {
        var (world, rules, state) = Fixture();
        state = state with { Cities = ValueList.From(state.Cities.Select(c => c with { SupplyTons = 0 })) };
        if (fromArmy)
            state = state with { Armies = ValueList.Of(state.Armies[0], state.Armies[0]
                with { Id = "provider", X = 4, SupplyTons = 40 }) };
        else state = state with { Fleets = ValueList.Of(state.Fleets[0] with { SupplyTons = 40 }) };
        var result = HumanAutoSupport.ForArmy(state, rules, world,
            new(SupplyArmies: true), new NoDraws(), "north-army-1");
        Assert.Equal(40, result.State.ArmyById("north-army-1")!.SupplyTons);
        Assert.Equal(0, fromArmy ? result.State.ArmyById("provider")!.SupplyTons : result.State.Fleets[0].SupplyTons);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Paid_foreign_town_stock_is_not_bought_by_the_free_supply_automation()
    {
        var (world, rules, state) = Fixture();
        state = state with { Cities = ValueList.From(state.Cities.Select(c => c.Id == "arx"
            ? c with { Owner = "south" } : c)) };
        var result = HumanAutoSupport.ForArmy(state, rules, world,
            new(SupplyArmies: true), new NoDraws(), "north-army-1");
        Assert.Same(state, result.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void Several_free_providers_fill_only_remaining_room_in_the_existing_provider_order()
    {
        var (world, rules, state) = Fixture();
        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(c => c with { SupplyTons = c.Id == "arx" ? 40 : 0 })),
            Fleets = ValueList.Of(state.Fleets[0] with { SupplyTons = 40 }),
            Armies = ValueList.Of(state.Armies[0], state.Armies[0]
                with { Id = "provider", X = 4, SupplyTons = 40 }),
        };
        var target = SupplyCapacity.ArmyDialogCapacityTons(state.Armies[0].TotalTroops, rules);
        Assert.InRange(target, 81, 119); // This control must need all three providers.
        var options = new HumanSupportOptions(SupplyArmies: true);
        var result = HumanAutoSupport.ForArmy(state, rules, world, options, new NoDraws(), "north-army-1");
        Assert.Equal(target, result.State.ArmyById("north-army-1")!.SupplyTons);
        Assert.Equal(0, result.State.CityById("arx")!.SupplyTons);
        Assert.Equal(0, result.State.Fleets[0].SupplyTons);
        Assert.Equal(120 - target, result.State.ArmyById("provider")!.SupplyTons);
        Assert.Empty(result.Failures);
        var again = HumanAutoSupport.ForArmy(result.State, rules, world, options, new NoDraws(), "north-army-1");
        Assert.Same(result.State, again.State);
        Assert.Empty(again.Events);
    }

    [Fact]
    public void No_stock_means_no_fabricated_supply_and_over_target_purses_are_not_pulled_back()
    {
        var (world, rules, state) = Fixture();
        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(c => c with { SupplyTons = 0 })),
            Armies = ValueList.Of(state.Armies[0] with { Money = rules.Economy.PurseCapPerUnit + 100 }),
        };
        var result = HumanAutoSupport.ForArmy(state, rules, world,
            new(SupplyArmies: true, FundArmies: true), new NoDraws(), "north-army-1");
        Assert.Same(state, result.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void AI_seats_and_fleets_under_construction_are_inert()
    {
        var (world, rules, state) = Fixture();
        var options = new HumanSupportOptions(true, true, true, true);
        var ai = state with { Nations = ValueList.From(state.Nations.Select(n => n with { Control = SeatControl.Ai })) };
        Assert.Same(ai, HumanAutoSupport.ForNation(ai, rules, world, options, new NoDraws()).State);
        var building = state with { Fleets = ValueList.Of(state.Fleets[0] with { ConstructionTicksRemaining = 3 }) };
        var result = HumanAutoSupport.ForFleet(building, rules, world, options, new NoDraws(), "north-fleet-1");
        Assert.Same(building, result.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void Ordinary_handler_refusal_is_returned_without_state_changes_or_published_effects()
    {
        var (world, rules, state) = Fixture();
        // An ambiguous imported/modded id must retain the money handler's ownership refusal.
        state = state with { Armies = ValueList.Of(state.Armies[0]
            with { Id = "north-fleet-1", Nation = "south" }) };
        var result = HumanAutoSupport.ForFleet(state, rules, world,
            new(FundFleets: true), new NoDraws(), "north-fleet-1");
        Assert.Same(state, result.State);
        Assert.Empty(result.Events);
        Assert.Equal(TransferMoneyRejections.NotYourUnit, Assert.Single(result.Failures).Rejection.Code);
    }

    private sealed class NoDraws : IRng
    {
        public ulong Seed => 123;
        public ulong State => 123;
        public IRng ForStream(string streamName) => this;
        public ulong NextUInt64() => throw new InvalidOperationException("Support drew random values.");
        public int NextInt(int exclusiveUpperBound) => throw new InvalidOperationException("Support drew random values.");
        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) => throw new InvalidOperationException("Support drew random values.");
        public bool NextChance(int numerator, int denominator) => throw new InvalidOperationException("Support drew random values.");
    }
}
