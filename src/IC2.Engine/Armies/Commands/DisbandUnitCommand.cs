using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Disbands one of an army's own unit slots — <c>docs/tasks/T107.md</c> "Change units: split, rename and
/// disband a single unit", the original's <c>TChangeArmyUnits</c> dialog's <c>Disband</c> method. The
/// unit's troops are lost; nothing is refunded.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The dialog confirms a per-unit disband exists; what an army's last unit does here is still
/// open</strong> <c>[confirmed: decompiled-unit-map-orders-and-record-fields.md</c>]: "Opens
/// <c>TChangeArmyUnits</c> — rename, split, join and disband individual units inside one army." Nothing
/// in that report or in <c>army-to-army-transfer-confirmed.md</c> decompiles <c>TChangeArmyUnits</c>'s own
/// <c>Disband</c> body. The transfer report does record a last-unit branch, but in the sibling
/// <c>TArmyToArmy</c> dialog: after a transfer, if either army's unit count reads zero, <c>OK</c> "merges
/// that now-empty army's supply and money into the other and disbands it" (<c>FUN_0044ab90</c>). That
/// branch has a partner army to receive the stores and is not <c>TChangeArmyUnits</c>, so it does not
/// settle this dialog's last unit. The user's default (refuse) therefore stands as a <c>[designed]</c>
/// choice, resolved on
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/565">issue #565</see> (user decision,
/// 2026-10-03), pending the EXPLORE experiment.
/// </para>
/// <para>
/// <strong>No refund, and the last unit is refused rather than disbanding the army.</strong> The troops
/// are destroyed, exactly as the order's name implies. Disbanding an army's only unit is rejected and the
/// reason points the player at <c>disband-army</c>: an army is a map entity with supply, money and a
/// position, and the per-unit order should not silently become the army-deletion order. The confirmed
/// army-level rule is already <c>disband-army</c> (<c>TUnitMap_DisbandArmy</c>, which pools the army's
/// money and supplies); this command deliberately does neither.
/// </para>
/// <para>
/// <strong><c>RemoveUnit</c> lowers the nation's mobilisation</strong>
/// <c>[confirmed: code, 2026-10-03-army-to-army-ok-supply-rebalancing.md, "A disband inside the dialog
/// changes mobilisation", RemoveUnit :44466-44507]</c>: disbanding a <em>regular</em> unit lowers its
/// nation's mobilisation by <c>troops x 1000 div wealth + 1</c>, floored at 0 (the inverse of mobilising;
/// <c>MobilizationRate.AfterOrderCancelled</c>). A mercenary unit's disband changes no nation field. The
/// confirmed reading is the <em>Army to army transfer</em> dialog's Disband; whether <c>TChangeArmyUnits</c>'s
/// own Disband also lowers mobilisation is unread, and the rule is applied to this one verb by design
/// <c>[designed]</c>, because the clone cannot tell the two dialogs apart.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation issuing the order; the army must be its own.</param>
/// <param name="ArmyId">The army holding the unit being disbanded.</param>
/// <param name="UnitIndex">Index into <see cref="Model.ArmyState.Units"/> of the unit being disbanded.</param>
public sealed record DisbandUnitCommand(
    string IssuingNationId, string ArmyId, int UnitIndex) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "armies.disband-unit";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class DisbandUnitRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("armies.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("armies.not-your-army");

    /// <summary>The unit index is outside the army's own unit list.</summary>
    public static readonly RejectionCode InvalidUnitIndex = new("armies.invalid-unit-index");

    /// <summary>The named unit is the army's only unit — use <c>disband-army</c> instead.</summary>
    public static readonly RejectionCode LastUnit = new("armies.disband-unit-last-unit");
}
