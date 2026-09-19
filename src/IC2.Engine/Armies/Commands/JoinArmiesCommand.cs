using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Joins two of the issuing nation's own, co-located armies — <c>docs/task-catalogue.md</c> "T15 Army
/// and unit management", Done-when 1: combined units capped at 20, combined troops capped at 100,000,
/// refused while either army is aboard a fleet. Units, supplies and money pool onto
/// <paramref name="SurvivingArmyId"/>; the survivor's moves are zeroed; <paramref name="AbsorbedArmyId"/>
/// is deleted.
/// </summary>
/// <remarks>
/// <c>TUnitMap_JoinArmies</c> (<c>0x004472FC</c>) <strong>[confirmed:
/// decompiled-unit-map-orders-and-record-fields.md]</strong>: "Both armies must be the active nation's
/// and co-located. Neither may be aboard a fleet (<c>army[+8] == -1</c> → <em>"An army on a fleet cannot
/// be combined with another."</em>). Combined units ≤ 20, combined troops ≤ 100,000. Units are moved one
/// at a time, supplies and money add, the emptied army is deleted, and the survivor's moves are zeroed."
/// <para>
/// <strong>The pooled purse is capped, same as <c>JoinFleetsCommand</c> (T14).</strong> <c>[derived]</c>:
/// "T08 Economy, supply, and purses" Done-when 6 already establishes the cap's scope — "the purse cap of
/// 1,000 is enforced on every path that credits a purse" — and this command is exactly such a path, so an
/// uncapped pooled purse would be the actual defect, not a free choice between two equal options. Any
/// excess over <see cref="Model.EconomyRules.PurseCapPerUnit"/> moves to the issuing nation's treasury,
/// the same "excess moves to the treasury, never destroyed" hygiene <c>JoinFleetsCommandHandler</c>
/// applies, so the join still conserves money exactly (army purse + treasury), never a plain uncapped
/// sum. Supplies have no such cap in the confirmed report or anywhere else in this ruleset's army fields,
/// so they are a plain, uncapped sum, exactly as decompiled.
/// </para>
/// </remarks>
/// <param name="SurvivingArmyId">The army that receives the absorbed army's units, money and supplies.</param>
/// <param name="AbsorbedArmyId">The army that is deleted once its contents move to <paramref name="SurvivingArmyId"/>.</param>
public sealed record JoinArmiesCommand(string IssuingNationId, string SurvivingArmyId, string AbsorbedArmyId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "armies.join-armies";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class JoinArmiesRejections
{
    /// <summary>The two named army ids are the same army.</summary>
    public static readonly RejectionCode SameArmy = new("armies.same-army");

    /// <summary>Either named army id does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("armies.unknown-army");

    /// <summary>Either named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("armies.not-your-army");

    /// <summary>The two armies are not on the same tile.</summary>
    public static readonly RejectionCode NotCoLocated = new("armies.not-co-located");

    /// <summary>
    /// <em>"An army on a fleet cannot be combined with another."</em> — neither army may be aboard a
    /// fleet (<see cref="Model.ArmyState.IsEmbarked"/>).
    /// </summary>
    public static readonly RejectionCode ArmyEmbarked = new("armies.army-embarked");

    /// <summary>
    /// Combined units would exceed <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/> (20) — the
    /// seam <c>docs/task-catalogue.md</c> Done-when 6 (issue #181) names as one of the three ways a unit
    /// can reach an army; see this task's PR body for the full seam enumeration.
    /// </summary>
    public static readonly RejectionCode CombinedUnitsTooLarge = new("armies.combined-units-too-large");

    /// <summary>Combined troops would exceed <see cref="Model.ArmyManagementRules.MaxTroopsPerArmy"/> (100,000).</summary>
    public static readonly RejectionCode CombinedTroopsTooLarge = new("armies.combined-troops-too-large");
}
