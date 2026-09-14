using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Supply as a purchased economy — <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and
/// purses", Done-when 5 and 9. Revised against <c>docs/design-audit.md</c> Q9, now answered from the
/// user's own play experience: resupplying at a city the buying army's (or fleet's) nation <em>owns</em>
/// is free; buying at a city it does not own costs money.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Free, own-city case [confirmed]</strong>: <c>galatia-elimination-and-city-resupply-confirmed.md</c>
/// shows Army 0 resupplying at its own nation's Mediolanum across three sampled frames with both the
/// army's money (256) and the national treasury (−818) unchanged. That is a pure ton transfer, city stock
/// to army/fleet stock, with no talents changing hands at all.
/// </para>
/// <para>
/// <strong>Paid, foreign-city case [confirmed debit, credit destination [open]]</strong>: the code's
/// <c>amount / 5</c> constant (<see cref="EconomyRules.SupplyTonsPerTalent"/>) debits the buyer. Where
/// those talents end up — the selling city's owner, or nowhere — is not yet confirmed
/// (<c>docs/design-audit.md</c> Q9's remaining evidence gap). This method implements the debit exactly as
/// specified and credits nobody: inventing a destination is exactly what the task's hazard note forbids.
/// </para>
/// <para>
/// <strong>Purse model flag (<c>docs/design-audit.md</c> Q4)</strong>: under
/// <see cref="EconomyPurseModel.PerUnitPurses"/> (<c>classical-faithful</c>) the debit lands on the
/// buying army's or fleet's own purse, through <see cref="PurseAccounting.Credit"/> so the 1,000-talent
/// cap (Done-when 6) is enforced on this path exactly as on every other purse-crediting path. Under
/// <see cref="EconomyPurseModel.CentralTreasury"/> (<c>improved</c>) the same debit lands on the buying
/// nation's treasury instead, with no per-unit purse touched at all — Done-when 9's assertion that the two
/// paths cannot silently collapse into one.
/// </para>
/// </remarks>
public static class SupplyPurchase
{
    /// <summary>The result of one supply purchase.</summary>
    /// <param name="Army">The buying army, with its supply stock increased and (if paid, per-unit purses) its purse debited.</param>
    /// <param name="City">The selling city, with its supply stock decreased.</param>
    /// <param name="BuyerNation">
    /// The buying nation, with its treasury debited under <see cref="EconomyPurseModel.CentralTreasury"/>;
    /// unchanged (the same instance passed in) under <see cref="EconomyPurseModel.PerUnitPurses"/>.
    /// </param>
    /// <param name="TalentsPaid">The talents debited — 0 for a free, own-city purchase.</param>
    /// <param name="WasFreeOwnCity">Whether this purchase took the free, own-city path.</param>
    public sealed record ArmyResult(ArmyState Army, CityState City, NationState BuyerNation, int TalentsPaid, bool WasFreeOwnCity);

    /// <summary>Buys <paramref name="tons"/> of supply for an army from a city.</summary>
    /// <param name="army">The buying army.</param>
    /// <param name="sellingCity">The city selling the supply.</param>
    /// <param name="buyerNation">
    /// The buying army's nation — read for <see cref="NationState.Id"/> (matched against
    /// <see cref="CityState.Owner"/> to decide free-vs-paid) and, under
    /// <see cref="EconomyPurseModel.CentralTreasury"/>, debited directly.
    /// </param>
    /// <param name="tons">Tons to buy. Must be positive.</param>
    /// <param name="ruleset">Supplies every constant and the <see cref="RulesetFlags.EconomyPurses"/> flag — never a C# literal.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tons"/> is not positive.</exception>
    /// <exception cref="ArgumentException">The city does not hold <paramref name="tons"/> tons of supply to sell.</exception>
    public static ArmyResult BuyForArmy(ArmyState army, CityState sellingCity, NationState buyerNation, int tons, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(sellingCity);
        ArgumentNullException.ThrowIfNull(buyerNation);
        ArgumentNullException.ThrowIfNull(ruleset);
        if (tons <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tons), tons, "Must buy a positive number of tons.");
        }

        if (sellingCity.SupplyTons < tons)
        {
            throw new ArgumentException(
                $"City '{sellingCity.Id}' holds only {sellingCity.SupplyTons} tons of supply, cannot sell {tons}.",
                nameof(tons));
        }

        var isOwnCity = string.Equals(sellingCity.Owner, army.Nation, StringComparison.Ordinal);
        var talents = isOwnCity ? 0 : tons / ruleset.Economy.SupplyTonsPerTalent;

        var updatedCity = sellingCity with { SupplyTons = sellingCity.SupplyTons - tons };
        var updatedArmy = army with { SupplyTons = army.SupplyTons + tons };
        var updatedNation = buyerNation;

        if (talents > 0)
        {
            if (ruleset.Flags.EconomyPurses == EconomyPurseModel.CentralTreasury)
            {
                updatedNation = buyerNation with { Treasury = buyerNation.Treasury - talents };
            }
            else
            {
                updatedArmy = updatedArmy with { Money = PurseAccounting.Credit(updatedArmy.Money, -talents, ruleset) };
            }
        }

        return new ArmyResult(updatedArmy, updatedCity, updatedNation, talents, isOwnCity);
    }

    /// <summary>The result of one fleet supply purchase — the naval twin of <see cref="ArmyResult"/>.</summary>
    public sealed record FleetResult(FleetState Fleet, CityState City, NationState BuyerNation, int TalentsPaid, bool WasFreeOwnCity);

    /// <summary>Buys <paramref name="tons"/> of supply for a fleet from a city. See <see cref="BuyForArmy"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tons"/> is not positive.</exception>
    /// <exception cref="ArgumentException">The city does not hold <paramref name="tons"/> tons of supply to sell.</exception>
    public static FleetResult BuyForFleet(FleetState fleet, CityState sellingCity, NationState buyerNation, int tons, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(sellingCity);
        ArgumentNullException.ThrowIfNull(buyerNation);
        ArgumentNullException.ThrowIfNull(ruleset);
        if (tons <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tons), tons, "Must buy a positive number of tons.");
        }

        if (sellingCity.SupplyTons < tons)
        {
            throw new ArgumentException(
                $"City '{sellingCity.Id}' holds only {sellingCity.SupplyTons} tons of supply, cannot sell {tons}.",
                nameof(tons));
        }

        var isOwnCity = string.Equals(sellingCity.Owner, fleet.Nation, StringComparison.Ordinal);
        var talents = isOwnCity ? 0 : tons / ruleset.Economy.SupplyTonsPerTalent;

        var updatedCity = sellingCity with { SupplyTons = sellingCity.SupplyTons - tons };
        var updatedFleet = fleet with { SupplyTons = fleet.SupplyTons + tons };
        var updatedNation = buyerNation;

        if (talents > 0)
        {
            if (ruleset.Flags.EconomyPurses == EconomyPurseModel.CentralTreasury)
            {
                updatedNation = buyerNation with { Treasury = buyerNation.Treasury - talents };
            }
            else
            {
                updatedFleet = updatedFleet with { Money = PurseAccounting.Credit(updatedFleet.Money, -talents, ruleset) };
            }
        }

        return new FleetResult(updatedFleet, updatedCity, updatedNation, talents, isOwnCity);
    }
}
