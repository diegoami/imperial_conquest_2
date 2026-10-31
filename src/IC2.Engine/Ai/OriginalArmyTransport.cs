using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;

namespace IC2.Engine.Ai;

[DomainEvent("ai.army-transport-attempted")]
public sealed record OriginalArmyTransportAttempted(string ArmyId, string NationId, int FromX, int FromY,
    int TargetX, int TargetY, string? FleetId) : DomainEvent;

/// <summary>T247's DBA8/CD08/D9A8 transport handoff, from the promoted navigation187 specification.</summary>
internal static class OriginalArmyTransport
{
    /// <summary>
    /// Research #159: an E84C continuation can write a non-adjacent rendezvous at exactly zero army
    /// moves. This is route planning, not an army order: the army neither walks nor boards here.
    /// </summary>
    internal static OriginalFleetRouteRequested? ZeroMoveRendezvous(AiView view, ArmyState army, GridPoint target)
    {
        if (army.Moves != 0 || army.IsEmbarked || army.Nation != view.NationId
            || view.Nation.Control != SeatControl.Ai || !ClassicalArmyNavigation.Applies(view)) return null;
        var from = new GridPoint(army.X, army.Y);
        bool InBounds(GridPoint p) => (uint)p.X < (uint)view.World.Width && (uint)p.Y < (uint)view.World.Height;
        if (!InBounds(from) || !InBounds(target) || !view.IsArmyPassable(target)) return null;
        // Keep the existing transport classification/line predicate. A same-geography land walk
        // at zero has no effect; this does not grant the adjacent-embark or carried landing paths.
        if (from == target || ClassicalArmyNavigation.Classify(from.X, from.Y)
            == ClassicalArmyNavigation.Classify(target.X, target.Y)
            || ClassicalArmyNavigation.DirectLine(view, from, target)) return null;
        var fleet = ClassicalArmyNavigation.NearestUsableFerry(view, army);
        if (fleet is null || AiView.Distance(army.X, army.Y, fleet.X, fleet.Y) <= 1) return null;
        var coast = ClassicalFleetNavigation.CoastTransition(view, from, new(fleet.X, fleet.Y));
        // D9A8 writes the rejected (first sea) point even when no accepted land point exists.
        return coast is { } transition
            ? new OriginalFleetRouteRequested(fleet.Id, transition.Rejected.X, transition.Rejected.Y) : null;
    }

    internal static CommandOutcome Carried(ArmyState army, GridPoint target, CommandContext context)
    {
        var state = context.State;
        var view = new AiView(state, context.Ruleset, context.World, army.Nation);
        var fleet = army.AboardFleetId is { } id ? state.FleetById(id) : null;
        if (fleet is null || fleet.CarriedArmyId != army.Id || fleet.Nation != army.Nation)
            return CommandOutcome.Accept(state); // Preserve the guarded native link boundary.
        context.Events.Publish(new OriginalArmyTransportAttempted(army.Id, army.Nation, army.X, army.Y,
            target.X, target.Y, fleet.Id));
        var coast = ClassicalFleetNavigation.CoastTransition(view, target, new(fleet.X, fleet.Y));
        if (coast is not { } transition) return CommandOutcome.Accept(state);
        context.Events.Publish(new OriginalFleetRouteRequested(fleet.Id, transition.Accepted.X, transition.Accepted.Y));
        return CommandOutcome.Accept(state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a.Id == army.Id ? a with { Moves = a.Moves - 2 } : a)),
        });
    }

    internal static CommandOutcome? Ferry(ArmyState army, CommandContext context, out GridPoint? approach)
    {
        approach = null;
        var view = new AiView(context.State, context.Ruleset, context.World, army.Nation);
        var fleet = ClassicalArmyNavigation.NearestUsableFerry(view, army);
        var order = (Movement.Commands.MoveArmyCommand)context.Command;
        context.Events.Publish(new OriginalArmyTransportAttempted(army.Id, army.Nation, army.X, army.Y,
            order.TargetX ?? order.X, order.TargetY ?? order.Y, fleet?.Id));
        if (fleet is null) return CommandOutcome.Accept(context.State);
        if (AiView.Distance(army.X, army.Y, fleet.X, fleet.Y) == 1)
        {
            var embark = new EmbarkArmyCommand(army.Nation, army.Id, fleet.Id);
            return new EmbarkArmyCommandHandler().Handle(embark,
                new CommandContext(context.State, context.Ruleset, context.World, embark, context.Rng, context.Events));
        }
        var coast = ClassicalFleetNavigation.CoastTransition(view, new(army.X, army.Y), new(fleet.X, fleet.Y));
        if (coast is not { } transition) return CommandOutcome.Accept(context.State);
        context.Events.Publish(new OriginalFleetRouteRequested(fleet.Id, transition.Rejected.X, transition.Rejected.Y));
        if (transition.Accepted.X < 0) return CommandOutcome.Accept(context.State);
        approach = transition.Accepted;
        return null; // Caller walks toward the last land cell; negative/no-op outputs keep the request.
    }
}
