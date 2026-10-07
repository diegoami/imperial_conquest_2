namespace IC2.Engine.Economy;

/// <summary>
/// The one shared place a purse is credited — <c>docs/task-catalogue.md</c> "T08 Economy, supply, and
/// purses", Done-when 6, as corrected by T72 (bug #315): <see cref="Credit"/> adds without the 1,000 cap
/// and keeps the purse inside the field's <see cref="PurseFieldMin"/> … <see cref="PurseFieldMax"/> range.
/// The 1,000 clamp survives only on the paths that read <see cref="Model.EconomyRules.PurseCapPerUnit"/>
/// themselves: the supply dialog's money arrows (<see cref="TreasuryPurseTransfer"/>) and the own-city
/// resupply hygiene (<see cref="AutomaticResupply"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The 1,000 cap is one write path's clamp, not a property of the record [confirmed:
/// 2026-10-05-army-purse-writes-and-the-1000-cap.md, research 9ae8924]</strong> — its rows 1, 4 and 10
/// are the only paths that cap a purse at 1,000 (the Supply army money arrows, the Army to army money
/// arrows — both <c>min(step, 1000 − receiver)</c> with no floor at 0 — and the AI's own-city refill),
/// and every other add is uncapped: the joins (row 7, and the Join-fleets row of
/// <c>decompiled-unit-map-orders-and-record-fields.md</c>, "ships, supplies and money add"), Buy supplies
/// with a negative amount (row 3: 10,000 troops, 206 supplies, purse 1,000 end at 1,021), and a battle's
/// captured purse (rows 12 and 12b). <c>IP016.sav</c> army 1 holds a purse of 1,066 (bug #315; the corpus
/// sweep <c>tests/IC2.Data.Tests/CorpusFixtures/ArmyFleetFieldRangeSweepTests.cs</c>), so a purse above
/// 1,000 is legitimate, and T21's import preserves such a purse as saved.
/// <see cref="Model.EconomyRules.PurseCapPerUnit"/> is therefore no longer read by this class — the
/// writers that need it (the dialog transfer, the own-city refill, and the still-unsettled
/// <c>Naval/Commands/FleetToFleetTransferCommandHandler</c>, marked <c>[open]</c> there) clamp with it
/// themselves.
/// </para>
/// <para>
/// <strong>Not the one credit — corrected by T72.</strong> The previous revision of these remarks called
/// this "the one place a purse is credited". One writer adds money without going through
/// <see cref="Credit"/>: battle absorption, <c>src/IC2.Engine/Battle/InstantBattleResolver.cs</c> line 194
/// (<c>Money = winner.Money + absorbedMoney</c>), which matches the original's uncaptured purse of a won
/// battle (report rows 12 and 12b [derived]) and needs no 1,000 clamp — but it is also a raw add with no
/// <see cref="PurseFieldMax"/> bound, where the original wraps and the clone must not. That file is
/// outside T72's Owns list (build-process.md §2.2), so the defect is reported to the bug list
/// (§4.6) and is not patched here.
/// </para>
/// <para>
/// <strong>The field range [derived: code; designed: the enforcement]</strong>. The purse field is signed
/// 16-bit floored at 0 — the upkeep tick's signed compare at <c>00451c1c</c>
/// (<c>upkeep-payment-and-desertion.md</c>) and the corpus sweep's <c>0..short.MaxValue</c> bound
/// (<c>ArmyFleetFieldRangeSweepTests</c>, ~40–58; the sweep finds 1,066, never more). The original's army
/// join and captured purse are 16-bit adds that <em>wrap</em> above 32,767, possibly to a negative purse
/// (report rows 7 and 12 [derived: code, not played]). The clone enforces the range instead of
/// reproducing the wrap [designed: like the other overflow and display glitches the user chose not to
/// reproduce on 2026-10-05; the main session's resolution of Sol's R2 on PR #758; what was searched:
/// every purse write in the 2026-10-05 report — no other bound is read anywhere in it]. A credit above
/// <see cref="PurseFieldMax"/> lands at <see cref="PurseFieldMax"/> and the excess goes NOWHERE — it is
/// not credited to any treasury, because no join or capture path touches a treasury in the original (the
/// joins' gates and the capture's write-back move only units, supplies and money between the records
/// themselves). Every writer whose add can reach this bound says the same in its own remarks.
/// </para>
/// </remarks>
public static class PurseAccounting
{
    /// <summary>
    /// The lowest value a purse legitimately holds: the upkeep tick floors every purse at 0 after paying
    /// mercenaries (<c>upkeep-payment-and-desertion.md</c>, the signed compare at <c>00451c1c</c> and the
    /// tick-end floor; sweep <c>ArmyFleetFieldRangeSweepTests</c> — no legitimate save shows a negative
    /// purse).
    /// </summary>
    public const int PurseFieldMin = 0;

    /// <summary>
    /// The highest value the purse field holds: the signed 16-bit bound, 32,767
    /// (<c>ArmyFleetFieldRangeSweepTests</c>'s <c>0..short.MaxValue</c> bound — see this type's remarks;
    /// not a ruleset constant, it is the field's own width, so no <c>Ruleset</c> carries it).
    /// </summary>
    public const int PurseFieldMax = short.MaxValue;

    /// <summary>
    /// Credits a purse by <paramref name="amount"/>: a plain signed add — a positive amount raises, a
    /// negative one debits — clamped only into the field's <see cref="PurseFieldMin"/> …
    /// <see cref="PurseFieldMax"/> range, never at <see cref="Model.EconomyRules.PurseCapPerUnit"/>.
    /// A debit is never larger than the amount given (no wired path debits past affordability, so
    /// <see cref="PurseFieldMin"/> binds only on a hand-built state), and a refund or a join sum is never
    /// cut short of its exact value below <see cref="PurseFieldMax"/>.
    /// </summary>
    /// <param name="currentMoney">The purse's current balance.</param>
    /// <param name="amount">The signed amount to add: positive credits, negative debits.</param>
    /// <returns>The new balance, inside <see cref="PurseFieldMin"/> … <see cref="PurseFieldMax"/>.</returns>
    public static int Credit(int currentMoney, int amount) =>
        Math.Clamp(currentMoney + amount, PurseFieldMin, PurseFieldMax);
}
