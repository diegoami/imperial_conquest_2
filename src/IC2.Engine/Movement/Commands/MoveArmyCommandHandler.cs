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

        // T237 (#920 item 3): an army aboard a fleet moves only with its fleet; disembarking is the
        // fleet's order (disembark-army), so a move order for it is refused rather than a silent no-op.
        var originalTransport = command.Walk == MoveWalkPolicy.AiContinuation
            && context.IssuingNation.Control == SeatControl.Ai
            && Ai.ClassicalArmyNavigation.Applies(new Ai.AiView(state, context.Ruleset, context.World, army.Nation));
        if (army.IsEmbarked && !originalTransport)
        {
            return CommandOutcome.Reject(
                MoveArmyRejections.ArmyEmbarked,
                $"Army '{army.Id}' is aboard fleet '{army.AboardFleetId}'; disembark it to move it.");
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

        TileType? TileTypeAt(GridPoint point) =>
            InBounds(point, world) ? world.TileTypeByCode(terrainCells[(point.Y * world.Width) + point.X]) : null;

        string? TileTypeIdAt(GridPoint point) => TileTypeAt(point)?.Id;

        // T237 (#1587): the map click's "army to land" row. A click on a tile armies cannot stand on
        // does nothing in the original and in the remake's map, so the engine refuses the order too;
        // a walk toward land that meets the sea part-way still stops at the shore (IsBlocked below).
        if (TileTypeAt(destination)?.PassableByArmies != true)
        {
            return CommandOutcome.Reject(
                MoveArmyRejections.DestinationNotLand,
                $"({command.X}, {command.Y}) is not land; an army can only be ordered to a land tile.");
        }

        var continuationTarget = new GridPoint(command.TargetX ?? command.X, command.TargetY ?? command.Y);
        if (originalTransport && army.IsEmbarked)
            return Ai.OriginalArmyTransport.Carried(army, continuationTarget, context);
        if (originalTransport && new GridPoint(army.X, army.Y) != continuationTarget
            && !Ai.ClassicalArmyNavigation.DirectLine(new Ai.AiView(state, context.Ruleset, world, army.Nation),
                new(army.X, army.Y), continuationTarget)
            && Ai.ClassicalArmyNavigation.Classify(army.X, army.Y)
                != Ai.ClassicalArmyNavigation.Classify(continuationTarget.X, continuationTarget.Y))
        {
            var ferry = Ai.OriginalArmyTransport.Ferry(army, context, out var approach);
            if (ferry is { } result) return result;
            if (approach is { } point) destination = continuationTarget = point;
        }

        bool IsBlocked(GridPoint point)
        {
            // T23 follow-up #226: an army's walk never checked TileType.PassableByArmies, so it could be
            // legally walked into the sea -- MoveFleetCommandHandler's mirror check (PassableByFleets) was
            // the only side of this asymmetry that existed. T22's AI never issues such a march on its own
            // (it reads the same flag through AiView), which is why nothing caught it: a human seat through
            // this CLI has no such scruple. Checked first, before occupancy, for the same reason
            // MoveFleetCommandHandler checks terrain before its own fleet-occupancy scan.
            if (TileTypeAt(point)?.PassableByArmies != true)
            {
                return true;
            }

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
        // #1012: the ruleset's step policy, and the map size for the original step helper's bounds guard.
        MovementWalkResult WalkTo(GridPoint origin, GridPoint to, int moves) => MovementWalker.Walk(
            origin, to, moves, context.Ruleset.Terrain, TileTypeIdAt, IsBlocked, context.Events,
            step: context.Ruleset.Flags.StrategicStep, worldSize: new GridPoint(world.Width, world.Height));

        MovementWalkResult WalkFrom(GridPoint origin, int moves) => WalkTo(origin, destination, moves);

        int MovesAfter(MovementWalkResult result) => result.StopReason == MovementStopReason.InsufficientMoves
            ? MovementAbortRule.MovesAfterAbortedStep(
                result.MovesRemaining, context.IssuingNation.Control, context.Ruleset.Flags.SeatAsymmetry)
            : result.MovesRemaining;

        var walk = WalkFrom(from, army.Moves);
        var movesAfter = MovesAfter(walk);
        CommandOutcome? transportOutcome = null;

        // T238 (#1585): a human order walks once more, on a fresh line from where the first walk ended,
        // when it ended short with moves left (TUnitMap_MoveHumanArmy). The two walks are one route.
        if (command.Walk == MoveWalkPolicy.HumanSecondWalk && walk.FinalPosition != destination && movesAfter > 0)
        {
            var second = WalkFrom(walk.FinalPosition, movesAfter);
            walk = walk.Then(second);
            movesAfter = MovesAfter(second);
        }

        // T239 (#1592): a faithful AI march keeps walking as FUN_0044dba8 does. Each level after a walk:
        // when the walk left the moves unchanged and positive, walk toward CEC8's local fallback point and
        // spend 1 move whatever happens (moves may reach -1; no point ends the order); then, while the army
        // is more than 1 tile from the target with moves left, start a level from the new cell: the direct
        // line when CB40 passes, otherwise D174's intermediate point (none, or an undefined original
        // context, ends the order). The levels are one route.
        if (command.Walk == MoveWalkPolicy.AiContinuation && command.TargetX is { } targetX && command.TargetY is { } targetY)
        {
            var target = continuationTarget;
            var movesBefore = army.Moves;

            // The original routines read the live map, where the marching army's marker stands on its cell.
            Ai.AiView ViewAt(GridPoint cell) => new(
                state with
                {
                    Armies = ValueList.From(state.Armies.Select(a =>
                        string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? a with { X = cell.X, Y = cell.Y } : a)),
                },
                context.Ruleset, world, army.Nation);
            while (true)
            {
                if (movesAfter == movesBefore && movesAfter > 0)
                {
                    var here = walk.FinalPosition;
                    var fallback = Ai.ClassicalArmyNavigation.LocalFallback(ViewAt(here), here);
                    if (fallback is { } point)
                    {
                        var side = WalkTo(here, point, movesAfter);
                        walk = walk.Then(side);
                        movesAfter = MovesAfter(side);
                    }

                    movesAfter -= 1;
                    if (fallback is null)
                    {
                        break;
                    }
                }

                var at = walk.FinalPosition;
                if (Math.Max(Math.Abs(target.X - at.X), Math.Abs(target.Y - at.Y)) <= 1 || movesAfter <= 0)
                {
                    break;
                }

                GridPoint aim;
                try
                {
                    var view = ViewAt(at);
                    if (Ai.ClassicalArmyNavigation.DirectLine(view, at, target))
                    {
                        aim = target;
                    }
                    else if (originalTransport && Ai.ClassicalArmyNavigation.Classify(at.X, at.Y)
                        != Ai.ClassicalArmyNavigation.Classify(target.X, target.Y))
                    {
                        // A later recursive level can cross the same ferry boundary as entry. Give
                        // D9A8 the live army, so embarking cannot be overwritten by the walk's old record.
                        var liveArmy = army with { X = at.X, Y = at.Y, Moves = movesAfter,
                            CoveredTileCode = terrainCells[at.Y * world.Width + at.X] };
                        var liveState = state with { Armies = ValueList.From(state.Armies.Select(a =>
                            a.Id == army.Id ? liveArmy : a)) };
                        var ferry = Ai.OriginalArmyTransport.Ferry(liveArmy,
                            new CommandContext(liveState, context.Ruleset, world, command, context.Rng, context.Events),
                            out var approach);
                        if (ferry is { } result) { transportOutcome = result; break; }
                        if (approach is not { } point) break;
                        aim = target = point;
                    }
                    else if (Ai.ClassicalArmyNavigation.Intermediate(view, at, target).Point is { } waypoint)
                    {
                        aim = waypoint;
                    }
                    else
                    {
                        break;
                    }
                }
                catch (NotSupportedException)
                {
                    break;
                }

                movesBefore = movesAfter;
                var level = WalkTo(at, aim, movesAfter);
                walk = walk.Then(level);
                movesAfter = MovesAfter(level);
            }
        }

        if (army.CoveredTileCode is not null && !army.IsEmbarked)
            context.RecordTravel(StrategicMarkerKind.Army, army.Id, army.Nation, army.AboardFleetId, null, walk);

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

        if (transportOutcome is { } transported) return transported;

        var updatedArmies = state.Armies.Select(a =>
            string.Equals(a.Id, army.Id, StringComparison.Ordinal) ? updatedArmy : a);

        return CommandOutcome.Accept(state with { Armies = ValueList.From(updatedArmies) });
    }

    private static bool InBounds(GridPoint point, World world) =>
        (uint)point.X < (uint)world.Width && (uint)point.Y < (uint)world.Height;
}
