using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Renames one of an army's own unit slots — <c>docs/tasks/T107.md</c> "Change units: split, rename and
/// disband a single unit", the original's <c>TChangeArmyUnits</c> dialog's <c>RenameUnit</c> method.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The report confirms the dialog renames individual units and is silent on the rule</strong>
/// <c>[confirmed: decompiled-unit-map-orders-and-record-fields.md</c>]: "Opens <c>TChangeArmyUnits</c> —
/// rename, split, join and disband individual units inside one army." No report states a maximum length, an
/// alphabet or whether free text is stored at all (the original's own names are type-plus-ordinal), so the
/// accepted length is fixed by what a save can hold rather than by a decompiled check —
/// <c>[designed]</c>, resolved on
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/565">issue #565</see> (user decision,
/// 2026-10-03). I searched <c>decompiled-unit-map-orders-and-record-fields.md</c>,
/// <c>army-records-and-roman-roster.md</c> and <c>decompiled-mobilization-and-mercenary-restock.md</c> for
/// a stated name cap and found none.
/// </para>
/// <para>
/// <strong>The cap is the save format's own field, not a ruleset value.</strong> The army unit slot is 32
/// bytes and its name is the 24-byte NUL-terminated ASCII field at <c>+8</c>
/// (<c>src/IC2.Data/SaveArmyTable.cs:100-111</c>, which searches exactly 24 bytes for the terminator and
/// rejects any byte outside <c>0x20..0x7e</c>). A 24-byte field holding a terminator therefore stores at
/// most <strong>23</strong> characters, which is the named constant the handler enforces. This is why the
/// command cannot live behind a ruleset field: the limit is a property of the file format the engine must
/// round-trip, and <c>src/IC2.Data</c> is not this task's to edit.
/// </para>
/// <para>
/// <strong>A rename feeds <see cref="ArmyNaming.NextName"/>'s scan, because that scan reads the ordinal
/// out of the name.</strong> A rename can therefore duplicate an existing battalion name, raise the next
/// ordinal — renaming a unit to <c>"98th Foot Battalion"</c> makes the next split <c>"99th"</c> — or free
/// the highest ordinal for reuse by renaming that unit away. This is what the original's own name-parsing
/// scan implies, pending the EXPLORE experiment.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation issuing the order; the army must be its own.</param>
/// <param name="ArmyId">The army holding the unit being renamed.</param>
/// <param name="UnitIndex">Index into <see cref="Model.ArmyState.Units"/> of the unit being renamed.</param>
/// <param name="Name">The new name: non-empty, printable ASCII (<c>0x20..0x7e</c>), at most 23 characters, with no leading or trailing whitespace.</param>
public sealed record RenameUnitCommand(
    string IssuingNationId, string ArmyId, int UnitIndex, string Name) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "armies.rename-unit";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class RenameUnitRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("armies.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("armies.not-your-army");

    /// <summary>The unit index is outside the army's own unit list.</summary>
    public static readonly RejectionCode InvalidUnitIndex = new("armies.invalid-unit-index");

    /// <summary>The name is empty, has leading or trailing whitespace, is longer than 23 characters, or has a byte outside <c>0x20..0x7e</c>.</summary>
    public static readonly RejectionCode InvalidName = new("armies.rename-unit-invalid-name");
}
