using System.Globalization;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Ai;

/// <summary>Everything one AI seat's turn did, for the caller and for the per-seed log.</summary>
/// <param name="State">The state after the turn.</param>
/// <param name="NationId">The seat that acted.</param>
/// <param name="Log">One line per decision, in the order they were made.</param>
/// <param name="CommandsIssued">How many commands were dispatched.</param>
/// <param name="CommandsRejected">
/// How many of them came back refused. <strong>This must be zero</strong>
/// (<c>docs/task-catalogue.md</c> T22 Done-when 1); the soak asserts it across all fifty seeds. It is
/// counted rather than thrown on so that a failing seed's log shows which command and why, instead of an
/// exception with no context.
/// </param>
/// <param name="ProjectionMismatches">
/// How many times a declare-war-then-attack pair had its attack refused <em>by the probe</em> after the
/// declaration actually landed, although the projection said it would be legal. Also required to be zero.
/// See <see cref="AiTurn"/>'s remarks.
/// </param>
/// <param name="HitActionCap">Whether the turn stopped because it reached <see cref="AiWeights.MaxActionsPerTurn"/>.</param>
public sealed record AiTurnOutcome(
    GameState State,
    string NationId,
    IReadOnlyList<string> Log,
    int CommandsIssued,
    int CommandsRejected,
    int ProjectionMismatches,
    bool HitActionCap);

/// <summary>
/// One AI seat's turn: the resupply pass, then a greedy loop of "score every candidate, take the best,
/// look again".
/// </summary>
/// <remarks>
/// <para>
/// <strong>Greedy and one step deep, deliberately.</strong> <c>docs/game-design.md</c> §AI asks for "<em>a
/// heuristic scoring function per candidate action, pick the highest score, no search/lookahead</em>",
/// and says why: it is easy to reason about, easy to tune from data, and easy to unit-test. The loop
/// re-generates candidates after every action instead of executing a pre-built plan, because a plan built
/// against the state at the start of the turn would be stale the moment its first command landed — an
/// army that has attacked has no moves, a nation that has recruited has less treasury — and a stale plan
/// is precisely how a command gets refused.
/// </para>
/// <para>
/// <strong>Ties are broken by the seat's own random stream.</strong> <c>docs/game-design.md</c>
/// §"Testing and determinism" names "AI tie-breaking" as one of the three places randomness belongs, so
/// candidates that score <em>exactly</em> equal are separated by <see cref="IRng.NextInt(int)"/> rather
/// than by list position. Only exact ties: a one-point difference in score decides itself, so a
/// behaviour a test pins never depends on a draw.
/// </para>
/// <para>
/// <strong>Nothing here edits the state directly except the resupply pass.</strong> Every decision goes
/// out as a command through <see cref="ICommandDispatch"/>, which is the seam
/// <see cref="SystemContext.Commands"/> exists for: "<em>an AI that wrote to the state directly would
/// skip every legality check a human seat's order goes through, and would leave no command log for a
/// replay to follow</em>". The resupply pass is the exception because T38 delivered automatic resupply as
/// a pure function with no command of its own, and inventing one would be a new command type outside this
/// task's Owns list.
/// </para>
/// <para>
/// <strong>The declare-then-attack pair is verified twice.</strong>
/// <see cref="AiMilitaryPhase"/> gates it against the state
/// <see cref="Diplomacy.RelationTransitions.DeclareWar"/> projects; this loop re-runs
/// <see cref="AttackLegality.Check(GameState, Ruleset, AttackArmyCommand)"/> against the state the real
/// declaration produced, and abandons the attack rather than sending one that would be refused. On a
/// correct engine the two always agree and <see cref="AiTurnOutcome.ProjectionMismatches"/> stays zero;
/// the counter exists so that if they ever stop agreeing, the soak says so instead of the AI quietly
/// collecting rejections.
/// </para>
/// </remarks>
public static class AiTurn
{
    /// <summary>Plays one AI seat's turn.</summary>
    /// <param name="state">The state at the start of the seat's orders phase.</param>
    /// <param name="ruleset">The loaded ruleset.</param>
    /// <param name="world">The loaded world.</param>
    /// <param name="commands">The dispatcher every decision goes through.</param>
    /// <param name="rng">This seat's own stream for this turn.</param>
    /// <param name="events">Where accepted commands' events are published.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static AiTurnOutcome Run(
        GameState state,
        Ruleset ruleset,
        World world,
        ICommandDispatch commands,
        IRng rng,
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(events);

        var nationId = state.ActiveNationId;
        var log = new List<string>();

        var nation = state.NationById(nationId);
        if (nation is null || nation.Eliminated || nation.Control != SeatControl.Ai)
        {
            // Not this system's seat. Recorded rather than silently skipped so a log with no decisions
            // in it says which of the three reasons applied.
            log.Add(Inv(
                "skipped: seat '{0}' is {1}", nationId,
                nation is null ? "not a known nation" : nation.Eliminated ? "eliminated" : "not AI-controlled"));
            return new AiTurnOutcome(state, nationId, log, 0, 0, 0, HitActionCap: false);
        }

        var personality = AiPersonalityProfile.For(nation);
        log.Add(Inv(
            "seat {0}: aggression {1}, expansionDrive {2}, loyaltyToAlliances {3} (permille)",
            nationId,
            personality.AggressionPermille,
            personality.ExpansionDrivePermille,
            personality.LoyaltyToAlliancesPermille));

        var resupply = AiResupplyPass.Run(state, ruleset, nationId);
        state = resupply.State;
        log.Add(resupply.Describe());

        var issued = 0;
        var rejected = 0;
        var mismatches = 0;
        var hitCap = true;
        var marched = new List<string>();

        // T60 Done-when 1: one siege-gate sample per turn, taken on the first proposal pass only. Later
        // passes see the same standing adjacencies again, so feeding them in too would count situations
        // once per action rather than once per turn. See AiSiegeGateTally.
        var siegeGates = new AiSiegeGateTally();

        for (var action = 0; action < AiWeights.MaxActionsPerTurn; action++)
        {
            var view = new AiView(state, ruleset, world, nationId);
            var candidates = new List<AiCandidate>();
            AiMilitaryPhase.Propose(
                view, personality, rng, marched, candidates, action == 0 ? siegeGates : null);
            AiEconomyPhase.Propose(view, personality, candidates);
            AiDiplomacyPhase.Propose(view, personality, candidates);

            var chosen = Select(candidates, rng);
            if (chosen is null)
            {
                log.Add(Inv("no candidate scored at least {0}; turn ends", AiWeights.MinimumActionScore));
                hitCap = false;
                break;
            }

            log.Add(Inv(
                "chose [{0}/{1}] score {2} of {3} candidates -- {4}",
                chosen.Phase, chosen.Kind, chosen.Score, candidates.Count, chosen.Rationale));

            if (chosen.IsMarch && chosen.SubjectId is { } marchingArmyId)
            {
                marched.Add(marchingArmyId);
            }

            var before = state;
            var execution = Execute(state, ruleset, commands, events, chosen, log);
            state = execution.State;
            issued += execution.Issued;
            rejected += execution.Rejected;
            mismatches += execution.Mismatches;

            if (AiSubstantiveState.AreEquivalent(before, state))
            {
                // The command was accepted and changed nothing the game can act on -- a blocked march,
                // for instance. Re-proposing it would produce the same non-event, so the turn stops
                // here rather than burning the action cap on it.
                log.Add("last action changed nothing substantive; turn ends");
                hitCap = false;
                break;
            }
        }

        if (hitCap)
        {
            log.Add(Inv("action cap {0} reached", AiWeights.MaxActionsPerTurn));
        }

        if (siegeGates.Describe() is { } gates)
        {
            log.Add(gates);
        }

        return new AiTurnOutcome(state, nationId, log, issued, rejected, mismatches, hitCap);
    }

    /// <summary>
    /// The highest-scoring candidate at or above <see cref="AiWeights.MinimumActionScore"/>, with exact
    /// ties broken by one draw from the seat's stream.
    /// </summary>
    /// <remarks>
    /// The tied set is collected in candidate order — that is, in the order the three phases proposed
    /// them, which is itself fixed by <see cref="GameState"/>'s own list orders — so the index the draw
    /// picks means the same thing on every run. A single-element tie takes no draw at all, so the number
    /// of draws a turn makes depends only on how many exact ties it saw.
    /// </remarks>
    private static AiCandidate? Select(List<AiCandidate> candidates, IRng rng)
    {
        var bestScore = long.MinValue;
        foreach (var candidate in candidates)
        {
            if (candidate.Score >= AiWeights.MinimumActionScore && candidate.Score > bestScore)
            {
                bestScore = candidate.Score;
            }
        }

        if (bestScore == long.MinValue)
        {
            return null;
        }

        var tied = new List<AiCandidate>();
        foreach (var candidate in candidates)
        {
            if (candidate.Score == bestScore)
            {
                tied.Add(candidate);
            }
        }

        return tied.Count == 1 ? tied[0] : tied[rng.NextInt(tied.Count)];
    }

    private readonly record struct ExecutionResult(GameState State, int Issued, int Rejected, int Mismatches);

    private static ExecutionResult Execute(
        GameState state,
        Ruleset ruleset,
        ICommandDispatch commands,
        IEventSink events,
        AiCandidate candidate,
        List<string> log)
    {
        var issued = 0;
        var rejected = 0;
        var mismatches = 0;

        foreach (var command in candidate.Commands)
        {
            if (LegalityProbe(state, ruleset, command) is { } refusal)
            {
                // Only reachable for the second half of a declare-then-attack pair: the projection said
                // the attack would be legal and the real declaration produced something else. Recorded
                // and abandoned -- never sent, because sending it would be a rejected command.
                mismatches++;
                log.Add(Inv(
                    "ABANDONED {0}: probe refused after the declaration landed ({1}: {2})",
                    command.Kind, refusal.Code, refusal.Message));
                break;
            }

            var result = commands.Dispatch(state, command, events);
            issued++;
            if (result.IsRejected)
            {
                rejected++;
                log.Add(Inv(
                    "REJECTED {0} ({1}): {2}", command.Kind, result.Code, result.Rejection!.Message));
                break;
            }

            state = result.State;
            log.Add(Inv("  issued {0}", command.Kind));
        }

        return new ExecutionResult(state, issued, rejected, mismatches);
    }

    /// <summary>
    /// T54's advance probe, applied to whichever of the three attack commands this is. Returns
    /// <see langword="null"/> for every other command kind, because for those the candidate generator's
    /// own gates are the whole check and there is no second, shared probe to call.
    /// </summary>
    private static CommandRejection? LegalityProbe(GameState state, Ruleset ruleset, ICommand command) =>
        command switch
        {
            AttackArmyCommand attack => AttackLegality.Check(state, ruleset, attack),
            BesiegeCityCommand siege => AttackLegality.Check(state, ruleset, siege),
            AttackFleetCommand naval => AttackLegality.Check(state, ruleset, naval),
            _ => null,
        };

    /// <summary>See <c>AiMilitaryPhase.Inv</c>: the per-seed log has to be locale-independent.</summary>
    private static string Inv(string format, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, format, arguments);
}
