using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Joins two of an army's own unit slots into one — <c>docs/task-catalogue.md</c> "T15 Army and unit
/// management", Done-when 4: requires the same type, regulars only (the mercenary marker blocks it), and
/// merged troops at most the type's battalion size; merged quality is the arithmetic mean.
/// </summary>
/// <remarks>
/// <c>TChangeArmyUnits_JoinUnits</c> (<c>0x00444E8C</c>) <strong>[confirmed:
/// decompiled-unit-map-orders-and-record-fields.md]</strong>: "only regular units may be joined
/// (<em>"You can only join regular units together."</em>) — <see cref="Recruitment.UnitMergeGuard.IsMergeAllowedByMarker"/>
/// is the marker half of this rule, T13's own Owns list, exposed for exactly this call rather than
/// re-derived — only units of the same type (<em>"You can only combine units of the same type."</em>),
/// and the combined troop count must not exceed the type's standard battalion size — unit-type-table
/// field <c>+0x1A</c> (<see cref="Model.UnitTypeRules.StandardBattalionSize"/>). The merged unit's
/// quality is the arithmetic mean of the merged units' qualities."
/// <para>
/// <strong>The mean's rounding, <c>[derived]</c>.</strong> No report states which way a non-integer mean
/// rounds. This follows the project's own "the original truncates at every step" pattern
/// (<c>docs/build-process.md</c> §4.2 gate 5) and uses plain C# integer division —
/// <c>(first.Quality + second.Quality) / 2</c>, truncating toward zero, which for two positive quality
/// tiers is the same as flooring.
/// </para>
/// <para>
/// The surviving unit is <paramref name="FirstUnitIndex"/>: it keeps its own name, type and mercenary
/// marker (always 0, since both must be regular to reach this far), and its troops and quality are
/// replaced by the merge. <paramref name="SecondUnitIndex"/> is removed from the army.
/// </para>
/// </remarks>
/// <param name="ArmyId">The army whose two unit slots are joined.</param>
/// <param name="FirstUnitIndex">Index into <see cref="Model.ArmyState.Units"/> of the surviving unit.</param>
/// <param name="SecondUnitIndex">Index into <see cref="Model.ArmyState.Units"/> of the absorbed unit.</param>
public sealed record JoinUnitsCommand(
    string IssuingNationId, string ArmyId, int FirstUnitIndex, int SecondUnitIndex) : ICommand
{
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

    /// <summary>Either unit index is out of range, or the two indices name the same slot.</summary>
    public static readonly RejectionCode InvalidUnitIndex = new("armies.invalid-unit-index");

    /// <summary><em>"You can only join regular units together."</em></summary>
    public static readonly RejectionCode MercenaryUnit = new("armies.mercenary-unit");

    /// <summary><em>"You can only combine units of the same type."</em></summary>
    public static readonly RejectionCode DifferentUnitTypes = new("armies.different-unit-types");

    /// <summary>Combined troops would exceed the type's <see cref="Model.UnitTypeRules.StandardBattalionSize"/>.</summary>
    public static readonly RejectionCode OverBattalionSize = new("armies.over-battalion-size");
}
