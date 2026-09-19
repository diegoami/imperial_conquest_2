using IC2.Engine.Core;

namespace IC2.Engine.Economy.Commands;

/// <summary>
/// Orders an army to buy supply at a city, or (folded follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/147">#147</see>) from one of its own
/// nation's fleets — <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy world", the
/// <c>buy &lt;army&gt; &lt;city&gt; &lt;tons&gt;</c> command, widened by "T46 Fleet-to-fleet transfer, and
/// the supply path that keeps fleets alive" (issue #148) Done-when 11. Exactly one of
/// <see cref="CityId"/>/<see cref="ProviderFleetId"/> must be set. Wraps
/// <see cref="SupplyPurchase.BuyForArmy"/>'s dialog path for the city case; the rule (free at your own
/// city, <c>amount / 5</c> abroad, clamped rather than rejected past a cap — "T38 Supply dialog
/// follow-ups" Done-when 1) lives there, never re-implemented here.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this direction was folded here rather than kept separate.</strong> Issue #147 was filed
/// while T14 was still in flight, to keep that task's own boundary clean:
/// <c>TAFSupply_FindProviders</c> [confirmed: decompiled-unit-map-orders-and-record-fields.md] offers a
/// buyer the nation's own fleets within one tile alongside cities, and the user confirmed both directions
/// from play — but at planning time <c>BuySupplyCommand</c> had no fleet provider at all. Since this is
/// the other direction of the exact same mechanic T46's naval commands add, it belongs with them rather
/// than in a task of its own; the T46 catalogue entry's Owns list scopes this file (and this file's tests)
/// to that one addition, never the rest of <c>T08</c>/<c>T38</c>'s economy surface.
/// </para>
/// <para>
/// <strong>The payment leg's <c>[open]</c> tag, shared with T46 Done-when 8.</strong>
/// <c>TAFSupply_TransferSupply</c> credits <c>treasury[cityOwner]</c>, and a fleet provider has no city
/// owner to credit — no report says whether the selling nation is paid at all when the provider is a
/// fleet. This command implements the confirmed <em>free</em> path's shape for that case (no talents move)
/// rather than inventing a recipient, exactly as
/// <c>IC2.Engine.Naval.Commands.FleetToFleetSupplyTransfer</c> does for the naval twin of this same direction.
/// </para>
/// <para>
/// The fleet provider is restricted to the buyer's own nation's fleets, matching
/// <c>TAFSupply_FindProviders</c>'s own reading ("the nation's own fleets within one tile") — unlike a
/// city provider, a fleet provider is never foreign.
/// </para>
/// </remarks>
/// <param name="CityId">The selling city — mutually exclusive with <see cref="ProviderFleetId"/>.</param>
/// <param name="ProviderFleetId">
/// The providing fleet, the buying army's own nation's, within one tile — mutually exclusive with
/// <see cref="CityId"/>. <c>null</c> by default, so every existing 4-argument call site is unaffected.
/// </param>
public sealed record BuySupplyCommand(
    string IssuingNationId, string ArmyId, string? CityId, int Tons, string? ProviderFleetId = null) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "economy.buy-supply";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
/// <remarks>
/// T38 (issue #78, Done-when 1) dropped <c>InsufficientCitySupply</c> and <c>InsufficientFunds</c>:
/// <see cref="SupplyPurchase.BuyForArmy"/> no longer throws for either condition, it clamps the transfer
/// instead, so a command that used to be rejected for exceeding a cap is now accepted at whatever the
/// cap allows — down to, and including, zero.
/// </remarks>
public static class BuySupplyRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("supply.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("supply.not-your-army");

    /// <summary>The command names a city id that does not exist.</summary>
    public static readonly RejectionCode UnknownCity = new("supply.unknown-city");

    /// <summary>
    /// The city exists, but its <see cref="Model.CityState.Owner"/> names a nation the state does not
    /// contain -- a data-integrity problem, not an unknown city. Review round 1, N7: kept distinct from
    /// <see cref="UnknownCity"/> rather than reusing it for a different failure.
    /// </summary>
    public static readonly RejectionCode UnresolvableCityOwner = new("supply.unresolvable-city-owner");

    /// <summary>The requested amount is not positive.</summary>
    public static readonly RejectionCode InvalidAmount = new("supply.invalid-amount");

    /// <summary>
    /// Neither, or both, of <see cref="BuySupplyCommand.CityId"/>/<see cref="BuySupplyCommand.ProviderFleetId"/>
    /// were named (issue #147's folded direction).
    /// </summary>
    public static readonly RejectionCode InvalidProvider = new("supply.invalid-provider");

    /// <summary>The command names a provider fleet id that does not exist.</summary>
    public static readonly RejectionCode UnknownProviderFleet = new("supply.unknown-fleet");

    /// <summary>The named provider fleet belongs to a nation other than the buying army's own — a fleet provider is never foreign.</summary>
    public static readonly RejectionCode ProviderFleetNotYours = new("supply.provider-fleet-not-yours");

    /// <summary>The named provider fleet is still under construction.</summary>
    public static readonly RejectionCode ProviderFleetUnderConstruction = new("supply.provider-fleet-under-construction");

    /// <summary>The named provider fleet is more than one tile from the buying army.</summary>
    public static readonly RejectionCode ProviderFleetNotWithinRange = new("supply.provider-fleet-not-within-range");

    /// <summary>
    /// T50 Done-when 5 (issue #167): the provider city is more than one tile from the buying army.
    /// <c>TAFSupply_FindProviders</c> [confirmed: decompiled-unit-map-orders-and-record-fields.md] offers
    /// "every city within one tile", a gate this dialog's city-provider path never had. Named to match
    /// <see cref="Naval.Commands.BuyFleetSupplyRejections.CityNotWithinRange"/>, the fleet-buys-at-a-city
    /// twin that already enforces it.
    /// </summary>
    public static readonly RejectionCode CityNotWithinRange = new("supply.city-not-within-range");

    /// <summary>
    /// T50 Done-when 5 (issue #167): the provider city's owner is at war with the buying army's nation.
    /// Same confirmed <c>TAFSupply_FindProviders</c> gate ("whose owner is not at war with the buyer") as
    /// <see cref="CityNotWithinRange"/>'s own remarks. Named to match
    /// <see cref="Naval.Commands.BuyFleetSupplyRejections.CityOwnerAtWar"/>.
    /// </summary>
    public static readonly RejectionCode CityOwnerAtWar = new("supply.city-owner-at-war");
}

/// <summary>
/// Published by <see cref="BuySupplyCommandHandler"/> once a purchase from a fleet provider is accepted —
/// issue #147's folded direction, the army-side twin of
/// <c>IC2.Engine.Naval.Commands.FleetSupplyPurchased</c>. Not marked news-worthy, for the same reason as
/// <see cref="ArmySupplyPurchased"/>.
/// </summary>
/// <param name="ArmyId">The buying army.</param>
/// <param name="NationId">The buying army's nation.</param>
/// <param name="ProviderFleetId">The providing fleet.</param>
/// <param name="RequestedTons">What the command asked for.</param>
/// <param name="AdmittedTons">What was actually transferred.</param>
[DomainEvent("economy.army-supply-purchased-from-fleet")]
public sealed record ArmySupplyPurchasedFromFleet(
    string ArmyId,
    string NationId,
    string ProviderFleetId,
    int RequestedTons,
    int AdmittedTons) : DomainEvent;
