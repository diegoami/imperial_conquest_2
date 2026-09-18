using IC2.Engine.Core;
using IC2.Engine.Model;

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

        var remainingFleet = fleet with { Ships = fleet.Ships - command.ShipsToNewFleet };
        var newFleet = new FleetState(
            Id: command.NewFleetId,
            Nation: fleet.Nation,
            X: fleet.X,
            Y: fleet.Y,
            Moves: 0,
            Ships: command.ShipsToNewFleet,
            ConditionPercent: fleet.ConditionPercent,
            Money: 0,
            SupplyTons: 0,
            ConstructionTicksRemaining: null,
            BuildCityId: null,
            CarriedArmyId: null,
            CoveredTileCode: fleet.CoveredTileCode);

        var updatedFleets = state.Fleets
            .Select(f => string.Equals(f.Id, fleet.Id, StringComparison.Ordinal) ? remainingFleet : f)
            .Append(newFleet);

        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleets) });
    }
}
