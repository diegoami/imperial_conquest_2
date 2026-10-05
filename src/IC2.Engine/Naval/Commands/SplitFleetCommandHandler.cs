using IC2.Engine.Armies;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval.Commands;

/// <summary><c>TUnitMap_SplitFleet</c>. See <see cref="SplitFleetCommand"/>'s remarks for the money/supply default.</summary>
[CommandHandler]
public sealed class SplitFleetCommandHandler : ICommandHandler<SplitFleetCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(SplitFleetCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var fleet = state.FleetById(command.FleetId);
        if (fleet is null)
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.UnknownFleet, $"'{command.FleetId}' is not a known fleet.");
        }

        if (!string.Equals(fleet.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.NotYourFleet,
                $"Fleet '{fleet.Id}' belongs to '{fleet.Nation}', not '{command.IssuingNationId}'.");
        }

        if (fleet.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.UnderConstruction, $"Fleet '{fleet.Id}' is still under construction.");
        }

        if (fleet.IsCarryingArmy)
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.CarryingArmy, $"Fleet '{fleet.Id}' is carrying an army and cannot be split.");
        }

        var rules = context.Ruleset.Naval;
        if (fleet.Ships < rules.SplitMinShips)
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.TooFewShipsToSplit,
                $"Fleet '{fleet.Id}' has fewer than {rules.SplitMinShips} ships and cannot be split.");
        }

        if (command.ShipsToNewFleet < 1 || command.ShipsToNewFleet >= fleet.Ships)
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.InvalidShipCount,
                $"Must move between 1 and {fleet.Ships - 1} ships to the new fleet, not {command.ShipsToNewFleet}.");
        }

        if (state.FleetById(command.NewFleetId) is not null)
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.DuplicateFleetId, $"Fleet id '{command.NewFleetId}' is already in use.");
        }

        // Each allocation is between 0 and what the parent fleet holds — the Split fleet dialog's own
        // spinner bounds (SplitFleetCommand's remarks). No capacity rebalance and no purse bound applies.
        if (command.SupplyTonsToNewFleet < 0 || command.SupplyTonsToNewFleet > fleet.SupplyTons)
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.InvalidSupplyAllocation,
                $"Cannot move {command.SupplyTonsToNewFleet} supply tons to the new fleet; fleet '{fleet.Id}' holds {fleet.SupplyTons}.");
        }

        if (command.MoneyToNewFleet < 0 || command.MoneyToNewFleet > fleet.Money)
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.InvalidMoneyAllocation,
                $"Cannot move {command.MoneyToNewFleet} money to the new fleet; fleet '{fleet.Id}' holds {fleet.Money}.");
        }

        // The new fleet stands one tile from the parent, not on it (bugs #584, #596), by the same 3×3
        // last-qualifying-cell scan the army split uses, restricted to fleet-passable water.
        if (SplitPlacement.FleetCell(state, context.World, new GridPoint(fleet.X, fleet.Y)) is not { } newCell)
        {
            return CommandOutcome.Reject(
                SplitFleetRejections.NoFreeAdjacentTile,
                $"Fleet '{fleet.Id}' has no free adjacent tile for the new fleet.");
        }

        var remainingFleet = fleet with
        {
            Ships = fleet.Ships - command.ShipsToNewFleet,
            SupplyTons = fleet.SupplyTons - command.SupplyTonsToNewFleet,
            Money = fleet.Money - command.MoneyToNewFleet,
        };
        var newFleet = new FleetState(
            Id: command.NewFleetId,
            Nation: fleet.Nation,
            X: newCell.X,
            Y: newCell.Y,
            Moves: 0,
            Ships: command.ShipsToNewFleet,
            ConditionPercent: fleet.ConditionPercent,
            Money: command.MoneyToNewFleet,
            SupplyTons: command.SupplyTonsToNewFleet,
            ConstructionTicksRemaining: null,
            BuildCityId: null,
            CarriedArmyId: null,
            CoveredTileCode: SplitPlacement.TerrainCodeAt(context.World, newCell));

        var updatedFleets = state.Fleets
            .Select(f => string.Equals(f.Id, fleet.Id, StringComparison.Ordinal) ? remainingFleet : f)
            .Append(newFleet);

        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleets) });
    }
}
