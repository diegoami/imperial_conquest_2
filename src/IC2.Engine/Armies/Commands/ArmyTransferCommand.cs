using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Moves the listed units, and the given supply and money, from one of the issuing nation's own armies
/// to another at Chebyshev distance exactly 1 — <c>docs/tasks/T106.md</c> "Army-to-army transfer of units,
/// supply and money", the original's <c>TUnitMap_ArmyToArmyTransfer</c> / <c>TArmyToArmy</c> dialog.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The original moves units, supply and money in one dialog, and conserves them exactly</strong>
/// [confirmed: <c>army-to-army-transfer-confirmed.md</c>] — six units (27,705 troops) and 70 talents moved
/// Rome army 2 → army 0 in one sitting, and both troops and money were exactly reciprocal. To move the
/// other way, swap <see cref="FromArmyId"/> and <see cref="ToArmyId"/>; the dialog's <c>10s</c>/<c>100s</c>
/// steppers are a UI detail of the same direction-per-call shape.
/// </para>
/// <para>
/// <strong>The receiving army keeps the army rules the Join armies limits come from</strong>
/// [confirmed: <c>decompiled-unit-map-orders-and-record-fields.md</c>]: at most
/// <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/> units and
/// <see cref="Model.ArmyManagementRules.MaxTroopsPerArmy"/> troops. Supply capacity and the purse cap are
/// reused from where the engine already computes them
/// (<see cref="Economy.SupplyCapacity.ArmyDialogCapacityTons"/>,
/// <see cref="Model.EconomyRules.PurseCapPerUnit"/>), never re-derived here. Whether <c>TArmyToArmy</c>
/// itself enforces any of these is <c>[open]</c> — neither report reads a cap in its code — so these are
/// the task's own enforcement, not a claim about the original.
/// </para>
/// <para>
/// <strong>An army left with no units is merged and disbanded</strong> [confirmed from code:
/// <c>army-to-army-transfer-confirmed.md</c>, the <c>OK</c> paragraph; the branch was not exercised in the
/// observed saves]. Its supply and money pool into the other army, exactly as
/// <c>JoinArmiesCommandHandler</c> pools them, and the emptied army is removed.
/// </para>
/// <para>
/// <strong>Not in scope.</strong> The report's supply-rebalancing branch in <c>TArmyToArmy_OK</c>
/// (<c>FUN_0044a698</c>), not observed triggering, is not implemented; the dialog's single-unit Disband is
/// T107's <c>disband-unit</c>. An army aboard a fleet is not addressed — the reports are silent and the
/// task leaves it to the distance rule (see this task's PR body).
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation issuing the order; both armies must be its own.</param>
/// <param name="FromArmyId">The army giving up the listed units, supply and money.</param>
/// <param name="ToArmyId">The receiving army, at Chebyshev distance exactly 1 from the source.</param>
/// <param name="UnitIndexes">
/// Indexes into <paramref name="FromArmyId"/>'s own <see cref="Model.ArmyState.Units"/> list, moved in the
/// source list's order. May be empty when only supply or money moves.
/// </param>
/// <param name="SupplyTons">Supply tons to move; non-negative and at most the source's own stock.</param>
/// <param name="Money">Talents to move; non-negative and at most the source's own purse.</param>
public sealed record ArmyTransferCommand(
    string IssuingNationId,
    string FromArmyId,
    string ToArmyId,
    ValueList<int> UnitIndexes,
    int SupplyTons,
    int Money) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "armies.army-transfer";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class ArmyTransferRejections
{
    /// <summary>The two named army ids are the same army.</summary>
    public static readonly RejectionCode SameArmy = new("armies.army-transfer-same-army");

    /// <summary>Either named army id does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("armies.army-transfer-unknown-army");

    /// <summary>Either named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("armies.army-transfer-not-your-army");

    /// <summary>
    /// The two armies are not at Chebyshev distance exactly 1 — the original's partner rule
    /// [derived: code; audit §1.6]. Distance 0 and distance 2 are both rejected.
    /// </summary>
    public static readonly RejectionCode NotAdjacent = new("armies.army-transfer-not-adjacent");

    /// <summary>A requested unit index is outside the source army's own unit list.</summary>
    public static readonly RejectionCode UnknownUnitIndex = new("armies.army-transfer-unknown-unit-index");

    /// <summary>The same unit index is listed more than once.</summary>
    public static readonly RejectionCode DuplicateUnitIndex = new("armies.army-transfer-duplicate-unit-index");

    /// <summary>A requested amount is negative, or the order moves nothing at all.</summary>
    public static readonly RejectionCode InvalidAmount = new("armies.army-transfer-invalid-amount");

    /// <summary>The source army does not have enough supply to move the requested tons.</summary>
    public static readonly RejectionCode InsufficientSupply = new("armies.army-transfer-insufficient-supply");

    /// <summary>The source army does not have enough money to move the requested talents.</summary>
    public static readonly RejectionCode InsufficientMoney = new("armies.army-transfer-insufficient-money");

    /// <summary>
    /// The receiving army would hold more than
    /// <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/> units — the Join armies limit.
    /// </summary>
    public static readonly RejectionCode CombinedUnitsTooLarge = new("armies.army-transfer-units-too-large");

    /// <summary>
    /// The receiving army would hold more than
    /// <see cref="Model.ArmyManagementRules.MaxTroopsPerArmy"/> troops — the Join armies limit.
    /// </summary>
    public static readonly RejectionCode CombinedTroopsTooLarge = new("armies.army-transfer-troops-too-large");

    /// <summary>
    /// The receiving army's supply would exceed its capacity
    /// (<see cref="Economy.SupplyCapacity.ArmyDialogCapacityTons"/>) after the transfer.
    /// </summary>
    public static readonly RejectionCode SupplyExceedsCapacity = new("armies.army-transfer-supply-too-large");

    /// <summary>
    /// The receiving army's purse would exceed <see cref="Model.EconomyRules.PurseCapPerUnit"/> after the
    /// transfer.
    /// </summary>
    public static readonly RejectionCode PurseCapExceeded = new("armies.army-transfer-purse-cap-exceeded");
}
