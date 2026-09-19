using IC2.Engine.Core;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// Hires one offer from the 50-slot mercenary pool into the issuing nation's own army, paid from that
/// army's own money purse, never the national treasury — <c>docs/task-catalogue.md</c> "T13 Recruitment
/// and mercenaries", Done-when 2.
/// </summary>
/// <remarks>
/// Wraps <c>TRecruitMercs_RecruitMercUnit</c> (<c>0x00441360</c>)
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong> for the hire cost, the
/// army-purse debit and the <c>Label</c> copy. On acceptance the pool slot is consumed (modelled as
/// removed from <see cref="Model.GameState.MercenaryPool"/>, the same "empty slots are simply absent"
/// convention <see cref="Model.MercenaryPoolSlot"/>'s own remarks describe for this model)
/// <strong>[confirmed: mercenary-pool-record.md's <c>winter_3</c> save pair, and in code by
/// decompiled-fleet-tax-and-mercenary-formulas.md's <c>0xFFFF</c> sentinel write]</strong>. The hired
/// unit is appended to the army with its pool <c>Label</c> copied into the new
/// <see cref="Model.UnitSlot.MercenaryLabel"/> marker, and <see cref="MercenaryHireCost.Compute"/> is
/// debited from the army's own <see cref="Model.ArmyState.Money"/>.
/// </remarks>
/// <param name="ArmyId">The hiring army — must be the issuing nation's own.</param>
/// <param name="PoolSlotIndex">
/// The offer's <see cref="Model.MercenaryPoolSlot.SlotIndex"/> in <see cref="Model.GameState.MercenaryPool"/>.
/// </param>
public sealed record HireMercenaryCommand(
    string IssuingNationId, string ArmyId, int PoolSlotIndex) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "recruitment.hire-mercenary";
}

/// <summary>Rejection codes <see cref="HireMercenaryCommandHandler"/> declares.</summary>
public static class HireMercenaryRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("mercenary.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("mercenary.not-your-army");

    /// <summary>
    /// <see cref="HireMercenaryCommand.PoolSlotIndex"/> names no occupied slot in
    /// <see cref="Model.GameState.MercenaryPool"/> — never offered, or already hired/expired.
    /// </summary>
    public static readonly RejectionCode UnknownPoolSlot = new("mercenary.unknown-pool-slot");

    /// <summary>
    /// The hiring army's own purse cannot afford <see cref="MercenaryHireCost.Compute"/> — the confirmed
    /// dialog refusal <em>"Your army has too little money to pay these mercenaries."</em>
    /// (<c>tests/fixtures/corpus.json</c> <c>error.mercenaryInsufficientMoney</c>).
    /// </summary>
    public static readonly RejectionCode InsufficientMoney = new("mercenary.insufficient-money");

    /// <summary>
    /// Hiring would push the army's total troops past <see cref="Model.ArmyManagementRules.MaxTroopsPerArmy"/>
    /// — the confirmed <em>"An army can not contain more than 100,000 troops."</em>
    /// (<c>tests/fixtures/corpus.json</c> <c>error.armyTooLarge100k</c>, also confirmed directly against
    /// <c>TRecruitMercs_RecruitMercUnit</c> in <c>decompiled-unit-map-orders-and-record-fields.md</c>:
    /// "Also confirmed: ... the 100,000-troop army cap").
    /// </summary>
    public static readonly RejectionCode OverArmyTroopCap = new("mercenary.over-army-troop-cap");

    /// <summary>
    /// The hiring army is embarked on a fleet with too little remaining capacity for the hired troops —
    /// the confirmed <em>"This fleet has too little space for these mercenaries."</em>
    /// (<c>tests/fixtures/corpus.json</c> <c>error.mercenaryFleetNoSpace</c>).
    /// </summary>
    public static readonly RejectionCode FleetNoSpace = new("mercenary.fleet-no-space");
}

/// <summary>Published by <see cref="HireMercenaryCommandHandler"/> once a hire is accepted.</summary>
/// <remarks>
/// <strong>Not marked news-worthy</strong> — see <see cref="RecruitmentOrdered"/>'s remarks; the same
/// exhaustive news-literal accounting has no entry for a mercenary hire either.
/// </remarks>
/// <param name="NationId">The hiring nation.</param>
/// <param name="ArmyId">The hiring army.</param>
/// <param name="PoolSlotIndex">The consumed pool slot.</param>
/// <param name="UnitTypeId">The hired unit's type.</param>
/// <param name="Troops">The hired unit's troop count.</param>
/// <param name="Quality">The hired unit's quality tier.</param>
/// <param name="TalentsPaid">Talents debited from the army's own purse.</param>
[DomainEvent("recruitment.mercenary-hired")]
public sealed record MercenaryHired(
    string NationId,
    string ArmyId,
    int PoolSlotIndex,
    string UnitTypeId,
    int Troops,
    int Quality,
    int TalentsPaid) : DomainEvent;
