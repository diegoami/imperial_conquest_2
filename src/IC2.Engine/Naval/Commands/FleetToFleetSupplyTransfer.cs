using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// The pure transfer step for <see cref="BuyFleetSupplyCommandHandler"/>'s fleet-provider path: one
/// fleet buying supply from another of its own nation's fleets — <c>docs/task-catalogue.md</c> "T46"
/// Done-when 6, 7 and 8. Named and kept separate from the command-layer checks (existence, ownership,
/// range, self-supply) so the pure resource arithmetic can be tested and reasoned about on its own, the
/// same split <see cref="SupplyPurchase"/> and <see cref="Economy.AutomaticResupply"/> already use.
/// </summary>
/// <remarks>
/// <para>
/// <strong>DoD 4 (T14 round-2 review, B9), a fleet cannot supply itself.</strong> The first attempt let
/// <c>providerFleetId == fleetId</c> through: <see cref="Transfer"/> computed a credited buyer and a
/// debited provider from the <em>same</em> record, but the caller's write-back matched the buyer's id
/// first, so only the credited copy was ever stored — 20 t → 40 t, repeatable to the cap, an unlimited
/// free counter to the very attrition this task exists to remedy. This method throws for a same-id call
/// as a defensive last line; <see cref="BuyFleetSupplyCommandHandler"/> is the primary guard and rejects
/// the command with a typed <see cref="Core.CommandRejection"/> before this method is ever reached.
/// </para>
/// <para>
/// <strong>DoD 8, the payment leg <c>[open]</c>.</strong> <c>TAFSupply_TransferSupply</c>
/// [confirmed: decompiled-unit-map-orders-and-record-fields.md] credits <c>treasury[cityOwner]</c> on the
/// foreign-city path, and a fleet provider has no city owner to credit — no report establishes whether
/// the selling <em>nation</em> is paid at all when the provider is a fleet rather than a city. Per
/// <c>docs/task-catalogue.md</c> "T46" Binding rules, this is <strong>not invented</strong>: this method
/// moves tons only, with no talents debited from the buyer and none credited anywhere, i.e. it implements
/// the confirmed <em>free</em> path's shape (the same shape <see cref="SupplyPurchase.BuyForFleet"/> takes
/// at an owned city) rather than guessing at a paid path with no confirmed recipient. T14's round-2 review
/// found this exact framing non-blocking when its own (differently-buggy) attempt used it: "Honestly
/// stated, right place ... implements the confirmed free-path shape rather than inventing."
/// </para>
/// <para>
/// <strong>The cap, matching <see cref="SupplyPurchase.BuyForFleet"/>'s model exactly.</strong> The
/// buyer's room (<see cref="SupplyCapacity.FleetCapacityTons"/> minus its current stock) is <em>not</em>
/// floored at zero, so a buyer already over its cap can give supply back to the provider — the same
/// "one clamping chain, never a rejection for exceeding it" convention T38 established. No dialog bonus
/// applies to a fleet cap either way [confirmed: <c>decompiled-unit-map-orders-and-record-fields.md</c>,
/// "The fleet cap is <c>ships × 8</c>, with no <c>+1</c>"].
/// </para>
/// </remarks>
public static class FleetToFleetSupplyTransfer
{
    /// <summary>The result of one fleet-to-fleet supply transfer.</summary>
    /// <param name="Buyer">The buying fleet, its supply stock increased (or, over cap, decreased).</param>
    /// <param name="Provider">The providing fleet, its supply stock changed oppositely to <see cref="Buyer"/>.</param>
    /// <param name="AdmittedTons">
    /// The tons actually transferred — the caller's request clamped by the provider's stock and the
    /// buyer's room; possibly negative when an over-capacity buyer gives supply back.
    /// </param>
    public sealed record Result(FleetState Buyer, FleetState Provider, int AdmittedTons);

    /// <summary>Buys (or gives back) <paramref name="tons"/> of supply for <paramref name="buyer"/> from <paramref name="provider"/>.</summary>
    /// <param name="buyer">The buying fleet.</param>
    /// <param name="provider">The providing fleet — must be a different fleet from <paramref name="buyer"/>.</param>
    /// <param name="tons">Tons requested. Must be positive; the clamp decides the actual (possibly negative) transfer.</param>
    /// <param name="ruleset">Supplies every constant — never a C# literal.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tons"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="provider"/> is the same fleet as <paramref name="buyer"/> — DoD 4's defensive guard.
    /// </exception>
    public static Result Transfer(FleetState buyer, FleetState provider, int tons, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(ruleset);
        if (tons <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tons), tons, "Must request a positive number of tons.");
        }

        if (string.Equals(buyer.Id, provider.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A fleet cannot supply itself -- the command handler must reject this before calling Transfer.",
                nameof(provider));
        }

        var capacity = SupplyCapacity.FleetCapacityTons(buyer.Ships, ruleset);
        var room = capacity - buyer.SupplyTons; // not floored at 0 -- see this type's remarks.
        var admittedTons = Math.Min(tons, Math.Min(provider.SupplyTons, room));

        var updatedBuyer = buyer with { SupplyTons = buyer.SupplyTons + admittedTons };
        var updatedProvider = provider with { SupplyTons = provider.SupplyTons - admittedTons };

        return new Result(updatedBuyer, updatedProvider, admittedTons);
    }
}
