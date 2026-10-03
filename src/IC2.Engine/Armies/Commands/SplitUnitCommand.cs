using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Splits <see cref="Troops"/> troops off one of an army's own unit slots into a new unit of the same
/// type, quality and regular-or-mercenary marker, still in the same army —
/// <c>docs/tasks/T107.md</c> "Change units: split, rename and disband a single unit", the original's
/// <c>TChangeArmyUnits</c> dialog's <c>SplitArmy</c> method (<c>TSplitArmyUnit_OK</c>,
/// <c>0x004444CC</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The split-off unit inherits type, quality and marker</strong> <c>[confirmed:
/// decompiled-unit-map-orders-and-record-fields.md</c> §"Unit-level join/split"]: "A split unit inherits
/// type and quality and is auto-named with the next free ordinal for its type across all of the nation's
/// armies and the city garrison (<c>1st/2nd/3rd/Nth</c> + <c>Foot/Guards/Bowmen/Lancers/Dragoons</c> +
/// <c>Battalion</c>)". The regular-or-mercenary marker is the unit slot's <c>+0</c> word, this model's
/// <see cref="Model.UnitSlot.MercenaryLabel"/>; the report's sentence names type and quality only, and the
/// marker is inherited because every other field of the slot is — a split copies the slot, it does not
/// mint a new kind of unit.
/// </para>
/// <para>
/// <strong>There is no division rule.</strong> The order names the exact troop count to move, as the
/// original's dialog does with its own transfer steppers, so this is a requested amount rather than a
/// computed half (contrast <c>SplitArmyCommand</c>, which moves whole units).
/// </para>
/// <para>
/// <strong>Which part gets the auto-generated name, and what a mercenary split is called.</strong> The
/// report's naming sentence is about the <em>split-off</em> unit, so the new unit is the one
/// <see cref="ArmyNaming.NextName"/> names (the source keeps its own name and its own identity). For a
/// regular split that is the confirmed <c>"Nth &lt;label&gt; Battalion"</c> scheme. For a
/// <em>mercenary</em> split the report is silent and <see cref="ArmyNaming"/> cannot help: its whole design
/// is that a mercenary never takes a battalion number (see its remarks — the nation-wide scan skips every
/// non-regular unit). This task therefore leaves the split-off mercenary carrying the source unit's own
/// name, <c>[designed]</c>: I searched <c>decompiled-unit-map-orders-and-record-fields.md</c>,
/// <c>army-records-and-roman-roster.md</c> and <c>decompiled-mobilization-and-mercenary-restock.md</c> for
/// a mercenary naming rule (an ethnic-name generator, an ordinal, a suffix) and found none, so inventing
/// one would invent a rule; duplicating the source name is the one behaviour that adds no new scheme and
/// keeps the slot round-trippable.
/// </para>
/// <para>
/// <strong>The minimum and maximum troop counts are <c>[designed]</c></strong>, resolved on
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/565">issue #565</see> (user decision,
/// 2026-10-03): each part keeps at least 1 troop — <c>0 &lt; <see cref="Troops"/> &lt;</c> the source
/// unit's own troops. No other minimum. I searched
/// <c>decompiled-unit-map-orders-and-record-fields.md</c>, <c>pending-offer-block-army-split-and-naupactus.md</c>
/// and <c>army-records-and-roman-roster.md</c> for a per-part floor or a "can't leave an empty unit"
/// message and found none, so the boundary is the arithmetic one: a unit with zero troops is not a unit,
/// and moving all the troops is a move, not a split.
/// </para>
/// <para>
/// <strong>The army unit cap is reused, not restated.</strong> The split adds one slot, so it is refused
/// when the army already holds <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/> units — the same
/// limit <c>JoinUnitsCommandHandler</c>, <c>SplitArmyCommandHandler</c> and
/// <c>ArmyTransferCommandHandler</c> take from <c>ruleset.armyManagement</c>. No literal 20.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation issuing the order; the army must be its own.</param>
/// <param name="ArmyId">The army holding the unit being split.</param>
/// <param name="UnitIndex">Index into <see cref="Model.ArmyState.Units"/> of the unit being split.</param>
/// <param name="Troops">Troops moved into the new unit; <c>0 &lt; Troops &lt; the unit's own troops</c>.</param>
public sealed record SplitUnitCommand(
    string IssuingNationId, string ArmyId, int UnitIndex, int Troops) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "armies.split-unit";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class SplitUnitRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("armies.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("armies.not-your-army");

    /// <summary>The unit index is outside the army's own unit list.</summary>
    public static readonly RejectionCode InvalidUnitIndex = new("armies.invalid-unit-index");

    /// <summary><see cref="SplitUnitCommand.Troops"/> is zero, negative, or all of the unit's troops.</summary>
    public static readonly RejectionCode InvalidTroops = new("armies.split-unit-invalid-troops");

    /// <summary>The army already holds <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/> units.</summary>
    public static readonly RejectionCode TooManyUnits = new("armies.split-unit-units-too-large");
}
