using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T38 Supply dialog follow-ups, treasury ↔ purse transfers, and
/// automatic resupply" (issue #78), Done-when 7 — pure functions for <c>FUN_0044F6D8</c> (army) and
/// <c>FUN_0044F7E4</c> (fleet), transcribed from <c>supply-capacity-rounding.md</c>. Every test here maps
/// to one of Done-when 7's bulleted scenarios.
/// </summary>
public sealed class AutomaticResupplyTests
{
    private static NationState Nation(string id, int treasury) =>
        new(id, id, "#000", "Leader", null, SeatControl.Human, null, treasury, 600, 0, 0, 15, 0, 100, 100, 500, 1,
            ValueList<RecruitmentSlot>.Empty, false);

    private static CityState City(string owner, int supplyTons) =>
        new("c1", "City", 0, 0, owner, owner, 80, supplyTons, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);

    /// <summary>
    /// Done-when 7, bullet 1: "a 48,173-troop army at its own city with 403 t goes to 481, not 482" --
    /// the general cap (no dialog +1), unlike <see cref="SupplyPurchase"/>'s dialog path.
    /// </summary>
    [Fact]
    public void ForArmy_AtOwnCity_FillsToTheGeneralCapWithNoDialogBonus()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 48_173, 6, "Roman 13-Unit Roster"));
        var army = new ArmyState("roman13", "rome", 0, 0, 9, 60, 600, 403, null, null, units);
        var city = City("rome", 1810);
        var nation = Nation("rome", 500);

        Assert.Equal(481, SupplyCapacity.ArmyCapacityTons(army.TotalTroops, EconomyTestbed.Ruleset));

        var result = AutomaticResupply.ForArmy(army, city, nation, nation, EconomyTestbed.Ruleset);

        Assert.Equal(78, result.AdmittedTons); // 481 - 403.
        Assert.Equal(481, result.Army.SupplyTons); // not 482: no dialog +1 here.
        Assert.Equal(1732, result.City.SupplyTons); // 1810 - 78.
        Assert.Equal(0, result.TalentsPaid);
    }

    /// <summary>Done-when 7, bullet 2: "the purse top-up case (purse 300, treasury positive → 800)".</summary>
    [Fact]
    public void ForArmy_AtOwnCity_PurseUnderThreshold_GainsAFlatTopUpFromTheTreasury()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 100, 6, "Tiny Battalion")); // capacity = 1.
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 300, 1, null, null, units); // already at capacity: no ton movement.
        var city = City("north", 1000);
        var nation = Nation("north", 500); // positive treasury.

        var result = AutomaticResupply.ForArmy(army, city, nation, nation, EconomyTestbed.Ruleset);

        Assert.Equal(0, result.AdmittedTons); // room is 0: isolates the purse-hygiene branch.
        Assert.Equal(800, result.Army.Money); // 300 + the flat 500 grant.
        Assert.Equal(0, result.ArmyNation.Treasury); // 500 - 500, the same 500 that funded the grant.
    }

    /// <summary>
    /// Review round 1, N3: the top-up grant is deliberately unclamped by the treasury's own balance --
    /// unlike <see cref="TreasuryPurseTransfer"/>'s manual dialog transfer, which never takes more than
    /// the source holds. The report gives this as a flat, unconditional grant with no clamping
    /// instruction ("purse gains 500 from the treasury"), so a treasury of 1 still funds the full 500 and
    /// is left at -499. Pinned explicitly so the two paths' deliberate disagreement does not regress into
    /// an accidental one.
    /// </summary>
    [Fact]
    public void ForArmy_AtOwnCity_PurseUnderThreshold_TreasuryOfOne_GrantsTheFullAmountUnclamped()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 100, 6, "Tiny Battalion")); // capacity = 1.
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 300, 1, null, null, units);
        var city = City("north", 1000);
        var nation = Nation("north", 1); // treasury barely positive.

        var result = AutomaticResupply.ForArmy(army, city, nation, nation, EconomyTestbed.Ruleset);

        Assert.Equal(800, result.Army.Money); // the full 500 grant, not clamped to the treasury's 1.
        Assert.Equal(-499, result.ArmyNation.Treasury); // 1 - 500, left negative.
    }

    /// <summary>Done-when 7, bullet 3: "the purse excess case (purse 1,200 → 1,000, treasury +200)".</summary>
    [Fact]
    public void ForArmy_AtOwnCity_PurseOverCap_SendsTheExcessToTheTreasury()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 100, 6, "Tiny Battalion"));
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 1200, 1, null, null, units);
        var city = City("north", 1000);
        var nation = Nation("north", 300);

        var result = AutomaticResupply.ForArmy(army, city, nation, nation, EconomyTestbed.Ruleset);

        Assert.Equal(1000, result.Army.Money); // capped at PurseCapPerUnit.
        Assert.Equal(500, result.ArmyNation.Treasury); // 300 + the 200 excess.
    }

    /// <summary>
    /// Done-when 7, bullet 4: "the foreign money / 5 cap" -- not money x 5 as in the dialog
    /// (<c>supply-capacity-rounding.md</c>'s explicit hazard). A 50-talent purse affords only 10 t here.
    /// </summary>
    [Fact]
    public void ForArmy_AtForeignCity_CapsTonsAtMoneyDividedByFive()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 100_000, 6, "Huge Battalion")); // room far exceeds 10 t.
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 50, 0, null, null, units);
        var city = City("south", 1000);
        var armyNation = Nation("north", 500);
        var cityNation = Nation("south", 500);

        var result = AutomaticResupply.ForArmy(army, city, armyNation, cityNation, EconomyTestbed.Ruleset);

        Assert.Equal(10, result.AdmittedTons); // 50 / 5 = 10, not 50 * 5 = 250.
        Assert.Equal(2, result.TalentsPaid); // 10 / 5 = 2.
        Assert.Equal(48, result.Army.Money); // 50 - 2.
        Assert.Equal(502, result.CityNation.Treasury); // the city's owner is paid.
    }

    /// <summary>
    /// Done-when 7, bullet 5: "an over-cap army trimmed back to troops / 100, with the surplus returned
    /// to the city" -- the room term is not floored at 0 here either.
    /// </summary>
    [Fact]
    public void ForArmy_OverCapacityAtOwnCity_GivesTheSurplusBackToTheCity()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 40_000, 6, "Over-Supplied Battalion")); // cap 400.
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 600, 500, null, null, units); // holds 500.
        var city = City("north", 1000);
        var nation = Nation("north", 0); // treasury 0: no top-up interference (purse well above 500 anyway).

        Assert.Equal(400, SupplyCapacity.ArmyCapacityTons(army.TotalTroops, EconomyTestbed.Ruleset));

        var result = AutomaticResupply.ForArmy(army, city, nation, nation, EconomyTestbed.Ruleset);

        Assert.Equal(-100, result.AdmittedTons);
        Assert.Equal(400, result.Army.SupplyTons); // trimmed back to troops / 100.
        Assert.Equal(1100, result.City.SupplyTons); // the surplus returned to the city.
    }

    /// <summary>Done-when 7, bullet 6: "a fleet capped at ships × 8".</summary>
    [Fact]
    public void ForFleet_AtOwnCity_FillsToShipsTimesEight()
    {
        var fleet = new FleetState("f1", "north", 0, 0, 4, 10, 100, 500, 0, null, null, null, null);
        var city = City("north", 1000);
        var nation = Nation("north", 500);

        Assert.Equal(80, SupplyCapacity.FleetCapacityTons(fleet.Ships, EconomyTestbed.Ruleset));

        var result = AutomaticResupply.ForFleet(fleet, city, nation, nation, EconomyTestbed.Ruleset);

        Assert.Equal(80, result.AdmittedTons);
        Assert.Equal(80, result.Fleet.SupplyTons);
        Assert.Equal(920, result.City.SupplyTons); // 1000 - 80.
    }

    [Fact]
    public void ForArmy_ArmyNationMismatchesArmysNation_Throws()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 100, 6, "Tiny Battalion"));
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 300, 0, null, null, units);
        var city = City("north", 1000);
        var wrongNation = Nation("south", 500);

        Assert.Throws<ArgumentException>(
            () => AutomaticResupply.ForArmy(army, city, wrongNation, wrongNation, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void ForFleet_CityNationMismatchesCitysOwner_Throws()
    {
        var fleet = new FleetState("f1", "north", 0, 0, 4, 10, 100, 500, 0, null, null, null, null);
        var city = City("south", 1000);
        var fleetNation = Nation("north", 500);
        var wrongCityNation = Nation("north", 500);

        Assert.Throws<ArgumentException>(
            () => AutomaticResupply.ForFleet(fleet, city, fleetNation, wrongCityNation, EconomyTestbed.Ruleset));
    }
}
