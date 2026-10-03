using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies.Commands;

/// <summary><c>TChangeArmyUnits</c>'s rename method. See <see cref="RenameUnitCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class RenameUnitCommandHandler : ICommandHandler<RenameUnitCommand>
{
    /// <summary>
    /// The most characters the save format's unit-name field can hold: a 24-byte NUL-terminated ASCII
    /// field at unit-slot <c>+8</c> needs one byte for its terminator
    /// (<c>src/IC2.Data/SaveArmyTable.cs:100-111</c>). A save-format limit, not a ruleset value — see
    /// <see cref="RenameUnitCommand"/>'s remarks.
    /// </summary>
    private const int MaxUnitNameLength = 23;

    /// <inheritdoc/>
    public CommandOutcome Handle(RenameUnitCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                RenameUnitRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                RenameUnitRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        if (command.UnitIndex < 0 || command.UnitIndex >= army.Units.Count)
        {
            return CommandOutcome.Reject(
                RenameUnitRejections.InvalidUnitIndex,
                $"Army '{army.Id}' has {army.Units.Count} units; index {command.UnitIndex} is out of range.");
        }

        if (!IsValidName(command.Name))
        {
            return CommandOutcome.Reject(
                RenameUnitRejections.InvalidName,
                $"A unit name must be 1 to {MaxUnitNameLength} printable ASCII characters.");
        }

        var renamed = army.Units[command.UnitIndex] with { Name = command.Name };
        var updatedUnits = new List<UnitSlot>(army.Units.Count);
        for (var i = 0; i < army.Units.Count; i++)
        {
            updatedUnits.Add(i == command.UnitIndex ? renamed : army.Units[i]);
        }

        var updatedArmy = army with { Units = ValueList.From(updatedUnits) };
        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);

        return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
    }

    /// <summary>
    /// Non-empty, at most <see cref="MaxUnitNameLength"/> characters, every byte in the printable-ASCII
    /// range <c>0x20..0x7e</c> the save reader accepts (<c>src/IC2.Data/SaveArmyTable.cs:109</c>).
    /// </summary>
    private static bool IsValidName(string name)
    {
        if (name.Length == 0 || name.Length > MaxUnitNameLength)
        {
            return false;
        }

        foreach (var character in name)
        {
            if (character < ' ' || character > '~')
            {
                return false;
            }
        }

        return true;
    }
}
