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
