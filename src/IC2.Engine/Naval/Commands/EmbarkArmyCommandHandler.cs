using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// <c>FUN_0044B79C</c>: zeroes the fleet's moves, sets the carried-army link both ways, clears the
/// army's covered map cell, snaps the army onto the fleet, and zeroes the army's moves too — trimming an
/// over-capacity army first when <see cref="EmbarkArmyCommand"/>'s <c>seatAsymmetry</c> gating admits it.
/// </summary>
[CommandHandler]
public sealed class EmbarkArmyCommandHandler : ICommandHandler<EmbarkArmyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(EmbarkArmyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        var fleet = state.FleetById(command.FleetId);
        if (fleet is null)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.UnknownFleet, $"'{command.FleetId}' is not a known fleet.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal)
            || !string.Equals(fleet.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.NotYours, "Both the army and the fleet must belong to the issuing nation.");
        }

        if (fleet.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.UnderConstruction, $"Fleet '{fleet.Id}' is still under construction.");
        }

        if (army.IsEmbarked)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.ArmyAlreadyEmbarked, $"Army '{army.Id}' is already embarked.");
        }

        if (fleet.IsCarryingArmy)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.FleetAlreadyCarrying, $"Fleet '{fleet.Id}' is already carrying an army.");
        }

        if (army.X != fleet.X || army.Y != fleet.Y)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.NotCoLocated, "The army and the fleet must be on the same tile to embark.");
        }

        var capacity = fleet.Ships * context.Ruleset.Naval.TransportTroopsPerShip;
        var totalTroops = army.TotalTroops;
        var overCapacity = totalTroops > capacity;

        // seatAsymmetry gating (design-audit.md Q6, EmbarkArmyCommand's own remarks): classical-faithful
        // trims an AI seat and refuses a human one; improved refuses every seat.
        var seatIsAi = context.IssuingNation.Control == SeatControl.Ai;
        var trims = overCapacity
            && context.Ruleset.Flags.SeatAsymmetry == SeatAsymmetryModel.Faithful
            && seatIsAi;
        var refuses = overCapacity && !trims;

        if (refuses)
        {
            return CommandOutcome.Reject(
                EmbarkArmyRejections.ArmyTooLarge,
                $"Army '{army.Id}' has {totalTroops} troops, more than fleet '{fleet.Id}''s capacity of {capacity}.");
        }

        var units = trims
            ? ValueList.From(ArmyTransportTrim.TrimToCapacity(army.Units, totalTroops, capacity))
            : army.Units;

        var updatedArmy = army with
        {
            X = fleet.X,
            Y = fleet.Y,
            Moves = 0,
            CoveredTileCode = null,
            AboardFleetId = fleet.Id,
            Units = units,
        };
        var updatedFleet = fleet with { CarriedArmyId = army.Id, Moves = 0 };

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);
        var updatedFleets = state.Fleets.Select(f =>
            string.Equals(f.Id, fleet.Id, StringComparison.Ordinal) ? updatedFleet : f);

        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(updatedArmies),
            Fleets = ValueList.From(updatedFleets),
        });
    }
}
