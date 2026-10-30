using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Strength;

namespace IC2.Engine.Ai;

/// <summary>F4F8: actual ordered strength draws and signed-word winner, research #131/#130.</summary>
public static class OriginalFleetHunt
{
    public sealed record Target(FleetState? Fleet, int Score, int Distance);

    public static Target Pick(AiView view, FleetState actor, IRng rng)
    {
        var stored = OriginalWordScore.Empty;
        FleetState? best = null;
        var ownStrength = Power(view, actor, rng); // One draw even when there is no eligible enemy.
        foreach (var target in view.State.Fleets)
        {
            if (target.Nation == actor.Nation || target.IsUnderConstruction
                || !view.IsAtWar(actor.Nation, target.Nation)
                || Protected(view.State, target)) continue;
            var strength = Power(view, target, rng);
            if (strength <= 0) continue; // Retain native division safety; no original fault emulation.
            var distance = AiView.Distance(actor.X, actor.Y, target.X, target.Y);
            var score = ownStrength * 100 / strength - distance;
            if (strength < ownStrength && distance < 18) score *= 2;
            if (!stored.IsBeatenBy(score)) continue;
            stored = stored.WithWinner(score, distance);
            best = target;
        }
        return new(best, stored.ReturnedScore, stored.Distance);
    }

    public static bool Protected(GameState state, FleetState fleet) => state.Cities.Any(c => c.Owner == fleet.Nation
        && AiView.Distance(c.X, c.Y, fleet.X, fleet.Y) <= 1);

    private static int Power(AiView view, FleetState fleet, IRng rng)
    {
        var archer = BattleCommandRuleset.ArcherUnitTypeIdIn(view.Ruleset);
        var cargo = fleet.CarriedArmyId is { } id ? view.State.ArmyById(id) : null;
        var carried = cargo is not null && archer is not null
            ? new FleetPower.CarriedArmyStrength(cargo.Units, cargo.Morale, archer) : null;
        return FleetPower.Compute(fleet.Ships, fleet.ConditionPercent, rng, view.Ruleset, carried);
    }
}
