using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Joins two or more of an army's own unit slots into one — <c>docs/task-catalogue.md</c> "T15 Army and unit
/// management", Done-when 4, generalised to any number of units by T164: requires the same type, regulars
/// only (the mercenary marker blocks it), and the summed troops at most the type's battalion size.
/// </summary>
/// <remarks>
/// <c>TChangeArmyUnits_JoinUnits</c> (<c>0x00444E8C</c>) <strong>[confirmed:
/// decompiled-unit-map-orders-and-record-fields.md]</strong>: "only regular units may be joined
/// (<em>"You can only join regular units together."</em>) — <see cref="Recruitment.UnitMergeGuard.IsMergeAllowedByMarker"/>
/// is the marker half of this rule, T13's own Owns list, exposed for exactly this call rather than
/// re-derived — only units of the same type (<em>"You can only combine units of the same type."</em>),
/// and the combined troop count must not exceed the type's standard battalion size — unit-type-table
/// field <c>+0x1A</c> (<see cref="Model.UnitTypeRules.StandardBattalionSize"/>)."
/// <para>
/// <strong>Every selected unit joins</strong> <strong>[confirmed: Wine, li]</strong> <strong>[code:
/// <c>TChangeArmyUnits_JoinUnits</c>, dump 45584-45683, every type]</strong>
/// (<c>2026-10-11-g2-original-checks.md</c> C5 and correction 2): the original sums every selected unit,
/// keeps the first selected slot, removes the others, and sets the kept unit's quality to the integer
/// average of all the selected units' qualities. It refuses the whole order, never a partial join.
/// </para>
/// <para>
/// <strong>The average's rounding, <c>[derived]</c>.</strong> No report observed it (every staged unit had
/// quality 6). This follows the project's own "the original truncates at every step" pattern
/// (<c>docs/build-process.md</c> §4.2 gate 5) and uses plain C# integer division, <c>sum / n</c>,
/// truncating toward zero (the same as flooring for positive tiers); for two units it is today's
/// <c>(a + b) / 2</c>. Searched: the report's C5 and correction 2 (no non-uniform quality staged).
/// </para>
/// <para>
/// The surviving unit is the first of <paramref name="UnitIndexes"/>; it keeps its own name, type and
/// mercenary marker (always 0), and its troops and quality are replaced by the merge. The other named
/// slots are removed. The list is a <see cref="ValueList{T}"/> so the record keeps value equality.
/// </para>
/// </remarks>
/// <param name="IssuingNationId">The nation issuing the command.</param>
/// <param name="ArmyId">The army whose unit slots are joined.</param>
/// <param name="UnitIndexes">Indexes into <see cref="Model.ArmyState.Units"/>, two or more, distinct; the first survives.</param>
public sealed record JoinUnitsCommand(
    string IssuingNationId, string ArmyId, ValueList<int> UnitIndexes) : ICommand
{
    /// <summary>The two-unit form: <paramref name="firstUnitIndex"/> survives, <paramref name="secondUnitIndex"/> is absorbed.</summary>
    public JoinUnitsCommand(string issuingNationId, string armyId, int firstUnitIndex, int secondUnitIndex)
        : this(issuingNationId, armyId, ValueList.From(new[] { firstUnitIndex, secondUnitIndex }))
    {
    }

    /// <summary>The general form over any selection; the first index survives.</summary>
    public JoinUnitsCommand(string issuingNationId, string armyId, IEnumerable<int> unitIndexes)
        : this(issuingNationId, armyId, ValueList.From(unitIndexes))
    {
    }

    /// <inheritdoc/>
    public string Kind => "armies.join-units";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class JoinUnitsRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("armies.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("armies.not-your-army");

    /// <summary>Fewer than two indices, an index out of range, or the same slot named twice.</summary>
    public static readonly RejectionCode InvalidUnitIndex = new("armies.invalid-unit-index");

    /// <summary><em>"You can only join regular units together."</em></summary>
    public static readonly RejectionCode MercenaryUnit = new("armies.mercenary-unit");

    /// <summary><em>"You can only combine units of the same type."</em></summary>
    public static readonly RejectionCode DifferentUnitTypes = new("armies.different-unit-types");

    /// <summary>Combined troops would exceed the type's <see cref="Model.UnitTypeRules.StandardBattalionSize"/>.</summary>
    public static readonly RejectionCode OverBattalionSize = new("armies.over-battalion-size");
}
