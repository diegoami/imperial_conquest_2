using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Naval.Commands;
using static IC2.Engine.Ai.AiFormat;

namespace IC2.Engine.Ai;

/// <summary>
/// T245: FUN_0044efc8, once before the per-army support/target loop. Rules and literal bounds:
/// research 2026-10-30-capital-defence-rule123.md, corrected by research PR128 (1582986).
/// Table order matters; neither admission nor equal-distance choices use ids or random draws.
/// </summary>
public static class AiCapitalDefence
{
    public readonly record struct Outcome(GameState State, int Dispatches, int Issued, int Rejected);

    public static IReadOnlyList<string> Threats(AiView view, CityState capital)
    {
        var ids = new List<string>();
        var geography = ClassicalArmyNavigation.Classify(capital.X, capital.Y);
        foreach (var army in view.State.Armies)
        {
            if (army.Nation == view.NationId
                || ClassicalArmyNavigation.Classify(army.X, army.Y) != geography)
                continue;
            var radius = view.IsAtWar(view.NationId, army.Nation) ? 19 : 9;
            if (AiView.Distance(army.X, army.Y, capital.X, capital.Y) > radius) continue;
            ids.Add(army.Id);
            if (ids.Count == 9) break;
        }
        return ids;
    }

    public static ArmyState? NearestThreat(AiView view, ArmyState responder, CityState capital,
        IReadOnlyList<string> threats)
    {
        ArmyState? chosen = null;
        var distance = int.MaxValue;
        var farFromHome = AiView.Distance(responder.X, responder.Y, capital.X, capital.Y) >= 20;
        foreach (var id in threats)
        {
            var threat = view.State.ArmyById(id);
            if (threat is null || (!view.IsAtWar(view.NationId, threat.Nation) && !farFromHome)) continue;
            var next = AiView.Distance(responder.X, responder.Y, threat.X, threat.Y);
            if (next >= distance) continue;
            chosen = threat;
            distance = next;
        }
        return chosen;
    }

    public static bool CommittedToOffence(int capitalDistance, int moves,
        long armyScore, int armyDistance, long cityScore, int cityDistance) =>
        capitalDistance > 3L * moves
        && ((armyScore > 100 && armyDistance < moves) || (cityScore > 100 && cityDistance < moves));

    /// <summary>The covered-cell test is on the chosen threat, never on the responder (0x44F2A3).</summary>
    public static GridPoint Destination(AiView view, ArmyState threat, CityState capital) =>
        threat.CoveredTileCode is not null && view.IsAtWar(view.NationId, threat.Nation)
            ? new GridPoint(threat.X, threat.Y) : new GridPoint(capital.X, capital.Y);

    public static Outcome Run(GameState state, Ruleset ruleset, World world, string nationId,
        ICommandDispatch commands, IEventSink events, List<string> log)
    {
        if (ruleset.Flags.AiEconomy != AiEconomyModel.Original)
            return new(state, 0, 0, 0);
        var view = new AiView(state, ruleset, world, nationId);
        var capital = view.Nation.CapitalCityId is { } id ? state.CityById(id) : null;
        if (capital is null) return new(state, 0, 0, 0);
        var threats = Threats(view, capital);
        if (threats.Count == 0) return new(state, 0, 0, 0);
        var responders = view.OwnArmies().Select(a => a.Id).ToArray();
        var dispatches = 0;
        var issued = 0;
        var rejected = 0;
        foreach (var armyId in responders)
        {
            if (dispatches >= threats.Count + 2) break;
            var army = state.ArmyById(armyId);
            if (army is null || army.Moves <= 0) continue;
            view = new AiView(state, ruleset, world, nationId);
            var armyTarget = AiArmyTargetTree.ScoreArmyTarget(view, army);
            var cityTarget = AiArmyTargetTree.ScoreCityTarget(view, army);
            if (CommittedToOffence(AiView.Distance(army.X, army.Y, capital.X, capital.Y), army.Moves,
                armyTarget.Score, armyTarget.Distance, cityTarget.Score, cityTarget.Distance)) continue;
            var threat = NearestThreat(view, army, capital, threats);
            if (threat is null) continue;
            // Count a dispatch, even when its movement makes no progress. The AI's substantive-command
            // counter is separate: accepting a no-op must not conceal a stalled turn.
            dispatches++;
            var destination = Destination(view, threat, capital);
            var command = Move(view, army, destination);
            log.Add(Inv("capital defence: {0} toward ({1}, {2}), threat {3}",
                army.Id, destination.X, destination.Y, threat.Id));
            if (command is null) continue;
            var result = commands.Dispatch(state, command, events);
            if (result.IsRejected)
            {
                rejected++;
                log.Add(Inv("REJECTED capital defence {0}: {1}", command.Kind, result.Rejection!.Message));
                continue;
            }
            if (!AiSubstantiveState.AreEquivalent(state, result.State)) issued++;
            state = result.State;
        }
        return new(state, dispatches, issued, rejected);
    }

    private static ICommand? Move(AiView view, ArmyState army, GridPoint target)
    {
        if ((uint)target.X >= (uint)view.World.Width || (uint)target.Y >= (uint)view.World.Height) return null;
        // DBA8 queues CD08's landing request and charges the army two moves. Sailing happens
        // later in F608, even for a request produced by the capital-defence prepass (#1632).
        if (army.AboardFleetId is { } fleetId)
        {
            var fleet = view.State.FleetById(fleetId);
            if (fleet is not { IsUnderConstruction: false }
                || fleet.Nation != view.NationId || fleet.CarriedArmyId != army.Id) return null;
            if (!ClassicalArmyNavigation.Applies(view))
                return fleet.Moves > 0 ? new MoveFleetCommand(view.NationId, fleet.Id, target.X, target.Y) : null;
            return new MoveArmyCommand(view.NationId, army.Id, target.X, target.Y,
                MoveWalkPolicy.AiContinuation, target.X, target.Y);
        }
        var aim = target;
        var continuation = ClassicalArmyNavigation.Applies(view)
            && ClassicalArmyNavigation.Classify(army.X, army.Y) == ClassicalArmyNavigation.Classify(target.X, target.Y);
        if (continuation && new GridPoint(army.X, army.Y) != target)
        {
            try
            {
                if (!ClassicalArmyNavigation.DirectLine(view, new GridPoint(army.X, army.Y), target))
                {
                    if (ClassicalArmyNavigation.Intermediate(view, new GridPoint(army.X, army.Y), target).Point is not { } point)
                        return null;
                    aim = point;
                }
            }
            catch (NotSupportedException) { return null; } // Existing undefined island-route boundary.
        }
        if (!view.IsArmyPassable(aim)) return null;
        return continuation
            ? new MoveArmyCommand(view.NationId, army.Id, aim.X, aim.Y, MoveWalkPolicy.AiContinuation, target.X, target.Y)
            : new MoveArmyCommand(view.NationId, army.Id, aim.X, aim.Y);
    }
}
