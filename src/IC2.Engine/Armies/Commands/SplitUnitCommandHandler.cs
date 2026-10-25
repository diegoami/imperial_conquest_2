using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies.Commands;

/// <summary><c>TChangeArmyUnits</c>'s split method (<c>TSplitArmyUnit_OK</c>, <c>0x004444CC</c>). See <see cref="SplitUnitCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class SplitUnitCommandHandler : ICommandHandler<SplitUnitCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(SplitUnitCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                SplitUnitRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                SplitUnitRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        if (command.UnitIndex < 0 || command.UnitIndex >= army.Units.Count)
        {
            return CommandOutcome.Reject(
                SplitUnitRejections.InvalidUnitIndex,
                $"Army '{army.Id}' has {army.Units.Count} units; index {command.UnitIndex} is out of range.");
        }

        var source = army.Units[command.UnitIndex];

        // #802 R39 (play CU02): the original splits only regular units.
        if (!source.IsRegular)
        {
            return CommandOutcome.Reject(SplitUnitRejections.NotRegular, "You can only split regular units.");
        }

        // TChangeArmyUnits_SplitUnit first requires 2/5 of the type's standard size to open a split,
        // then keeps at least 1/5 of that size in each resulting unit (research g2 checks C5c and the
        // 2026-10-11 g2-original-checks report; broad conversion cases F04/F51 are independent controls).
        var type = context.Ruleset.UnitTypeById(source.UnitTypeId);
        if (type is null)
        {
            return CommandOutcome.Reject(
                SplitUnitRejections.InvalidTroops, $"'{source.UnitTypeId}' is not a declared unit type.");
        }

        var sizeLimit = type.StandardBattalionSize;
        var minimumSource = (int)((long)sizeLimit * 2 / 5);
        var minimumPart = sizeLimit / 5;
        if (source.Troops < minimumSource)
        {
            return CommandOutcome.Reject(
                SplitUnitRejections.TooSmall,
                "This unit is too small to split.");
        }

        if (command.Troops < minimumPart || source.Troops - command.Troops < minimumPart)
        {
            return CommandOutcome.Reject(
                SplitUnitRejections.InvalidTroops,
                $"Each resulting unit must have at least {minimumPart} troops.");
        }

        var rules = context.Ruleset.ArmyManagement;
        if (army.Units.Count >= rules.MaxUnitsPerArmy)
        {
            return CommandOutcome.Reject(
                SplitUnitRejections.TooManyUnits,
                $"This army already has {rules.MaxUnitsPerArmy} units.");
        }

        // Regulars are auto-named with the next free ordinal for their type across the nation's armies and
        // city garrison, using the original split-specific spacing. A mercenary split keeps the source
        // name -- see SplitUnitCommand's remarks.
        var newName = source.IsRegular
            ? ArmyNaming.NextSplitUnitName(state, command.IssuingNationId, source.UnitTypeId)
            : source.Name;

        var kept = source with { Troops = source.Troops - command.Troops };
        var splitOff = source with { Troops = command.Troops, Name = newName };

        var updatedUnits = new List<UnitSlot>(army.Units.Count + 1);
        for (var i = 0; i < army.Units.Count; i++)
        {
            updatedUnits.Add(i == command.UnitIndex ? kept : army.Units[i]);
        }

        // The new unit stays in the same army; it is appended so the source unit's own index is stable.
        updatedUnits.Add(splitOff);

        var updatedArmy = army with { Units = ValueList.From(updatedUnits) };
        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);

        return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
    }
}
