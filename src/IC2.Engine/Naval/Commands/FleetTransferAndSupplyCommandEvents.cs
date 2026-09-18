using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Published by <see cref="FleetToFleetTransferCommandHandler"/> once a transfer is accepted. Not marked
/// news-worthy — see <c>IC2.Engine.Economy.Commands.ArmySupplyPurchased</c>'s remarks; the same reasoning
/// applies here.
/// </summary>
/// <param name="NationId">The issuing (and both fleets') nation.</param>
/// <param name="FromFleetId">The source fleet.</param>
/// <param name="ToFleetId">The target fleet.</param>
/// <param name="ShipsMoved">Ships moved from source to target.</param>
/// <param name="SupplyTonsMoved">
/// Supply tons moved — the requested amount on an ordinary transfer, or the source's <em>entire</em>
/// remaining stock when <paramref name="SourceDisbanded"/> pooled it (Done-when 3).
/// </param>
/// <param name="MoneyMoved">Money moved — see <paramref name="SupplyTonsMoved"/>'s remarks on disbanding.</param>
/// <param name="SourceDisbanded">Whether the source fleet reached zero ships and was removed.</param>
[DomainEvent("naval.fleet-to-fleet-transfer-completed")]
public sealed record FleetToFleetTransferCompleted(
    string NationId,
    string FromFleetId,
    string ToFleetId,
    int ShipsMoved,
    int SupplyTonsMoved,
    int MoneyMoved,
    bool SourceDisbanded) : DomainEvent;

/// <summary>
/// Published by <see cref="BuyFleetSupplyCommandHandler"/> once a fleet's supply purchase is accepted —
/// the naval twin of <c>IC2.Engine.Economy.Commands.ArmySupplyPurchased</c>. Exactly one of
/// <see cref="ProviderCityId"/>/<see cref="ProviderFleetId"/> is set, matching
/// <see cref="BuyFleetSupplyCommand"/>'s own provider choice. Not marked news-worthy, for the same reason
/// as its army twin.
/// </summary>
/// <param name="FleetId">The buying fleet.</param>
/// <param name="NationId">The buying fleet's nation.</param>
/// <param name="ProviderCityId">The selling city, or <see langword="null"/> on the fleet-provider path.</param>
/// <param name="ProviderFleetId">The providing fleet, or <see langword="null"/> on the city-provider path.</param>
/// <param name="RequestedTons">What the command asked for.</param>
/// <param name="AdmittedTons">What was actually transferred.</param>
/// <param name="TalentsPaid">
/// Talents debited — zero for a free, own-city purchase, and always zero on the fleet-provider path
/// (Done-when 8, <c>[open]</c>; see <see cref="BuyFleetSupplyCommand"/>'s remarks).
/// </param>
/// <param name="WasFreeOwnCity">
/// Whether this purchase took a free path — the own-city case on the city-provider branch, or always
/// <see langword="true"/> on the fleet-provider branch.
/// </param>
[DomainEvent("naval.fleet-supply-purchased")]
public sealed record FleetSupplyPurchased(
    string FleetId,
    string NationId,
    string? ProviderCityId,
    string? ProviderFleetId,
    int RequestedTons,
    int AdmittedTons,
    int TalentsPaid,
    bool WasFreeOwnCity) : DomainEvent;
