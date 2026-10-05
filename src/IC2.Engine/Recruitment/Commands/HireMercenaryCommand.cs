using IC2.Engine.Core;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// Hires one offer from the 50-slot mercenary pool into the issuing nation's own army, gated by that
/// army's own money purse, never the national treasury — <c>docs/task-catalogue.md</c> "T13 Recruitment
/// and mercenaries", Done-when 2.
/// </summary>
/// <remarks>
/// Wraps <c>TRecruitMercs_RecruitMercUnit</c> (<c>0x00441360</c>)
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong> for the <c>Label</c>
/// copy. On acceptance the pool slot is consumed (modelled as
/// removed from <see cref="Model.GameState.MercenaryPool"/>, the same "empty slots are simply absent"
/// convention <see cref="Model.MercenaryPoolSlot"/>'s own remarks describe for this model)
/// <strong>[confirmed: mercenary-pool-record.md's <c>winter_3</c> save pair, and in code by
/// decompiled-fleet-tax-and-mercenary-formulas.md's <c>0xFFFF</c> sentinel write]</strong>. The hired
/// unit is appended to the army with its pool <c>Label</c> copied into the new
/// <see cref="Model.UnitSlot.MercenaryLabel"/> marker; <strong>no purse and no treasury moves</strong>.
/// <see cref="MercenaryHireCost.Compute"/> is the <em>minimum</em> the army's own
/// <see cref="Model.ArmyState.Money"/> must hold for the hire to be allowed, not a charge: T143
/// (bug #755) turns the one-time debit the clone used to apply into the gate the original has, and the
/// first money a mercenary costs is its quarterly <c>ArmyUpkeep</c> pay
/// <strong>[derived: code, <c>TRecruitMercs_RecruitMercUnit</c>: the gate is at :43633-43639 and nothing
/// in :43618-43701 writes the purse or the treasury]</strong>.
/// <para>
/// <strong>T76 adds the position gate the original has</strong>
/// <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §1, §2]</strong>: the ordering
/// command <c>TUnitMap_RecruitMercenaries</c> calls <c>FUN_00449D08</c>, which takes the first live
/// offer <em>in slot order</em> at Chebyshev distance <strong>exactly 1</strong> from the army and uses
/// that offer's city; the dialog then lists only the live offers on that one city's tile. An army that
/// is not adjacent to any offer's city does nothing at all — no message. The handler replays that shape:
/// it rejects with <see cref="HireMercenaryRejections.NoAdjacentOffer"/> when no live offer is adjacent,
/// with <see cref="HireMercenaryRejections.OfferNotAdjacentCity"/> when the named offer is not on the
/// chosen city's tile, and with <see cref="HireMercenaryRejections.EnemyCity"/> when that city's owner
/// is at war with the hiring nation (the order's own <c>relation[city.owner][me] == 3</c> refusal).
/// </para>
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
    /// No live offer lies at Chebyshev distance exactly 1 from the army — <c>FUN_00449D08</c> returned
    /// no city, so the original's order does nothing and shows no message
    /// <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §1]</strong>.
    /// </summary>
    public static readonly RejectionCode NoAdjacentOffer = new("mercenary.no-adjacent-offer");

    /// <summary>
    /// The named offer is live and its tile may even be adjacent, but it is not on the <em>chosen</em>
    /// city's tile — the city of the first live offer in slot order at distance 1. The original's dialog
    /// lists only offers on that one city's tile, so a second adjacent offer city's offers cannot be
    /// picked <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §1, §2]</strong>.
    /// </summary>
    public static readonly RejectionCode OfferNotAdjacentCity = new("mercenary.offer-not-on-adjacent-city");

    /// <summary>
    /// The offer's city owner is at war with the hiring nation — the order's own
    /// <c>relation[city.owner][me] == 3</c> refusal, <em>"You cannot recruit from an enemy city."</em>
    /// <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §1]</strong>.
    /// </summary>
    public static readonly RejectionCode EnemyCity = new("mercenary.enemy-city");

    /// <summary>
    /// Bug #769: the army holds too few supplies for any mercenary to join it — the order's own
    /// <c>army.supplies*10000/total &lt; 15</c> refusal, <em>"No mercenaries will join an army with so
    /// few supplies."</em> <strong>[confirmed: decompiled-mercenary-offer-list-and-position.md §1,
    /// <c>TUnitMap_RecruitMercenaries</c> @ <c>0x00446FF4</c>, :46858–46925]</strong>. The code string is
    /// <strong>[designed]</strong>, the refusal itself is the original's. An army with no troops is
    /// refused the same way without dividing <strong>[designed: the original's <c>div</c> by zero is no
    /// rule to copy]</strong>.
    /// </summary>
    public static readonly RejectionCode TooFewSupplies = new("mercenary.too-few-supplies");

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

    /// <summary>
    /// Hiring would push the army's unit count past <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/>
    /// (20) — the same cap <c>TUnitMap_JoinArmies</c> enforces on army join
    /// (<c>decompiled-unit-map-orders-and-record-fields.md</c>). Added by T15 ("Army and unit management",
    /// Done-when 6, issue #181): the field was read by no code anywhere in <c>src/</c>, and T13's
    /// reviewer proved an army already holding 20 units accepts a hire and ends at 21, a shape the
    /// original's 20-slot army record cannot hold.
    /// </summary>
    /// <remarks>
    /// T70 (#212 N1): moved here from its own <c>HireMercenaryUnitCapRejections</c> class in
    /// <c>HireMercenaryCommandHandler.cs</c> — this command's rejection codes lived in two files, one per
    /// task that added them, with nothing forcing the split once both tasks had merged. The code string
    /// is unchanged, so no save or test that already matched on it needed to change.
    /// </remarks>
    public static readonly RejectionCode OverArmyUnitCap = new("mercenary.over-army-unit-cap");
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
/// <param name="HireGate">
/// The minimum purse the hiring army's own money had to hold for the hire to pass —
/// <see cref="MercenaryHireCost.Compute"/>'s value. <strong>Not money paid</strong>: an accepted hire
/// debits no purse and no treasury (T143, bug #755). The field is named for the gate so no reader can
/// take it for a debit; the type and the domain-event name are unchanged.
/// </param>
[DomainEvent("recruitment.mercenary-hired")]
public sealed record MercenaryHired(
    string NationId,
    string ArmyId,
    int PoolSlotIndex,
    string UnitTypeId,
    int Troops,
    int Quality,
    int HireGate) : DomainEvent;
