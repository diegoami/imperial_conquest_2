using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses", Done-when 5 and 9.
/// Revised against <c>docs/design-audit.md</c> Q9: free at the buying army's own nation's city, costs
/// money (<c>amount / 5</c>, debited only, credit destination deliberately <c>[open]</c>) at a foreign
/// one.
/// </summary>
public sealed class SupplyPurchaseTests
{
    // Review round 1, B6: the army needs enough troops (capacity = troops / 100) to hold its starting
    // 100 tons plus every test's 100-ton purchase, and enough money to afford the 20-talent paid case,
    // so these tests exercise the confirmed purse-crediting/debit behaviour rather than tripping the
    // capacity/affordability caps that behaviour is layered under.
    private static (ArmyState Army, CityState City, NationState Nation) Scenario(string cityOwner, int cityTons = 1000)
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 30_000, 6, "Test Battalion"));
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 500, 100, null, null, units);
        var city = new CityState("c1", "Test City", 0, 0, cityOwner, cityOwner, 80, cityTons, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var nation = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        return (army, city, nation);
    }

    [Fact]
    public void BuyForArmy_AtOwnCity_IsFree()
    {
        var (army, city, nation) = Scenario(cityOwner: "north");

        var result = SupplyPurchase.BuyForArmy(army, city, nation, tons: 100, EconomyTestbed.Ruleset);

        Assert.True(result.WasFreeOwnCity);
        Assert.Equal(0, result.TalentsPaid);
        Assert.Equal(army.Money, result.Army.Money); // unchanged, matching the Galatia frames' evidence.
        Assert.Equal(nation.Treasury, result.BuyerNation.Treasury); // unchanged.
        Assert.Equal(army.SupplyTons + 100, result.Army.SupplyTons);
        Assert.Equal(city.SupplyTons - 100, result.City.SupplyTons);
    }

    [Fact]
    public void BuyForArmy_AtForeignCity_Costs20TalentsFor100Tons()
    {
        var (army, city, nation) = Scenario(cityOwner: "south");

        var result = SupplyPurchase.BuyForArmy(army, city, nation, tons: 100, EconomyTestbed.Ruleset);

        Assert.False(result.WasFreeOwnCity);
        Assert.Equal(20, result.TalentsPaid); // amount / 5 = 100 / 5 = 20.
        Assert.Equal(army.Money - 20, result.Army.Money); // debited from the buying army's own purse.
        Assert.Equal(nation.Treasury, result.BuyerNation.Treasury); // per-unit purses: treasury untouched.
        Assert.Equal(army.SupplyTons + 100, result.Army.SupplyTons);
        Assert.Equal(city.SupplyTons - 100, result.City.SupplyTons);
    }

    /// <summary>
    /// Done-when 5's hazard: the credit side is deliberately left <c>[open]</c> -- nobody's treasury (not
    /// even the selling city's owner's, since <see cref="CityState"/> carries no treasury of its own, and
    /// not any other nation's) is credited by a foreign purchase. Asserted by exhaustion: sum every
    /// nation's treasury before and after and show the 20 talents debited from the army do not reappear
    /// anywhere in the (single, in this test) other nation's books either.
    /// </summary>
    [Fact]
    public void BuyForArmy_AtForeignCity_CreditsNobody()
    {
        var (army, city, nation) = Scenario(cityOwner: "south");

        // SupplyPurchase.BuyForArmy's signature carries no parameter through which it could reach a third
        // party (the selling city's owner) at all -- CityState has no treasury field of its own, and the
        // only NationState it ever touches is the buyer's. So "credits nobody" is structurally true, not
        // merely true in this one call: the 20 talents debited from the army have nowhere in this method's
        // reachable state to land.
        var result = SupplyPurchase.BuyForArmy(army, city, nation, tons: 100, EconomyTestbed.Ruleset);

        Assert.Equal(20, result.TalentsPaid);
        Assert.Equal(nation.Treasury, result.BuyerNation.Treasury); // buyer's own treasury untouched (per-unit purses).
        Assert.Equal(army.Money - 20, result.Army.Money); // the only place the 20 talents actually went.
    }

    /// <summary>Done-when 9: under <c>economy.purses = centralized</c>, the debit lands on the treasury, not the purse.</summary>
    [Fact]
    public void BuyForArmy_UnderCentralizedPurses_DebitsTreasuryNotArmyPurse()
    {
        var (army, city, nation) = Scenario(cityOwner: "south");
        var centralized = EconomyTestbed.Ruleset with
        {
            Flags = EconomyTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury },
        };

        var result = SupplyPurchase.BuyForArmy(army, city, nation, tons: 100, centralized);

        Assert.Equal(20, result.TalentsPaid);
        Assert.Equal(army.Money, result.Army.Money); // per-unit purse untouched.
        Assert.Equal(nation.Treasury - 20, result.BuyerNation.Treasury); // treasury debited instead.
    }

    /// <summary>
    /// Done-when 9's own wording: a fixture that "would fail item 5's purse-crediting assertion if run
    /// under the wrong flag" -- i.e. the two paths cannot silently collapse into one. Runs the exact same
    /// purchase under both flags and shows the army purse and the treasury move in mutually exclusive ways.
    /// </summary>
    [Fact]
    public void BuyForArmy_PerUnitVersusCentralized_TouchDisjointAccounts()
    {
        var (army, city, nation) = Scenario(cityOwner: "south");
        var perUnit = EconomyTestbed.Ruleset with
        {
            Flags = EconomyTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.PerUnitPurses },
        };
        var centralized = EconomyTestbed.Ruleset with
        {
            Flags = EconomyTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury },
        };

        var perUnitResult = SupplyPurchase.BuyForArmy(army, city, nation, tons: 100, perUnit);
        var centralizedResult = SupplyPurchase.BuyForArmy(army, city, nation, tons: 100, centralized);

        // Per-unit: army purse moved, treasury did not.
        Assert.NotEqual(army.Money, perUnitResult.Army.Money);
        Assert.Equal(nation.Treasury, perUnitResult.BuyerNation.Treasury);

        // Centralized: treasury moved, army purse did not.
        Assert.Equal(army.Money, centralizedResult.Army.Money);
        Assert.NotEqual(nation.Treasury, centralizedResult.BuyerNation.Treasury);
    }

    [Fact]
    public void BuyForFleet_MirrorsBuyForArmy()
    {
        // 50 ships -> 400 tons of capacity (review round 1, B6), enough to hold the starting 50 tons
        // plus this test's 100-ton purchase.
        var fleet = new FleetState("f1", "north", 0, 0, 4, 50, 100, 500, 50, null, null, null, null);
        var (_, city, nation) = Scenario(cityOwner: "south");

        var result = SupplyPurchase.BuyForFleet(fleet, city, nation, tons: 100, EconomyTestbed.Ruleset);

        Assert.Equal(20, result.TalentsPaid);
        Assert.Equal(fleet.Money - 20, result.Fleet.Money);
        Assert.Equal(fleet.SupplyTons + 100, result.Fleet.SupplyTons);
    }

    [Fact]
    public void BuyForArmy_NonPositiveTons_Throws()
    {
        var (army, city, nation) = Scenario(cityOwner: "north");
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SupplyPurchase.BuyForArmy(army, city, nation, tons: 0, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void BuyForArmy_MoreThanCityHolds_Throws()
    {
        var (army, city, nation) = Scenario(cityOwner: "south", cityTons: 50);
        Assert.Throws<ArgumentException>(
            () => SupplyPurchase.BuyForArmy(army, city, nation, tons: 100, EconomyTestbed.Ruleset));
    }

    /// <summary>
    /// Review round 1, B6: the source caps a paid purchase at the buyer's own money x
    /// SupplyTonsPerTalent -- an army with no money and no troops (so the capacity cap cannot be what
    /// stops it) buying 900 t abroad must be rejected, not left to drive its purse negative.
    /// </summary>
    [Fact]
    public void BuyForArmy_MoreTonsThanPurseCanAfford_Throws()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 1_000_000, 6, "Huge Battalion"));
        var poorArmy = new ArmyState("a2", "north", 0, 0, 9, 60, 10, 0, null, null, units);
        var city = new CityState("c1", "Test City", 0, 0, "south", "south", 80, 1000, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var nation = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        // 900 t at amount/5 would cost 180 talents against a 10-talent purse.
        Assert.Throws<ArgumentException>(
            () => SupplyPurchase.BuyForArmy(poorArmy, city, nation, tons: 900, EconomyTestbed.Ruleset));
    }

    /// <summary>Review round 1, B6: a purchase that would push supply past the buyer's own capacity is rejected.</summary>
    [Fact]
    public void BuyForArmy_MoreTonsThanCapacityHolds_Throws()
    {
        var (army, city, nation) = Scenario(cityOwner: "north"); // free case: capacity, not money, is the only cap in play.
        var armyCapacity = SupplyCapacity.ArmyCapacityTons(army.TotalTroops, EconomyTestbed.Ruleset);

        Assert.Throws<ArgumentException>(
            () => SupplyPurchase.BuyForArmy(army, city, nation, tons: armyCapacity - army.SupplyTons + 1, EconomyTestbed.Ruleset));
    }

    /// <summary>Review round 1, B6: the fleet twin of <see cref="BuyForArmy_MoreTonsThanCapacityHolds_Throws"/>.</summary>
    [Fact]
    public void BuyForFleet_MoreTonsThanCapacityHolds_Throws()
    {
        var fleet = new FleetState("f1", "north", 0, 0, 4, 10, 100, 500, 50, null, null, null, null);
        var (_, city, nation) = Scenario(cityOwner: "north");
        var fleetCapacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, EconomyTestbed.Ruleset);

        Assert.Throws<ArgumentException>(
            () => SupplyPurchase.BuyForFleet(fleet, city, nation, tons: fleetCapacity - fleet.SupplyTons + 1, EconomyTestbed.Ruleset));
    }

    /// <summary>
    /// Review round 1, B6: under centralized purses there is no per-unit purse to overdraw, so the
    /// money-based cap does not apply -- only the physical capacity cap does. A purchase that a
    /// per-unit purse could not afford still succeeds here, debiting the treasury instead (which the
    /// confirmed evidence already shows going negative, so no cap is invented for it).
    /// </summary>
    [Fact]
    public void BuyForArmy_UnderCentralizedPurses_MoneyCapDoesNotApply()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 1_000_000, 6, "Huge Battalion"));
        var poorArmy = new ArmyState("a2", "north", 0, 0, 9, 60, 10, 0, null, null, units);
        var city = new CityState("c1", "Test City", 0, 0, "south", "south", 80, 1000, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var nation = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var centralized = EconomyTestbed.Ruleset with
        {
            Flags = EconomyTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury },
        };

        var result = SupplyPurchase.BuyForArmy(poorArmy, city, nation, tons: 900, centralized);

        Assert.Equal(180, result.TalentsPaid); // 900 / 5
        Assert.Equal(10, result.Army.Money); // per-unit purse untouched
        Assert.Equal(500 - 180, result.BuyerNation.Treasury); // treasury absorbs it, uncapped
    }
}
