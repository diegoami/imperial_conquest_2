using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Supply as a purchased economy — <c>docs/task-catalogue.md</c> "T08 Economy, supply, and
/// purses", Done-when 5 and 9, finished by "T38 Supply dialog follow-ups, treasury ↔ purse transfers,
/// and automatic resupply" (issue #78) Done-when 1, 3, 4 and 5. Revised against <c>docs/design-audit.md</c>
/// Q9, now answered from the user's own play experience: resupplying at a city the buying army's (or
/// fleet's) nation <em>owns</em> is free; buying at a city it does not own costs money — and, per Q9's
/// 2026-09-14 update (<c>supply-capacity-rounding.md</c>), the paid case's remaining evidence gaps (the
/// caps, and the seller's credit) are now settled from the code.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Free, own-city case [confirmed]</strong>: <c>galatia-elimination-and-city-resupply-confirmed.md</c>
/// shows Army 0 resupplying at its own nation's Mediolanum across three sampled frames with both the
/// army's money (256) and the national treasury (−818) unchanged. That is a pure ton transfer, city stock
/// to army/fleet stock, with no talents changing hands at all.
/// </para>
/// <para>
/// <strong>One clamping model, never a rejection for exceeding a cap (T38 #78, Done-when 1)</strong>:
/// <c>supply-capacity-rounding.md</c>'s own pseudocode for both <c>TAFSupply_ChangeSupply</c> (own-city,
/// free) and <c>TAFSupply_ChangeBuyAmount</c>/<c>TAFSupply_TransferSupply</c> (foreign, paid) is one chain
/// of minimums — the request, the provider's stock, the room left under the dialog capacity and, on the
/// paid path only, <c>money × SupplyTonsPerTalent</c> — with no branch that rejects the call for
/// exceeding any one of them. <see cref="BuyForArmy"/> and <see cref="BuyForFleet"/> compute exactly that
/// chain and admit whatever it allows, down to (and, per the next paragraph, below) zero. Only a caller
/// error — the wrong nation passed as the buyer or as the selling city's owner — still throws.
/// </para>
/// <para>
/// <strong>The room term is not floored at 0 (T38 #78, Done-when 3)</strong>: <c>supply-capacity-rounding.md</c>
/// is explicit that neither dialog function floors <c>capacity − currentSupplyTons</c> at zero. An army
/// already past its dialog capacity computes a <em>negative</em> room, so the request is admitted at that
/// negative value — supply flows back from the army to the provider. On the paid path the resulting
/// <c>talents</c> is negative too, and <c>amount / 5</c> truncates toward zero exactly as C#'s built-in
/// integer division does, so a refund of, say, −99 tons is exactly −19 talents, not −20. This is the
/// original's own behaviour, reproduced deliberately, not "fixed".
/// </para>
/// <para>
/// <strong>The result reports what moved (T38 #78, Done-when 4)</strong>: <see cref="ArmyResult"/> and
/// <see cref="FleetResult"/> carry <c>AdmittedTons</c> — the actual transfer, which the clamp above can
/// make smaller than (or, giving supply back, negative relative to) the caller's request — alongside
/// <c>TalentsPaid</c>.
/// </para>
/// <para>
/// <strong>The seller is paid (T38 #78, Done-when 5)</strong>: a foreign purchase credits <c>amount / 5</c>
/// to the selling city's owner's treasury (<c>TAFSupply_TransferSupply</c>, lines 43071-43073) — the credit
/// side T08 left <c>[open]</c> because <c>CityState</c> carries no treasury of its own. This method now
/// takes the selling city's own nation as a parameter so that treasury exists to credit. It is tagged
/// <c>[derived]</c>, code only, per <c>docs/design-audit.md</c> Q9's 2026-09-14 update: no save has a
/// foreign purchase yet.
/// </para>
/// <para>
/// <strong>Purse model flag (<c>docs/design-audit.md</c> Q4)</strong>: under
/// <see cref="EconomyPurseModel.PerUnitPurses"/> (<c>classical-faithful</c>) the debit lands on the
/// buying army's or fleet's own purse, through <see cref="PurseAccounting.Credit"/> so the 1,000-talent
/// cap is enforced on this path exactly as on every other purse-crediting path. Under
/// <see cref="EconomyPurseModel.CentralTreasury"/> (<c>improved</c>) the same debit lands on the buying
/// nation's treasury instead, with no per-unit purse touched at all, and the money-based room cap does not
/// apply (the treasury has no confirmed cap, so none is invented for it) — the selling nation's treasury is
/// still credited either way, since <c>CityState</c> never had a purse of its own to begin with.
/// </para>
/// </remarks>
public static class SupplyPurchase
{
    /// <summary>The result of one supply purchase.</summary>
    /// <param name="Army">The buying army, with its supply stock changed and (if paid, per-unit purses) its purse debited.</param>
    /// <param name="City">The selling city, with its supply stock changed oppositely to <see cref="Army"/>.</param>
    /// <param name="BuyerNation">
    /// The buying nation, with its treasury debited under <see cref="EconomyPurseModel.CentralTreasury"/>;
    /// unchanged (the same instance passed in) under <see cref="EconomyPurseModel.PerUnitPurses"/> or a
    /// free, own-city purchase.
    /// </param>
    /// <param name="SellingCityNation">
    /// The selling city's own nation, with its treasury credited <c>TalentsPaid</c> on a foreign (paid)
    /// purchase; unchanged (the same instance passed in) on a free, own-city purchase.
    /// </param>
    /// <param name="AdmittedTons">
    /// The tons actually transferred — the caller's request clamped by provider stock, dialog-capacity
    /// room and (paid path only) affordable money; possibly negative, when an over-capacity army gives
    /// supply back to the provider.
    /// </param>
    /// <param name="TalentsPaid">The talents debited (negative: refunded) — 0 for a free, own-city purchase.</param>
    /// <param name="WasFreeOwnCity">Whether this purchase took the free, own-city path.</param>
    public sealed record ArmyResult(
        ArmyState Army,
        CityState City,
        NationState BuyerNation,
        NationState SellingCityNation,
        int AdmittedTons,
        int TalentsPaid,
        bool WasFreeOwnCity);

    /// <summary>Buys (or, if over capacity, gives back) <paramref name="tons"/> of supply for an army at a city.</summary>
    /// <param name="army">The buying army.</param>
    /// <param name="sellingCity">The city selling the supply.</param>
    /// <param name="buyerNation">
    /// The buying army's own nation. Its <see cref="NationState.Id"/> must equal <paramref name="army"/>'s
    /// <see cref="Model.ArmyState.Nation"/> — it is read for <see cref="NationState.Id"/> (matched against
    /// <see cref="CityState.Owner"/> to decide free-vs-paid) and, under
    /// <see cref="EconomyPurseModel.CentralTreasury"/>, debited directly.
    /// </param>
    /// <param name="sellingCityNation">
    /// The selling city's own nation. Its <see cref="NationState.Id"/> must equal
    /// <paramref name="sellingCity"/>'s <see cref="CityState.Owner"/> — it is credited
    /// <c>TalentsPaid</c> on a foreign (paid) purchase (Done-when 5).
    /// </param>
    /// <param name="tons">
    /// Tons requested. Must be positive — the sign of the actual transfer is decided by the clamp, not by
    /// the caller. The <em>actual</em> transfer is clamped to the selling city's stock and the army's
    /// dialog-capacity room (Done-when 1) before any cost is computed, so this is a request, not a
    /// guarantee.
    /// </param>
    /// <param name="ruleset">Supplies every constant and the <see cref="RulesetFlags.EconomyPurses"/> flag — never a C# literal.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tons"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="buyerNation"/> is not <paramref name="army"/>'s own nation, or
    /// <paramref name="sellingCityNation"/> is not <paramref name="sellingCity"/>'s own nation.
    /// </exception>
    public static ArmyResult BuyForArmy(
        ArmyState army, CityState sellingCity, NationState buyerNation, NationState sellingCityNation, int tons, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(sellingCity);
        ArgumentNullException.ThrowIfNull(buyerNation);
        ArgumentNullException.ThrowIfNull(sellingCityNation);
        ArgumentNullException.ThrowIfNull(ruleset);
        if (tons <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tons), tons, "Must request a positive number of tons.");
        }

        if (!string.Equals(buyerNation.Id, army.Nation, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{buyerNation.Id}' is not army '{army.Id}''s own nation ('{army.Nation}').",
                nameof(buyerNation));
        }

        if (!string.Equals(sellingCityNation.Id, sellingCity.Owner, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{sellingCityNation.Id}' does not own city '{sellingCity.Id}' ('{sellingCity.Owner}' does).",
                nameof(sellingCityNation));
        }

        var isOwnCity = string.Equals(sellingCity.Owner, army.Nation, StringComparison.Ordinal);

        // T38 #78, Done-when 1 and 3: one clamping chain, the room term not floored at 0.
        var dialogCapacity = SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, ruleset);
        var room = dialogCapacity - army.SupplyTons;
        var admittedTons = Math.Min(tons, Math.Min(sellingCity.SupplyTons, room));
        if (!isOwnCity && ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses)
        {
            admittedTons = Math.Min(admittedTons, army.Money * ruleset.Economy.SupplyTonsPerTalent);
        }

        var talents = isOwnCity ? 0 : admittedTons / ruleset.Economy.SupplyTonsPerTalent;

        var updatedCity = sellingCity with { SupplyTons = sellingCity.SupplyTons - admittedTons };
        var updatedArmy = army with { SupplyTons = army.SupplyTons + admittedTons };
        var updatedBuyerNation = buyerNation;
        var updatedSellingCityNation = sellingCityNation;

        if (talents != 0)
        {
            if (ruleset.Flags.EconomyPurses == EconomyPurseModel.CentralTreasury)
            {
                updatedBuyerNation = buyerNation with { Treasury = buyerNation.Treasury - talents };
            }
            else
            {
                updatedArmy = updatedArmy with { Money = PurseAccounting.Credit(updatedArmy.Money, -talents, ruleset) };
            }

            // Done-when 5: the selling city's owner's treasury is credited the same talents the buyer
            // paid -- and, on the negative-room giveback path, debited the same refund the buyer received.
            updatedSellingCityNation = sellingCityNation with { Treasury = sellingCityNation.Treasury + talents };
        }

        return new ArmyResult(updatedArmy, updatedCity, updatedBuyerNation, updatedSellingCityNation, admittedTons, talents, isOwnCity);
    }

    /// <summary>The result of one fleet supply purchase — the naval twin of <see cref="ArmyResult"/>.</summary>
    public sealed record FleetResult(
        FleetState Fleet,
        CityState City,
        NationState BuyerNation,
        NationState SellingCityNation,
        int AdmittedTons,
        int TalentsPaid,
        bool WasFreeOwnCity);

    /// <summary>
    /// Buys (or gives back) <paramref name="tons"/> of supply for a fleet at a city. See
    /// <see cref="BuyForArmy"/> — the fleet cap has no dialog bonus (<see cref="SupplyCapacity.FleetCapacityTons"/>
    /// on both paths).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tons"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="buyerNation"/> is not <paramref name="fleet"/>'s own nation, or
    /// <paramref name="sellingCityNation"/> is not <paramref name="sellingCity"/>'s own nation.
    /// </exception>
    public static FleetResult BuyForFleet(
        FleetState fleet, CityState sellingCity, NationState buyerNation, NationState sellingCityNation, int tons, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(sellingCity);
        ArgumentNullException.ThrowIfNull(buyerNation);
        ArgumentNullException.ThrowIfNull(sellingCityNation);
        ArgumentNullException.ThrowIfNull(ruleset);
        if (tons <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tons), tons, "Must request a positive number of tons.");
        }

        if (!string.Equals(buyerNation.Id, fleet.Nation, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{buyerNation.Id}' is not fleet '{fleet.Id}''s own nation ('{fleet.Nation}').",
                nameof(buyerNation));
        }

        if (!string.Equals(sellingCityNation.Id, sellingCity.Owner, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nation '{sellingCityNation.Id}' does not own city '{sellingCity.Id}' ('{sellingCity.Owner}' does).",
                nameof(sellingCityNation));
        }

        var isOwnCity = string.Equals(sellingCity.Owner, fleet.Nation, StringComparison.Ordinal);

        var fleetCapacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, ruleset);
        var room = fleetCapacity - fleet.SupplyTons;
        var admittedTons = Math.Min(tons, Math.Min(sellingCity.SupplyTons, room));
        if (!isOwnCity && ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses)
        {
            admittedTons = Math.Min(admittedTons, fleet.Money * ruleset.Economy.SupplyTonsPerTalent);
        }

        var talents = isOwnCity ? 0 : admittedTons / ruleset.Economy.SupplyTonsPerTalent;

        var updatedCity = sellingCity with { SupplyTons = sellingCity.SupplyTons - admittedTons };
        var updatedFleet = fleet with { SupplyTons = fleet.SupplyTons + admittedTons };
        var updatedBuyerNation = buyerNation;
        var updatedSellingCityNation = sellingCityNation;

        if (talents != 0)
        {
            if (ruleset.Flags.EconomyPurses == EconomyPurseModel.CentralTreasury)
            {
                updatedBuyerNation = buyerNation with { Treasury = buyerNation.Treasury - talents };
            }
            else
            {
                updatedFleet = updatedFleet with { Money = PurseAccounting.Credit(updatedFleet.Money, -talents, ruleset) };
            }

            updatedSellingCityNation = sellingCityNation with { Treasury = sellingCityNation.Treasury + talents };
        }

        return new FleetResult(updatedFleet, updatedCity, updatedBuyerNation, updatedSellingCityNation, admittedTons, talents, isOwnCity);
    }
}
