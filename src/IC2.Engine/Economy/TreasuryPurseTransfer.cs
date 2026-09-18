using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The supply dialog's money-transfer panel, <c>TAFSupply_ChangeMoney</c> — <c>docs/task-catalogue.md</c>
/// "T38 Supply dialog follow-ups, treasury ↔ purse transfers, and automatic resupply" (issue #78),
/// Done-when 6. Moves talents between the national treasury and one army's or fleet's own purse, in
/// either direction, within the same dialog the supply transfer itself uses.
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
/// <strong><c>[open]</c>: a co-located fleet as an alternative source/target, instead of the treasury.</strong>
/// The task entry names this explicitly and settles it as open, not a gap to close here. Searched:
/// <c>supply-capacity-rounding.md</c> (this task's cited evidence) traces <c>TAFSupply_ChangeSupply</c> /
/// <c>TAFSupply_ChangeBuyAmount</c> instruction-by-instruction but says nothing about
/// <c>TAFSupply_ChangeMoney</c> beyond the 1,000 cap; <c>decompiled-unit-map-orders-and-record-fields.md</c>
/// (the report that first found that cap) names only the nation ↔ army/fleet purse direction, with no
/// mention of a fleet acting as the money source/target in place of the treasury. Neither source disassembles
/// <c>TAFSupply_ChangeMoney</c> itself, so there is no instruction evidence either way for the fleet variant —
/// implementing it would be invention, not transcription. <see cref="TransferWithArmy"/> and
/// <see cref="TransferWithFleet"/> therefore implement only the nation ↔ one unit's own purse direction,
/// which is exactly what Done-when 6's checkable assertion requires ("moves talents between the national
/// treasury... and an army or fleet in the dialog, in either direction"); a fleet standing in for the
/// treasury is left <c>[open]</c> for whichever later task's evidence settles it.
/// </para>
/// <para>
/// <strong>Conservation</strong>: the amount actually applied is derived from the purse's own before/after
/// balance (via <see cref="PurseAccounting.Credit"/>, which enforces the cap), and the treasury moves by
/// exactly that applied amount in the opposite direction — so <c>nation.Treasury + unit.Money</c> is
/// invariant across the call.
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

        var clampedRequest = ClampToSource(talentsIntoPurse, nation.Treasury, army.Money);
        var updatedMoney = PurseAccounting.Credit(army.Money, clampedRequest, ruleset);
        var applied = updatedMoney - army.Money;

        var updatedArmy = army with { Money = updatedMoney };
        var updatedNation = nation with { Treasury = nation.Treasury - applied };

        return new ArmyResult(updatedNation, updatedArmy, applied);
    }

    /// <summary>The result of one treasury ↔ fleet-purse transfer — the naval twin of <see cref="ArmyResult"/>.</summary>
    public sealed record FleetResult(NationState Nation, FleetState Fleet, int AppliedTalents);

    /// <summary>The fleet twin of <see cref="TransferWithArmy"/>.</summary>
    public static FleetResult TransferWithFleet(NationState nation, FleetState fleet, int talentsIntoPurse, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(ruleset);

        var clampedRequest = ClampToSource(talentsIntoPurse, nation.Treasury, fleet.Money);
        var updatedMoney = PurseAccounting.Credit(fleet.Money, clampedRequest, ruleset);
        var applied = updatedMoney - fleet.Money;

        var updatedFleet = fleet with { Money = updatedMoney };
        var updatedNation = nation with { Treasury = nation.Treasury - applied };

        return new FleetResult(updatedNation, updatedFleet, applied);
    }

    /// <summary>
    /// Never takes more than the source holds: a positive request (treasury → purse) is capped at the
    /// treasury's own balance; a negative request (purse → treasury) is capped at the purse's own balance.
    /// </summary>
    private static int ClampToSource(int requested, int treasury, int purse) => requested switch
    {
        > 0 => Math.Min(requested, treasury),
        < 0 => Math.Max(requested, -purse),
        _ => 0,
    };
}
