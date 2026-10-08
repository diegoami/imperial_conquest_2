using IC2.Engine.Core;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// Removes one of the issuing nation's recruitment slots — the original's
/// <c>TArmyRecruits_DisbandUnits</c> (<c>0x004553B0</c>) over the recruit table, one row per command.
/// <c>docs/tasks/T108.md</c> "Disband a recruitment slot".
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing is refunded</strong>, for a ready slot and an unready one alike
/// <strong>[derived: code, <c>TArmyRecruits_DisbandUnits</c> @ <c>004553B0</c> → <c>FUN_0044A610</c>
/// never writes the treasury; Wine candidate: two disbands left Rome's treasury at 1,880
/// (<c>Q3_00</c> → <c>Q3_01</c> → <c>Q3_02</c>), <c>2026-10-05-disbanding-a-queued-recruitment.md</c>]</strong>.
/// The whole recruiting price was taken from the treasury when the order was placed
/// (<see cref="RecruitStandingUnitCommandHandler"/>), and disbanding the slot writes no money back.
/// The original's help text says the same in words — the money already invested is lost
/// <c>[help, not tested]</c>.
/// </para>
/// <para>
/// <strong>The later slots shift up</strong> to close the gap, exactly as
/// <see cref="MobilizeRecruitSlotCommandHandler"/>'s slot deletion does:
/// <see cref="Model.NationState.RecruitmentSlots"/> models the original's compacted 40-slot table by
/// holding only occupied slots, so removing the entry is <c>FUN_0044A610</c>'s
/// shift-everything-down-and-zero-slot-39, and the table's queue-full word is a derived fact of the
/// list's length here (<c>Count &gt;= MaxSlots</c>), which this removal lowers by one
/// <c>[derived; code, <c>FUN_0044A610</c> :49003-49014]</c>.
/// </para>
/// <para>
/// <strong>Mobilisation falls by the placement's mirror.</strong>
/// <c>mobilized = max(0, mobilized − 1 − troops × 1000 / wealth)</c>, the truncating mirror of the
/// order's <c>min(100, mobilized + 1 + troops × 1000 / wealth)</c> — see
/// <see cref="MobilizationRate.AfterOrderCancelled"/>. Because the rise is capped at 100 and the fall
/// is not, the two are exact inverses only when the placement did not hit the cap and the nation's
/// wealth is unchanged in between <c>[derived]</c>.
/// </para>
/// <para>
/// <strong>The original asks first.</strong> <em>"Are you sure you want to disband N unit(s)."</em>
/// (Yes / No / Cancel), and only Yes acts; with several entries selected each is processed, last row
/// first, by the same formula <c>[derived: code, :56133-56170]</c>. That confirmation is the UI's
/// (T109); this verb removes one slot and asks nothing.
/// </para>
/// </remarks>
/// <param name="SlotIndex">
/// The index into <see cref="Model.NationState.RecruitmentSlots"/> to disband. The list is compacted,
/// so this index moves when an earlier slot is removed.
/// </param>
public sealed record DisbandRecruitmentSlotCommand(string IssuingNationId, int SlotIndex) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "recruitment.disband-recruitment-slot";
}

/// <summary>Rejection codes <see cref="DisbandRecruitmentSlotCommandHandler"/> declares.</summary>
public static class DisbandRecruitmentSlotRejections
{
    /// <summary>
    /// <see cref="DisbandRecruitmentSlotCommand.SlotIndex"/> is outside the issuing nation's own
    /// recruitment table — an index that happens to be valid in <em>another</em> nation's table is out
    /// of range here, because the verb reads only the issuer's slots.
    /// </summary>
    public static readonly RejectionCode UnknownSlot = new("recruitment.unknown-recruitment-slot");
}

/// <summary>Published by <see cref="DisbandRecruitmentSlotCommandHandler"/> once a slot is disbanded.</summary>
/// <remarks>
/// <strong>Not marked news-worthy</strong>, for the same reason <see cref="RecruitmentOrdered"/> is
/// not: the exhaustive 24-call-site accounting of every news literal the original EXE can emit
/// (<c>news-log-format-and-messages.md</c> Q4) has no entry for a recruit being disbanded, and only
/// events the original itself raises belong in <c>News.NewsMessageCatalog</c>.
/// </remarks>
/// <param name="NationId">The disbanding nation.</param>
/// <param name="CityId">The city the recruit was training at.</param>
/// <param name="UnitTypeId">The disbanded order's unit type.</param>
/// <param name="Troops">The disbanded order's troop count.</param>
/// <param name="StateCode">The slot's readiness code at disband — the only place the ready/unready distinction is recorded, since the rule does not branch on it.</param>
[DomainEvent("recruitment.recruitment-slot-disbanded")]
public sealed record RecruitmentSlotDisbanded(
    string NationId,
    string CityId,
    string UnitTypeId,
    int Troops,
    int StateCode) : DomainEvent;
