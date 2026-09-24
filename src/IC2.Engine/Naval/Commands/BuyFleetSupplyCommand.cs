using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// <c>TUnitMap_SupplyFleet</c> (<c>0x004477FC</c>) → <c>TAFSupply</c> "against a city or another fleet" —
/// <c>docs/task-catalogue.md</c> "T46 Fleet-to-fleet transfer, and the supply path that keeps fleets
/// alive" (issue #148), Done-when 6, 7 and 8. Exactly one of <see cref="ProviderCityId"/> and
/// <see cref="ProviderFleetId"/> must be set — the two provider kinds <c>TAFSupply_FindProviders</c>
/// offers, corroborated by the user's own play testimony recorded against
/// <c>decompiled-unit-map-orders-and-record-fields.md</c>: <em>"fleet can supply from and to armies,
/// towns and other fleets."</em>
/// </summary>
/// <remarks>
/// <para>
/// <strong>DoD 7, T38's rules called, never re-implemented.</strong> The city-provider path wraps
/// <see cref="Economy.SupplyPurchase.BuyForFleet"/> — the fleet cap is <c>ships × 8</c> with no dialog
/// bonus, the purchase is capped at the buyer's <c>money × 5</c> and the provider's stock, and the price
/// is one talent per five tons, paid from the buyer's purse to the selling city's owner (who may be
/// another nation), exactly as that method already implements.
/// </para>
/// <para>
/// <strong>The city provider's "not at war" gate</strong> [confirmed:
/// <c>supply-capacity-rounding.md</c>:33, checked 2026-09-23 at research <c>3f6ca09</c> —
/// "every city within one tile whose owner is not at war (relation <c>3</c>)". T70 (#189): an earlier
/// revision cited <c>decompiled-unit-map-orders-and-record-fields.md</c> for this same quote, which that
/// report does not carry; <c>supply-capacity-rounding.md</c> is the report that states both the
/// one-tile range and the not-at-war gate together.] is checked against <c>GameState.Relations</c> only
/// for a <em>foreign</em> city; a buyer's own city is never at war with itself, so the check is skipped
/// there rather than resolved through the relation matrix at all.
/// </para>
/// <para>
/// <strong>DoD 4 (T14 round-2 review, B9), a fleet cannot supply itself.</strong> The handler rejects
/// <c>ProviderFleetId == FleetId</c> before any transfer math runs — see
/// <see cref="FleetToFleetSupplyTransfer"/>'s remarks for the exact defect this closes.
/// </para>
/// <para>
/// <strong>DoD 8, the fleet-provider payment leg <c>[open]</c>.</strong> See
/// <see cref="FleetToFleetSupplyTransfer"/>'s remarks: no report establishes whether a fleet provider's
/// nation is paid at all, so this command implements the confirmed <em>free</em> shape for that path — no
/// talents change hands — rather than inventing a recipient for a formula built around
/// <c>treasury[cityOwner]</c>, which a fleet does not have.
/// </para>
/// <para>
/// The fleet-provider path restricts the provider to the buyer's <em>own</em> nation's fleets, matching
/// the report's own reading of <c>TAFSupply_FindProviders</c> ("the nation's own fleets within one tile") —
/// unlike the city path, a fleet provider is never foreign.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation issuing the order; the buying fleet must be its own.</param>
/// <param name="FleetId">The buying fleet.</param>
/// <param name="ProviderCityId">The selling city, within one tile — mutually exclusive with <see cref="ProviderFleetId"/>.</param>
/// <param name="ProviderFleetId">The providing fleet, the buyer's own nation's, within one tile — mutually exclusive with <see cref="ProviderCityId"/>.</param>
/// <param name="Tons">Tons requested.</param>
public sealed record BuyFleetSupplyCommand(
    string IssuingNationId,
    string FleetId,
    string? ProviderCityId,
    string? ProviderFleetId,
    int Tons) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.buy-fleet-supply";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class BuyFleetSupplyRejections
{
    /// <summary>The command names a fleet id that does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>The buying fleet belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourFleet = new("naval.not-your-fleet");

    /// <summary>The buying fleet is still under construction.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");

    /// <summary>The requested amount is not positive.</summary>
    public static readonly RejectionCode InvalidAmount = new("naval.invalid-amount");

    /// <summary>Neither, or both, of <see cref="BuyFleetSupplyCommand.ProviderCityId"/>/<see cref="BuyFleetSupplyCommand.ProviderFleetId"/> were named.</summary>
    public static readonly RejectionCode InvalidProvider = new("naval.invalid-provider");

    /// <summary>The command names a provider city id that does not exist.</summary>
    public static readonly RejectionCode UnknownCity = new("naval.unknown-city");

    /// <summary>
    /// The provider city exists, but its owner names a nation the state does not contain — a
    /// data-integrity problem, kept distinct from <see cref="UnknownCity"/> exactly as
    /// <c>Economy.Commands.BuySupplyRejections.UnresolvableCityOwner</c> is (review round 1, N7 there).
    /// </summary>
    public static readonly RejectionCode UnresolvableCityOwner = new("naval.unresolvable-city-owner");

    /// <summary>The provider city is more than one tile from the buying fleet.</summary>
    public static readonly RejectionCode CityNotWithinRange = new("naval.city-not-within-range");

    /// <summary>The provider city's owner is at war with the buyer's nation.</summary>
    public static readonly RejectionCode CityOwnerAtWar = new("naval.city-owner-at-war");

    /// <summary>The provider fleet belongs to a nation other than the buyer's own — a fleet provider is never foreign.</summary>
    public static readonly RejectionCode ProviderFleetNotYours = new("naval.provider-fleet-not-yours");

    /// <summary>The provider fleet is more than one tile from the buying fleet.</summary>
    public static readonly RejectionCode ProviderFleetNotWithinRange = new("naval.provider-fleet-not-within-range");

    /// <summary>Done-when 4 (T14 round-2 review, B9): a fleet cannot buy supply from itself.</summary>
    public static readonly RejectionCode SelfSupply = new("naval.self-supply");
}
