using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using static IC2.Engine.Ai.AiFormat;

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
/// <param name="HitActionCap">Whether the turn stopped because it reached <see cref="AiWeightsRules.MaxActionsPerTurn"/>.</param>
public sealed record AiTurnOutcome(
    GameState State,
    string NationId,
    IReadOnlyList<string> Log,
    int CommandsIssued,
    int CommandsRejected,
    int ProjectionMismatches,
    bool HitActionCap);

/// <summary>
/// One AI seat's turn: the interleaved army resupply-and-hire step, the fleet resupply, then a greedy loop
/// of "score every candidate, take the best, look again".
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
/// <strong>Only the two unconditional up-front passes edit the state directly.</strong> Every decision
/// goes out as a command through <see cref="ICommandDispatch"/>, which is the seam
/// <see cref="SystemContext.Commands"/> exists for: "<em>an AI that wrote to the state directly would
/// skip every legality check a human seat's order goes through, and would leave no command log for a
/// replay to follow</em>". The two exceptions are both T38's automatic resupply and T76's mercenary hire,
/// which are pure functions with no command of their own: <see cref="AiMercenaryHirePass"/> reproduces
/// the original's single <c>FUN_0044E41C</c> step — each army's resupply and hire interleaved city by
/// city — and <see cref="AiResupplyPass"/> runs the fleet half of automatic resupply. Both are
/// unconditional within their gates rather than scored decisions, and inventing commands for them would
/// be new command types outside those tasks' Owns lists.
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

        var personality = AiPersonalityProfile.For(nation, ruleset);
        log.Add(Inv(
            "seat {0}: aggression {1}, expansionDrive {2}, loyaltyToAlliances {3} (permille)",
            nationId,
            personality.AggressionPermille,
            personality.ExpansionDrivePermille,
            personality.LoyaltyToAlliancesPermille));

        // R1: the original's FUN_0044E41C is one loop per army over the cities, and each in-range city
        // iteration resupplies the army (FUN_0044f6d8) before hiring at that city. AiMercenaryHirePass.Run
        // reproduces that loop, so the hire at an earlier city raises the troop count a later city's
        // resupply sees. The pass captures each army's entry purse itself, before that army's own first
        // resupply, so it already gates the hire on the pre-resupply purse here; the snapshot below only
        // matters to a caller that resupplies the army before calling the pass, and is kept because that
        // caller exists (AiMercenaryHirePassTests.The_money_gate_reads_the_entry_purse_not_the_post_resupply_one).
        var moneyAtTurnStart = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var army in state.Armies)
        {
            moneyAtTurnStart[army.Id] = army.Money;
        }

        var mercenary = AiMercenaryHirePass.Run(state, ruleset, nationId, moneyAtTurnStart);
        state = mercenary.State;

        // The hire pass already ran every army's resupply, interleaved city by city. Only the fleet half
        // of AiResupplyPass is left, so slice the armies out and let its own army loop do nothing. Its
        // cities, fleets and nations are this state's own, so merge those fields back and keep the armies
        // the hire pass produced.
        var fleetResupply = AiResupplyPass.Run(
            state with { Armies = ValueList<ArmyState>.Empty }, ruleset, nationId);
        state = state with
        {
            Cities = fleetResupply.State.Cities,
            Fleets = fleetResupply.State.Fleets,
            Nations = fleetResupply.State.Nations,
        };

        // One resupply line, the same shape AiResupplyPass reported before T76, now summing the army half
        // done inside the hire pass and the fleet half done here.
        log.Add(Inv(
            "resupply: {0} army transfers, {1} fleet transfers, {2} tons, {3} talents",
            mercenary.ArmyTransfers,
            fleetResupply.FleetTransfers,
            mercenary.TonsMoved + fleetResupply.TonsMoved,
            mercenary.TalentsPaid + fleetResupply.TalentsPaid));
        log.Add(mercenary.Describe());

        var issued = 0;
        var rejected = 0;
        var mismatches = 0;
        var hitCap = true;
        var marched = new List<string>();

        // T60 Done-when 1: one siege-gate sample per turn, taken on the first proposal pass only. Later
        // passes see the same standing adjacencies again, so feeding them in too would count situations
        // once per action rather than once per turn. See AiSiegeGateTally.
        var siegeGates = new AiSiegeGateTally();

        for (var action = 0; action < ruleset.Ai.MaxActionsPerTurn; action++)
        {
            var view = new AiView(state, ruleset, world, nationId);
            var candidates = new List<AiCandidate>();
            AiMilitaryPhase.Propose(
                view, personality, rng, marched, candidates, action == 0 ? siegeGates : null);
            AiEconomyPhase.Propose(view, personality, candidates);
            // T82 (#359, bug #357, Owns amendment PR #378): the same seat-turn IRng AiMilitaryPhase
            // already receives above, so AiDiplomacyPhase's own Random(20) alliance roll is drawn from
            // the identical turn-stable stream as AiMilitaryPhase's Random(10) war roll -- see
            // AiDiplomacyPhase.ProposeOwnAlliance's own remarks for why that stability matters.
            AiDiplomacyPhase.Propose(view, personality, rng, candidates);

            var chosen = Select(candidates, rng, ruleset);
            if (chosen is null)
            {
                log.Add(Inv("no candidate scored at least {0}; turn ends", ruleset.Ai.MinimumActionScore));
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
            var subjectId = chosen.SubjectId;
            var beforeArmy = subjectId is { } sid ? state.ArmyById(sid) : null;
            var execution = Execute(state, ruleset, commands, events, chosen, log);
            state = execution.State;
            var changed = !AiSubstantiveState.AreEquivalent(before, state);
            if (changed)
            {
                // CommandsIssued counts only what actually moved the state. An accepted command that left
                // every substantive field untouched -- the "no-op" case a blocked march falls into, for
                // instance -- does not belong on the same axis as one that took effect: a soak's stall
                // counter increments only when CommandsIssued == 0, so counting a no-op there hides a
                // true stall from the metric that exists to find it. Rejected commands are tracked on
                // their own counter; they never count here.
                issued += execution.Issued;
            }

            rejected += execution.Rejected;
            mismatches += execution.Mismatches;

            // T156 (issue #925) Done-when 3: the original's garrison fallback
            // (FUN_0044ebe8). When the chosen command was accepted but that army's tile is unchanged --
            // compared directly, before and after the command, not through
            // AiSubstantiveState.AreEquivalent's whole-state comparison -- the driver runs the fallback
            // for that army, whether or not the command changed something else (a supply, a treasury).
            // An army that moved takes no fallback; an army whose command was rejected was never on the
            // hook for one; an army with no moves left (an attack zeroes them) takes none either, since
            // a MoveArmyCommand would only be refused. The fallback is one more command, so it feeds the
            // same counters as any other command this loop processed.
            if (execution.Issued > 0
                && execution.Rejected == 0
                && beforeArmy is { } beforeTile
                && subjectId is { } armyId
                && state.ArmyById(armyId) is { } afterTile
                && afterTile.Moves > 0
                && beforeTile.X == afterTile.X
                && beforeTile.Y == afterTile.Y)
            {
                RunGarrisonFallback(view, armyId, ruleset, commands, events, ref state, log,
                    ref issued, ref rejected, ref mismatches, marched);
            }

            if (AiSubstantiveState.AreEquivalent(before, state))
            {
                // The command (and any fallback after it) was accepted and changed nothing the game can
                // act on -- a blocked march, for instance. Re-proposing it would produce the same
                // non-event, so the turn stops here rather than burning the action cap on it.
                log.Add("last action changed nothing substantive; turn ends");
                hitCap = false;
                break;
            }
        }

        if (hitCap)
        {
            log.Add(Inv("action cap {0} reached", ruleset.Ai.MaxActionsPerTurn));
        }

        if (siegeGates.Describe() is { } gates)
        {
            log.Add(gates);
        }

        return new AiTurnOutcome(state, nationId, log, issued, rejected, mismatches, hitCap);
    }

    /// <summary>
    /// The highest-scoring candidate at or above <see cref="Model.AiWeightsRules.MinimumActionScore"/>,
    /// with exact ties broken by one draw from the seat's stream.
    /// </summary>
    /// <remarks>
    /// The tied set is collected in candidate order — that is, in the order the three phases proposed
    /// them, which is itself fixed by <see cref="GameState"/>'s own list orders — so the index the draw
    /// picks means the same thing on every run. A single-element tie takes no draw at all, so the number
    /// of draws a turn makes depends only on how many exact ties it saw.
    /// </remarks>
    private static AiCandidate? Select(List<AiCandidate> candidates, IRng rng, Ruleset ruleset)
    {
        var bestScore = long.MinValue;
        foreach (var candidate in candidates)
        {
            if (candidate.Score >= ruleset.Ai.MinimumActionScore && candidate.Score > bestScore)
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

    /// <summary>
    /// T156 (issue #925) Done-when 3: the original's garrison fallback
    /// (<c>FUN_0044ebe8</c>). When the chosen command was accepted but that army's tile is unchanged --
    /// directly, not via <see cref="AiSubstantiveState.AreEquivalent"/>'s whole-state comparison -- the
    /// driver runs the fallback for that army: if some own army (the army itself included, on the
    /// report's "if *some own army*" reading) is already within
    /// <see cref="AiWeightsRules.GarrisonFallbackCapitalDistance"/> tiles of the capital, head for the
    /// nearest city of any owner; otherwise head for the capital. The fallback is one more command
    /// (<c>MoveArmyCommand</c>), so its accepted/rejected path feeds the same counters as any other
    /// command this loop processed.
    /// </summary>
    private static void RunGarrisonFallback(
        AiView view,
        string armyId,
        Ruleset ruleset,
        ICommandDispatch commands,
        IEventSink events,
        ref GameState state,
        List<string> log,
        ref int issued,
        ref int rejected,
        ref int mismatches,
        List<string> marched)
    {
        var army = state.ArmyById(armyId)!;
        (int X, int Y)? dest = AiArmyTargetTree.GarrisonFallback(view, army, state);
        string? tierLabel = null;
        if (dest is null)
        {
            // Hazards' "fallback is reachable too": when no garrison candidate is reachable, try the
            // emergency move (the tile the existing move command can reach this turn).
            dest = AiArmyTargetTree.EmergencyMoveDestination(view, army, state);
            tierLabel = dest is null ? null : "emergency move";
        }

        if (dest is not { } target)
        {
            // Boxed in: every distinct tile is unreachable. The Hazards' "Staying put" branch — the AI
            // is not idle by choice; nothing is reachable and the log records that explicitly.
            log.Add(Inv("no reachable move for {0}: army stays put", armyId));
            return;
        }

        var rationale = tierLabel is null
            ? Inv("garrison fallback {0} toward ({1}, {2})", armyId, target.X, target.Y)
            : Inv("emergency move {0} toward ({1}, {2}): no garrison destination is reachable", armyId, target.X, target.Y);

        var candidate = AiCandidate.Single(
            AiPhase.Military,
            AiCandidate.ApproachKind,
            new Movement.Commands.MoveArmyCommand(view.NationId, armyId, target.X, target.Y),
            ruleset.Ai.MinimumActionScore,
            rationale,
            armyId);

        var before = state;
        var execution = Execute(state, ruleset, commands, events, candidate, log);
        state = execution.State;

        if (!AiSubstantiveState.AreEquivalent(before, state))
        {
            issued += execution.Issued;
        }

        rejected += execution.Rejected;
        mismatches += execution.Mismatches;

        if (execution.Issued > 0)
        {
            marched.Add(armyId);
            log.Add(Inv(
                "{0} moved {1} to ({2}, {3})",
                tierLabel ?? "garrison fallback",
                armyId,
                target.X,
                target.Y));
        }
    }

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
}
