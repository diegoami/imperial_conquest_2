using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Naval.Commands;

/// <summary><c>TUnitMap_JoinFleets</c>.</summary>
[CommandHandler]
public sealed class JoinFleetsCommandHandler : ICommandHandler<JoinFleetsCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(JoinFleetsCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(command.SurvivingFleetId, command.AbsorbedFleetId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.SameFleet, "A fleet cannot join itself.");
        }

        var state = context.State;
        var survivor = state.FleetById(command.SurvivingFleetId);
        if (survivor is null)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.UnknownFleet, $"'{command.SurvivingFleetId}' is not a known fleet.");
        }

        var absorbed = state.FleetById(command.AbsorbedFleetId);
        if (absorbed is null)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.UnknownFleet, $"'{command.AbsorbedFleetId}' is not a known fleet.");
        }

        if (!string.Equals(survivor.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(absorbed.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.NotYourFleet, "Both fleets must belong to the issuing nation.");
        }

        if (survivor.IsUnderConstruction || absorbed.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.UnderConstruction, "Neither fleet may still be under construction.");
        }

        if (survivor.X != absorbed.X || survivor.Y != absorbed.Y)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.NotCoLocated, "Both fleets must be on the same tile to join.");
        }

        if (survivor.IsCarryingArmy || absorbed.IsCarryingArmy)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.CarryingArmy, "Neither fleet may carry an army to join.");
        }

        var rules = context.Ruleset.Naval;
        var combinedShips = survivor.Ships + absorbed.Ships;
        if (combinedShips > rules.JoinMaxShips)
        {
            return CommandOutcome.Reject(
                JoinFleetsRejections.CombinedShipsTooLarge,
                $"There are more than {rules.JoinMaxShips} ships in these fleets combined.");
        }

        var joined = survivor with
        {
            Ships = combinedShips,
            SupplyTons = survivor.SupplyTons + absorbed.SupplyTons,
            Money = survivor.Money + absorbed.Money,
            Moves = 0,
        };

        var updatedFleets = state.Fleets
            .Where(f => !string.Equals(f.Id, absorbed.Id, StringComparison.Ordinal))
            .Select(f => string.Equals(f.Id, survivor.Id, StringComparison.Ordinal) ? joined : f);

        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleets) });
    }
}
