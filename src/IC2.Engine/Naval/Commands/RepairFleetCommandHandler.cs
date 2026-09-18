using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// <c>TRepairFleet_OK</c> — clamps the requested points to <c>[0, 100 − condition]</c> exactly like the
/// original's own dialog (never rejecting an over-cap request, the same clamp-not-reject convention T38
/// established for the supply dialog), charges <c>ships × points / 5</c>, adds the points to
/// <see cref="FleetState.ConditionPercent"/>, and zeroes the fleet's moves.
/// </summary>
[CommandHandler]
public sealed class RepairFleetCommandHandler : ICommandHandler<RepairFleetCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(RepairFleetCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var fleet = state.FleetById(command.FleetId);
        if (fleet is null)
        {
            return CommandOutcome.Reject(
                RepairFleetRejections.UnknownFleet, $"'{command.FleetId}' is not a known fleet.");
        }

        if (!string.Equals(fleet.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                RepairFleetRejections.NotYourFleet,
                $"Fleet '{fleet.Id}' belongs to '{fleet.Nation}', not '{command.IssuingNationId}'.");
        }

        if (fleet.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                RepairFleetRejections.UnderConstruction, $"Fleet '{fleet.Id}' is still under construction.");
        }

        if (fleet.IsCarryingArmy)
        {
            return CommandOutcome.Reject(
                RepairFleetRejections.CarryingArmy, $"Fleet '{fleet.Id}' is carrying an army and cannot be repaired.");
        }

        if (command.Points <= 0)
        {
            return CommandOutcome.Reject(
                RepairFleetRejections.InvalidPoints, "Must request a positive number of repair points.");
        }

        var atOwnedCity = false;
        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal))
            {
                continue;
            }

            // Adjacency, not an exact tile match: a launched fleet only ever occupies a sea tile
            // (CoastalCity's own remarks), so it can never literally stand on a city's land tile.
            var distance = Math.Max(Math.Abs(city.X - fleet.X), Math.Abs(city.Y - fleet.Y));
            if (distance <= 1)
            {
                atOwnedCity = true;
                break;
            }
        }

        if (!atOwnedCity)
        {
            return CommandOutcome.Reject(
                RepairFleetRejections.NotAtOwnedCity,
                $"Fleet '{fleet.Id}' can only be repaired at one of its own nation's cities.");
        }

        var rules = context.Ruleset.Naval;
        var room = rules.MaxConditionPercent - fleet.ConditionPercent;
        var points = Math.Min(command.Points, room);
        var cost = (fleet.Ships * points) / rules.RepairCostDivisor;

        var updatedFleet = fleet with { ConditionPercent = fleet.ConditionPercent + points, Moves = 0 };
        var updatedNation = context.IssuingNation with { Treasury = context.IssuingNation.Treasury - cost };

        var updatedFleets = state.Fleets.Select(f =>
            string.Equals(f.Id, fleet.Id, StringComparison.Ordinal) ? updatedFleet : f);
        var updatedNations = state.Nations.Select(n =>
            string.Equals(n.Id, updatedNation.Id, StringComparison.Ordinal) ? updatedNation : n);

        return CommandOutcome.Accept(state with
        {
            Fleets = ValueList.From(updatedFleets),
            Nations = ValueList.From(updatedNations),
        });
    }
}
