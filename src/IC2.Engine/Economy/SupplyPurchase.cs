using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Supply as a purchased economy — <c>docs/task-catalogue.md</c> "T08 Economy, supply, and
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
/// <para>
/// <strong>Purchase caps (review round 1, B6)</strong>: <c>decompiled-unit-map-orders-and-record-fields.md</c>
/// §TAFSupply confirms <c>TAFSupply_ChangeBuyAmount</c> caps a <em>paid</em> purchase at the buyer's
/// <c>money × SupplyTonsPerTalent</c> tons (a talent buys a fixed number of tons, so the buyer cannot ask
/// for more tons than its own money could ever pay for) and at the buyer's own supply capacity
/// (<see cref="SupplyCapacity.ArmyCapacityTons"/> / <see cref="SupplyCapacity.FleetCapacityTons"/>).
/// Neither cap was enforced before review round 1, letting a paid purchase drive a purse negative or a
/// supply stock past capacity; both methods below now reject a paid purchase that would violate either.
/// </para>
/// <para>
/// <strong>The capacity cap is paid-purchase-only (review round 2, R1) — [derived]</strong>. Round 1
/// applied the same <c>troops / ArmySupplyTonsPerTroops</c> cap to the free, own-city case too, and that
/// regresses a confirmed observation: <c>controlled-army-supply-transfer.md</c>'s Roman 13-unit roster
/// (<see cref="Model.ArmyState.TotalTroops"/> 48,173) transfers 79 t at its own city, going from 403 to
/// 482 t (<c>supplyTransfer.*</c> in the fixtures corpus) — and <c>roman13.supplyPercent</c> independently
/// confirms 482 t reads exactly 100% on the panel. <c>troops / 100</c> truncates to 481, one ton short of
/// the observed, legal result. The two sources — the cap formula from <c>TAFSupply_ChangeBuyAmount</c>,
/// and the free-transfer save data — disagree by exactly one ton, and neither says which rounding or
/// which code path produced the extra one. The reconciling reading: <c>design-audit.md</c> Q9 already
/// establishes that the free, own-city case is a <em>different, <c>TArmyToArmy</c>-shaped dialog</em>
/// from the paid <c>TAFSupply</c> purchase this cap's formula was decompiled from — nothing in the cited
/// report says <c>TArmyToArmy</c> enforces the same clamp, and the Roman transfer is direct evidence it
/// does not (or enforces a differently-rounded one). Both methods below therefore skip the capacity check
/// on the free path and apply it only to a paid one, which both keeps the confirmed cap where its evidence
/// actually comes from and admits the confirmed free transfer. <c>SupplyCapacityTests</c>' own DoD 4
/// values (998 for 99,882 troops, 282 for 28,227) are restatements of the formula's arithmetic in
/// <c>decompiled-unit-map-orders-and-record-fields.md</c>, not an independently observed maximum fill the
/// way the Roman transfer is, so they are not evidence the cap must also bind free transfers — they stay
/// exactly as `troops / 100` describes, since nothing here changes <see cref="SupplyCapacity"/> itself.
/// </para>
/// <para>
/// The money-based cap applies only under <see cref="EconomyPurseModel.PerUnitPurses"/>, for a paid
/// (non-own-city) purchase — under <see cref="EconomyPurseModel.CentralTreasury"/> there is no per-unit
/// purse to exceed, and the national treasury itself has no confirmed cap (it is observed negative in the
/// fixtures corpus, see <see cref="PurseAccounting"/>'s own remark), so this is a deliberate
/// <c>[designed]</c> choice not to invent a treasury-side funds cap. It compares whole tons against whole
/// talents (<c>tons &gt; money × SupplyTonsPerTalent</c>, review round 2, NB1) rather than truncating tons
/// to talents first, which previously let a purchase slip through for up to
/// <c>SupplyTonsPerTalent − 1</c> tons more than the buyer could actually pay for.
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
    /// The buying army's own nation. Its <see cref="NationState.Id"/> must equal <paramref name="army"/>'s
    /// <see cref="Model.ArmyState.Nation"/> (review round 2, NB4) — it is read for
    /// <see cref="NationState.Id"/> (matched against <see cref="CityState.Owner"/> to decide free-vs-paid)
    /// and, under <see cref="EconomyPurseModel.CentralTreasury"/>, debited directly, so a caller passing
    /// the wrong nation would silently debit someone else's treasury.
    /// </param>
    /// <param name="tons">Tons to buy. Must be positive.</param>
    /// <param name="ruleset">Supplies every constant and the <see cref="RulesetFlags.EconomyPurses"/> flag — never a C# literal.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tons"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="buyerNation"/> is not <paramref name="army"/>'s own nation; or the city does not
    /// hold <paramref name="tons"/> tons of supply to sell; or (paid case only) the purchase would exceed
    /// the army's supply capacity or, under per-unit purses, its own purse.
    /// </exception>
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

        if (!string.Equals(buyerNation.Id, army.Nation, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{buyerNation.Id}' is not army '{army.Id}''s own nation ('{army.Nation}').",
                nameof(buyerNation));
        }

        if (sellingCity.SupplyTons < tons)
        {
            throw new ArgumentException(
                $"City '{sellingCity.Id}' holds only {sellingCity.SupplyTons} tons of supply, cannot sell {tons}.",
                nameof(tons));
        }

        var isOwnCity = string.Equals(sellingCity.Owner, army.Nation, StringComparison.Ordinal);

        // Review round 2, R1: the capacity cap is confirmed only for the paid path (TAFSupply); the
        // free, own-city resupply is a different dialog (TArmyToArmy, design-audit.md Q9) not shown to
        // share it, and the confirmed Roman transfer (403 -> 482 t, its own city) exceeds troops/100 by
        // a ton -- see the class remarks above.
        if (!isOwnCity)
        {
            var armyCapacity = SupplyCapacity.ArmyCapacityTons(army.TotalTroops, ruleset);
            if (army.SupplyTons + tons > armyCapacity)
            {
                throw new ArgumentException(
                    $"Army '{army.Id}' has {armyCapacity - army.SupplyTons} tons of free supply capacity, cannot buy {tons}.",
                    nameof(tons));
            }
        }

        var talents = isOwnCity ? 0 : tons / ruleset.Economy.SupplyTonsPerTalent;

        // Review round 2, NB1: compare whole tons against whole talents (tons > money * SupplyTonsPerTalent)
        // rather than truncating tons to talents first, which let a purchase through for up to
        // SupplyTonsPerTalent - 1 tons more than the buyer could actually pay for.
        if (!isOwnCity && ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses
            && tons > army.Money * ruleset.Economy.SupplyTonsPerTalent)
        {
            throw new ArgumentException(
                $"Army '{army.Id}' has only {army.Money} talents, cannot pay for {tons} tons.",
                nameof(tons));
        }

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
    /// <exception cref="ArgumentException">
    /// <paramref name="buyerNation"/> is not <paramref name="fleet"/>'s own nation; or the city does not
    /// hold <paramref name="tons"/> tons of supply to sell; or (paid case only) the purchase would exceed
    /// the fleet's supply capacity or, under per-unit purses, its own purse.
    /// </exception>
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

        if (!string.Equals(buyerNation.Id, fleet.Nation, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{buyerNation.Id}' is not fleet '{fleet.Id}''s own nation ('{fleet.Nation}').",
                nameof(buyerNation));
        }

        if (sellingCity.SupplyTons < tons)
        {
            throw new ArgumentException(
                $"City '{sellingCity.Id}' holds only {sellingCity.SupplyTons} tons of supply, cannot sell {tons}.",
                nameof(tons));
        }

        var isOwnCity = string.Equals(sellingCity.Owner, fleet.Nation, StringComparison.Ordinal);

        // Review round 2, R1 (see BuyForArmy and the class remarks): the capacity cap is confirmed only
        // for the paid path.
        if (!isOwnCity)
        {
            var fleetCapacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, ruleset);
            if (fleet.SupplyTons + tons > fleetCapacity)
            {
                throw new ArgumentException(
                    $"Fleet '{fleet.Id}' has {fleetCapacity - fleet.SupplyTons} tons of free supply capacity, cannot buy {tons}.",
                    nameof(tons));
            }
        }

        var talents = isOwnCity ? 0 : tons / ruleset.Economy.SupplyTonsPerTalent;

        // Review round 2, NB1 (see BuyForArmy): compare whole tons against whole talents.
        if (!isOwnCity && ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses
            && tons > fleet.Money * ruleset.Economy.SupplyTonsPerTalent)
        {
            throw new ArgumentException(
                $"Fleet '{fleet.Id}' has only {fleet.Money} talents, cannot pay for {tons} tons.",
                nameof(tons));
        }

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
