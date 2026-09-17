using IC2.Engine.Core;

namespace IC2.Engine.Economy.Commands;

/// <summary>
/// Published by <see cref="BuySupplyCommandHandler"/> once a purchase is accepted. Not marked
/// news-worthy — see <see cref="Movement.Commands.ArmyMoved"/>'s remarks; the same reasoning applies here.
/// </summary>
/// <param name="ArmyId">The buying army.</param>
/// <param name="NationId">The buying army's nation.</param>
/// <param name="CityId">The selling city.</param>
/// <param name="RequestedTons">What the command asked for.</param>
/// <param name="AdmittedTons">
/// What was actually transferred — <see cref="SupplyPurchase.ArmyResult"/>'s dialog-capacity clamp can
/// admit less than requested.
/// </param>
/// <param name="TalentsPaid">Talents debited — zero for a free, own-city purchase.</param>
/// <param name="WasFreeOwnCity">Whether this purchase took the free, own-city path.</param>
[DomainEvent("economy.army-supply-purchased")]
public sealed record ArmySupplyPurchased(
    string ArmyId,
    string NationId,
    string CityId,
    int RequestedTons,
    int AdmittedTons,
    int TalentsPaid,
    bool WasFreeOwnCity) : DomainEvent;
