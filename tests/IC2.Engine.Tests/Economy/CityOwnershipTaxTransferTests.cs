using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 7: the city-ownership tax-base and wealth transfer, for T17 to call at a
/// capture or defection.
/// </summary>
public sealed class CityOwnershipTaxTransferTests
{
    /// <summary>
    /// The report's own Naupactus capture, exactly (<c>1_rome_270_winter_7_b.sav → 1_rome_270_winter_9_b.sav</c>,
    /// Greece → Illyria). After siege damage Naupactus has tribute 15, population 25, maximum 30, so its
    /// contribution is <c>15 × 25 / 30 = 12</c>.
    /// </summary>
    [Fact]
    public void ReproducesTheNaupactusCaptureExactly()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var naupactus = new CityState(
            "naupactus", "Naupactus", 0, 0, "illyria", "illyria",
            Loyalty: 40, SupplyTons: 0, FortificationCode: 0,
            PopulationThousands: 25, MaxPopulationThousands: 30, Tribute: 15,
            UnderSiege: false, Garrison: ValueList<UnitSlot>.Empty);

        var illyria = MakeNation("illyria", treasury: -2021, taxBase: 396, wealth: 768_000);
        var greece = MakeNation("greece", treasury: 0, taxBase: 2296, wealth: 2_490_000);

        var (newOwner, oldOwner) = CityOwnershipTaxTransfer.Transfer(naupactus, illyria, greece, ruleset);

        Assert.Equal(444, newOwner.TaxBase); // 396 + 12*4 = 444.
        Assert.Equal(2248, oldOwner.TaxBase); // 2296 - 48 = 2248.
        Assert.Equal(843_000, newOwner.Wealth); // 768,000 + 25*3000 = 843,000.
        Assert.Equal(2_415_000, oldOwner.Wealth); // 2,490,000 - 75,000 = 2,415,000.

        // This helper covers only the tax-base and wealth terms; the treasury credit, unity and city
        // count are T17's own to apply.
        Assert.Equal(illyria.Treasury, newOwner.Treasury);
        Assert.Equal(greece.Treasury, oldOwner.Treasury);
    }

    [Fact]
    public void TheOldOwnerLosesExactlyWhatTheNewOwnerGains()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var city = new CityState(
            "c1", "City", 0, 0, "b", "b", 80, 0, 0, 40, 50, 10, false, ValueList<UnitSlot>.Empty);
        var newOwner = MakeNation("a", 0, taxBase: 100, wealth: 500_000);
        var oldOwner = MakeNation("b", 0, taxBase: 200, wealth: 800_000);

        var (updatedNewOwner, updatedOldOwner) = CityOwnershipTaxTransfer.Transfer(city, newOwner, oldOwner, ruleset);

        var taxGain = updatedNewOwner.TaxBase - newOwner.TaxBase;
        var taxLoss = oldOwner.TaxBase - updatedOldOwner.TaxBase;
        Assert.Equal(taxGain, taxLoss);

        var wealthGain = updatedNewOwner.Wealth - newOwner.Wealth;
        var wealthLoss = oldOwner.Wealth - updatedOldOwner.Wealth;
        Assert.Equal(wealthGain, wealthLoss);
    }

    private static NationState MakeNation(string id, int treasury, int taxBase, int wealth) => new(
        Id: id, Name: id, ColorHex: "#000", LeaderName: "Leader", CapitalCityId: null,
        Control: SeatControl.Ai, Personality: null,
        Treasury: treasury, Unity: 600, Wealth: wealth, TaxBase: taxBase, TaxRatePercent: 15,
        MobilizedPercent: 0, Population: 100, PopulationAtStart: 100, TreasuryAtStart: treasury,
        CityCountAtStart: 1, RecruitmentSlots: ValueList<RecruitmentSlot>.Empty, Eliminated: false);
}
