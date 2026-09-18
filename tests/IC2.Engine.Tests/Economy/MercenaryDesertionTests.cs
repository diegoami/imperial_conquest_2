using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays, mercenary desertion, and deposition
/// for debt", Done-when 1, 2 and 3: the treasury pays regulars with no balance check, the army's own
/// purse pays its mercenaries, an unpaid mercenary deserts as a whole unit taking its share of supplies,
/// the last unit fills the resulting hole without being revisited, and regulars never desert.
/// </summary>
public sealed class MercenaryDesertionTests
{
    private static ArmyState Army(int money, int supplyTons, params UnitSlot[] units) => new(
        Id: "a1",
        Nation: "north",
        X: 0,
        Y: 0,
        Moves: 9,
        Morale: 60,
        Money: money,
        SupplyTons: supplyTons,
        CoveredTileCode: null,
        AboardFleetId: null,
        Units: ValueList.Of(units));

    [Fact]
    public void BillArmy_RegularUnit_IsChargedToTheReturnedTotal_NeverToThePurse()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var army = Army(money: 0, supplyTons: 0, new UnitSlot(0, "light_infantry", 15_000, 6, "Regular"));

        var result = MercenaryDesertion.BillArmy(army, ruleset);

        Assert.Equal(75, result.RegularUpkeepCharged); // 15,000 light infantry -> 75 (docs/task-catalogue.md Done-when 1).
        Assert.Equal(0, result.DesertedUnitCount);
        Assert.NotNull(result.Army);
        Assert.Equal(0, result.Army!.Money); // untouched by a regular's charge.
    }

    [Fact]
    public void BillArmy_RegularsNeverDesert_HoweverEmptyThePurse()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var army = Army(
            money: -999_999, // however deep the debt, per the report's own Rome example.
            supplyTons: 500,
            new UnitSlot(0, "heavy_infantry", 6_000, 6, "Regular One"),
            new UnitSlot(0, "light_infantry", 15_000, 6, "Regular Two"));

        var result = MercenaryDesertion.BillArmy(army, ruleset);

        Assert.Equal(0, result.DesertedUnitCount);
        Assert.NotNull(result.Army);
        Assert.Equal(2, result.Army!.Units.Count);
        Assert.Equal(21_000, result.Army.TotalTroops); // both regulars survive intact.
        Assert.Equal(60 + 75, result.RegularUpkeepCharged); // 6,000 HI -> 60; 15,000 LI -> 75.
    }

    [Fact]
    public void BillArmy_MercenaryWithAPositivePurse_IsPaidFromThePurse_NotDeserted()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var mercenary = new UnitSlot(11, "light_infantry", 6_438, 8, "Gallic Mercenary"); // pay 51.
        var army = Army(money: 100, supplyTons: 0, mercenary);

        var result = MercenaryDesertion.BillArmy(army, ruleset);

        Assert.Equal(0, result.DesertedUnitCount);
        Assert.Equal(0, result.RegularUpkeepCharged); // never charged to the treasury.
        Assert.NotNull(result.Army);
        Assert.Single(result.Army!.Units);
        Assert.Equal(49, result.Army.Money); // 100 - 51.
    }

    [Fact]
    public void BillArmy_TheMercenaryThatDrivesThePurseNegative_IsStillPaid_ThenFlooredAtZero()
    {
        var ruleset = EconomyTestbed.Ruleset;
        // Two mercenaries, pay 51 each: the first leaves the purse positive (100 -> 49); the second
        // (still checked with a positive purse, 49 > 0) is paid in full even though it drives the purse
        // to -2, and only the post-army floor brings it back to zero -- not a mid-loop clamp.
        var first = new UnitSlot(11, "light_infantry", 6_438, 8, "First Mercenary");
        var second = new UnitSlot(12, "light_infantry", 6_438, 8, "Second Mercenary");
        var army = Army(money: 100, supplyTons: 0, first, second);

        var result = MercenaryDesertion.BillArmy(army, ruleset);

        Assert.Equal(0, result.DesertedUnitCount); // both were paid; the purse was still positive each time it was checked.
        Assert.NotNull(result.Army);
        Assert.Equal(2, result.Army!.Units.Count);
        Assert.Equal(0, result.Army.Money); // 100 - 51 - 51 = -2, floored to 0.
    }

    [Fact]
    public void BillArmy_MercenaryReachedWithAnEmptyPurse_DesertsAsAWholeUnit_TakingItsSupplyShare()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var mercenary = new UnitSlot(11, "light_infantry", 6_438, 8, "Gallic Mercenary");
        var regular = new UnitSlot(0, "heavy_infantry", 6_000, 6, "Regular Companion"); // keeps the army alive.
        var army = Army(money: 0, supplyTons: 500, mercenary, regular); // purse already at 0 -- not merely low.

        var result = MercenaryDesertion.BillArmy(army, ruleset);

        Assert.Equal(1, result.DesertedUnitCount);
        Assert.NotNull(result.Army);
        Assert.Single(result.Army!.Units);
        Assert.Equal(500 - (6_438 / 100), result.Army.SupplyTons); // troops / 100 taken with it.
    }

    [Fact]
    public void BillArmy_TreasuryNeverBacksAMercenaryUp_EvenWithAFullPurseUnrelatedToTheArmy()
    {
        // The report's own point, restated: there is no "purse first, then treasury" fallback, and this
        // method never even sees the treasury -- it only ever reads and writes the army's own purse. A
        // rich nation cannot save this mercenary; nothing outside this army's own Money field can.
        var ruleset = EconomyTestbed.Ruleset;
        var mercenary = new UnitSlot(11, "light_infantry", 6_438, 8, "Gallic Mercenary");
        var army = Army(money: 0, supplyTons: 0, mercenary);

        var result = MercenaryDesertion.BillArmy(army, ruleset);

        Assert.Equal(1, result.DesertedUnitCount);
    }

    /// <summary>
    /// The report's own worked Carthaginian example (<c>1_cartago_271_spring_11 → summer_1</c>): six
    /// units, purse 100. The Moor mercenary in slot 3 deserts; the army's last unit, the Numidian
    /// mercenary in slot 5, fills the hole and survives despite the purse it lands in already reading
    /// negative -- because the loop has advanced past slot 3 and never checks it again this quarter.
    /// The supply figure here (100, not the report's own 0) is chosen so the desertion's supply
    /// deduction is actually observable rather than floored away, as it was in the source data.
    /// </summary>
    [Fact]
    public void BillArmy_ReproducesTheCarthaginianCase_LastUnitFillsTheHoleAndSurvivesUnrevisited()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var army = Army(
            money: 100,
            supplyTons: 100,
            new UnitSlot(21, "light_infantry", 4_800, 7, "Greek"), // merc, pay 33.
            new UnitSlot(0, "heavy_infantry", 2_300, 6, "2nd Guards"), // regular, cost 22.
            new UnitSlot(22, "heavy_infantry", 5_700, 7, "Greek"), // merc, pay 78.
            new UnitSlot(23, "archers", 2_700, 6, "Moor"), // merc, pay 15 -- deserts.
            new UnitSlot(0, "heavy_cavalry", 1_100, 6, "2nd Dragoons"), // regular, cost 20.
            new UnitSlot(24, "heavy_cavalry", 2_000, 7, "Numidian")); // merc, pay 56 -- swapped, unchecked.

        var result = MercenaryDesertion.BillArmy(army, ruleset);

        Assert.NotNull(result.Army);
        var after = result.Army!;

        Assert.Equal(5, after.Units.Count);
        Assert.Equal(15_900, after.TotalTroops); // 18,600 - the deserted Moor's 2,700.
        Assert.Equal(0, after.Money); // 100 - 33 - 78 = -11, floored to 0.
        Assert.Equal(73, after.SupplyTons); // 100 - (2,700 / 100).
        Assert.Equal(1, result.DesertedUnitCount);
        Assert.Equal(42, result.RegularUpkeepCharged); // 22 (2nd Guards) + 20 (2nd Dragoons).

        // The report's own signature: the Numidian survives in slot 3, not slot 5 -- swapped in, not
        // shifted along -- despite the negative purse, because it is never itself checked this quarter.
        Assert.Equal("Numidian", after.Units[3].Name);
        Assert.True(after.Units[3].IsMercenary);
    }

    [Theory]
    [InlineData(1, 0, true)] // the last mercenary deletes the army.
    [InlineData(2, 1, false)]
    [InlineData(3, 1, false)]
    [InlineData(4, 2, false)]
    [InlineData(5, 2, false)]
    public void BillArmy_UnpaidAllMercenaryArmy_LosesTheCeilingOfHalfItsUnits(
        int unitCount, int expectedSurvivors, bool expectArmyDeleted)
    {
        var ruleset = EconomyTestbed.Ruleset;
        var units = new UnitSlot[unitCount];
        for (var i = 0; i < unitCount; i++)
        {
            units[i] = new UnitSlot(i + 1, "light_infantry", 1_000, 6, $"Mercenary {i}");
        }

        var army = Army(money: 0, supplyTons: 10_000, units);

        var result = MercenaryDesertion.BillArmy(army, ruleset);

        Assert.Equal(unitCount - expectedSurvivors, result.DesertedUnitCount); // deserted = ceil(n/2).

        if (expectArmyDeleted)
        {
            Assert.Null(result.Army);
        }
        else
        {
            Assert.NotNull(result.Army);
            Assert.Equal(expectedSurvivors, result.Army!.Units.Count);
        }
    }
}
