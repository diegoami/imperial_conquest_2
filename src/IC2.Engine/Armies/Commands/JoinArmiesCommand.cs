using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// Joins two of the issuing nation's own armies at Chebyshev distance exactly 1 —
/// <c>docs/task-catalogue.md</c> "T15 Army and unit management", Done-when 1: combined units capped at
/// 20, combined troops capped at 100,000, refused while either army is aboard a fleet. Units, supplies
/// and money pool onto <paramref name="SurvivingArmyId"/>; the survivor's moves are zeroed;
/// <paramref name="AbsorbedArmyId"/> is deleted.
/// </summary>
/// <remarks>
/// <c>TUnitMap_JoinArmies</c> (<c>0x004472FC</c>) <strong>[confirmed:
/// decompiled-unit-map-orders-and-record-fields.md]</strong>: "Both armies must be the active nation's
/// and exactly one tile apart, the partner rule <c>FUN_00449D64</c> reads through
/// <c>FUN_004492A0</c>'s <c>distance == 1</c> (<strong>[derived: code]</strong> for Join armies'
/// use of it; the report reads it as Transfer unit's gate — audit §1.6). Neither may be aboard a fleet
/// (<c>army[+8] == -1</c> → <em>"An army on a fleet cannot
/// be combined with another."</em>). Combined units ≤ 20, combined troops ≤ 100,000. Units are moved one
/// at a time, supplies and money add, the emptied army is deleted, and the survivor's moves are zeroed."
/// <para>
/// <strong>The pooled purse adds UNCAPPED (T72, bug #315).</strong> Row 7 of
/// <c>2026-10-05-army-purse-writes-and-the-1000-cap.md</c> reads <c>TUnitMap_JoinArmies</c> at
/// <c>:46992-46993</c> as <c>kept.purse += partner.purse</c>, a 16-bit add with no cap <c>[derived]</c>,
/// and 1,000 + 1,000 gave <strong>2,000</strong> in play <c>[Wine candidate: Q1_05_before_join.SAV →
/// Q1_06_after_join.SAV]</c>; the same report's answer names the clone's cap at 1,000 on Join armies as
/// "not in the original". The cited <c>decompiled-unit-map-orders-and-record-fields.md</c> row — whose
/// join's caps are 20 units and 100,000 troops — says "supplies and money add", no money cap, and
/// <c>IP016.sav</c> army 1 holds 1,066 (bug #315): this is the path that makes such a purse. T08's
/// Done-when 6 wording ("enforced on every path that credits a purse") is superseded by that report —
/// see <see cref="IC2.Engine.Economy.PurseAccounting"/>'s remarks for which three paths do cap. No treasury is
/// touched here: the original's join moves money only between the two army records, so — unlike the
/// capped dialog paths — there is no "excess to the treasury" hygiene. The field's own bound still
/// holds: the 16-bit add that could exceed 32,767 is enforced to
/// <see cref="IC2.Engine.Economy.PurseAccounting.PurseFieldMax"/> and the excess goes NOWHERE
/// <c>[designed: the user's
/// 2026-10-05 choice not to reproduce the original's wrap, PR #758's R2 resolution; the original's wrap
/// is row 7, derived: code, not played]</c>. Supplies, likewise uncapped, are a plain sum as decompiled.
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

    /// <summary>
    /// The two armies are not at Chebyshev distance exactly 1 — the original's partner rule
    /// (<c>FUN_00449D64</c>; <strong>[derived: code]</strong> for Join armies' use of it, audit §1.6).
    /// Distance 0 and distance 2 are both rejected.
    /// </summary>
    public static readonly RejectionCode NotAdjacent = new("armies.not-adjacent");

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
