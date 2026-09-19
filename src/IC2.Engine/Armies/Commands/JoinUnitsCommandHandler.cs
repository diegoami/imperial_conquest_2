using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;

namespace IC2.Engine.Armies.Commands;

/// <summary><c>TChangeArmyUnits_JoinUnits</c>. See <see cref="JoinUnitsCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class JoinUnitsCommandHandler : ICommandHandler<JoinUnitsCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(JoinUnitsCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        if (command.FirstUnitIndex == command.SecondUnitIndex
            || command.FirstUnitIndex < 0 || command.FirstUnitIndex >= army.Units.Count
            || command.SecondUnitIndex < 0 || command.SecondUnitIndex >= army.Units.Count)
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.InvalidUnitIndex,
                $"Army '{army.Id}' has {army.Units.Count} units; both indices must be distinct and in range.");
        }

        var first = army.Units[command.FirstUnitIndex];
        var second = army.Units[command.SecondUnitIndex];

        if (!UnitMergeGuard.IsMergeAllowedByMarker(first, second))
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.MercenaryUnit, "You can only join regular units together.");
        }

        if (!string.Equals(first.UnitTypeId, second.UnitTypeId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.DifferentUnitTypes, "You can only combine units of the same type.");
        }

        var type = context.Ruleset.UnitTypeById(first.UnitTypeId);
        if (type is null)
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.DifferentUnitTypes, $"'{first.UnitTypeId}' is not a unit type in ruleset '{context.Ruleset.Id}'.");
        }

        var combinedTroops = first.Troops + second.Troops;
        if (combinedTroops > type.StandardBattalionSize)
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.OverBattalionSize,
                $"Combined troops ({combinedTroops}) would exceed {first.UnitTypeId}'s battalion size of {type.StandardBattalionSize}.");
        }

        // Arithmetic mean, truncated toward zero -- see JoinUnitsCommand's remarks on rounding.
        var mergedQuality = (first.Quality + second.Quality) / 2;
        var merged = first with { Troops = combinedTroops, Quality = mergedQuality };

        var updatedUnits = new List<UnitSlot>(army.Units.Count - 1);
        for (var i = 0; i < army.Units.Count; i++)
        {
            if (i == command.SecondUnitIndex)
            {
                continue;
            }

            updatedUnits.Add(i == command.FirstUnitIndex ? merged : army.Units[i]);
        }

        var updatedArmy = army with { Units = ValueList.From(updatedUnits) };
        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);

        return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
    }
}
