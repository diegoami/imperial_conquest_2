using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Buys (or, if over capacity, gives back) supply for a fleet — <c>docs/task-catalogue.md</c>
/// "T14 Naval" DoD 17. The provider is either a city within one tile whose owner is not at war with the
/// buyer, or one of the buyer's own fleets within one tile
/// (<c>decompiled-unit-map-orders-and-record-fields.md</c>: <c>TAFSupply_FindProviders</c>). Exactly one
/// of <see cref="ProviderCityId"/>/<see cref="ProviderFleetId"/> must be set.
/// </summary>
/// <remarks>
/// <para>
/// <strong>City provider</strong>: wraps <see cref="Economy.SupplyPurchase.BuyForFleet"/> exactly — free
/// at the buyer's own city, <c>amount / 5</c> talents abroad (paid to the selling city's owner), never
/// re-implemented here (<c>docs/task-catalogue.md</c> T14's own Hazards).
/// </para>
/// <para>
/// <strong>Fleet provider — <c>[open]</c></strong>: the confirmed report names only "the nation's own
/// fleets" as eligible providers, which makes this direction always same-nation and therefore always the
/// free path by the same free/paid test <see cref="Economy.SupplyPurchase"/> already uses (does the
/// buyer own the provider). <c>TAFSupply_TransferSupply</c>'s formula credits <c>treasury[cityOwner]</c>
/// unconditionally, and neither cited report states what that line does, if anything, when there is no
/// city — a genuine gap, not a free/paid ambiguity this task can resolve by re-reading the confirmed
/// rule. This implements the confirmed free-path shape (a same-nation transfer moves tons only, no
/// talents) and leaves open, rather than inventing, whether the original's dialog does something further
/// even for a same-nation fleet provider. See <see cref="FleetToFleetSupplyTransfer"/>.
/// </para>
/// </remarks>
public sealed record BuyFleetSupplyCommand(
    string IssuingNationId, string FleetId, string? ProviderCityId, string? ProviderFleetId, int Tons) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.buy-fleet-supply";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class BuyFleetSupplyRejections
{
    /// <summary>The command names a fleet id that does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>The named buying fleet belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourFleet = new("naval.not-your-fleet");

    /// <summary>The buying fleet is still under construction.</summary>
    public static readonly RejectionCode UnderConstruction = new("naval.under-construction");

    /// <summary>Neither, or both, of <see cref="BuyFleetSupplyCommand.ProviderCityId"/>/<see cref="BuyFleetSupplyCommand.ProviderFleetId"/> were set.</summary>
    public static readonly RejectionCode ExactlyOneProviderRequired = new("naval.exactly-one-provider-required");

    /// <summary>The command names a provider city id that does not exist.</summary>
    public static readonly RejectionCode UnknownCity = new("naval.unknown-city");

    /// <summary>The city exists, but its owner does not resolve to a known nation.</summary>
    public static readonly RejectionCode UnresolvableCityOwner = new("naval.unresolvable-city-owner");

    /// <summary>The command names a provider fleet id that does not exist.</summary>
    public static readonly RejectionCode UnknownProviderFleet = new("naval.unknown-provider-fleet");

    /// <summary>A fleet provider must be the buyer's own -- "the nation's own fleets".</summary>
    public static readonly RejectionCode ProviderFleetNotOwnNation = new("naval.provider-fleet-not-own-nation");

    /// <summary>The provider (city or fleet) is not within one tile of the buying fleet.</summary>
    public static readonly RejectionCode ProviderTooFar = new("naval.provider-too-far");

    /// <summary>The provider city's owner is at war with the buyer.</summary>
    public static readonly RejectionCode ProviderAtWar = new("naval.provider-at-war");

    /// <summary>The requested amount is not positive.</summary>
    public static readonly RejectionCode InvalidAmount = new("naval.invalid-amount");
}
