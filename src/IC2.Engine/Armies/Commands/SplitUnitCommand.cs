using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Splits <see cref="Troops"/> troops off one of an army's own unit slots into a new unit of the same
/// type, quality and regular-or-mercenary marker, still in the same army —
/// <c>docs/tasks/T107.md</c> "Change units: split, rename and disband a single unit", the original's
/// <c>TChangeArmyUnits</c> dialog's <c>SplitUnit</c> method (<c>TSplitArmyUnit_OK</c>,
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
/// report's naming sentence is about the <em>split-off</em> unit, so the new regular unit is the one
/// <see cref="ArmyNaming.NextSplitUnitName"/> names; the source keeps its own name. The split-only format
/// has the two spaces before "Battalion" measured in F51, while mobilization retains its single-space
/// format. A mercenary split keeps the source name and origin label. The original hire table at DAT
/// 0x1F8C6 is represented by <c>MercenaryNameCatalog</c> for new hires, but a mercenary may have been
/// renamed since hiring; a split copies that source name rather than resetting a player's name from its
/// label [confirmed table: <c>decompiled-mercenary-offer-list-and-position.md</c> §5; F09].
/// </para>
/// <para>
/// <strong>Original split size limits</strong> <c>[confirmed: code, 2026-10-11-g2-original-checks.md, C5c]</c>:
/// the source unit must contain at least two fifths of its type's standard battalion size to split; each
/// resulting unit must retain at least one fifth. The standard size comes from
/// <see cref="Model.UnitTypeRules.StandardBattalionSize"/>. For light infantry these limits are 6,000
/// source troops and 3,000 per resulting unit; the other four types have their own corresponding sizes.
/// Invalid inputs still return without changing state.
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
/// <param name="Troops">Troops moved into the new unit; both resulting units must meet the original type-specific one-fifth floor.</param>
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

    /// <summary>The requested split leaves either resulting unit below its type-specific one-fifth floor.</summary>
    public static readonly RejectionCode InvalidTroops = new("armies.split-unit-invalid-troops");

    /// <summary>The source unit is below two fifths of its type's standard battalion size.</summary>
    public static readonly RejectionCode TooSmall = new("armies.split-unit-too-small");

    /// <summary>The army already holds <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/> units.</summary>
    public static readonly RejectionCode TooManyUnits = new("armies.split-unit-units-too-large");

    /// <summary>#802 R39: the unit is a mercenary; the original splits only regular units.</summary>
    public static readonly RejectionCode NotRegular = new("armies.split-unit-not-regular");
}
