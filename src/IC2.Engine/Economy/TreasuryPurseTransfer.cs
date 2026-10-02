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
/// that <c>TAFSupply_ChangeMoney</c> caps a purse at <see cref="EconomyRules.PurseCapPerUnit"/>
/// (<c>caps.maxPurseTalents</c> in the fixtures corpus) — the same cap <see cref="PurseAccounting.Credit"/>
/// already enforces on every other purse-crediting path, reused here rather than re-implemented.
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
/// <see cref="Commands.TransferMoneyCommandHandler"/> — and the fleet carrying the named army counts even
/// when the army's own last land position is elsewhere.
/// </para>
/// <para>
/// <strong>Conservation</strong>: the amount actually applied is derived from the receiving purse's own
/// before/after balance (via <see cref="PurseAccounting.Credit"/>, which enforces the cap), and the funding
/// account moves by exactly that applied amount in the opposite direction — so both
/// <c>nation.Treasury + unit.Money</c> (the treasury paths) and
/// <c>fromMoney + toMoney</c> (the <c>via</c>-fleet <see cref="TransferBetweenPurses"/> path) are invariant
/// across the call.
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
    /// is never taken below zero (never more than it holds); negative moves talents from the purse to the
    /// treasury, clamped so the purse is never taken below zero. Either direction is further clamped so
    /// the purse never exceeds <see cref="EconomyRules.PurseCapPerUnit"/>.
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
    /// <param name="FromMoney">The funding purse's new balance.</param>
    /// <param name="ToMoney">The receiving purse's new balance.</param>
    /// <param name="AppliedTalents">
    /// The talents actually moved into <paramref name="ToMoney"/> — positive or negative, after both the
    /// funding purse's balance clamp and the receiving purse's
    /// <see cref="EconomyRules.PurseCapPerUnit"/> cap.
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

        var (updatedFrom, updatedTo, applied) = ApplyTransfer(fromMoney, toMoney, talentsIntoTo, ruleset);
        return new PursesResult(updatedFrom, updatedTo, applied);
    }

    /// <summary>
    /// The one place the source-balance clamp and the receiving-purse cap are combined: a positive
    /// <paramref name="talentsIntoTarget"/> is funded by <paramref name="sourceMoney"/>, a negative one by
    /// <paramref name="targetMoney"/>, and the receiving side is capped by
    /// <see cref="PurseAccounting.Credit"/>. Every public transfer above pairs its two accounts and calls
    /// this — the rule is not copied per direction.
    /// </summary>
    private static (int SourceMoney, int TargetMoney, int AppliedTalents) ApplyTransfer(
        int sourceMoney, int targetMoney, int talentsIntoTarget, Ruleset ruleset)
    {
        var clampedRequest = ClampToSource(talentsIntoTarget, sourceMoney, targetMoney);
        var updatedTarget = PurseAccounting.Credit(targetMoney, clampedRequest, ruleset);
        var applied = updatedTarget - targetMoney;

        return (sourceMoney - applied, updatedTarget, applied);
    }

    /// <summary>
    /// Never takes more than the source holds: a positive request (the first account funds the second) is
    /// capped at the first's own balance; a negative request (the second funds the first) is capped at the
    /// second's own balance. The first account is the treasury — or a funding purse for
    /// <see cref="TransferBetweenPurses"/> — and the second is always the receiving purse.
    /// </summary>
    /// <remarks>
    /// Review round 1, B1: a negative source balance must clamp the request to zero, not pass it
    /// through unchanged. <c>Math.Min(requested, treasury)</c> with a negative <paramref name="treasury"/>
    /// returned the (negative) treasury itself for a positive request — moving money <em>out of</em> the
    /// purse when the caller asked to move it <em>in</em>, and by however negative the treasury happened
    /// to be, not by the requested amount. The source's own balance is floored at zero before either
    /// clamp: a source that holds nothing (or less) can supply nothing, in either direction, rather than
    /// reversing the transfer. <see cref="TreasuryPurseTransferTests"/> pins both directions against a
    /// non-positive source.
    /// </remarks>
    private static int ClampToSource(int requested, int treasury, int purse) => requested switch
    {
        > 0 => Math.Min(requested, Math.Max(0, treasury)),
        < 0 => Math.Max(requested, -Math.Max(0, purse)),
        _ => 0,
    };
}
