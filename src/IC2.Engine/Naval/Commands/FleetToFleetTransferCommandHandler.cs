using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Naval.Commands;

/// <summary><c>TFleetToFleet_OK</c>. See <see cref="FleetToFleetTransferCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class FleetToFleetTransferCommandHandler : ICommandHandler<FleetToFleetTransferCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(FleetToFleetTransferCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(command.SourceFleetId, command.TargetFleetId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(FleetToFleetTransferRejections.SameFleet, "A fleet cannot transfer to itself.");
        }

        var state = context.State;
        var source = state.FleetById(command.SourceFleetId);
        if (source is null)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.UnknownFleet, $"'{command.SourceFleetId}' is not a known fleet.");
        }

        var target = state.FleetById(command.TargetFleetId);
        if (target is null)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.UnknownFleet, $"'{command.TargetFleetId}' is not a known fleet.");
        }

        if (!string.Equals(source.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(target.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.NotYourFleet, "Both fleets must belong to the issuing nation.");
        }

        if (source.IsUnderConstruction || target.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.UnderConstruction, "Neither fleet may still be under construction.");
        }

        if (source.X != target.X || source.Y != target.Y)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.NotCoLocated, "Both fleets must be on the same tile to transfer.");
        }

        if (command.Ships <= 0 && command.SupplyTons <= 0 && command.Money <= 0)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.NothingRequested, "Nothing was requested to transfer.");
        }

        var ships = Math.Clamp(command.Ships, 0, source.Ships);
        var supplyTons = Math.Clamp(command.SupplyTons, 0, source.SupplyTons);
        var money = Math.Clamp(command.Money, 0, source.Money);

        var updatedSource = source with
        {
            Ships = source.Ships - ships,
            SupplyTons = source.SupplyTons - supplyTons,
            Money = source.Money - money,
        };
        var updatedTarget = target with
        {
            Ships = target.Ships + ships,
            SupplyTons = target.SupplyTons + supplyTons,
            Money = target.Money + money,
        };

        // "a fleet left at zero ships deleted" -- decompiled-unit-map-orders-and-record-fields.md.
        var updatedFleets = updatedSource.Ships <= 0
            ? state.Fleets
                .Where(f => !string.Equals(f.Id, source.Id, StringComparison.Ordinal))
                .Select(f => string.Equals(f.Id, target.Id, StringComparison.Ordinal) ? updatedTarget : f)
            : state.Fleets.Select(f =>
                string.Equals(f.Id, source.Id, StringComparison.Ordinal) ? updatedSource
                : string.Equals(f.Id, target.Id, StringComparison.Ordinal) ? updatedTarget
                : f);

        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleets) });
    }
}
