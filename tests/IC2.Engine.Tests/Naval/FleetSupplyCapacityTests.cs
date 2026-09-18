using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 9: supply capacity is <c>ships × 8</c> tons,
/// purchased through T08's economy at 1 talent per 5 tons. Both formulas are T08/T38's own
/// (<see cref="SupplyCapacity.FleetCapacityTons"/>, <see cref="SupplyPurchase.BuyForFleet"/>) — this
/// task calls them, it does not re-implement a cap (the T14 entry's own Hazards).
/// </summary>
public sealed class FleetSupplyCapacityTests
{
    [Fact]
    public void FleetCapacity_IsShipsTimesEight()
    {
        var ruleset = NavalTestbed.Ruleset;
        Assert.Equal(8, ruleset.Economy.FleetSupplyTonsPerShip);
        Assert.Equal(80, SupplyCapacity.FleetCapacityTons(10, ruleset));
    }

    [Fact]
    public void BuyingSupplyAbroad_Costs1TalentPer5Tons()
    {
        var state = NavalTestbed.InitialState();
        var ruleset = NavalTestbed.Ruleset;
        Assert.Equal(5, ruleset.Economy.SupplyTonsPerTalent);

        var buyerNation = state.NationById("south")!; // fleet's own nation
        var sellingCity = state.CityById("arx")!; // owned by "north" -- a foreign purchase.
        var sellingCityNation = state.NationById(sellingCity.Owner)!;

        var fleet = new FleetState(
            "supply-test-fleet", buyerNation.Id, sellingCity.X, sellingCity.Y, Moves: 3, Ships: 10,
            ConditionPercent: 90, Money: 100, SupplyTons: 0, ConstructionTicksRemaining: null,
            BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

        var result = SupplyPurchase.BuyForFleet(fleet, sellingCity, buyerNation, sellingCityNation, tons: 40, ruleset);

        Assert.False(result.WasFreeOwnCity);
        Assert.Equal(40, result.AdmittedTons);
        Assert.Equal(8, result.TalentsPaid); // 40 / 5.
    }

    [Fact]
    public void BuyingSupplyAtAnOwnedCity_IsFree()
    {
        var state = NavalTestbed.InitialState();
        var ruleset = NavalTestbed.Ruleset;

        var nation = state.NationById("north")!;
        var ownCity = state.CityById("arx")!;

        var fleet = new FleetState(
            "supply-test-fleet-2", nation.Id, ownCity.X, ownCity.Y, Moves: 3, Ships: 10,
            ConditionPercent: 90, Money: 100, SupplyTons: 0, ConstructionTicksRemaining: null,
            BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

        var result = SupplyPurchase.BuyForFleet(fleet, ownCity, nation, nation, tons: 30, ruleset);

        Assert.True(result.WasFreeOwnCity);
        Assert.Equal(0, result.TalentsPaid);
    }
}
