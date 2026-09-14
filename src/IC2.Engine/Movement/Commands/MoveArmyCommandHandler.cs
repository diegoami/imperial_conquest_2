using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Movement.Commands;

/// <summary>
/// The first command handler in the engine — <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy
/// world": wires <see cref="MovementWalker"/> and <see cref="MovementAbortRule"/> behind the command seam
/// T03 built and T09 never had a caller for. Re-implements no movement rule of its own: every cost and
/// every abort decision is T09's, read straight off <see cref="CommandContext.Ruleset"/> and
/// <see cref="CommandContext.World"/>.
/// </summary>
/// <remarks>
/// <strong>Occupancy, not a decoded marker code</strong> — the same convention <see cref="MovementWalker"/>
/// itself documents: a cell is blocked when a city, another army, or a fleet currently stands on it,
/// read directly off <see cref="GameState.Cities"/>/<see cref="GameState.Armies"/>/<see cref="GameState.Fleets"/>,
/// never from a re-derived marker-code range.
/// </remarks>
[CommandHandler]
public sealed class MoveArmyCommandHandler : ICommandHandler<MoveArmyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(MoveArmyCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var army = state.ArmyById(command.ArmyId);
        if (army is null)
        {
            return CommandOutcome.Reject(
                MoveArmyRejections.UnknownArmy, $"'{command.ArmyId}' is not a known army.");
        }

        if (!string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                MoveArmyRejections.NotYourArmy,
                $"Army '{army.Id}' belongs to '{army.Nation}', not '{command.IssuingNationId}'.");
        }

        if (army.Moves <= 0)
        {
            return CommandOutcome.Reject(
                MoveArmyRejections.NoMovesLeft, $"Army '{army.Id}' has no moves left this turn.");
        }

        var world = context.World;
        var destination = new GridPoint(command.X, command.Y);
        if (!InBounds(destination, world))
        {
            return CommandOutcome.Reject(
                MoveArmyRejections.OutOfBounds,
                $"({command.X}, {command.Y}) is outside the {world.Width}x{world.Height} map.");
        }

        var terrainCells = world.Terrain.Decode(world.Width, world.Height);

        string? TileTypeIdAt(GridPoint point)
        {
            if (!InBounds(point, world))
            {
                return null;
            }

            var code = terrainCells[(point.Y * world.Width) + point.X];
            return world.TileTypeByCode(code)?.Id;
        }

        bool IsBlocked(GridPoint point)
        {
            foreach (var city in state.Cities)
            {
                if (city.X == point.X && city.Y == point.Y)
                {
                    return true;
                }
            }

            foreach (var otherArmy in state.Armies)
            {
                if (!string.Equals(otherArmy.Id, army.Id, StringComparison.Ordinal)
                    && otherArmy.CoveredTileCode is not null
                    && otherArmy.X == point.X && otherArmy.Y == point.Y)
                {
                    return true;
                }
            }

            foreach (var fleet in state.Fleets)
            {
                if (!fleet.IsUnderConstruction && fleet.X == point.X && fleet.Y == point.Y)
                {
                    return true;
                }
            }

            return false;
        }

        var from = new GridPoint(army.X, army.Y);
        var walk = MovementWalker.Walk(
            from, destination, army.Moves, context.Ruleset.Terrain, TileTypeIdAt, IsBlocked, context.Events);

        var movesAfter = walk.StopReason == MovementStopReason.InsufficientMoves
            ? MovementAbortRule.MovesAfterAbortedStep(
                walk.MovesRemaining, context.IssuingNation.Control, context.Ruleset.Flags.SeatAsymmetry)
            : walk.MovesRemaining;

        var finalPosition = walk.FinalPosition;
        var coveredTileCode = InBounds(finalPosition, world)
            ? terrainCells[(finalPosition.Y * world.Width) + finalPosition.X]
            : army.CoveredTileCode;

        var updatedArmy = army with
        {
            X = finalPosition.X,
            Y = finalPosition.Y,
            Moves = movesAfter,
            CoveredTileCode = coveredTileCode,
        };

        context.Events.Publish(new ArmyMoved(
            army.Id, army.Nation, from.X, from.Y, finalPosition.X, finalPosition.Y, walk.MovesSpent));

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);

        return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
    }

    private static bool InBounds(GridPoint point, World world) =>
        (uint)point.X < (uint)world.Width && (uint)point.Y < (uint)world.Height;
}
