using IC2.Engine.Armies;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// <c>FUN_0044a4e0</c>, statement for statement. See <see cref="MobilizeRecruitSlotCommand"/>'s remarks.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The order of operations is the original's, and it is load-bearing</strong>
/// <strong>[confirmed: decompiled-mobilization-and-mercenary-restock.md §1]</strong>: "read the slot,
/// write the unit, name it, <em>then</em> delete the slot, then refresh the marker. Nothing is written
/// back to the city." An implementation that deleted the slot first would be a different rule under
/// failure — the recruit would be gone with no unit to show for it. Here every step builds a value and
/// the state is replaced once at the end, so a rejection anywhere leaves the slot exactly where it was;
/// <c>MobilizeRecruitSlotCommandHandlerTests.A_refused_mobilization_leaves_the_slot_and_the_armies_untouched</c>
/// drives the failing path and asserts it.
/// </para>
/// <para>
/// <strong>The marker refresh has nothing to refresh here.</strong> <c>FUN_0044a80c</c> recomputes the
/// army's map marker from a troop band (<c>mapMarkers.armyTroopTierThresholds</c>, 25,000 and 50,000),
/// but this engine stores no marker: the map array the original writes into is terrain <em>and</em>
/// occupancy at once, while <see cref="GameState"/> keeps them apart and derives a marker at render
/// time. The one piece of that array the model does carry is
/// <see cref="ArmyState.CoveredTileCode"/> — "the map cell this army's marker covers" — and a created
/// army gets it from the terrain it is placed on (<see cref="MobilizationArmyCreation"/>), while a
/// receiving army's is untouched, exactly as the original's marker write leaves <c>covered</c> alone.
/// </para>
/// </remarks>
[CommandHandler]
public sealed class MobilizeRecruitSlotCommandHandler : ICommandHandler<MobilizeRecruitSlotCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(MobilizeRecruitSlotCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var nation = context.IssuingNation;

        if (command.SlotIndex < 0 || command.SlotIndex >= nation.RecruitmentSlots.Count)
        {
            return CommandOutcome.Reject(
                MobilizeRecruitSlotRejections.UnknownSlot,
                $"Nation '{nation.Id}' has {nation.RecruitmentSlots.Count} recruitment slot(s), "
                + $"so slot {command.SlotIndex} cannot be mobilized.");
        }

        // 1. Read the slot, and the city it is training at.
        var slot = nation.RecruitmentSlots[command.SlotIndex];
        var city = state.CityById(slot.TargetCityId);
        if (city is null)
        {
            return CommandOutcome.Reject(
                MobilizeRecruitSlotRejections.UnknownCity,
                $"Recruitment slot {command.SlotIndex} trains at '{slot.TargetCityId}', which is not a known city.");
        }

        var recruitment = context.Ruleset.Recruitment;
        var asymmetry = context.Ruleset.Flags.SeatAsymmetry;
        if (!MobilizationReadiness.IsReady(slot.StateCode, nation.Control, recruitment, asymmetry))
        {
            return CommandOutcome.Reject(
                MobilizeRecruitSlotRejections.SlotNotReady,
                $"Recruitment slot {command.SlotIndex} is at state code {slot.StateCode}, below the "
                + $"{MobilizationReadiness.MinStateCode(nation.Control, recruitment, asymmetry)} this seat needs.");
        }

        // 2. Find the receiving army -- or, when none will take it, create one beside the city.
        var choice = MobilizationReceivingArmy.Find(state, city, nation, context.Ruleset, slot.Troops);
        ArmyState receivingArmy;
        var unitSlotIndex = 0;
        var armyWasCreated = false;

        if (choice is not null)
        {
            receivingArmy = choice.Army;
            unitSlotIndex = choice.UnitSlotIndex;
        }
        else
        {
            if (state.ArmyById(command.NewArmyId) is not null)
            {
                return CommandOutcome.Reject(
                    MobilizeRecruitSlotRejections.DuplicateArmyId,
                    $"Army id '{command.NewArmyId}' is already in use, so no army can be created for this recruit.");
            }

            var created = MobilizationArmyCreation.Create(
                state, context.World, city, nation, context.Ruleset, command.NewArmyId);
            if (created is null)
            {
                return CommandOutcome.Reject(
                    MobilizeRecruitSlotRejections.NoReceivingArmy,
                    $"No army of '{nation.Id}' can receive the recruit at '{city.Id}', and none can be "
                    + "created beside it.");
            }

            receivingArmy = created;
            armyWasCreated = true;

            // FUN_0044a4e0 sets the unit slot to 0 when it has just created the army: a new army is
            // empty, so lastOccupied + 1 is 0 and the two agree.
            unitSlotIndex = 0;
        }

        // 3. Write the unit, then 4. name it. The original writes the unit's troops and type first
        //    and calls FUN_0044a218 after, so its scan sees the new unit; this resolves the name
        //    against the state as it stands BEFORE the insert. The two orders cannot differ here, and
        //    the reason is structural rather than incidental: ArmyNaming identifies a unit's ordinal
        //    by matching its NAME against "<N>(st|nd|rd|th) <Label> Battalion", and the unit being
        //    named is the one whose Name this expression is computing -- there is no earlier value to
        //    match, because UnitSlot is immutable and no half-built slot with troops but no name ever
        //    exists in this engine. So no fixture can visit an "unnamed unit in the scan" edge, and
        //    none is written; what a test CAN visit is that a unit with a non-matching name is skipped,
        //    which is what MobilizeRecruitSlotCommandHandlerTests
        //    .A_mercenary_of_the_same_type_does_not_consume_a_battalion_ordinal asserts.
        var unit = new UnitSlot(
            MercenaryLabel: 0,
            UnitTypeId: slot.UnitTypeId,
            Troops: slot.Troops,
            Quality: MobilizationReadiness.QualityFor(slot.StateCode, recruitment),
            Name: ArmyNaming.NextName(state, nation.Id, slot.UnitTypeId));

        var updatedArmy = receivingArmy with { Units = WithUnitAt(receivingArmy.Units, unitSlotIndex, unit) };

        // 5. Delete the slot. NationState.RecruitmentSlots models the original's compacted 40-slot
        //    table directly -- an empty slot is simply absent -- so removing the entry is exactly
        //    FUN_0044a610's shift-everything-down-and-zero-slot-39.
        var remainingSlots = new List<RecruitmentSlot>(nation.RecruitmentSlots.Count - 1);
        for (var i = 0; i < nation.RecruitmentSlots.Count; i++)
        {
            if (i != command.SlotIndex)
            {
                remainingSlots.Add(nation.RecruitmentSlots[i]);
            }
        }

        var updatedNation = nation with { RecruitmentSlots = ValueList.From(remainingSlots) };

        var updatedArmies = armyWasCreated
            ? ValueList.From(state.Armies.Append(updatedArmy))
            : ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, updatedArmy.Id, StringComparison.Ordinal) ? updatedArmy : a));

        context.Events.Publish(new RecruitMobilized(
            nation.Id, city.Id, updatedArmy.Id, armyWasCreated,
            unit.UnitTypeId, unit.Troops, unit.Quality, unit.Name));

        return CommandOutcome.Accept(state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n)),
            Armies = updatedArmies,
        });
    }

    /// <summary>
    /// Writes <paramref name="unit"/> into slot <paramref name="index"/>, which is one past the army's
    /// highest occupied slot: either appended, or overwriting an exhausted slot that a still-occupied
    /// one sits above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both cases are reachable, and together they are what "gaps are never reused" means. The
    /// original's army record is a fixed 20-slot array in which an empty slot reads <c>troops == 0</c>,
    /// and <c>FUN_0044a66c</c> answers <c>lastOccupied + 1</c> rather than the first hole. So a
    /// zero-troop slot <em>below</em> an occupied one is skipped over — the write appends past it —
    /// while a zero-troop slot <em>at</em> the write position is overwritten.
    /// <c>MobilizeRecruitSlotCommandHandlerTests</c> visits each: one army whose slot 0 is empty and
    /// whose slot 1 is not (the new unit lands in slot 2, the hole left where it was), and one whose
    /// slot 1 is empty below an occupied slot 0 (the new unit replaces it).
    /// </para>
    /// <para>
    /// <paramref name="index"/> is never past <c>units.Count</c>, because that is what
    /// <see cref="MobilizationReceivingArmy.FirstFreeUnitSlot"/> computes it from — hence two branches
    /// and no padding.
    /// </para>
    /// </remarks>
    private static ValueList<UnitSlot> WithUnitAt(ValueList<UnitSlot> units, int index, UnitSlot unit)
    {
        var updated = new List<UnitSlot>(units.Count + 1);
        foreach (var existing in units)
        {
            updated.Add(existing);
        }

        if (index < updated.Count)
        {
            updated[index] = unit;
        }
        else
        {
            updated.Add(unit);
        }

        return ValueList.From(updated);
    }
}
