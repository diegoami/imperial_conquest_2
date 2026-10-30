using IC2.Engine.Ai;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Naval.Commands;

/// <summary>Diagnostic arrival of a returning E1FC level; not persistent route state.</summary>
[DomainEvent("ai.fleet-arrival")]
public sealed record OriginalFleetArrival(string FleetId, int Depth, string ArrivalKind, int X, int Y,
    int FleetX, int FleetY, int Moves) : DomainEvent;

/// <summary>T247: bounded E1FC contract (fleet-move-contract131, b490785). Reuses the real walker/kernels.</summary>
internal static class OriginalFleetMove
{
    private enum TargetKind { Sea, Land, City, Army, Fleet }
    private readonly record struct Frame(int Depth, TargetKind Kind, string? Id);

    internal static CommandOutcome Handle(MoveFleetCommand command, CommandContext context, FleetState originalFleet)
    {
        var state = context.State;
        var rules = context.Ruleset;
        var world = context.World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        var fleet = originalFleet;
        var target = new GridPoint(command.X, command.Y);
        var origin = new GridPoint(fleet.X, fleet.Y);
        if (fleet.Moves <= 0) return CommandOutcome.Accept(state);
        // Standalone CB40 coincidence has an uninitialised original input. Preserve the previous
        // deterministic no-op; the observed 24-move round trip is not a universal original rule.
        if (origin == target) return CommandOutcome.Accept(state);
        var frames = new List<Frame>();
        MovementWalkResult? route = null;
        AiView View() => new(state, rules, world, fleet.Nation);

        void WriteFleet(FleetState changed)
        {
            fleet = changed;
            state = state with
            {
                Fleets = ValueList.From(state.Fleets.Select(f => f.Id == fleet.Id ? fleet : f)),
                Armies = ValueList.From(state.Armies.Select(a => a.Id == fleet.CarriedArmyId
                    ? a with { X = fleet.X, Y = fleet.Y } : a)),
            };
        }

        void Walk(GridPoint aim)
        {
            bool InWorld(GridPoint p) => (uint)p.X < (uint)world.Width && (uint)p.Y < (uint)world.Height;
            string? TypeAt(GridPoint p) => InWorld(p) ? world.TileTypeByCode(cells[p.Y * world.Width + p.X])?.Id : null;
            bool Blocked(GridPoint p) => !InWorld(p)
                || world.TileTypeByCode(cells[p.Y * world.Width + p.X])?.PassableByFleets != true
                || state.Cities.Any(c => c.X == p.X && c.Y == p.Y)
                || state.Armies.Any(a => a.CoveredTileCode is not null && a.X == p.X && a.Y == p.Y)
                || state.Fleets.Any(f => f.Id != fleet.Id && !f.IsUnderConstruction && f.X == p.X && f.Y == p.Y);
            int? Rough(GridPoint p) => state.IsRoughSea(p.X, p.Y) ? rules.Economy.Weather.RoughSeaMoveCost : null;
            var part = MovementWalker.Walk(new(fleet.X, fleet.Y), aim, fleet.Moves, rules.Terrain,
                TypeAt, Blocked, context.Events, Rough, rules.Flags.StrategicStep, new(world.Width, world.Height));
            route = route is null ? part : route.Then(part);
            var moves = part.StopReason == MovementStopReason.InsufficientMoves
                ? MovementAbortRule.MovesAfterAbortedStep(part.MovesRemaining, SeatControl.Ai, rules.Flags.SeatAsymmetry)
                : part.MovesRemaining;
            var at = part.FinalPosition;
            WriteFleet(fleet with
            {
                X = at.X, Y = at.Y, Moves = moves,
                CoveredTileCode = state.IsRoughSea(at.X, at.Y) ? rules.Economy.Weather.RoughSeaTileCode
                    : cells[at.Y * world.Width + at.X],
            });
        }

        for (var depth = 0; ; depth++)
        {
            var view = View();
            var kind = ClassifyTarget(state, world, cells, target, out var targetId);
            frames.Add(new(depth, kind, targetId));
            var here = new GridPoint(fleet.X, fleet.Y);
            if (here == target) break; // Same undefined standalone predicate at a later level.
            GridPoint aim;
            try
            {
                if (ClassicalArmyNavigation.DirectLine(view, here, target)) aim = target;
                else
                {
                    var replaced = kind is TargetKind.Land or TargetKind.City or TargetKind.Army
                        ? ClassicalFleetNavigation.ReplaceLandTarget(view, target) : target;
                    if (replaced is not { } point) break;
                    var waypoint = ClassicalArmyNavigation.FleetIntermediate(view, here, point).Point;
                    if (waypoint is not { } next) break;
                    aim = next; // No second direct-line test after 4939C.
                }
            }
            catch (NotSupportedException) { break; }
            var beforeMoves = fleet.Moves;
            Walk(aim);
            if (fleet.Moves == beforeMoves && fleet.Moves > 0)
            {
                var fallback = ClassicalArmyNavigation.LocalFallback(View(), new(fleet.X, fleet.Y), fleetMode: true);
                if (fallback is { } point) Walk(point);
                WriteFleet(fleet with { Moves = fleet.Moves - 1 });
                if (fallback is null) break;
            }
            if (fleet.Moves <= 0 || LandingTile.ChebyshevDistance(new(fleet.X, fleet.Y), target) <= 1) break;
        }

        // One recursive child per level: all movement happens on descent, then arrivals unwind.
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            if (state.FleetById(originalFleet.Id) is not { } live) break;
            fleet = live;
            if (fleet.Moves <= 0 || LandingTile.ChebyshevDistance(new(fleet.X, fleet.Y), target) != 1) continue;
            var frame = frames[i];
            if (frame.Kind == TargetKind.Land && fleet.CarriedArmyId is { } armyId)
            {
                // E1FC passes the original target to B840. Its AI mode-1 resolver rejects
                // live markers, rather than selecting the first terrain-only adjacent cell (#1633).
                if (ClassicalFleetNavigation.LandingTarget(View(), target) is not { } landing) continue;
                var land = new DisembarkArmyCommand(fleet.Nation, armyId, landing.X, landing.Y);
                var result = new DisembarkArmyCommandHandler().Handle(land,
                    new CommandContext(state, rules, world, land, context.Rng, context.Events));
                if (result.IsAccepted) state = result.AcceptedState!;
                context.Events.Publish(new OriginalFleetArrival(fleet.Id, frame.Depth, "landing", target.X, target.Y,
                    fleet.X, fleet.Y, fleet.Moves));
            }
            else if (frame.Kind == TargetKind.City && frame.Id is { } cityId && state.CityById(cityId) is { } city)
            {
                if (View().IsAtWar(fleet.Nation, city.Owner)) continue;
                var support = AutomaticResupply.ForFleet(fleet, city, state.NationById(fleet.Nation)!, state.NationById(city.Owner)!, rules);
                state = state with
                {
                    Fleets = ValueList.From(state.Fleets.Select(f => f.Id == fleet.Id ? support.Fleet : f)),
                    Cities = ValueList.From(state.Cities.Select(c => c.Id == city.Id ? support.City : c)),
                    Nations = ValueList.From(state.Nations.Select(n => n.Id == fleet.Nation ? support.FleetNation
                        : n.Id == city.Owner ? support.CityNation : n)),
                };
                context.Events.Publish(new OriginalFleetArrival(fleet.Id, frame.Depth, "port", target.X, target.Y,
                    fleet.X, fleet.Y, fleet.Moves));
            }
            else if (frame.Kind == TargetKind.Fleet && frame.Id is { } enemyId && state.FleetById(enemyId) is { } enemy
                && View().IsAtWar(fleet.Nation, enemy.Nation))
            {
                var attack = new AttackFleetCommand(fleet.Nation, fleet.Id, enemyId);
                var result = new AttackFleetCommandHandler().Handle(attack,
                    new CommandContext(state, rules, world, attack, context.Rng, context.Events));
                if (result.IsAccepted) state = result.AcceptedState!;
                context.Events.Publish(new OriginalFleetArrival(fleet.Id, frame.Depth, "naval-contact", target.X, target.Y,
                    fleet.X, fleet.Y, fleet.Moves));
            }
        }
        var final = state.FleetById(originalFleet.Id);
        if (route is not null)
        {
            route = route with { MovesSpent = originalFleet.Moves - (final?.Moves ?? fleet.Moves), MovesRemaining = final?.Moves ?? fleet.Moves };
            context.RecordTravel(StrategicMarkerKind.Fleet, originalFleet.Id, originalFleet.Nation, null, originalFleet.CarriedArmyId, route);
            if (route.Moved) context.Events.Publish(new FleetMoved(originalFleet.Id, originalFleet.Nation,
                origin.X, origin.Y, route.FinalPosition.X, route.FinalPosition.Y));
        }
        return CommandOutcome.Accept(state);
    }

    private static TargetKind ClassifyTarget(GameState state, World world, int[] cells, GridPoint point, out string? id)
    {
        id = null;
        var fleet = state.Fleets.FirstOrDefault(f => !f.IsUnderConstruction && f.X == point.X && f.Y == point.Y);
        if (fleet is not null) { id = fleet.Id; return TargetKind.Fleet; }
        var army = state.Armies.FirstOrDefault(a => a.CoveredTileCode is not null && a.X == point.X && a.Y == point.Y);
        if (army is not null) { id = army.Id; return TargetKind.Army; }
        var city = state.Cities.FirstOrDefault(c => c.X == point.X && c.Y == point.Y);
        if (city is not null) { id = city.Id; return TargetKind.City; }
        return cells[point.Y * world.Width + point.X] is >= 2 and <= 11 ? TargetKind.Land : TargetKind.Sea;
    }
}
