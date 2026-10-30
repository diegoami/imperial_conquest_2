using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;
using static IC2.Engine.Ai.AiFormat;

namespace IC2.Engine.Ai;

/// <summary>F608 decision boundary, useful diagnostics without a persistent order/save field.</summary>
[DomainEvent("ai.fleet-decision")]
public sealed record OriginalFleetDecision(string FleetId, string? PortId, string Branch,
    int? X, int? Y, int HuntScore) : DomainEvent;

/// <summary>T247: one ordered original fleet phase after army requests; designed policy never enters here.</summary>
public static class OriginalFleetPhase
{
    public readonly record struct Outcome(GameState State, int Attempts, int Issued, int Rejected);

    public static Outcome Run(GameState state, Ruleset rules, World world, string nationId,
        OriginalFleetRoutes routes, ICommandDispatch commands, IEventSink events, IRng rng, List<string> log)
    {
        var view = new AiView(state, rules, world, nationId);
        if (view.Nation.Control != SeatControl.Ai || !ClassicalFleetNavigation.Applies(view)) return new(state, 0, 0, 0);
        var ids = view.OwnFleets().Select(f => f.Id).ToArray();
        var attempts = 0;
        var issued = 0;
        var rejected = 0;
        foreach (var id in ids)
        {
            var fleet = state.FleetById(id);
            if (fleet is null || fleet.IsUnderConstruction || fleet.Nation != nationId) continue;
            var support = AiResupplyPass.Run(state, rules, nationId, onlyFleetId: id);
            state = support.State;
            fleet = state.FleetById(id)!;
            view = new AiView(state, rules, world, nationId);
            var port = OriginalFleetPort.Pick(view, fleet); // Always before the stored-route/moves tests.
            var destination = routes.For(id);
            var branch = destination is null ? "idle" : "stored-route";
            var huntScore = -10000;
            if (destination is null && fleet.Moves > 0)
            {
                var hunt = OriginalFleetHunt.Pick(view, fleet, rng);
                huntScore = hunt.Score;
                if (hunt.Fleet is { } target && hunt.Score > 100)
                {
                    destination = new(target.X, target.Y);
                    branch = "hunt";
                }
                else if (port is not null)
                {
                    destination = new(port.X, port.Y);
                    branch = "port";
                }
            }
            events.Publish(new OriginalFleetDecision(id, port?.Id, branch, destination?.X, destination?.Y, huntScore));
            log.Add(Inv("fleet phase: {0} {1}, support {2}, hunt {3}", id, branch, support.FleetTransfers, huntScore));
            if (destination is not { } point) continue;
            if ((uint)point.X >= (uint)world.Width || (uint)point.Y >= (uint)world.Height)
            {
                log.Add(Inv("fleet phase: {0} has an unsupported route coordinate", id));
                continue;
            }
            attempts++;
            var before = state;
            var result = commands.Dispatch(state, new MoveFleetCommand(nationId, id, point.X, point.Y,
                MoveWalkPolicy.AiFleetContinuation), events);
            if (result.IsRejected)
            {
                rejected++;
                log.Add(Inv("REJECTED original fleet {0}: {1}", id, result.Rejection!.Message));
                continue;
            }
            state = result.State;
            if (!AiSubstantiveState.AreEquivalent(before, state)) issued++;
        }
        return new(state, attempts, issued, rejected);
    }
}
