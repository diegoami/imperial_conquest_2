using IC2.Engine.Barbarians;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Naval.Commands;

namespace IC2.Engine.Ai;

public static partial class AiTurn
{
    /// <summary>Restricted orchestration around the existing military proposals, selector and executor.</summary>
    private static AiTurnOutcome RunBarbarian(GameState state, Ruleset ruleset, World world,
        ICommandDispatch commands, IRng rng, IEventSink events, NationState nation)
    {
        var log = new List<string> { "barbarian military orders; no economy, recruitment, hiring, capital defence or diplomacy" };
        state = AiResupplyPass.Run(state, ruleset, nation.Id).State;
        var marched = new List<string>();
        var issued = 0;
        var rejected = 0;
        var mismatches = 0;
        var hitCap = true;
        var personality = AiPersonalityProfile.For(nation, ruleset);
        for (var action = 0; action < ruleset.Ai.MaxActionsPerTurn; action++)
        {
            // No mercenary offers are eligible for this faction. This is a proposal-only view;
            // accepted orders always execute against the full authoritative state below.
            var view = new AiView(state with { MercenaryPool = ValueList<MercenaryPoolSlot>.Empty }, ruleset, world, nation.Id);
            var chosen = BarbarianTransport.Propose(view, marched);
            if (chosen is null)
            {
                var candidates = new List<AiCandidate>();
                AiMilitaryPhase.Propose(view, personality, rng, marched, candidates);
                candidates.RemoveAll(c => c.Commands.Any(command => command is not
                    (MoveArmyCommand or MoveFleetCommand or AttackArmyCommand or BesiegeCityCommand or AttackFleetCommand))
                    || c.Commands.OfType<MoveFleetCommand>().Any(move => state.FleetById(move.FleetId)?.CarriedArmyId is not null));
                chosen = Select(candidates, rng, ruleset);
            }
            if (chosen is null)
            {
                log.Add("no legal military candidate; turn ends");
                hitCap = false;
                break;
            }
            if (chosen.IsMarch && chosen.SubjectId is { } subject) marched.Add(subject);
            log.Add(chosen.Rationale);
            var before = state;
            var result = Execute(state, ruleset, commands, events, chosen, log);
            state = result.State;
            if (!AiSubstantiveState.AreEquivalent(before, state)) issued += result.Issued;
            rejected += result.Rejected;
            mismatches += result.Mismatches;
            if (result.Rejected > 0 || commands is IAiBattleInterruption { ArmyPhaseInterrupted: true })
            {
                hitCap = false;
                break;
            }
            if (AiSubstantiveState.AreEquivalent(before, state) && !chosen.IsMarch)
            {
                log.Add("last order made no progress; turn ends");
                hitCap = false;
                break;
            }
        }
        return new(state, nation.Id, log, issued, rejected, mismatches, hitCap);
    }

}
