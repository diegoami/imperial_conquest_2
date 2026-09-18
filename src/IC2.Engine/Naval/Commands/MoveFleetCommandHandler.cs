using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Wires <see cref="MovementWalker"/> and <see cref="TerrainCostLookup"/> behind the fleet-move command —
/// <c>docs/task-catalogue.md</c> "T14 Naval" Scope, "sea movement via T09's walker," and its Hazards:
/// prices exclusively through <see cref="MovementWalker.Walk"/>, never <see cref="Ruleset.MoveCostFor"/>.
/// </summary>
/// <remarks>
/// <strong>Blocking, for a fleet, is a different set of markers than for an army.</strong>
/// <see cref="MoveArmyCommandHandler"/> blocks on every city, army and other launched fleet
/// (<see cref="Movement.MovementWalker"/>'s own remarks: "the same convention... a city, another army, or
/// a fleet currently stands on it"). A fleet instead treats <em>land itself</em> as the blocker — any
/// tile whose <see cref="TileType.PassableByFleets"/> is <see langword="false"/> — plus any other
/// launched fleet occupying the cell; it does not block on cities or on armies, both of which stand on
/// land tiles a fleet's own terrain-passability check already excludes, with one deliberate exception: a
/// city's own tile is never itself land-blocked here, because <see cref="CoastalCity"/>'s remarks explain
/// why a fleet must be able to approach one to repair, and terrain-passability alone (a city sits on a
/// land tile type) would otherwise make every city unreachable by sea. This is <c>[designed]</c>: no
/// report describes fleet-versus-city blocking specifically, so this reproduces the confirmed effect
/// (fleets dock at coastal cities) rather than inventing a distinct exception field for it.
/// </remarks>
[CommandHandler]
public sealed class MoveFleetCommandHandler : ICommandHandler<MoveFleetCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(MoveFleetCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var fleet = state.FleetById(command.FleetId);
        if (fleet is null)
        {
            return CommandOutcome.Reject(
                MoveFleetRejections.UnknownFleet, $"'{command.FleetId}' is not a known fleet.");
        }

        if (!string.Equals(fleet.Nation, command.IssuingNationId, StringComparison.Ordinal))
        {
            return CommandOutcome.Reject(
                MoveFleetRejections.NotYourFleet,
                $"Fleet '{fleet.Id}' belongs to '{fleet.Nation}', not '{command.IssuingNationId}'.");
        }

        if (fleet.IsUnderConstruction)
        {
            return CommandOutcome.Reject(
                MoveFleetRejections.UnderConstruction, $"Fleet '{fleet.Id}' is still under construction.");
        }

        if (fleet.Moves <= 0)
        {
            return CommandOutcome.Reject(
                MoveFleetRejections.NoMovesLeft, $"Fleet '{fleet.Id}' has no moves left this turn.");
        }

        var world = context.World;
        var destination = new GridPoint(command.X, command.Y);
        if (!InBounds(destination, world))
        {
            return CommandOutcome.Reject(
                MoveFleetRejections.OutOfBounds,
                $"({command.X}, {command.Y}) is outside the {world.Width}x{world.Height} map.");
        }

        var terrainCells = world.Terrain.Decode(world.Width, world.Height);

        TileType? TileTypeAt(GridPoint point) =>
            InBounds(point, world) ? world.TileTypeByCode(terrainCells[(point.Y * world.Width) + point.X]) : null;

        string? TileTypeIdAt(GridPoint point) => TileTypeAt(point)?.Id;

        bool IsCityCell(GridPoint point)
        {
            foreach (var city in state.Cities)
            {
                if (city.X == point.X && city.Y == point.Y)
                {
                    return true;
                }
            }

            return false;
        }

        bool IsBlocked(GridPoint point)
        {
            if (!IsCityCell(point) && TileTypeAt(point)?.PassableByFleets != true)
            {
                return true;
            }

            foreach (var otherFleet in state.Fleets)
            {
                if (!string.Equals(otherFleet.Id, fleet.Id, StringComparison.Ordinal)
                    && !otherFleet.IsUnderConstruction
                    && otherFleet.X == point.X && otherFleet.Y == point.Y)
                {
                    return true;
                }
            }

            return false;
        }

        var from = new GridPoint(fleet.X, fleet.Y);
        var walk = MovementWalker.Walk(
            from, destination, fleet.Moves, context.Ruleset.Terrain, TileTypeIdAt, IsBlocked, context.Events);

        var finalPosition = walk.FinalPosition;
        var coveredTileCode = InBounds(finalPosition, world)
            ? terrainCells[(finalPosition.Y * world.Width) + finalPosition.X]
            : fleet.CoveredTileCode;

        var updatedFleet = fleet with
        {
            X = finalPosition.X,
            Y = finalPosition.Y,
            Moves = walk.MovesRemaining,
            CoveredTileCode = coveredTileCode,
        };

        var updatedFleets = state.Fleets.Select(f =>
            string.Equals(f.Id, fleet.Id, StringComparison.Ordinal) ? updatedFleet : f);

        // A carried army's coordinates travel with the fleet -- docs/task-catalogue.md T14 DoD 15's
        // first clause. The army stays off-map (CoveredTileCode null, AboardFleetId set) the whole time;
        // only X/Y move, exactly mirroring the fleet's own new position.
        var updatedArmies = fleet.CarriedArmyId is { } carriedArmyId
            ? state.Armies.Select(a => string.Equals(a.Id, carriedArmyId, StringComparison.Ordinal)
                ? a with { X = finalPosition.X, Y = finalPosition.Y }
                : a)
            : state.Armies;

        return CommandOutcome.Accept(state with
        {
            Fleets = ValueList.From(updatedFleets),
            Armies = ValueList.From(updatedArmies),
        });
    }

    private static bool InBounds(GridPoint point, World world) =>
        (uint)point.X < (uint)world.Width && (uint)point.Y < (uint)world.Height;
}
