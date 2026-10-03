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

        // [designed] 0 < troops < the unit's own troops -- see SplitUnitCommand's remarks (issue #565).
        if (command.Troops <= 0 || command.Troops >= source.Troops)
        {
            return CommandOutcome.Reject(
                SplitUnitRejections.InvalidTroops,
                $"Must split between 1 and {source.Troops - 1} troops off a unit of {source.Troops}.");
        }

        var rules = context.Ruleset.ArmyManagement;
        if (army.Units.Count >= rules.MaxUnitsPerArmy)
        {
            return CommandOutcome.Reject(
                SplitUnitRejections.TooManyUnits,
                $"Army '{army.Id}' already holds {rules.MaxUnitsPerArmy} units; it cannot take another.");
        }

        // Regulars are auto-named with the next free ordinal for their type across the nation's armies and
        // city garrison. A mercenary split keeps the source name -- see SplitUnitCommand's remarks.
        var newName = source.IsRegular
            ? ArmyNaming.NextName(state, command.IssuingNationId, source.UnitTypeId)
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
