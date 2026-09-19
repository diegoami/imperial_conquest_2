using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Armies.Commands;

/// <summary><c>TUnitMap_SplitArmy</c> / <c>FUN_00449F08</c>. See <see cref="SplitArmyCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class SplitArmyCommandHandler : ICommandHandler<SplitArmyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(SplitArmyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                SplitArmyRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                SplitArmyRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        if (army.IsEmbarked)
        {
            return CommandOutcome.Reject(
                SplitArmyRejections.ArmyEmbarked, $"Army '{army.Id}' is aboard a fleet and cannot be split.");
        }

        var rules = context.Ruleset.ArmyManagement;
        if (army.Units.Count < rules.SplitMinUnits)
        {
            return CommandOutcome.Reject(
                SplitArmyRejections.TooFewUnitsToSplit,
                $"Army '{army.Id}' has fewer than {rules.SplitMinUnits} units and cannot be split.");
        }

        var selection = command.UnitIndicesToNewArmy;
        var selected = new HashSet<int>();
        var selectionValid = selection.Count > 0 && selection.Count < army.Units.Count;
        if (selectionValid)
        {
            foreach (var index in selection)
            {
                if (index < 0 || index >= army.Units.Count || !selected.Add(index))
                {
                    selectionValid = false;
                    break;
                }
            }
        }

        if (!selectionValid)
        {
            return CommandOutcome.Reject(
                SplitArmyRejections.InvalidUnitSelection,
                $"Must select between 1 and {army.Units.Count - 1} distinct, in-range unit indices to move to the new army.");
        }

        if (state.ArmyById(command.NewArmyId) is not null)
        {
            return CommandOutcome.Reject(
                SplitArmyRejections.DuplicateArmyId, $"Army id '{command.NewArmyId}' is already in use.");
        }

        if (state.Armies.Count >= rules.MaxArmies)
        {
            return CommandOutcome.Reject(
                SplitArmyRejections.TooManyArmies, $"The army table already holds {rules.MaxArmies} armies.");
        }

        if (command.MoneyToNewArmy < 0 || command.MoneyToNewArmy > army.Money)
        {
            return CommandOutcome.Reject(
                SplitArmyRejections.InvalidMoneyAllocation,
                $"Cannot move {command.MoneyToNewArmy} money to the new army; army '{army.Id}' holds {army.Money}.");
        }

        if (command.SupplyTonsToNewArmy < 0 || command.SupplyTonsToNewArmy > army.SupplyTons)
        {
            return CommandOutcome.Reject(
                SplitArmyRejections.InvalidSupplyAllocation,
                $"Cannot move {command.SupplyTonsToNewArmy} supply tons to the new army; army '{army.Id}' holds {army.SupplyTons}.");
        }

        var movedUnits = new List<UnitSlot>(selected.Count);
        var keptUnits = new List<UnitSlot>(army.Units.Count - selected.Count);
        for (var i = 0; i < army.Units.Count; i++)
        {
            (selected.Contains(i) ? movedUnits : keptUnits).Add(army.Units[i]);
        }

        // seatAsymmetry gating (design-audit.md Q6, SplitArmyCommand's own remarks): classical-faithful
        // gives a human seat 0 moves and an AI seat 1; improved generalises the AI's value to every seat.
        var seatIsAi = context.IssuingNation.Control == SeatControl.Ai;
        var newArmyMoves = context.Ruleset.Flags.SeatAsymmetry == SeatAsymmetryModel.Faithful
            ? (seatIsAi ? rules.NewArmyMovesAiSeat : rules.NewArmyMovesHumanSeat)
            : rules.NewArmyMovesAiSeat;

        var updatedParent = army with
        {
            Units = ValueList.From(keptUnits),
            Money = army.Money - command.MoneyToNewArmy,
            SupplyTons = army.SupplyTons - command.SupplyTonsToNewArmy,
        };

        var newArmy = new ArmyState(
            Id: command.NewArmyId,
            Nation: army.Nation,
            X: army.X,
            Y: army.Y,
            Moves: newArmyMoves,
            Morale: rules.NewArmyMorale,
            Money: command.MoneyToNewArmy,
            SupplyTons: command.SupplyTonsToNewArmy,
            CoveredTileCode: army.CoveredTileCode,
            AboardFleetId: null,
            Units: ValueList.From(movedUnits));

        var updatedArmies = state.Armies
            .Select(a => string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedParent : a)
            .Append(newArmy);

        return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
    }
}
