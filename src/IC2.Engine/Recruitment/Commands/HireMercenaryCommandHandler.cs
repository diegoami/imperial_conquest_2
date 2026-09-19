using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Recruitment.Commands;

/// <summary>
/// The <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/> rejection this task (T15, "Army and unit
/// management") adds to the mercenary hire — <c>docs/task-catalogue.md</c> T15 Done-when 6 (issue #181):
/// the field was read by no code anywhere in <c>src/</c>, and T13's reviewer proved an army already
/// holding 20 units accepts a hire and ends at 21, a shape the original's 20-slot army record cannot
/// hold. T13's own hire logic is correct and unchanged; this is the one guard it had no evidence to add.
/// </summary>
/// <remarks>
/// Declared here rather than alongside <see cref="HireMercenaryRejections"/> in
/// <c>HireMercenaryCommand.cs</c> — this task's Owns-list grant (the plan commit granting T15 this seam,
/// <c>cfdd06b</c>) is <em>the handler file only</em>, not the command file, so the rejection code lives
/// where the check itself does.
/// </remarks>
public static class HireMercenaryUnitCapRejections
{
    /// <summary>
    /// Hiring would push the army's unit count past <see cref="Model.ArmyManagementRules.MaxUnitsPerArmy"/>
    /// (20) — the same cap <c>TUnitMap_JoinArmies</c> enforces on army join
    /// (<c>decompiled-unit-map-orders-and-record-fields.md</c>), applied here as the seam T15's Done-when 6
    /// found missing.
    /// </summary>
    public static readonly RejectionCode OverArmyUnitCap = new("mercenary.over-army-unit-cap");
}

/// <summary>
/// Wires <see cref="MercenaryHireCost.Compute"/> behind the command seam — <c>docs/task-catalogue.md</c>
/// "T13 Recruitment and mercenaries", Done-when 2 and 4.
/// </summary>
[CommandHandler]
public sealed class HireMercenaryCommandHandler : ICommandHandler<HireMercenaryCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(HireMercenaryCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        var slot = FindPoolSlot(state, command.PoolSlotIndex);
        if (slot is null)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.UnknownPoolSlot,
                $"Mercenary pool slot {command.PoolSlotIndex} has no offer to hire.");
        }

        var cost = MercenaryHireCost.Compute(slot.Troops, slot.UnitTypeId, slot.Quality, context.Ruleset);
        if (army.Money < cost)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.InsufficientMoney,
                "Your army has too little money to pay these mercenaries.");
        }

        var troopsAfterHire = army.TotalTroops + slot.Troops;
        if (troopsAfterHire > context.Ruleset.ArmyManagement.MaxTroopsPerArmy)
        {
            return CommandOutcome.Reject(
                HireMercenaryRejections.OverArmyTroopCap,
                "An army can not contain more than 100,000 troops.");
        }

        // T15 Done-when 6 (issue #181): the hire always appends exactly one unit slot, so the check is
        // the same shape as JoinArmiesCommandHandler's own MaxUnitsPerArmy cap -- inclusive, so an army
        // at 19 units accepting a hire (ending at 20) is still allowed.
        if (army.Units.Count + 1 > context.Ruleset.ArmyManagement.MaxUnitsPerArmy)
        {
            return CommandOutcome.Reject(
                HireMercenaryUnitCapRejections.OverArmyUnitCap,
                $"Army '{army.Id}' already has {army.Units.Count} units; hiring one more would exceed the "
                + $"{context.Ruleset.ArmyManagement.MaxUnitsPerArmy}-unit cap.");
        }

        if (army.IsEmbarked)
        {
            var fleet = state.FleetById(army.AboardFleetId!);
            var capacity = fleet is null ? 0 : fleet.Ships * context.Ruleset.Naval.TransportTroopsPerShip;
            if (fleet is null || troopsAfterHire > capacity)
            {
                return CommandOutcome.Reject(
                    HireMercenaryRejections.FleetNoSpace,
                    "This fleet has too little space for these mercenaries.");
            }
        }

        var hiredUnit = new UnitSlot(
            MercenaryLabel: slot.NameLabel,
            UnitTypeId: slot.UnitTypeId,
            Troops: slot.Troops,
            Quality: slot.Quality,
            Name: $"Mercenary unit (label {slot.NameLabel})");

        var updatedArmy = army with
        {
            Money = army.Money - cost,
            Units = ValueList.From(army.Units.Append(hiredUnit)),
        };

        var updatedPool = state.MercenaryPool.Where(s => s.SlotIndex != slot.SlotIndex);

        context.Events.Publish(new MercenaryHired(
            army.Nation, army.Id, slot.SlotIndex, slot.UnitTypeId, slot.Troops, slot.Quality, cost));

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);

        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(updatedArmies),
            MercenaryPool = ValueList.From(updatedPool),
        });
    }

    private static MercenaryPoolSlot? FindPoolSlot(GameState state, int slotIndex)
    {
        foreach (var slot in state.MercenaryPool)
        {
            if (slot.SlotIndex == slotIndex)
            {
                return slot;
            }
        }

        return null;
    }
}
