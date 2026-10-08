using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// The recruit-table half of <c>TArmyRecruits_DisbandUnits</c> (<c>0x004553B0</c>): read the slot,
/// delete it (shifting the later slots up), apply the mobilisation fall. See
/// <see cref="DisbandRecruitmentSlotCommand"/>'s remarks for the provenance of each step.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No refund, and no branch on readiness.</strong> The treasury is never read and never
/// written: the whole cost was taken at <see cref="RecruitStandingUnitCommandHandler"/> time and the
/// read found no write to undo, for a ready slot and an unready one alike. The handler is therefore
/// the same code path for both — the only place the distinction survives is the
/// <see cref="RecruitmentSlotDisbanded.StateCode"/> the event carries for the record.
/// </para>
/// <para>
/// <strong>Deletion-sweep.</strong> The slot is an entry in the nation's compacted slot list and
/// nothing else references it: no army, fleet or city holds a slot id, the quarterly upkeep bill
/// (<see cref="Economy.GarrisonUpkeep"/>) and the readiness system both read the list itself, and the
/// slot's upkeep therefore stops from the next quarter without any further write. The mobilisation
/// fall below is the one counter the placement raised that must be lowered back.
/// </para>
/// </remarks>
[CommandHandler]
public sealed class DisbandRecruitmentSlotCommandHandler : ICommandHandler<DisbandRecruitmentSlotCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(DisbandRecruitmentSlotCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var nation = context.IssuingNation;

        if (command.SlotIndex < 0 || command.SlotIndex >= nation.RecruitmentSlots.Count)
        {
            return CommandOutcome.Reject(
                DisbandRecruitmentSlotRejections.UnknownSlot,
                $"Nation '{nation.Id}' has {nation.RecruitmentSlots.Count} recruitment slot(s), "
                + $"so slot {command.SlotIndex} cannot be disbanded.");
        }

        // 1. Read the slot. The original's dialog processes its selected rows last-row-first for the
        //    same stale-table reason MobilizeRecruitSlotCommand's remarks give; a caller disbanding
        //    several slots at once issues these commands in descending index order.
        var slot = nation.RecruitmentSlots[command.SlotIndex];

        // 2. Delete the slot, shifting the later slots up -- FUN_0044a610's compaction, modelled by
        //    removing the entry from the compacted list, exactly as the mobilize handler's step 5 does.
        var remainingSlots = new List<RecruitmentSlot>(nation.RecruitmentSlots.Count - 1);
        for (var i = 0; i < nation.RecruitmentSlots.Count; i++)
        {
            if (i != command.SlotIndex)
            {
                remainingSlots.Add(nation.RecruitmentSlots[i]);
            }
        }

        // 3. Apply the mobilisation fall -- the placement's truncating mirror, floored at 0.
        var updatedNation = nation with
        {
            RecruitmentSlots = ValueList.From(remainingSlots),
            MobilizedPercent = MobilizationRate.AfterOrderCancelled(
                nation.MobilizedPercent, slot.Troops, nation.Wealth, context.Ruleset.Recruitment),
        };

        context.Events.Publish(new RecruitmentSlotDisbanded(
            nation.Id, slot.TargetCityId, slot.UnitTypeId, slot.Troops, slot.StateCode));

        return CommandOutcome.Accept(context.State with
        {
            Nations = ValueList.From(context.State.Nations.Select(n =>
                string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n)),
        });
    }
}
