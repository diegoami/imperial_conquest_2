using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>An automatic attempt failed; the enclosing movement still follows its own legality.</summary>
[DomainEvent("automation.human-support-refused")]
public sealed record HumanSupportRefused(string UnitId, string CommandKind, RejectionCode Code, string Message) : DomainEvent;

/// <summary>Support accepted movement steps without nested movement dispatch or extra root RNG advances.</summary>
internal static class HumanMovementSupport
{
    public static GameState Army(GameState state, CommandContext context, string id) =>
        Publish(HumanAutoSupport.ForArmy(state, context.Ruleset, context.World,
            context.HumanSupport, context.Rng, id), context);

    public static GameState Fleet(GameState state, CommandContext context, string id)
    {
        // Match turn support order: cargo first, then replenish the carrier from
        // the town, so filling cargo does not leave avoidable holes in its stock.
        if (state.FleetById(id)?.CarriedArmyId is { } cargo)
            state = Army(state, context, cargo);
        return Publish(HumanAutoSupport.ForFleet(state, context.Ruleset, context.World,
            context.HumanSupport, context.Rng, id), context);
    }

    private static GameState Publish(HumanSupportResult result, CommandContext context)
    {
        foreach (var item in result.Events) context.Events.Publish(item);
        foreach (var failure in result.Failures)
            context.Events.Publish(new HumanSupportRefused(failure.UnitId, failure.CommandKind,
                failure.Rejection.Code, failure.Rejection.Message));
        return result.State;
    }
}
