using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class EndTurnWarningsTests
{
    private static (World World, GameState State, Ruleset Ruleset) Fixture(
        EndTurnWarningScope scope = EndTurnWarningScope.EveryUnit)
    {
        var resolved = ToyFixtures.Toy;
        var state = GameStateFactory.CreateInitial(resolved.World, resolved.Ruleset, resolved.Scenario);
        var ruleset = resolved.Ruleset with
        {
            Flags = resolved.Ruleset.Flags with { EndTurnWarningScope = scope },
        };
        return (resolved.World, state, ruleset);
    }

    [Fact]
    public void Army_supply_boundary_and_city_gate()
    {
        var (world, state, ruleset) = Fixture();
        var army = Army(50_000, supply: 99);

        Assert.Contains(EndTurnWarnings.ArmyNeedsSupplies, Query(world, state with { Armies = ValueList.Of(army) }, ruleset));

        var atBoundary = army with { SupplyTons = 100 };
        Assert.DoesNotContain(EndTurnWarnings.ArmyNeedsSupplies, Query(world, state with { Armies = ValueList.Of(atBoundary) }, ruleset));

        var noCity = state with
        {
            Armies = ValueList.Of(army),
            Cities = ValueList.From(state.Cities.Select(city => city with { Owner = "south" })),
        };
        Assert.DoesNotContain(EndTurnWarnings.ArmyNeedsSupplies, Query(world, noCity, ruleset));
    }

    [Fact]
    public void Mercenary_pay_is_strict_and_empty_roster_only_warns_below_zero()
    {
        var (world, state, ruleset) = Fixture();
        var mercenary = new UnitSlot(1, "archers", 5_000, 7, "Mercenary");
        var pay = ArmyUpkeep.ComputeUnit(mercenary, ruleset);

        var below = Army(50_000, supply: 100) with
        {
            Money = pay - 1,
            Units = ValueList.Of(mercenary),
        };
        Assert.Contains(EndTurnWarnings.ArmyCannotPayMercenaries, Query(world, state with { Armies = ValueList.Of(below) }, ruleset));

        var equal = below with { Money = pay };
        Assert.DoesNotContain(EndTurnWarnings.ArmyCannotPayMercenaries, Query(world, state with { Armies = ValueList.Of(equal) }, ruleset));

        var empty = Army(50_000, supply: 100) with { Money = 0, Units = ValueList.Of(RegularUnit(50_000)) };
        Assert.DoesNotContain(EndTurnWarnings.ArmyCannotPayMercenaries, Query(world, state with { Armies = ValueList.Of(empty) }, ruleset));
        var negative = empty with { Money = -1 };
        Assert.Contains(EndTurnWarnings.ArmyCannotPayMercenaries, Query(world, state with { Armies = ValueList.Of(negative) }, ruleset));
    }

    [Fact]
    public void Fleet_docking_and_supply_boundaries_use_owned_port()
    {
        var (world, state, ruleset) = Fixture();
        var fleet = state.Fleets[0] with { X = 0, Y = 3, SupplyTons = 1 };
        var result = Query(world, state with { Fleets = ValueList.Of(fleet) }, ruleset);
        Assert.Contains(EndTurnWarnings.FleetNotDocked, result);
        Assert.Contains(EndTurnWarnings.FleetNeedsSupplies, result);

        var docked = fleet with { X = 3, Y = 2, SupplyTons = 2 };
        result = Query(world, state with { Fleets = ValueList.Of(docked) }, ruleset);
        Assert.DoesNotContain(EndTurnWarnings.FleetNotDocked, result);
        Assert.DoesNotContain(EndTurnWarnings.FleetNeedsSupplies, result);

        var noPort = state with
        {
            Fleets = ValueList.Of(fleet),
            Cities = ValueList.From(state.Cities.Select(city => city.Id == "portus"
                ? city with { Owner = "south" }
                : city.Id == "arx" ? city with { X = 3, Y = 1 } : city)),
        };
        Assert.DoesNotContain(EndTurnWarnings.FleetNeedsSupplies, Query(world, noPort, ruleset));
    }

    [Fact]
    public void Aboard_army_uses_fleet_port_gate_and_skips_land_loop()
    {
        var (world, state, ruleset) = Fixture();
        var army = Army(50_000, supply: 99) with { Money = -1, AboardFleetId = "north-fleet-1", CoveredTileCode = null };
        var fleet = state.Fleets[0] with { X = 3, Y = 2, CarriedArmyId = army.Id, SupplyTons = 2 };
        var inlandOnly = state with
        {
            Armies = ValueList.Of(army),
            Fleets = ValueList.Of(fleet),
            Cities = ValueList.From(state.Cities.Select(city => city.Id == "portus"
                ? city with { Owner = "south" }
                : city.Id == "arx" ? city with { X = 3, Y = 1 } : city)),
        };

        var withoutPort = Query(world, inlandOnly, ruleset);
        Assert.DoesNotContain(EndTurnWarnings.ArmyNeedsSupplies, withoutPort);
        Assert.Contains(EndTurnWarnings.ArmyCannotPayMercenaries, withoutPort);

        var onLand = inlandOnly with
        {
            Armies = ValueList.Of(army with { AboardFleetId = null, CoveredTileCode = 2 }),
            Fleets = ValueList.Of(fleet with { CarriedArmyId = null }),
        };
        Assert.Contains(EndTurnWarnings.ArmyNeedsSupplies, Query(world, onLand, ruleset));

        var withPort = inlandOnly with
        {
            Cities = ValueList.From(inlandOnly.Cities.Select(city => city.Id == "arx"
                ? city with { X = 2, Y = 1 }
                : city)),
        };
        Assert.Contains(EndTurnWarnings.ArmyNeedsSupplies, Query(world, withPort, ruleset));
    }

    [Fact]
    public void Order_cap_and_repair_line_are_preserved()
    {
        var (world, state, ruleset) = Fixture();
        var first = Army(50_000, supply: 99) with { Id = "first" };
        var second = Army(50_000, supply: 99) with { Id = "second" };
        var fleet = state.Fleets[0] with { X = 0, Y = 3, SupplyTons = 1 };
        var ordered = Query(world, state with
        {
            Armies = ValueList.Of(first, second),
            Fleets = ValueList.Of(fleet),
        }, ruleset);
        Assert.Equal(
            new[]
            {
                EndTurnWarnings.ArmyNeedsSupplies,
                EndTurnWarnings.ArmyNeedsSupplies,
                EndTurnWarnings.FleetNotDocked,
                EndTurnWarnings.FleetNeedsSupplies,
            },
            ordered);

        var payWarning = first with { Id = "pay", Money = -1 };
        var secondPayWarning = second with { Money = -1 };
        var sixTriggers = Query(world, state with
        {
            Armies = ValueList.Of(payWarning, secondPayWarning),
            Fleets = ValueList.Of(fleet),
        }, ruleset);
        Assert.Equal(5, sixTriggers.Count);
        Assert.Equal(EndTurnWarnings.ArmyNeedsSupplies, sixTriggers[0]);
        Assert.Equal(EndTurnWarnings.ArmyCannotPayMercenaries, sixTriggers[1]);
        Assert.Equal(EndTurnWarnings.ArmyNeedsSupplies, sixTriggers[2]);
        Assert.Equal(EndTurnWarnings.ArmyCannotPayMercenaries, sixTriggers[3]);
        Assert.Equal(EndTurnWarnings.FleetNotDocked, sixTriggers[4]);

        var repair = fleet with { ConditionPercent = 64, X = 3, Y = 2, SupplyTons = 2 };
        var noOtherTrigger = Query(world, state with { Fleets = ValueList.Of(repair) }, ruleset);
        Assert.Empty(noOtherTrigger);

        var repairWithTrigger = Query(world, state with
        {
            Armies = ValueList.Of(first),
            Fleets = ValueList.Of(repair with { X = 0, Y = 3 }),
        }, ruleset);
        Assert.Equal(EndTurnWarnings.ArmyNeedsSupplies, repairWithTrigger[0]);
        Assert.Equal(EndTurnWarnings.FleetNeedsRepairing, repairWithTrigger[1]);
        Assert.Equal(EndTurnWarnings.FleetNotDocked, repairWithTrigger[2]);
    }

    [Fact]
    public void Faithful_filter_uses_recomputed_allowance_and_improved_checks_every_unit()
    {
        var (world, state, faithful) = Fixture(EndTurnWarningScope.NotActedOnly);
        var army = Army(50_000, supply: 99, moves: 8);
        var movedArmy = army with { Moves = 7 };
        Assert.Empty(Query(world, state with { Armies = ValueList.Of(movedArmy) }, faithful));
        Assert.NotEmpty(Query(world, state with { Armies = ValueList.Of(army) }, faithful));

        var tickPenaltyArmy = Army(50_000, supply: 10, moves: 7);
        Assert.Empty(Query(world, state with { Armies = ValueList.Of(tickPenaltyArmy) }, faithful));

        var fleet = state.Fleets[0] with { Moves = 33, X = 0, Y = 3, SupplyTons = 1 };
        var fleetMoved = state with { Fleets = ValueList.Of(fleet), Armies = ValueList<ArmyState>.Empty };
        Assert.Empty(Query(world, fleetMoved, faithful));

        var (improvedWorld, _, improved) = Fixture(EndTurnWarningScope.EveryUnit);
        Assert.NotEmpty(Query(improvedWorld, state with { Armies = ValueList.Of(movedArmy), Fleets = ValueList.Of(fleet) }, improved));
    }

    [Fact]
    public void Computer_seat_never_receives_warnings()
    {
        var (world, state, ruleset) = Fixture();
        Assert.Empty(Query(world, state, ruleset, "south"));
    }

    private static IReadOnlyList<string> Query(World world, GameState state, Ruleset ruleset, string nation = "north") =>
        EndTurnWarnings.For(state, world, ruleset, nation).Lines;

    private static ArmyState Army(int troops, int supply, int moves = 0) => new(
        Id: "north-army-1",
        Nation: "north",
        X: 3,
        Y: 2,
        Moves: moves,
        Morale: 59,
        Money: 0,
        SupplyTons: supply,
        CoveredTileCode: 2,
        AboardFleetId: null,
        Units: ValueList.Of(RegularUnit(troops)));

    private static UnitSlot RegularUnit(int troops) =>
        new(0, "light_infantry", troops, 6, "Regulars");
}
