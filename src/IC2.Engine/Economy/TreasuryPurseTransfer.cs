using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The supply dialog's money-transfer panel, <c>TAFSupply_ChangeMoney</c> — <c>docs/task-catalogue.md</c>
/// "T38 Supply dialog follow-ups, treasury ↔ purse transfers, and automatic resupply" (issue #78),
/// Done-when 6, and T105's <c>transfer-money</c> command. Moves talents between the national treasury (or,
/// with a <c>via</c> fleet, that fleet's own purse) and one army's or fleet's own purse, in either
/// direction, within the same dialog the supply transfer itself uses.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is confirmed</strong>: <c>decompiled-unit-map-orders-and-record-fields.md</c> establishes
/// that <c>TAFSupply_ChangeMoney</c> caps a purse at <see cref="EconomyRules.PurseCapPerUnit"/>, and
/// <c>2026-10-05-army-purse-writes-and-the-1000-cap.md</c> (research 9ae8924) reads the clamp exactly:
/// rows 1 and 2 (lines 43101-43170) — a positive arrow step is <c>min(step, 1000 − receiving purse)</c>
/// with <strong>no floor at 0</strong>, so an over-cap purse gets a <em>negative</em> step and is pulled
/// back to exactly 1,000 while the provider gains the excess — seen in play: from a joined purse of
/// 2,000, one "+100" click left the purse at 1,000 and raised the treasury by 1,000 (Wine candidate
/// <c>Q1_06_after_join.SAV</c> → <c>Q1b_01_after_one_up_click.SAV</c>). This is one of only three paths
/// that cap a purse in the original (<see cref="PurseAccounting"/>'s remarks name the other two), so the
/// clamp lives <em>here</em>, in the dialog's own writer — not in <see cref="PurseAccounting.Credit"/>,
/// which the uncapped paths use. T72 (bug #757, folded in) replaced the T105 round-1 N5 guard — which
/// applied nothing to a positive request into an over-cap purse — with the original's signed arrow.
/// </para>
/// <para>
/// <strong>What neither report states, and this deliberately does not invent</strong>: whether the
/// treasury itself may go negative as a result of this transfer. The task entry calls this out by name
/// ("where the report is silent... escalate rather than decide") — but the same Done-when line already
/// settles it operationally: the transfer "never takes more than the source holds", so when the treasury
/// is the source it is clamped to its own balance and never driven negative by <em>this</em> method; when
/// the purse is the source, the treasury only ever gains. No separate treasury floor is invented, and none
/// is needed for that requirement to hold.
/// </para>
/// <para>
/// <strong>The co-located fleet as the money's other side — implemented here (T105), not <c>[open]</c>.</strong>
/// The audit's Supply row reads: <c>TAFSupply_ChangeMoney</c> "moves money between the treasury, or a
/// co-located fleet, and the army, capped at 1,000" <strong>[confirmed:
/// <c>decompiled-unit-map-orders-and-record-fields.md</c>, <c>supply-capacity-rounding.md</c>; quoted in
/// <c>docs/investigations/original-ui-command-audit.md</c> §1.6]</strong>. The same report that
/// establishes the 1,000 cap therefore <em>does</em> name a fleet as the money's other side, and only says
/// it must be co-located. A previous revision of this note claimed the reports named only the
/// nation ↔ one unit's own purse direction and that the fleet variant "would be invention"; that was
/// wrong, and is retracted with the code here. <see cref="TransferBetweenPurses"/> is that direction:
/// with <c>via &lt;fleet&gt;</c> the other account is that fleet's own purse rather than the treasury, and
/// both rules (<see cref="ClampToSource"/> and <see cref="PurseAccounting.Credit"/>) are the same ones the
/// treasury path already uses. "Co-located" is the dialog's own provider radius — the audit's §1.6
/// <c>TAFSupply_FindProviders</c> "within one tile" reading, mapped onto
/// <see cref="EconomyRules.CommandAdjacencyRadiusTiles"/> by
/// <see cref="Commands.TransferMoneyCommandHandler"/> — and the fleet carrying the named army always
/// counts: an embarked army's X/Y follow its carrier in this engine (<c>EmbarkArmyCommandHandler</c>,
/// <c>MoveFleetCommandHandler</c>), so that branch is belt-and-braces against a hand-built state, not a
/// distinct position case.
/// </para>
/// <para>
/// <strong>Conservation</strong>: the amount actually applied is derived from the receiving purse's own
/// before/after balance (the signed <c>min(step, cap − receiver)</c> below, landed through
/// <see cref="PurseAccounting.Credit"/>), and the other account moves by exactly that applied amount in
/// the opposite direction — so both <c>nation.Treasury + unit.Money</c> (the treasury paths) and
/// <c>fromMoney + toMoney</c> (the <c>via</c>-fleet <see cref="TransferBetweenPurses"/> path) are invariant
/// across the call, including the pull-back: the excess a receiver above 1,000 sheds is exactly what the
/// provider gains (report row 1; <c>Q1_06</c> → <c>Q1b_01</c>: purse −1,000, treasury +1,000).
/// </para>
/// </remarks>
public static class TreasuryPurseTransfer
{
    /// <summary>The result of one treasury ↔ army-purse transfer.</summary>
    /// <param name="Nation">The nation, its treasury moved by <see cref="AppliedTalents"/> in the opposite direction to <see cref="Army"/>'s purse.</param>
    /// <param name="Army">The army, its purse moved by <see cref="AppliedTalents"/>.</param>
    /// <param name="AppliedTalents">
    /// The talents actually moved into the purse — positive (treasury to purse) or negative (purse to
    /// treasury) — after both the source-balance clamp and the purse's 1,000 cap. May be smaller in
    /// magnitude than <c>talentsIntoPurse</c> requested it.
    /// </param>
    public sealed record ArmyResult(NationState Nation, ArmyState Army, int AppliedTalents);

    /// <summary>
    /// Moves <paramref name="talentsIntoPurse"/> talents from the treasury into <paramref name="army"/>'s
    /// purse (or, if negative, that many out of the purse and into the treasury).
    /// </summary>
    /// <param name="nation">The army's own nation.</param>
    /// <param name="army">The army.</param>
    /// <param name="talentsIntoPurse">
    /// The requested move. Positive moves talents from the treasury to the purse, clamped so the treasury
    /// is never drained below zero (never more than it holds) — and, when the purse already sits above
    /// <see cref="EconomyRules.PurseCapPerUnit"/>, the original's arrow is signed: the request turns into
    /// <c>PurseCapPerUnit − purse</c> (negative), the purse falls to exactly the cap and the excess is
    /// credited to the treasury. Negative moves talents from the purse to the treasury, clamped so the
    /// purse is never taken below zero; a purse above the cap drains normally, untouched by the clamp.
    /// Either way the purse never ends above <see cref="EconomyRules.PurseCapPerUnit"/> through a
    /// positive request.
    /// </param>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.PurseCapPerUnit"/> — never a C# literal.</param>
    public static ArmyResult TransferWithArmy(NationState nation, ArmyState army, int talentsIntoPurse, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(ruleset);

        var (updatedTreasury, updatedMoney, applied) =
            ApplyTransfer(nation.Treasury, army.Money, talentsIntoPurse, ruleset);

        return new ArmyResult(nation with { Treasury = updatedTreasury }, army with { Money = updatedMoney }, applied);
    }

    /// <summary>The result of one treasury ↔ fleet-purse transfer — the naval twin of <see cref="ArmyResult"/>.</summary>
    public sealed record FleetResult(NationState Nation, FleetState Fleet, int AppliedTalents);

    /// <summary>The fleet twin of <see cref="TransferWithArmy"/>.</summary>
    public static FleetResult TransferWithFleet(NationState nation, FleetState fleet, int talentsIntoPurse, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(ruleset);

        var (updatedTreasury, updatedMoney, applied) =
            ApplyTransfer(nation.Treasury, fleet.Money, talentsIntoPurse, ruleset);

        return new FleetResult(nation with { Treasury = updatedTreasury }, fleet with { Money = updatedMoney }, applied);
    }

    /// <summary>
    /// The result of one purse ↔ purse transfer — the <c>via &lt;fleet&gt;</c> direction T105 adds.
    /// </summary>
    /// <param name="FromMoney">The <c>via</c> fleet's (first argument's) new balance.</param>
    /// <param name="ToMoney">The named unit's (second argument's) new balance.</param>
    /// <param name="AppliedTalents">
    /// The talents actually moved into <paramref name="ToMoney"/> — positive or negative, after the
    /// funding purse's balance clamp and the receiving purse's
    /// <see cref="EconomyRules.PurseCapPerUnit"/> cap. Whichever of the two purses receives is the one
    /// capped: a negative request is evaluated with the roles swapped (T105 review round 1, B1).
    /// </param>
    public sealed record PursesResult(int FromMoney, int ToMoney, int AppliedTalents);

    /// <summary>
    /// Moves <paramref name="talentsIntoTo"/> talents from one purse into another (or, if negative, that
    /// many back out of <paramref name="toMoney"/> into <paramref name="fromMoney"/>) — the same rules
    /// <see cref="TransferWithArmy"/> and <see cref="TransferWithFleet"/> apply, with a purse standing in
    /// for the treasury on the funding side.
    /// </summary>
    /// <param name="fromMoney">The purse the <c>via</c> fleet holds; funds a positive move.</param>
    /// <param name="toMoney">The named unit's own purse; funds a negative move.</param>
    /// <param name="talentsIntoTo">The requested signed move, clamped exactly as the treasury paths clamp it.</param>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.PurseCapPerUnit"/> — never a C# literal.</param>
    public static PursesResult TransferBetweenPurses(int fromMoney, int toMoney, int talentsIntoTo, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        // T105 review round 1, B1: ApplyTransfer caps its *target* argument, but on a negative request the
        // named unit funds and the via fleet receives, so the cap landed on the funder and the receiving
        // fromMoney could climb past PurseCapPerUnit. Route a negative request with the two purses
        // swapped, so whichever purse receives is the one the dialog's signed clamp caps (and pulls back
        // to the cap if already over it — T72, bug #757), then map the pair back and negate the applied
        // amount. The treasury paths are unaffected — the treasury has no cap.
        if (talentsIntoTo < 0)
        {
            var (negativeUpdatedTo, negativeUpdatedFrom, appliedIntoFrom) =
                ApplyTransfer(toMoney, fromMoney, -talentsIntoTo, ruleset);
            return new PursesResult(negativeUpdatedFrom, negativeUpdatedTo, -appliedIntoFrom);
        }

        var (updatedFrom, updatedTo, applied) = ApplyTransfer(fromMoney, toMoney, talentsIntoTo, ruleset);
        return new PursesResult(updatedFrom, updatedTo, applied);
    }

    /// <summary>
    /// The one place the dialog's signed money arrow and the source-balance clamp are combined: a positive
    /// <paramref name="talentsIntoTarget"/> is funded by <paramref name="sourceMoney"/> and lands on the
    /// receiving purse through the dialog's own clamp,
    /// <c>min(request, PurseCapPerUnit − targetMoney)</c> — signed, with no floor at 0, exactly
    /// <c>TAFSupply_ChangeMoney</c>'s step (2026-10-05 report rows 1 and 2), so a purse already above the
    /// cap is pulled back to it and the provider gains the difference; a negative request is funded by
    /// the purse itself and clamped to its balance. Every public transfer above pairs its two accounts
    /// and calls this — the rule is not copied per direction.
    /// </summary>
    private static (int SourceMoney, int TargetMoney, int AppliedTalents) ApplyTransfer(
        int sourceMoney, int targetMoney, int talentsIntoTarget, Ruleset ruleset)
    {
        // T72 (bug #757, folded in): this replaced the T105 round-1 N5 guard, which applied nothing when
        // a positive request met a purse already above PurseCapPerUnit. The original applies the SIGNED
        // arrow: from 2,000, one "+100" click moved min(100, 1000 − 2000) = −1,000, the purse fell to
        // 1,000 and the treasury rose by 1,000 [Wine candidate: Q1_06_after_join.SAV →
        // Q1b_01_after_one_up_click.SAV; the rule is row 1 [derived: TAFSupply_ChangeMoney :43118-43122]].
        var step = talentsIntoTarget > 0
            ? Math.Min(talentsIntoTarget, ruleset.Economy.PurseCapPerUnit - targetMoney)
            : ClampFundingPurse(talentsIntoTarget, targetMoney);

        // Never drains a funding source below zero — but the clamp bites only when money leaves the
        // source: a signed pull-back (a positive request into an over-cap purse turns the step negative)
        // feeds the source, and the original checks no balance of it at all ("no check of the treasury's
        // sign", row 1).
        if (step > 0)
        {
            step = Math.Min(step, Math.Max(0, sourceMoney));
        }

        var updatedTarget = PurseAccounting.Credit(targetMoney, step);
        var applied = updatedTarget - targetMoney;

        // `step` is ≤ PurseCapPerUnit − targetMoney on every positive request and ≥ −targetMoney on
        // every negative one, so targetMoney + step always lands inside the purse field's range and
        // Credit's bound never rewrites `applied` here: the two accounts conserve exactly, pull-back
        // included.
        return (sourceMoney - applied, updatedTarget, applied);
    }

    /// <summary>
    /// The negative request is funded by the receiving purse itself (the dialog's "down" arrow, row 2:
    /// <c>step := min(step, purse)</c>): never takes more than that purse holds, so the purse never ends
    /// below zero through this path — and a purse above the cap simply drains, the cap not applying to
    /// money leaving it. The drain case of a positive request is clamped in <see cref="ApplyTransfer"/>
    /// against the funding account, which is the treasury — or a funding purse for
    /// <see cref="TransferBetweenPurses"/>.
    /// </summary>
    /// <remarks>
    /// T105 review round 1, B1 (kept): a non-positive funding account must clamp to zero, not pass the
    /// request through — a source that holds nothing (or less) can supply nothing rather than reverse
    /// the transfer. <see cref="TreasuryPurseTransferTests"/> pins both directions against a non-positive
    /// source.
    /// </remarks>
    private static int ClampFundingPurse(int requested, int purse) =>
        Math.Max(requested, -Math.Max(0, purse));
}
