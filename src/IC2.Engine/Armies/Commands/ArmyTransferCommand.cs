using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Moves the listed units, and the given supply and money, between two of the issuing nation's own armies
/// at Chebyshev distance exactly 1 — <c>docs/tasks/T106.md</c> "Army-to-army transfer of units, supply and
/// money", corrected by <c>docs/tasks/T117.md</c> to the original's <c>TUnitMap_ArmyToArmyTransfer</c> /
/// <c>TArmyToArmy</c> dialog and its single <c>OK</c> commit. The command carries both directions of that
/// one dialog: <see cref="UnitIndexes"/>/<see cref="SupplyTons"/>/<see cref="Money"/> go from
/// <see cref="FromArmyId"/> (A, the selected army) to <see cref="ToArmyId"/> (B, its partner), and the
/// <c>Back…</c> fields go from B to A.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One command is one dialog <c>OK</c>, both ways</strong> [designed: the user's decision of
/// 2026-10-03, with the code evidence below; one <c>OK</c> in the original commits the whole dialog].
/// In the original A is the army selected when the order was given and B is its partner, and one
/// <c>OK</c> commits every change at once; here A is the first named army and B the second. An order with
/// only the A-to-B options is exactly T106's order, so every existing script line still parses.
/// </para>
/// <para>
/// <strong>The original moves units, supply and money in one dialog, and conserves them exactly</strong>
/// [confirmed: <c>army-to-army-transfer-confirmed.md</c>] — six units (27,705 troops) and 70 talents moved
/// Rome army 2 → army 0 in one sitting, and both troops and money were exactly reciprocal.
/// </para>
/// <para>
/// <strong>After the move, <c>OK</c> rebalances supply and merges an emptied army</strong> [confirmed:
/// <c>2026-10-03-army-to-army-ok-supply-rebalancing.md</c> (research <c>a380a8e</c>), <c>TArmyToArmy_OK</c>
/// <c>:44572–44649</c>]. With <c>capA = troops(A) div 100</c> and <c>capB = troops(B) div 100</c>
/// (<see cref="Economy.SupplyCapacity.ArmyCapacityTons"/>, no dialog bonus): A's supply above <c>capA</c>
/// goes to B, then B's above <c>capB</c> goes back to A. So supply is conserved exactly, and when both end
/// over capacity B ends at exactly <c>capB</c> while A keeps everything else, above its own capacity. An
/// army left with no units is merged into the other and disbanded, taking all its supply and money
/// uncapped. The steps run once, after both directions are written, on every accepted transfer — a
/// money-only or unit-only order included. <c>OK</c> refuses nothing on capacity: the dialog's supply
/// spinner room, <c>troops div 100 + 1</c>, is that dialog's own bound, not this command's.
/// </para>
/// <para>
/// <strong>The 20-unit and 100,000-troop limits are the Join armies rules</strong> [confirmed:
/// <c>decompiled-unit-map-orders-and-record-fields.md</c>]: at most
/// <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/> units and
/// <see cref="Model.ArmyManagementRules.MaxTroopsPerArmy"/> troops, applied to each army's composition
/// after both directions. They and <see cref="Model.EconomyRules.PurseCapPerUnit"/> never re-derive a
/// literal here. Money is never rebalanced: the purse cap is the dialog's money stepper,
/// <c>min(step, 1000 − money(receiver), money(giver))</c>, so it bounds only money moved between two
/// surviving armies, and the emptied-army merge pools a purse uncapped.
/// </para>
/// <para>
/// <strong>Not in scope.</strong> The dialog's single-unit Disband is T107's <c>disband-unit</c>. An army
/// aboard a fleet is not addressed — the reports are silent and the task leaves it to the distance rule
/// (see this task's PR body); only the now-dangling back-reference is dropped.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation issuing the order; both armies must be its own.</param>
/// <param name="FromArmyId">A: the army giving up the listed units, supply and money, at Chebyshev distance exactly 1 from B.</param>
/// <param name="ToArmyId">B: the receiving army, and the giver of the <c>Back…</c> fields.</param>
/// <param name="UnitIndexes">
/// Indexes into <paramref name="FromArmyId"/>'s own <see cref="Model.ArmyState.Units"/> list, moved A → B in
/// the source list's order. May be empty when only supply or money moves.
/// </param>
/// <param name="SupplyTons">Supply tons to move A → B; non-negative and at most A's own stock.</param>
/// <param name="Money">Talents to move A → B; non-negative and at most A's own purse.</param>
/// <param name="BackUnitIndexes">Indexes into B's own unit list, moved B → A, in B's list order.</param>
/// <param name="BackSupplyTons">Supply tons to move B → A; non-negative and at most B's own stock.</param>
/// <param name="BackMoney">Talents to move B → A; non-negative and at most B's own purse.</param>
public sealed record ArmyTransferCommand(
    string IssuingNationId,
    string FromArmyId,
    string ToArmyId,
    ValueList<int> UnitIndexes,
    int SupplyTons,
    int Money,
    ValueList<int> BackUnitIndexes,
    int BackSupplyTons,
    int BackMoney) : ICommand
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

    /// <summary>A requested unit index is outside the giving army's own unit list.</summary>
    public static readonly RejectionCode UnknownUnitIndex = new("armies.army-transfer-unknown-unit-index");

    /// <summary>The same unit index is listed more than once.</summary>
    public static readonly RejectionCode DuplicateUnitIndex = new("armies.army-transfer-duplicate-unit-index");

    /// <summary>A requested amount is negative, or the order moves nothing at all.</summary>
    public static readonly RejectionCode InvalidAmount = new("armies.army-transfer-invalid-amount");

    /// <summary>A giving army does not have enough supply to move the requested tons.</summary>
    public static readonly RejectionCode InsufficientSupply = new("armies.army-transfer-insufficient-supply");

    /// <summary>A giving army does not have enough money to move the requested talents.</summary>
    public static readonly RejectionCode InsufficientMoney = new("armies.army-transfer-insufficient-money");

    /// <summary>
    /// Either army would hold more than
    /// <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/> units after both directions — the Join
    /// armies limit.
    /// </summary>
    public static readonly RejectionCode CombinedUnitsTooLarge = new("armies.army-transfer-units-too-large");

    /// <summary>
    /// Either army would hold more than
    /// <see cref="Model.ArmyManagementRules.MaxTroopsPerArmy"/> troops after both directions — the Join
    /// armies limit.
    /// </summary>
    public static readonly RejectionCode CombinedTroopsTooLarge = new("armies.army-transfer-troops-too-large");

    /// <summary>
    /// An army that receives money between two surviving armies would exceed
    /// <see cref="Model.EconomyRules.PurseCapPerUnit"/> — the dialog's money stepper.
    /// </summary>
    public static readonly RejectionCode PurseCapExceeded = new("armies.army-transfer-purse-cap-exceeded");
}
