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
/// for more tons than its own money could ever pay for) and at the buyer's own supply capacity. Neither
/// cap was enforced before review round 1, letting a paid purchase drive a purse negative or a supply
/// stock past capacity; the money cap below still rejects a paid purchase that would violate it, and the
/// capacity cap below (see the next paragraph, corrected in review round 3) clamps the transfer down
/// instead of rejecting it, on both paths.
/// </para>
/// <para>
/// <strong>The capacity cap is the dialog cap, on both paths alike (review round 3, the resolution of
/// round 2's R1) — [confirmed]</strong>. Round 2 restricted the capacity check to the paid path, reasoning
/// that <c>design-audit.md</c> Q9 established the free, own-city case as a <em>different,
/// <c>TArmyToArmy</c>-shaped dialog</em> from the paid <c>TAFSupply</c> purchase the cap's formula was
/// decompiled from. That reading does not survive a check against either source it cited:
/// <c>design-audit.md</c> Q9 (line 252) lists exactly that question — "whether that is a genuinely
/// different dialog/code path" — as <em>still open</em>, not settled; and
/// <c>decompiled-unit-map-orders-and-record-fields.md</c>'s own §"Supply is bought, not moved" names the
/// free, own-city resupply dialog <c>TAFSupply</c> too, the very dialog the cap's formula comes from. The
/// one-ton disagreement round 2 was reconciling — <c>troops / 100</c> gives 481 for the Roman 13-unit
/// roster's 48,173 troops, one short of the confirmed 482 t at its own city
/// (<c>controlled-army-supply-transfer.md</c>, <c>supplyTransfer.*</c> in the fixtures corpus, and
/// <c>roman13.supplyPercent</c>'s independent 100% reading) — has an instruction-level answer instead:
/// <c>supply-capacity-rounding.md</c> disassembled both <c>TAFSupply_ChangeSupply</c> (own-city, free) and
/// <c>TAFSupply_ChangeBuyAmount</c> (foreign, paid) and found the identical sequence in each — <c>IDIV</c>
/// by 100, then an unconditional <c>INC</c> — with no FPU instruction and no <c>Round</c>/<c>Trunc</c>
/// anywhere. The dialog's real cap is <c>troops / ArmySupplyTonsPerTroops +
/// SupplyDialogArmyCapacityBonus</c> (<see cref="SupplyCapacity.ArmyDialogCapacityTons"/>), applied
/// identically on both paths — 481 + 1 = 482, exactly the observed transfer. It is a supply-dialog-only
/// cap: <see cref="SupplyCapacity.ArmyCapacityTons"/> (<c>troops / 100</c>, no bonus) stays the general
/// capacity every other path (automatic resupply, army-to-army rebalancing, battle absorption) uses
/// unmodified, and <c>SupplyCapacityTests</c>' own DoD 4 values (998 for 99,882 troops, 282 for 28,227)
/// are untouched by this change. The fleet dialog cap needed no such reconciliation and stays
/// <see cref="SupplyCapacity.FleetCapacityTons"/> (<c>ships × 8</c>, no bonus) on both paths — the report
/// found the identical fleet-branch instructions on both paths too, with no <c>INC</c> on either.
/// </para>
/// <para>
/// <strong>The cap clamps the transfer; it does not reject it.</strong> Both dialogs implement the cap as
/// a room computation — <c>room = capacity − currentSupplyTons</c>, then <c>step = min(requestedTons,
/// room)</c> — exactly the shape <c>supply-capacity-rounding.md</c>'s own pseudocode gives for every one
/// of the dialog's clamps (city stock, capacity, money). <see cref="BuyForArmy"/> and
/// <see cref="BuyForFleet"/> apply that shape for the capacity room specifically: the room is floored at
/// zero here (the source notes it is not floored in the original, but flooring only differs for an army
/// already over its cap being pulled backwards, which is out of this round's scope), and the actually
/// admitted tons — never more than the requested amount, possibly less — are what gets transferred and
/// charged for. A request for more than the room allows is admitted at whatever the room is, down to
/// zero, rather than throwing. The money and city-stock caps on the paid path are unchanged by this round
/// and still throw exactly as before — only the capacity check changed shape.
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
    /// <param name="tons">
    /// Tons requested. Must be positive. The <em>actual</em> transfer is clamped to the army's dialog
    /// capacity room (review round 3) before any cost is computed, so this is a request, not a guarantee
    /// — an army already at or past its dialog capacity buys nothing, rather than throwing.
    /// </param>
    /// <param name="ruleset">Supplies every constant and the <see cref="RulesetFlags.EconomyPurses"/> flag — never a C# literal.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tons"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="buyerNation"/> is not <paramref name="army"/>'s own nation; or the city does not
    /// hold <paramref name="tons"/> tons of supply to sell; or (paid case only) the room the dialog
    /// capacity admits would cost more than, under per-unit purses, the army's own purse holds.
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

        // Review round 3 (R1's resolution): the supply dialog clamps the requested transfer to the room
        // left under troops/100 + SupplyDialogArmyCapacityBonus, identically on the free (own-city) and
        // paid (foreign) paths -- see the class remarks above and supply-capacity-rounding.md. The room is
        // floored at zero here (an army already at or past its cap admits nothing, rather than being
        // pulled backwards, which is out of this round's scope); it is a clamp, not a rejection, so a
        // request for more than the cap allows buys whatever room is left instead of throwing.
        var dialogCapacity = SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, ruleset);
        var room = Math.Max(0, dialogCapacity - army.SupplyTons);
        var admittedTons = Math.Min(tons, room);

        var talents = isOwnCity || admittedTons == 0 ? 0 : admittedTons / ruleset.Economy.SupplyTonsPerTalent;

        // Review round 2, NB1: compare whole tons against whole talents (tons > money * SupplyTonsPerTalent)
        // rather than truncating tons to talents first, which let a purchase through for up to
        // SupplyTonsPerTalent - 1 tons more than the buyer could actually pay for. Compares against the
        // capacity-clamped admittedTons (review round 3), the amount actually being charged for.
        if (!isOwnCity && ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses
            && admittedTons > army.Money * ruleset.Economy.SupplyTonsPerTalent)
        {
            throw new ArgumentException(
                $"Army '{army.Id}' has only {army.Money} talents, cannot pay for {admittedTons} tons.",
                nameof(tons));
        }

        var updatedCity = sellingCity with { SupplyTons = sellingCity.SupplyTons - admittedTons };
        var updatedArmy = army with { SupplyTons = army.SupplyTons + admittedTons };
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

    /// <summary>
    /// Buys <paramref name="tons"/> of supply for a fleet from a city. See <see cref="BuyForArmy"/> —
    /// <paramref name="tons"/> is a request, clamped (review round 3) to the fleet's own capacity room
    /// before any cost is computed.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tons"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="buyerNation"/> is not <paramref name="fleet"/>'s own nation; or the city does not
    /// hold <paramref name="tons"/> tons of supply to sell; or (paid case only) the room the capacity
    /// admits would cost more than, under per-unit purses, the fleet's own purse holds.
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

        // Review round 3 (see BuyForArmy and the class remarks): the supply dialog clamps the requested
        // transfer to the room left under ships * FleetSupplyTonsPerShip, identically on both paths --
        // no bonus on the fleet side, unlike the army's SupplyDialogArmyCapacityBonus.
        var fleetCapacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, ruleset);
        var room = Math.Max(0, fleetCapacity - fleet.SupplyTons);
        var admittedTons = Math.Min(tons, room);

        var talents = isOwnCity || admittedTons == 0 ? 0 : admittedTons / ruleset.Economy.SupplyTonsPerTalent;

        // Review round 2, NB1 (see BuyForArmy): compare whole tons against whole talents, against the
        // capacity-clamped admittedTons (review round 3).
        if (!isOwnCity && ruleset.Flags.EconomyPurses == EconomyPurseModel.PerUnitPurses
            && admittedTons > fleet.Money * ruleset.Economy.SupplyTonsPerTalent)
        {
            throw new ArgumentException(
                $"Fleet '{fleet.Id}' has only {fleet.Money} talents, cannot pay for {admittedTons} tons.",
                nameof(tons));
        }

        var updatedCity = sellingCity with { SupplyTons = sellingCity.SupplyTons - admittedTons };
        var updatedFleet = fleet with { SupplyTons = fleet.SupplyTons + admittedTons };
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
