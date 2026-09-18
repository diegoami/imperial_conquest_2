using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Naval.Commands;

/// <summary>See <see cref="FleetToFleetTransferCommand"/> for the full rule and its provenance.</summary>
[CommandHandler]
public sealed class FleetToFleetTransferCommandHandler : ICommandHandler<FleetToFleetTransferCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(FleetToFleetTransferCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(command.FromFleetId, command.ToFleetId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.SameFleet, "A fleet cannot transfer to itself.");
        }

        var state = context.State;
        var source = state.FleetById(command.FromFleetId);
        if (source is null)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.UnknownFleet, $"'{command.FromFleetId}' is not a known fleet.");
        }

        var target = state.FleetById(command.ToFleetId);
        if (target is null)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.UnknownFleet, $"'{command.ToFleetId}' is not a known fleet.");
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

        // DoD 2 (T14 round-2 review, B6): refuse outright rather than delete a carrier out from under an
        // embarked army. Every sibling naval order already refuses a carrying fleet.
        if (source.IsCarryingArmy || target.IsCarryingArmy)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.CarryingArmy, "Neither fleet may carry an army to transfer.");
        }

        if (command.Ships < 0 || command.SupplyTons < 0 || command.Money < 0)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.InvalidAmount, "Cannot transfer a negative amount of any resource.");
        }

        if (command.Ships == 0 && command.SupplyTons == 0 && command.Money == 0)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.InvalidAmount,
                "Must transfer a positive amount of at least one of ships, supply or money.");
        }

        if (command.Ships > source.Ships)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.InsufficientShips,
                $"Fleet '{source.Id}' has only {source.Ships} ships.");
        }

        if (command.SupplyTons > source.SupplyTons)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.InsufficientSupply,
                $"Fleet '{source.Id}' has only {source.SupplyTons} tons of supply.");
        }

        if (command.Money > source.Money)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.InsufficientMoney,
                $"Fleet '{source.Id}' has only {source.Money} money.");
        }

        var rules = context.Ruleset.Naval;
        var remainingShips = source.Ships - command.Ships;
        var combinedShips = target.Ships + command.Ships;

        // DoD 5 [designed]: capped at the same ceiling JoinFleets already enforces -- see this command's
        // remarks for why a new field or literal is not used instead.
        if (combinedShips > rules.JoinMaxShips)
        {
            return CommandOutcome.Reject(
                FleetToFleetTransferRejections.CombinedShipsTooLarge,
                $"There are more than {rules.JoinMaxShips} ships in these fleets combined.");
        }

        if (remainingShips == 0)
        {
            // DoD 3 (T14 round-2 review, B7): pool the source's FULL remaining supply/money into the
            // target -- not just the requested SupplyTons/Money -- so nothing left aboard a disbanding
            // fleet is annihilated. The naval twin of the confirmed army-to-army auto-disband-on-empty
            // mechanism; see this command's remarks.
            var pooledTarget = target with
            {
                Ships = combinedShips,
                SupplyTons = target.SupplyTons + source.SupplyTons,
                Money = target.Money + source.Money,
            };

            var updatedFleetsOnDisband = state.Fleets
                .Where(f => !string.Equals(f.Id, source.Id, StringComparison.Ordinal))
                .Select(f => string.Equals(f.Id, target.Id, StringComparison.Ordinal) ? pooledTarget : f);

            context.Events.Publish(new FleetToFleetTransferCompleted(
                source.Nation, source.Id, target.Id, command.Ships, source.SupplyTons, source.Money, SourceDisbanded: true));

            return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleetsOnDisband) });
        }

        var updatedSource = source with
        {
            Ships = remainingShips,
            SupplyTons = source.SupplyTons - command.SupplyTons,
            Money = source.Money - command.Money,
        };
        var updatedTarget = target with
        {
            Ships = combinedShips,
            SupplyTons = target.SupplyTons + command.SupplyTons,
            Money = target.Money + command.Money,
        };

        var updatedFleets = state.Fleets.Select(f =>
            string.Equals(f.Id, source.Id, StringComparison.Ordinal) ? updatedSource :
            string.Equals(f.Id, target.Id, StringComparison.Ordinal) ? updatedTarget : f);

        context.Events.Publish(new FleetToFleetTransferCompleted(
            source.Nation, source.Id, target.Id, command.Ships, command.SupplyTons, command.Money, SourceDisbanded: false));

        return CommandOutcome.Accept(state with { Fleets = ValueList.From(updatedFleets) });
    }
}
