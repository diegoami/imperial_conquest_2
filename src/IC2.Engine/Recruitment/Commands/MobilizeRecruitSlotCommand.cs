using IC2.Engine.Core;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// Collects one ready standing-recruitment slot and turns it into an army unit — the original's
/// <c>FUN_0044a4e0</c>, <c>MobilizeRecruitSlot(nation, slot, out ok)</c>.
/// <c>docs/task-catalogue.md</c> "T55 Mobilization: a ready recruit becomes an army unit".
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is not a garrison transfer</strong>
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §1]</strong>. The source is the
/// nation's 40-slot recruitment table, and nothing in the original's function touches a city record:
/// "the 'city units' the recruit dialog lists <em>are</em> the recruitment slots; mobilizing removes
/// one from that list and creates an army unit". <see cref="Model.CityState.Garrison"/> is untouched by
/// this command, and the earlier reports that call this a "garrison to army transfer" are corrected by
/// that one — the mechanic is right, the word is not.
/// </para>
/// <para>
/// <strong>One slot per command, and the dialog issues several.</strong> The original's player path
/// (<c>TArmyRecruits_MobilizeUnits</c>) walks the listbox selection <em>backwards</em> and calls
/// <c>FUN_0044a4e0</c> once per selected row. That order is not a stylistic choice: the form's
/// row-to-slot table is built once, before any deletion, and deleting a slot shifts only the slots
/// <em>above</em> it, so descending order is the only order under which the stale table stays correct
/// <c>[derived, same report §7]</c>. A caller mobilizing several slots at once therefore issues these
/// commands in descending <see cref="SlotIndex"/> order, which is what
/// <c>RomeAutumnMobilizationReplayTests</c> does.
/// </para>
/// <para>
/// <strong>The AI issues the same command.</strong> The original has two call sites — the player's
/// dialog and the AI's <c>FUN_004504f4</c> — and they differ only in the readiness threshold they
/// apply (<see cref="MobilizationReadiness.MinStateCode"/>) and in the radius the shared
/// receiving-army helper gives their seat (<see cref="Armies.MobilizationReceivingArmy.Accepts"/>).
/// Both differences are keyed on the issuing nation's seat here, so there is one rule and no
/// AI-only path.
/// </para>
/// </remarks>
/// <param name="SlotIndex">
/// The index into <see cref="Model.NationState.RecruitmentSlots"/> to mobilize — the original's index
/// into the nation's 40-slot table. The list is compacted, so this index moves when an earlier slot is
/// deleted.
/// </param>
/// <param name="NewArmyId">
/// The id for the army this mobilization creates <em>if</em> no existing army will take the unit. The
/// engine invents no id (<c>docs/game-design.md</c> principle 4, and the same contract
/// <see cref="Armies.Commands.SplitArmyCommand"/> and <c>OrderFleetCommand</c> already state): a
/// generated id would not replay identically. It is ignored when an existing army receives the unit,
/// so a caller mobilizing several slots may pass the same id to all of them — after the first
/// mobilization creates that army, it is the last matching index and receives the rest.
/// </param>
public sealed record MobilizeRecruitSlotCommand(
    string IssuingNationId, int SlotIndex, string NewArmyId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "recruitment.mobilize-recruit-slot";
}

/// <summary>Rejection codes <see cref="MobilizeRecruitSlotCommandHandler"/> declares.</summary>
public static class MobilizeRecruitSlotRejections
{
    /// <summary>
    /// <see cref="MobilizeRecruitSlotCommand.SlotIndex"/> is outside the issuing nation's
    /// recruitment table.
    /// </summary>
    public static readonly RejectionCode UnknownSlot = new("recruitment.unknown-recruitment-slot");

    /// <summary>The slot names a city id that is not in the state.</summary>
    public static readonly RejectionCode UnknownCity = new("recruitment.slot-city-missing");

    /// <summary>
    /// The slot's <see cref="Model.RecruitmentSlot.StateCode"/> is below the issuing seat's threshold —
    /// the original's <em>"You cannot mobilise a unit at this time."</em> for a recruit that still reads
    /// <c>not ready</c>.
    /// </summary>
    public static readonly RejectionCode SlotNotReady = new("recruitment.slot-not-ready");

    /// <summary><see cref="MobilizeRecruitSlotCommand.NewArmyId"/> is already in use.</summary>
    public static readonly RejectionCode DuplicateArmyId = new("recruitment.duplicate-army-id");

    /// <summary>
    /// No army would take the unit and none could be created — no qualifying cell beside the city, or
    /// the army table is already at <see cref="Model.ArmyManagementRules.MaxArmies"/>. The original's
    /// own failure: <c>FUN_0044a4e0</c> clears its success flag and the dialog says <em>"You cannot
    /// mobilise a unit at this time."</em>
    /// </summary>
    public static readonly RejectionCode NoReceivingArmy = new("recruitment.no-receiving-army");
}

/// <summary>Published by <see cref="MobilizeRecruitSlotCommandHandler"/> once a recruit is mobilized.</summary>
/// <remarks>
/// <strong>Not marked news-worthy</strong>, for the same reason <see cref="RecruitmentOrdered"/> is not:
/// the exhaustive 24-call-site accounting of every news literal the original EXE can emit
/// (<c>news-log-format-and-messages.md</c> Q4) has no entry for a recruit being mobilized, and only
/// events the original itself raises belong in <c>News.NewsMessageCatalog</c>.
/// </remarks>
/// <param name="NationId">The mobilizing nation.</param>
/// <param name="CityId">The city the recruit was training at.</param>
/// <param name="ArmyId">The army the new unit joined, whether found or created.</param>
/// <param name="ArmyWasCreated">Whether <paramref name="ArmyId"/> is an army this mobilization created.</param>
/// <param name="UnitTypeId">The new unit's type, copied from the slot.</param>
/// <param name="Troops">The new unit's troops, copied from the slot.</param>
/// <param name="Quality">The new unit's permanent quality — <see cref="MobilizationReadiness.QualityFor"/>.</param>
/// <param name="UnitName">The auto-generated battalion name — <see cref="Armies.ArmyNaming.NextName"/>.</param>
[DomainEvent("recruitment.recruit-mobilized")]
public sealed record RecruitMobilized(
    string NationId,
    string CityId,
    string ArmyId,
    bool ArmyWasCreated,
    string UnitTypeId,
    int Troops,
    int Quality,
    string UnitName) : DomainEvent;
