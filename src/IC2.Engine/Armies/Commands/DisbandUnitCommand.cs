using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Disbands one of an army's own unit slots — <c>docs/tasks/T107.md</c> "Change units: split, rename and
/// disband a single unit", the original's <c>TChangeArmyUnits</c> dialog's <c>Disband</c> method. The
/// unit's troops are lost; nothing is refunded.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The dialog confirms a per-unit disband exists; the report is silent on the refund and on an
/// army's last unit</strong> <c>[confirmed: decompiled-unit-map-orders-and-record-fields.md</c>]: "Opens
/// <c>TChangeArmyUnits</c> — rename, split, join and disband individual units inside one army." Nothing in
/// that report or in <c>army-to-army-transfer-confirmed.md</c> decompiles <c>Disband</c>'s own body, so
/// both open points are <c>[designed]</c>, resolved on
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/565">issue #565</see> (user decision,
/// 2026-10-03). I searched <c>decompiled-unit-map-orders-and-record-fields.md</c>,
/// <c>army-to-army-transfer-confirmed.md</c>, <c>pending-offer-block-army-split-and-naupactus.md</c> and
/// <c>army-records-and-roman-roster.md</c> for a refund sentence or a last-unit branch and found none.
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
/// <strong><c>RemoveUnit</c> is not built.</strong> The report's opening list names ten
/// <c>TUnitMap_*</c> methods including <c>ChangeUnitDetails</c>, and the task entry's Scope names
/// <c>RemoveUnit</c> as a fifth dialog method distinct from <c>Disband</c>, but no report decompiles it and
/// the audit's "Change unit details" row names only the four. With no evidence of what it does, this task
/// leaves it unbuilt rather than guessing (see the task's PR body).
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
