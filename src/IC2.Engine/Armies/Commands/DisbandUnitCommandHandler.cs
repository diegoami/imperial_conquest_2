using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;

namespace IC2.Engine.Armies.Commands;

/// <summary><c>TChangeArmyUnits</c>'s disband method. See <see cref="DisbandUnitCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class DisbandUnitCommandHandler : ICommandHandler<DisbandUnitCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(DisbandUnitCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                DisbandUnitRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                DisbandUnitRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        if (command.UnitIndex < 0 || command.UnitIndex >= army.Units.Count)
        {
            return CommandOutcome.Reject(
                DisbandUnitRejections.InvalidUnitIndex,
                $"Army '{army.Id}' has {army.Units.Count} units; index {command.UnitIndex} is out of range.");
        }

        // [designed] the army's last unit is refused so this per-unit order can never delete the army;
        // disband-army is the order that does that (issue #565). See DisbandUnitCommand's remarks.
        if (army.Units.Count == 1)
        {
            return CommandOutcome.Reject(
                DisbandUnitRejections.LastUnit,
                $"Army '{army.Id}' has only one unit; use disband-army to disband the whole army.");
        }

        var removed = army.Units[command.UnitIndex];
        var updatedUnits = new List<UnitSlot>(army.Units.Count - 1);
        for (var i = 0; i < army.Units.Count; i++)
        {
            if (i != command.UnitIndex)
            {
                updatedUnits.Add(army.Units[i]);
            }
        }

        // The unit's troops are lost -- no refund (see DisbandUnitCommand's remarks). No other entity
        // references a unit slot (units have no ids and are nested inside the army), so removing one needs
        // no dangling-reference sweep (build-process.md §4.2 gate 5).
        var updatedArmy = army with { Units = ValueList.From(updatedUnits) };
        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);

        var next = state with { Armies = ValueList.From(updatedArmies) };

        // A regular unit's disband lowers the nation's mobilisation (RemoveUnit, 2026-10-03 report); a
        // mercenary's changes no nation field. The rule is MobilizationRate's, read and not edited.
        var nation = state.NationById(command.IssuingNationId);
        if (!removed.IsMercenary && nation is not null)
        {
            var lowered = MobilizationRate.AfterOrderCancelled(
                nation.MobilizedPercent, removed.Troops, nation.Wealth, context.Ruleset.Recruitment);
            var updatedNation = nation with { MobilizedPercent = lowered };
            next = next with
            {
                Nations = ValueList.From(state.Nations.Select(n =>
                    string.Equals(n.Id, nation.Id, StringComparison.Ordinal) ? updatedNation : n)),
            };
        }

        return CommandOutcome.Accept(next);
    }
}
