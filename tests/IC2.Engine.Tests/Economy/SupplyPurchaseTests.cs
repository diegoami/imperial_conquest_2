using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses" Done-when 5 and 9, and "T38 Supply
/// dialog follow-ups, treasury ↔ purse transfers, and automatic resupply" (issue #78) Done-when 1, 3, 4
/// and 5. Revised against <c>docs/design-audit.md</c> Q9: free at the buying army's own nation's city,
/// costs money (<c>amount / 5</c>) at a foreign one, with the seller now paid and every cap a clamp,
/// never a rejection.
/// </summary>
public sealed class SupplyPurchaseTests
{
    // Review round 1, B6: the army needs enough troops (capacity = troops / 100) to hold its starting
    // 100 tons plus every test's 100-ton purchase, and enough money to afford the 20-talent paid case,
    // so these tests exercise the confirmed purse-crediting/debit behaviour rather than tripping the
    // capacity/affordability caps that behaviour is layered under.
    private static (ArmyState Army, CityState City, NationState BuyerNation, NationState SellerNation) Scenario(
        string cityOwner, int cityTons = 1000)
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 30_000, 6, "Test Battalion"));
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 500, 100, null, null, units);
        var city = new CityState("c1", "Test City", 0, 0, cityOwner, cityOwner, 80, cityTons, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var buyerNation = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var sellerNation = string.Equals(cityOwner, "north", StringComparison.Ordinal)
            ? buyerNation
            : new NationState(cityOwner, cityOwner, "#000", "Leader", null, SeatControl.Ai, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        return (army, city, buyerNation, sellerNation);
    }

    [Fact]
    public void BuyForArmy_AtOwnCity_IsFree()
    {
        var (army, city, buyer, seller) = Scenario(cityOwner: "north");

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, EconomyTestbed.Ruleset);

        Assert.True(result.WasFreeOwnCity);
        Assert.Equal(0, result.TalentsPaid);
        Assert.Equal(100, result.AdmittedTons);
        Assert.Equal(army.Money, result.Army.Money); // unchanged, matching the Galatia frames' evidence.
        Assert.Equal(buyer.Treasury, result.BuyerNation.Treasury); // unchanged.
        Assert.Equal(army.SupplyTons + 100, result.Army.SupplyTons);
        Assert.Equal(city.SupplyTons - 100, result.City.SupplyTons);
    }

    [Fact]
    public void BuyForArmy_AtForeignCity_Costs20TalentsFor100Tons()
    {
        var (army, city, buyer, seller) = Scenario(cityOwner: "south");

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, EconomyTestbed.Ruleset);

        Assert.False(result.WasFreeOwnCity);
        Assert.Equal(100, result.AdmittedTons);
        Assert.Equal(20, result.TalentsPaid); // amount / 5 = 100 / 5 = 20.
        Assert.Equal(army.Money - 20, result.Army.Money); // debited from the buying army's own purse.
        Assert.Equal(buyer.Treasury, result.BuyerNation.Treasury); // per-unit purses: treasury untouched.
        Assert.Equal(army.SupplyTons + 100, result.Army.SupplyTons);
        Assert.Equal(city.SupplyTons - 100, result.City.SupplyTons);
    }

    /// <summary>T38 (#78, Done-when 5): the credit side T08 left open is now paid to the selling city's owner.</summary>
    [Fact]
    public void BuyForArmy_AtForeignCity_CreditsTheSellingCitysOwner()
    {
        var (army, city, buyer, seller) = Scenario(cityOwner: "south");

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, EconomyTestbed.Ruleset);

        Assert.Equal(20, result.TalentsPaid);
        Assert.Equal(buyer.Treasury, result.BuyerNation.Treasury); // buyer's own treasury untouched (per-unit purses).
        Assert.Equal(army.Money - 20, result.Army.Money); // debited from the buyer's purse.
        Assert.Equal(seller.Treasury + 20, result.SellingCityNation.Treasury); // credited to the seller's treasury.
    }

    /// <summary>Done-when 9: under <c>economy.purses = centralized</c>, the debit lands on the treasury, not the purse.</summary>
    [Fact]
    public void BuyForArmy_UnderCentralizedPurses_DebitsTreasuryNotArmyPurse()
    {
        var (army, city, buyer, seller) = Scenario(cityOwner: "south");
        var centralized = EconomyTestbed.Ruleset with
        {
            Flags = EconomyTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury },
        };

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, centralized);

        Assert.Equal(20, result.TalentsPaid);
        Assert.Equal(army.Money, result.Army.Money); // per-unit purse untouched.
        Assert.Equal(buyer.Treasury - 20, result.BuyerNation.Treasury); // treasury debited instead.
        Assert.Equal(seller.Treasury + 20, result.SellingCityNation.Treasury); // seller still credited either way.
    }

    /// <summary>
    /// Done-when 9's own wording: a fixture that "would fail item 5's purse-crediting assertion if run
    /// under the wrong flag" -- i.e. the two paths cannot silently collapse into one. Runs the exact same
    /// purchase under both flags and shows the army purse and the treasury move in mutually exclusive ways.
    /// </summary>
    [Fact]
    public void BuyForArmy_PerUnitVersusCentralized_TouchDisjointAccounts()
    {
        var (army, city, buyer, seller) = Scenario(cityOwner: "south");
        var perUnit = EconomyTestbed.Ruleset with
        {
            Flags = EconomyTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.PerUnitPurses },
        };
        var centralized = EconomyTestbed.Ruleset with
        {
            Flags = EconomyTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury },
        };

        var perUnitResult = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, perUnit);
        var centralizedResult = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, centralized);

        // Per-unit: army purse moved, treasury did not.
        Assert.NotEqual(army.Money, perUnitResult.Army.Money);
        Assert.Equal(buyer.Treasury, perUnitResult.BuyerNation.Treasury);

        // Centralized: treasury moved, army purse did not.
        Assert.Equal(army.Money, centralizedResult.Army.Money);
        Assert.NotEqual(buyer.Treasury, centralizedResult.BuyerNation.Treasury);
    }

    [Fact]
    public void BuyForFleet_MirrorsBuyForArmy()
    {
        // 50 ships -> 400 tons of capacity (review round 1, B6), enough to hold the starting 50 tons
        // plus this test's 100-ton purchase.
        var fleet = new FleetState("f1", "north", 0, 0, 4, 50, 100, 500, 50, null, null, null, null);
        var (_, city, buyer, seller) = Scenario(cityOwner: "south");

        var result = SupplyPurchase.BuyForFleet(fleet, city, buyer, seller, tons: 100, EconomyTestbed.Ruleset);

        Assert.Equal(20, result.TalentsPaid);
        Assert.Equal(fleet.Money - 20, result.Fleet.Money);
        Assert.Equal(fleet.SupplyTons + 100, result.Fleet.SupplyTons);
        Assert.Equal(seller.Treasury + 20, result.SellingCityNation.Treasury);
    }

    [Fact]
    public void BuyForArmy_NonPositiveTons_Throws()
    {
        var (army, city, buyer, seller) = Scenario(cityOwner: "north");
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 0, EconomyTestbed.Ruleset));
    }

    /// <summary>
    /// T38 (#78, Done-when 1): "one clamping model" -- a request for more tons than the city holds is no
    /// longer rejected, it is admitted at whatever the city's stock allows.
    /// </summary>
    [Fact]
    public void BuyForArmy_MoreThanCityHolds_ClampsToTheCitysStock()
    {
        var (army, city, buyer, seller) = Scenario(cityOwner: "south", cityTons: 50);

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, EconomyTestbed.Ruleset);

        Assert.Equal(50, result.AdmittedTons); // clamped to the city's 50 t, not thrown.
        Assert.Equal(0, result.City.SupplyTons);
        Assert.Equal(army.SupplyTons + 50, result.Army.SupplyTons);
        Assert.Equal(10, result.TalentsPaid); // 50 / 5 = 10, charged for what was admitted.
    }

    /// <summary>
    /// Done-when 1's own worked example: an army with 79 t of room, requesting 100 t at a city holding
    /// 90 t, gets 79 -- the room is the binding cap here, not the city's stock.
    /// </summary>
    [Fact]
    public void BuyForArmy_RequestExceedsBothStockAndRoom_ClampsToTheSmallerRoom()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 7_800, 6, "Test Battalion")); // capacity = 7800/100 + 1 = 79.
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 500, 0, null, null, units);
        var city = new CityState("c1", "Test City", 0, 0, "south", "south", 80, 90, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var buyer = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var seller = new NationState("south", "South", "#000", "Leader", null, SeatControl.Ai, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        Assert.Equal(79, SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, EconomyTestbed.Ruleset));

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, EconomyTestbed.Ruleset);

        Assert.Equal(79, result.AdmittedTons);
        Assert.Equal(11, result.City.SupplyTons); // 90 - 79.
    }

    /// <summary>Done-when 1: a request that clamps all the way to 0 moves nothing and succeeds, never throws.</summary>
    [Fact]
    public void BuyForArmy_RequestClampsToZero_MovesNothingAndSucceeds()
    {
        var (army, city, buyer, seller) = Scenario(cityOwner: "south", cityTons: 0);

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, EconomyTestbed.Ruleset);

        Assert.Equal(0, result.AdmittedTons);
        Assert.Equal(0, result.TalentsPaid);
        Assert.Equal(army.Money, result.Army.Money);
        Assert.Equal(army.SupplyTons, result.Army.SupplyTons);
    }

    /// <summary>
    /// Review round 1, B6 (fixture wording corrected per review round 2, NB3): the source caps a paid
    /// purchase at the buyer's own money x SupplyTonsPerTalent. This army has plenty of troops (so the
    /// capacity cap cannot be what stops it, only the money one) but a 10-talent purse; buying 900 t
    /// abroad admits only 50 t (T38 #78, Done-when 1: clamped, not rejected).
    /// </summary>
    [Fact]
    public void BuyForArmy_MoreTonsThanPurseCanAfford_ClampsToWhatThePurseCanAfford()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 1_000_000, 6, "Huge Battalion"));
        var poorArmy = new ArmyState("a2", "north", 0, 0, 9, 60, 10, 0, null, null, units);
        var city = new CityState("c1", "Test City", 0, 0, "south", "south", 80, 1000, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var buyer = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var seller = new NationState("south", "South", "#000", "Leader", null, SeatControl.Ai, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        // 900 t against a 10-talent purse: 10 x 5 = 50 t is the most it can afford.
        var result = SupplyPurchase.BuyForArmy(poorArmy, city, buyer, seller, tons: 900, EconomyTestbed.Ruleset);

        Assert.Equal(50, result.AdmittedTons);
        Assert.Equal(10, result.TalentsPaid);
        Assert.Equal(0, result.Army.Money);
    }

    /// <summary>
    /// Review round 2, NB1: the money cap must compare whole tons against whole talents
    /// (<c>tons &gt; money x SupplyTonsPerTalent</c>), not a truncated talent count -- the two differ by
    /// up to <c>SupplyTonsPerTalent - 1</c> tons. An army with a 10-talent purse can afford exactly 50 t
    /// (<c>10 x 5</c>); a request for 51 admits only the affordable 50.
    /// </summary>
    [Fact]
    public void BuyForArmy_ExactlyAffordableTons_Succeeds_OneMoreClampsToTheSameAffordableAmount()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 1_000_000, 6, "Huge Battalion"));
        var army = new ArmyState("a2", "north", 0, 0, 9, 60, 10, 0, null, null, units);
        var city = new CityState("c1", "Test City", 0, 0, "south", "south", 80, 1000, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var buyer = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var seller = new NationState("south", "South", "#000", "Leader", null, SeatControl.Ai, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 50, EconomyTestbed.Ruleset);
        Assert.Equal(10, result.TalentsPaid); // 50 / 5 = 10, exactly the purse.
        Assert.Equal(0, result.Army.Money);

        var oneMore = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 51, EconomyTestbed.Ruleset);
        Assert.Equal(50, oneMore.AdmittedTons); // clamped to the same affordable 50, not 51.
        Assert.Equal(10, oneMore.TalentsPaid);
    }

    /// <summary>
    /// Review round 3 (R1's resolution): the dialog capacity cap is now confirmed on both paths and is a
    /// clamp, not a rejection (<c>supply-capacity-rounding.md</c>) -- a request for more than the room
    /// allows is admitted at whatever room is left, not thrown away. This buys from a foreign city, so the
    /// clamped amount is also what gets charged for (<c>admittedTons / 5</c>, not the requested tons).
    /// </summary>
    [Fact]
    public void BuyForArmy_MoreTonsThanCapacityHolds_ClampsToTheRoomInstead()
    {
        var (army, city, buyer, seller) = Scenario(cityOwner: "south");
        var dialogCapacity = SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, EconomyTestbed.Ruleset);
        var room = dialogCapacity - army.SupplyTons;

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: room + 50, EconomyTestbed.Ruleset);

        Assert.Equal(dialogCapacity, result.Army.SupplyTons); // clamped to exactly the dialog capacity, not thrown.
        Assert.Equal(room / EconomyTestbed.Ruleset.Economy.SupplyTonsPerTalent, result.TalentsPaid); // charged for the admitted tons only.
        Assert.Equal(army.Money - result.TalentsPaid, result.Army.Money);
        Assert.Equal(city.SupplyTons - room, result.City.SupplyTons);
    }

    /// <summary>Review round 1, B6 / round 3: the fleet twin of <see cref="BuyForArmy_MoreTonsThanCapacityHolds_ClampsToTheRoomInstead"/>, plus the same check at the fleet's own city, to cover "a 10-ship fleet -> 80 on both paths".</summary>
    [Theory]
    [InlineData("north")] // own city, free path.
    [InlineData("south")] // foreign city, paid path.
    public void BuyForFleet_MoreTonsThanCapacityHolds_ClampsToTheRoomOnBothPaths(string cityOwner)
    {
        var fleet = new FleetState("f1", "north", 0, 0, 4, 10, 100, 500, 50, null, null, null, null);
        var (_, city, buyer, seller) = Scenario(cityOwner);
        var fleetCapacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, EconomyTestbed.Ruleset);
        Assert.Equal(80, fleetCapacity); // 10 ships * 8, the same value on both paths.
        var room = fleetCapacity - fleet.SupplyTons;

        var result = SupplyPurchase.BuyForFleet(fleet, city, buyer, seller, tons: room + 50, EconomyTestbed.Ruleset);

        Assert.Equal(80, result.Fleet.SupplyTons); // clamped to exactly the fleet capacity, not thrown.
        Assert.Equal(city.SupplyTons - room, result.City.SupplyTons);
    }

    /// <summary>
    /// Review round 3 (R1's resolution): the confirmed Roman transfer (<c>controlled-army-supply-transfer.md</c>,
    /// <c>supplyTransfer.*</c> in the fixtures corpus). The Roman 13-unit roster (48,173 troops,
    /// <c>troops / 100</c> = 481) goes from 403 to 482 t at its own city (Rome, 1,810 -> 1,731 t), reading
    /// exactly 100% on the panel (<c>roman13.supplyPercent</c>) -- one ton past the truncated
    /// <c>ArmyCapacityTons</c>, admitted by the dialog's own <c>troops / 100 + 1</c> cap
    /// (<c>ArmyDialogCapacityTons</c>, <c>supply-capacity-rounding.md</c>). Requests 100 t (a dialog
    /// "+100" step, <c>armyTransfer.stepperIncrements</c> in the corpus) to exercise the clamp itself: only
    /// 79 of the 100 requested tons are admitted, landing exactly on the dialog capacity, not thrown.
    /// </summary>
    [Fact]
    public void BuyForArmy_ReplaysTheConfirmedRomeTransfer_AtOwnCity_ClampsTheRequestTo482()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 48_173, 6, "Roman 13-Unit Roster"));
        var army = new ArmyState("roman13", "rome", 0, 0, 9, 60, 500, 403, null, null, units);
        var city = new CityState("rome-city", "Rome", 0, 0, "rome", "rome", 80, 1810, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var nation = new NationState("rome", "Rome", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        Assert.Equal(481, SupplyCapacity.ArmyCapacityTons(army.TotalTroops, EconomyTestbed.Ruleset)); // the general formula, unchanged.
        Assert.Equal(482, SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, EconomyTestbed.Ruleset)); // the dialog's own cap.

        var result = SupplyPurchase.BuyForArmy(army, city, nation, nation, tons: 100, EconomyTestbed.Ruleset);

        Assert.True(result.WasFreeOwnCity);
        Assert.Equal(0, result.TalentsPaid);
        Assert.Equal(482, result.Army.SupplyTons); // clamped from a 100-ton request to the 79 tons of room.
        Assert.Equal(1731, result.City.SupplyTons); // only the 79 admitted tons left the city, not 100.
        Assert.Equal(100, SupplyCapacity.PercentFull(result.Army.SupplyTons, army.TotalTroops, EconomyTestbed.Ruleset));
    }

    /// <summary>
    /// Review round 3: the same Roman transfer, but at a foreign city with enough money -- the dialog cap
    /// (482) applies identically on the paid path, and the buyer is charged only for the 79 admitted tons
    /// (<c>79 / 5 = 15</c> talents), not the 100 requested.
    /// </summary>
    [Fact]
    public void BuyForArmy_ReplaysTheConfirmedRomeTransfer_AtForeignCityWithEnoughMoney_ClampsTheRequestTo482()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 48_173, 6, "Roman 13-Unit Roster"));
        var army = new ArmyState("roman13", "rome", 0, 0, 9, 60, 500, 403, null, null, units);
        var city = new CityState("carthage-city", "Carthage", 0, 0, "carthage", "carthage", 80, 1810, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var buyer = new NationState("rome", "Rome", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var seller = new NationState("carthage", "Carthage", "#000", "Leader", null, SeatControl.Ai, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, EconomyTestbed.Ruleset);

        Assert.False(result.WasFreeOwnCity);
        Assert.Equal(15, result.TalentsPaid); // 79 / 5 = 15, the admitted tons, not 100 / 5 = 20.
        Assert.Equal(army.Money - 15, result.Army.Money);
        Assert.Equal(482, result.Army.SupplyTons);
        Assert.Equal(1731, result.City.SupplyTons);
        Assert.Equal(seller.Treasury + 15, result.SellingCityNation.Treasury);
    }

    /// <summary>
    /// Review round 3: an army already at its dialog capacity buys nothing -- the room is zero, so the
    /// purchase succeeds with no state change and no charge, rather than throwing.
    /// </summary>
    [Theory]
    [InlineData("rome")] // own city, free path.
    [InlineData("carthage")] // foreign city, paid path.
    public void BuyForArmy_AlreadyAtDialogCapacity_BuysNothing(string cityOwner)
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 48_173, 6, "Roman 13-Unit Roster"));
        var army = new ArmyState("roman13", "rome", 0, 0, 9, 60, 500, 482, null, null, units); // already at the dialog cap.
        var city = new CityState("c1", "City", 0, 0, cityOwner, cityOwner, 80, 1810, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var buyer = new NationState("rome", "Rome", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var seller = string.Equals(cityOwner, "rome", StringComparison.Ordinal)
            ? buyer
            : new NationState(cityOwner, cityOwner, "#000", "Leader", null, SeatControl.Ai, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        Assert.Equal(482, SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, EconomyTestbed.Ruleset));

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 10, EconomyTestbed.Ruleset);

        Assert.Equal(0, result.AdmittedTons);
        Assert.Equal(0, result.TalentsPaid);
        Assert.Equal(482, result.Army.SupplyTons); // unchanged.
        Assert.Equal(1810, result.City.SupplyTons); // unchanged: nothing sold.
        Assert.Equal(army.Money, result.Army.Money); // unchanged: nothing charged.
    }

    /// <summary>
    /// T38 (#78, Done-when 3): the room term is not floored at 0. An army holding 500 t at 40,000 troops
    /// (cap 401 = 40,000/100 + 1) that opens the dialog at its own city ends at 401, and the city gains
    /// the 99-ton surplus back -- the deviation T08 documented (flooring the room at 0) is removed.
    /// </summary>
    [Fact]
    public void BuyForArmy_OverCapacityAtOwnCity_GivesTheSurplusBackToTheCity()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 40_000, 6, "Over-Supplied Battalion"));
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 500, 500, null, null, units); // holds 500, cap 401.
        var city = new CityState("c1", "Test City", 0, 0, "north", "north", 80, 1000, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var nation = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        Assert.Equal(401, SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, EconomyTestbed.Ruleset));

        var result = SupplyPurchase.BuyForArmy(army, city, nation, nation, tons: 100, EconomyTestbed.Ruleset);

        Assert.Equal(-99, result.AdmittedTons); // the request is a positive "+100" press; the room is -99.
        Assert.Equal(401, result.Army.SupplyTons);
        Assert.Equal(1099, result.City.SupplyTons); // the city gains the 99-ton surplus back.
        Assert.Equal(0, result.TalentsPaid); // free path: no money moves.
    }

    /// <summary>
    /// T38 (#78, Done-when 3): the same over-capacity giveback, but through the paid, foreign path -- the
    /// negative amount is refunded at <c>amount / 5</c>, truncating toward zero: <c>99 / 5 = 19</c>.
    /// </summary>
    [Fact]
    public void BuyForArmy_OverCapacityAtForeignCity_IsRefundedForTheSurplusGivenBack()
    {
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 40_000, 6, "Over-Supplied Battalion"));
        var army = new ArmyState("a1", "north", 0, 0, 9, 60, 500, 500, null, null, units); // holds 500, cap 401.
        var city = new CityState("c1", "Test City", 0, 0, "south", "south", 80, 1000, 100, 10, 10, 0, false, ValueList<UnitSlot>.Empty);
        var buyer = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var seller = new NationState("south", "South", "#000", "Leader", null, SeatControl.Ai, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        var result = SupplyPurchase.BuyForArmy(army, city, buyer, seller, tons: 100, EconomyTestbed.Ruleset);

        Assert.Equal(-99, result.AdmittedTons);
        Assert.Equal(401, result.Army.SupplyTons);
        Assert.Equal(1099, result.City.SupplyTons);
        Assert.Equal(-19, result.TalentsPaid); // -99 / 5 truncates toward zero to -19, not -20.
        Assert.Equal(army.Money + 19, result.Army.Money); // the buyer is refunded.
        Assert.Equal(seller.Treasury - 19, result.SellingCityNation.Treasury); // the seller pays the refund back.
    }

    /// <summary>Review round 2, NB4: a caller passing a nation that is not the army's own must be rejected, not silently misattributed.</summary>
    [Fact]
    public void BuyForArmy_BuyerNationMismatchesArmysNation_Throws()
    {
        var (army, city, _, seller) = Scenario(cityOwner: "north");
        var wrongNation = new NationState("south", "South", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        Assert.Throws<ArgumentException>(
            () => SupplyPurchase.BuyForArmy(army, city, wrongNation, seller, tons: 10, EconomyTestbed.Ruleset));
    }

    /// <summary>T38 (#78, Done-when 1): a caller passing a nation that does not own the selling city is also rejected.</summary>
    [Fact]
    public void BuyForArmy_SellingCityNationMismatchesCitysOwner_Throws()
    {
        var (army, city, buyer, _) = Scenario(cityOwner: "south");
        var wrongNation = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        Assert.Throws<ArgumentException>(
            () => SupplyPurchase.BuyForArmy(army, city, buyer, wrongNation, tons: 10, EconomyTestbed.Ruleset));
    }

    /// <summary>Review round 2, NB4: the fleet twin of <see cref="BuyForArmy_BuyerNationMismatchesArmysNation_Throws"/>.</summary>
    [Fact]
    public void BuyForFleet_BuyerNationMismatchesFleetsNation_Throws()
    {
        var fleet = new FleetState("f1", "north", 0, 0, 4, 50, 100, 500, 50, null, null, null, null);
        var (_, city, _, seller) = Scenario(cityOwner: "north");
        var wrongNation = new NationState("south", "South", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);

        Assert.Throws<ArgumentException>(
            () => SupplyPurchase.BuyForFleet(fleet, city, wrongNation, seller, tons: 10, EconomyTestbed.Ruleset));
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
        var buyer = new NationState("north", "North", "#000", "Leader", null, SeatControl.Human, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var seller = new NationState("south", "South", "#000", "Leader", null, SeatControl.Ai, null, 500, 600, 0, 15, 100, 100, 500, 1, false);
        var centralized = EconomyTestbed.Ruleset with
        {
            Flags = EconomyTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury },
        };

        var result = SupplyPurchase.BuyForArmy(poorArmy, city, buyer, seller, tons: 900, centralized);

        Assert.Equal(180, result.TalentsPaid); // 900 / 5
        Assert.Equal(10, result.Army.Money); // per-unit purse untouched
        Assert.Equal(buyer.Treasury - 180, result.BuyerNation.Treasury); // treasury absorbs it, uncapped
    }
}
