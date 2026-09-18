using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval.Commands;

/// <summary><c>FUN_0044B840</c>. See <see cref="DisembarkArmyCommand"/>'s remarks.</summary>
[CommandHandler]
public sealed class DisembarkArmyCommandHandler : ICommandHandler<DisembarkArmyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(DisembarkArmyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                DisembarkArmyRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                DisembarkArmyRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        if (army.AboardFleetId is not { } fleetId)
        {
            return CommandOutcome.Reject(
                DisembarkArmyRejections.ArmyNotEmbarked, $"Army '{army.Id}' is not aboard a fleet.");
        }

        var fleet = state.FleetById(fleetId);
        if (fleet is null)
        {
            return CommandOutcome.Reject(
                DisembarkArmyRejections.UnknownFleet, $"Carrying fleet '{fleetId}' is not a known fleet.");
        }

        var world = context.World;
        var fleetPoint = new GridPoint(fleet.X, fleet.Y);

        GridPoint target;
        if (command.X is { } x && command.Y is { } y)
        {
            target = new GridPoint(x, y);
            if (LandingTile.ChebyshevDistance(fleetPoint, target) > 1)
            {
                return CommandOutcome.Reject(
                    DisembarkArmyRejections.LandingTileTooFar,
                    $"({x}, {y}) is not adjacent to fleet '{fleet.Id}' at {fleetPoint}.");
            }

            if (!LandingTile.IsPassableForArmy(target, world))
            {
                return CommandOutcome.Reject(
                    DisembarkArmyRejections.LandingTileNotPassable, $"({x}, {y}) is not passable for an army.");
            }
        }
        else
        {
            if (context.IssuingNation.Control != SeatControl.Ai)
            {
                return CommandOutcome.Reject(
                    DisembarkArmyRejections.LandingTileRequired,
                    "A human seat must name a landing tile; the automatic branch is AI-only.");
            }

            var autoPicked = LandingTile.FirstAdjacentLandTile(fleetPoint, world);
            if (autoPicked is null)
            {
                return CommandOutcome.Reject(
                    DisembarkArmyRejections.NoLandingTileAvailable,
                    $"No passable landing tile is adjacent to fleet '{fleet.Id}' at {fleetPoint}.");
            }

            target = autoPicked.Value;
        }

        var terrainCells = world.Terrain.Decode(world.Width, world.Height);
        var landedTileCode = terrainCells[(target.Y * world.Width) + target.X];

        var updatedArmy = army with
        {
            X = target.X,
            Y = target.Y,
            Moves = 0,
            CoveredTileCode = landedTileCode,
            AboardFleetId = null,
        };
        var updatedFleet = fleet with { CarriedArmyId = null };

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
