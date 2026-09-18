using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Engine.Naval;

/// <summary>
/// The fleet-as-provider half of <c>docs/task-catalogue.md</c> "T14 Naval" DoD 17's supply purchase —
/// the direction <see cref="Economy.SupplyPurchase"/> (city-shaped) does not cover. See
/// <see cref="Commands.BuyFleetSupplyCommand"/>'s remarks for the <c>[open]</c> treasury-credit question
/// this deliberately leaves unresolved.
/// </summary>
/// <remarks>
/// Reuses <see cref="SupplyCapacity.FleetCapacityTons"/> for the buyer's cap — never a re-implemented
/// constant — and the same clamping shape <see cref="SupplyPurchase.BuyForFleet"/> uses for its free,
/// own-city path: the request clamped by the provider's stock and the buyer's capacity room, with no
/// money changing hands, since a fleet provider is always the buyer's own nation
/// (<c>decompiled-unit-map-orders-and-record-fields.md</c>: <c>TAFSupply_FindProviders</c> names only
/// "the nation's own fleets" as eligible).
/// </remarks>
public static class FleetToFleetSupplyTransfer
{
    /// <summary>The result of one fleet-to-fleet supply transfer.</summary>
    /// <param name="Buyer">The buying fleet, with its supply stock increased.</param>
    /// <param name="Provider">The providing fleet, with its supply stock decreased by the same amount.</param>
    /// <param name="AdmittedTons">The tons actually transferred — never negative, never more than either clamp allows.</param>
    public sealed record Result(FleetState Buyer, FleetState Provider, int AdmittedTons);

    /// <summary>Transfers up to <paramref name="tons"/> from <paramref name="provider"/> to <paramref name="buyer"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tons"/> is not positive.</exception>
    public static Result Transfer(FleetState buyer, FleetState provider, int tons, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(ruleset);
        if (tons <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tons), tons, "Must request a positive number of tons.");
        }

        var capacity = SupplyCapacity.FleetCapacityTons(buyer.Ships, ruleset);
        var room = capacity - buyer.SupplyTons;
        var admitted = Math.Max(0, Math.Min(tons, Math.Min(provider.SupplyTons, room)));

        var updatedBuyer = buyer with { SupplyTons = buyer.SupplyTons + admitted };
        var updatedProvider = provider with { SupplyTons = provider.SupplyTons - admitted };

        return new Result(updatedBuyer, updatedProvider, admitted);
    }
}
