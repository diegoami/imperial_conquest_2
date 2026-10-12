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

        var indexes = command.UnitIndexes;
        if (indexes.Count < 2
            || indexes.Any(i => i < 0 || i >= army.Units.Count)
            || indexes.Distinct().Count() != indexes.Count)
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.InvalidUnitIndex,
                $"Army '{army.Id}' has {army.Units.Count} units; at least two distinct in-range indices are needed.");
        }

        var first = army.Units[indexes[0]];
        var selected = indexes.Select(i => army.Units[i]).ToList();

        if (selected.Any(u => !UnitMergeGuard.IsMergeAllowedByMarker(first, u)))
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.MercenaryUnit, "You can only join regular units together.");
        }

        if (selected.Any(u => !string.Equals(first.UnitTypeId, u.UnitTypeId, StringComparison.Ordinal)))
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

        var combinedTroops = selected.Sum(u => u.Troops);
        if (combinedTroops > type.StandardBattalionSize)
        {
            return CommandOutcome.Reject(
                JoinUnitsRejections.OverBattalionSize,
                $"Combined troops ({combinedTroops}) would exceed {first.UnitTypeId}'s battalion size of {type.StandardBattalionSize}.");
        }

        // Integer average (sum / n, truncated toward zero) -- see JoinUnitsCommand's remarks on rounding.
        var mergedQuality = selected.Sum(u => u.Quality) / selected.Count;
        var merged = first with { Troops = combinedTroops, Quality = mergedQuality };

        var removed = indexes.Skip(1).ToHashSet();
        var updatedUnits = new List<UnitSlot>(army.Units.Count - removed.Count);
        for (var i = 0; i < army.Units.Count; i++)
        {
            if (removed.Contains(i))
            {
                continue;
            }

            updatedUnits.Add(i == indexes[0] ? merged : army.Units[i]);
        }

        var updatedArmy = army with { Units = ValueList.From(updatedUnits) };
        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);

        return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
    }
}
