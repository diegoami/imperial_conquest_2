using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// The mercenary-marker merge block — <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries",
/// Scope: "the unit slot <c>+0</c> regular/mercenary marker and the merge block it implies", Done-when 5.
/// </summary>
/// <remarks>
/// <para>
/// <c>TChangeArmyUnits_JoinUnits</c> (<c>0x00444E8C</c>) refuses to join two units unless both are
/// regular — <em>"You can only join regular units together."</em>
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>. The check reads exactly
/// the unit slot's <c>+0</c> word this task's model exposes as <see cref="UnitSlot.MercenaryLabel"/>
/// (<see cref="UnitSlot.IsMercenary"/>/<see cref="UnitSlot.IsRegular"/>): <c>0</c> is a regular unit, any
/// other value is a mercenary — gameplay-relevant precisely because it blocks this merge
/// (<c>docs/design-audit.md</c> §2.8).
/// </para>
/// <para>
/// This is the marker guard only, not the whole join rule — the same type, battalion-size and
/// arithmetic-mean-quality rules the report also confirms belong to "T15 Army and unit management"
/// (<c>src/IC2.Engine/Armies/**</c>), which does not exist yet. <see cref="IsMergeAllowedByMarker"/> is
/// the one piece of that rule this task's Scope claims, exposed so T15 calls it rather than
/// re-deriving the marker check.
/// </para>
/// </remarks>
public static class UnitMergeGuard
{
    /// <summary>
    /// Whether two unit slots may be considered for a join at all, as far as the mercenary marker is
    /// concerned — <see langword="false"/> the moment either is a mercenary, regardless of type or troop
    /// count.
    /// </summary>
    /// <param name="first">One candidate unit.</param>
    /// <param name="second">The other candidate unit.</param>
    public static bool IsMergeAllowedByMarker(UnitSlot first, UnitSlot second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return first.IsRegular && second.IsRegular;
    }
}
