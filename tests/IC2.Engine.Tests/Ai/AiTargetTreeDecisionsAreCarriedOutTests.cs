using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;
using Xunit.Abstractions;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925) Done-when 6: attack decisions are carried out. Instrumented by the tree's own decision
/// log, over 16 rounds on the default seed, on seeds 7 and 123 as Rome, and on the default seed as
/// Carthage: whenever the tree selected <em>attack the city</em> or <em>attack the army</em> for an army,
/// an attack or a march toward that target was issued and accepted in that seat's turn. A count of zero
/// exceptions is asserted, and the counts are written to the test output for the PR.
/// </summary>
public sealed class AiTargetTreeDecisionsAreCarriedOutTests(ITestOutputHelper output)
{
    private const int Rounds = 16;

    public static TheoryData<string, ulong?> Runs => new()
    {
        { "rome", null },
        { "rome", 7UL },
        { "rome", 123UL },
        { "carthage", null },
    };

    [Theory]
    [MemberData(nameof(Runs))]
    public void Every_attack_decision_is_followed_by_an_accepted_attack_or_march_toward_its_target(string seat, ulong? seed)
    {
        var resolved = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");
        var session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, seed, seat);
        var log = AiArmyTargetTree.NewDecisionLog();
        try
        {
            var round = new List<DomainEvent>();
            session.EventsPublished += events => round.AddRange(events);

            var attackDecisions = 0;
            var checkedDecisions = 0;
            var carriedOut = 0;
            var skipped = 0;
            var allDecisions = 0;
            var allSkipped = 0;
            var captures = 0;
            var exceptions = new List<string>();
            var exempted = new List<string>();
            var seatName = resolved.World.Nations.Single(n => string.Equals(n.Id, seat, StringComparison.Ordinal)).Name;

            for (var r = 1; r <= Rounds; r++)
            {
                round.Clear();
                log.Clear();
                var startOfRound = session.State;
                session.Submit("end");

                captures += round.OfType<CityFallsToNation>().Count(c => c.NewOwner != seatName);
                var turns = SplitByNationTurn(round);
                // The whole-run measure (the final review's R5): every decision the log records, whatever its
                // kind, with the candidates the reachability rule skipped while making it.
                allDecisions += log.Count;
                allSkipped += log.Sum(d => d.Skipped);
                attackDecisions += log.Count(d => d.Kind is AiArmyTargetTree.Kind.AttackCity or AiArmyTargetTree.Kind.AttackArmy);

                // EVERY attack decision the log records is owed a carried-out attack or march toward its
                // target (the review's R1/R2): an army that has spent its march is not decided again, so
                // there is no later decision to exempt.
                var roundDecisions = log.ToList();
                foreach (var (decision, decisionIndex) in roundDecisions
                    .Select((d, i) => (d, i))
                    .Where(x => x.d.Kind is AiArmyTargetTree.Kind.AttackCity or AiArmyTargetTree.Kind.AttackArmy))
                {
                    checkedDecisions++;
                    skipped += decision.Skipped;
                    if (turns.TryGetValue(decision.NationId, out var carriedEvents) && IsCarriedOut(decision, carriedEvents))
                    {
                        carriedOut++;
                    }

                    if (!turns.TryGetValue(decision.NationId, out var turnEvents) || !IsCarriedOut(decision, turnEvents))
                    {
                        // T157 (#926): the narrow exemption. A later decision for the same army from the same
                        // position supersedes this one when this one's target is unreachable from that position
                        // by the reachability rule (a friendly army marched onto the straight path in between).
                        // The superseding decision is still owed its own attack or march.
                        if (IsSuperseded(roundDecisions, decisionIndex, TurnBoundaryState(startOfRound, session.State, round, decision.NationId), resolved.Ruleset, resolved.World))
                        {
                            exempted.Add(Describe(r, decision));
                            continue;
                        }

                        exceptions.Add(Describe(r, decision));
                    }
                }
            }

            output.WriteLine(
                $"seat {seat} seed {(seed is null ? "default" : seed.ToString())}: "
                + $"{attackDecisions} attack decisions, {checkedDecisions} checked, {captures} AI captures, {carriedOut} carried out, "
                + $"{allSkipped} candidates skipped by the reachability rule across all {allDecisions} logged decisions "
                + $"({skipped} of them in attack decisions), "
                + $"{exceptions.Count} exceptions, {exempted.Count} exempted (superseded, target unreachable)");
            foreach (var exemption in exempted.Take(12))
            {
                output.WriteLine("  exempted: " + exemption);
            }

            foreach (var exception in exceptions.Take(12))
            {
                output.WriteLine("  exception: " + exception);
            }

            Assert.True(checkedDecisions > 0, "the runs must contain attack decisions to be a test");
            Assert.True(carriedOut > 0, "each run must log at least one attack decision that is carried out");
            Assert.Empty(exceptions);
        }
        finally
        {
            AiArmyTargetTree.ClearDecisionLog();
        }
    }

    /// <summary>One round's events, cut into the AI nations' turns: a turn ends with its <see cref="AiTurnDecided"/>.</summary>
    private static Dictionary<string, List<DomainEvent>> SplitByNationTurn(List<DomainEvent> round)
    {
        var turns = new Dictionary<string, List<DomainEvent>>(StringComparer.Ordinal);
        var current = new List<DomainEvent>();
        foreach (var domainEvent in round)
        {
            current.Add(domainEvent);
            if (domainEvent is AiTurnDecided decided)
            {
                turns[decided.NationId] = current;
                current = new List<DomainEvent>();
            }
        }

        return turns;
    }

    private static bool IsCarriedOut(AiArmyTargetTree.DecisionRecord decision, List<DomainEvent> turnEvents)
    {
        var attackCity = decision.Kind == AiArmyTargetTree.Kind.AttackCity;
        var targetId = attackCity ? decision.TargetCityId : decision.TargetArmyId;
        var targetX = attackCity ? decision.TargetCityX : decision.TargetArmyX;
        var targetY = attackCity ? decision.TargetCityY : decision.TargetArmyY;

        foreach (var domainEvent in turnEvents)
        {
            switch (domainEvent)
            {
                case BattleResolved battle
                    when string.Equals(battle.Result.AttackerId, decision.ArmyId, StringComparison.Ordinal)
                        && string.Equals(battle.Result.DefenderId, targetId, StringComparison.Ordinal):
                    return true;

                case ArmyMoved moved
                    when string.Equals(moved.ArmyId, decision.ArmyId, StringComparison.Ordinal)
                        && Distance(moved.ToX, moved.ToY, targetX, targetY) < Distance(moved.FromX, moved.FromY, targetX, targetY):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The state at the end of <paramref name="nationId"/>'s turn, rebuilt from the round's start state, its end state
    /// and its events (the session exposes no per-turn snapshot): exactly the incarnation of each army id that was
    /// alive at the boundary, at its position then.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Every way an army comes into or leaves existence, found by grepping <c>src/IC2.Engine</c> for what adds
    /// to or removes from <c>GameState.Armies</c>.</strong> Creation: <c>MobilizationArmyCreation</c> (the
    /// <see cref="RecruitMobilized"/> event, <c>ArmyWasCreated</c>), <c>SplitArmyCommandHandler</c> (publishes no
    /// event), and the start-of-game factory and importer (not in a round). <c>MergedInstantCandidate</c> builds
    /// armies only for the detailed-resolver tournament harness. Removal: a battle's destroyed loser
    /// (<see cref="BattleResolved"/>), and <c>JoinArmies</c>, <c>DisbandArmy</c>, <c>EliminationForces</c>, the
    /// quarterly mercenary desertion and the fleet tick's lost-with-ship armies, none of which publishes an event.
    /// Positions change by <see cref="ArmyMoved"/>, <see cref="ArmyScattered"/> (no origin) and a fleet carrying an
    /// embarked army (no event).
    /// </para>
    /// <para>
    /// Each id is simulated through the round: alive at the start if the start state has it, created by
    /// <c>RecruitMobilized</c>, destroyed by <c>BattleResolved</c>. Anything the events cannot explain is fail
    /// closed (the id is left out): an event that contradicts the lifetime (a second destruction, a move of a dead
    /// army, a creation of a live one), an end state that disagrees with the simulated final liveness (an army
    /// created without a creation event, or one gone without a destruction event), an army aboard a fleet, a
    /// position that no move, no start position and no end position gives.
    /// </para>
    /// </remarks>
    internal static GameState TurnBoundaryState(
        GameState startOfRound, GameState endOfRound, List<DomainEvent> round, string nationId)
    {
        var boundary = round.FindIndex(e => e is AiTurnDecided decided
            && string.Equals(decided.NationId, nationId, StringComparison.Ordinal));
        if (boundary < 0)
        {
            return endOfRound;
        }

        var ids = new List<string>();
        void Note(string id)
        {
            if (!ids.Contains(id, StringComparer.Ordinal))
            {
                ids.Add(id);
            }
        }

        foreach (var army in startOfRound.Armies)
        {
            Note(army.Id);
        }

        foreach (var army in endOfRound.Armies)
        {
            Note(army.Id);
        }

        foreach (var published in round)
        {
            switch (published)
            {
                case ArmyMoved moved:
                    Note(moved.ArmyId);
                    break;
                case ArmyScattered scattered:
                    Note(scattered.ArmyId);
                    break;
                case RecruitMobilized { ArmyWasCreated: true } mobilized:
                    Note(mobilized.ArmyId);
                    break;
                case BattleResolved { Result.LoserFate: LoserFate.Destroyed } battle:
                    Note(battle.Result.LoserId);
                    break;
            }
        }

        var armies = new List<ArmyState>();
        foreach (var id in ids)
        {
            if (ArmyAtBoundary(id, startOfRound, endOfRound, round, boundary) is { } rebuilt)
            {
                armies.Add(rebuilt);
            }
        }

        return endOfRound with { Armies = ValueList.From(armies) };
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static ArmyState? ArmyAtBoundary(
        string id, GameState start, GameState end, List<DomainEvent> round, int boundary)
    {
        var atStart = start.ArmyById(id);
        var atEnd = end.ArmyById(id);
        if (atStart?.AboardFleetId is not null || atEnd?.AboardFleetId is not null)
        {
            return null;
        }

        var alive = atStart is not null;
        (int X, int Y)? position = atStart is null ? null : (atStart.X, atStart.Y);
        var nation = atStart?.Nation;
        var initialIncarnation = atStart is not null;
        var aliveAtBoundary = false;
        (int X, int Y)? positionAtBoundary = null;
        var changedAfterBoundary = false;

        for (var i = 0; i < round.Count; i++)
        {
            if (i == boundary)
            {
                aliveAtBoundary = alive;
                positionAtBoundary = position;
            }

            switch (round[i])
            {
                case RecruitMobilized { ArmyWasCreated: true } created when Same(created.ArmyId, id):
                    if (alive)
                    {
                        return null;
                    }

                    alive = true;
                    nation = created.NationId;
                    initialIncarnation = false;
                    changedAfterBoundary |= i > boundary;
                    break;

                case BattleResolved { Result.LoserFate: LoserFate.Destroyed } battle when Same(battle.Result.LoserId, id):
                    if (!alive)
                    {
                        return null;
                    }

                    alive = false;
                    position = null;
                    break;

                case ArmyMoved moved when Same(moved.ArmyId, id):
                    if (!alive)
                    {
                        return null;
                    }

                    // The first move after the boundary of an incarnation whose position is still unknown tells
                    // where it stood at the boundary: its origin.
                    if (i > boundary && aliveAtBoundary && !changedAfterBoundary && positionAtBoundary is null)
                    {
                        positionAtBoundary = (moved.FromX, moved.FromY);
                    }

                    position = (moved.ToX, moved.ToY);
                    break;

                case ArmyScattered scattered when Same(scattered.ArmyId, id):
                    if (!alive)
                    {
                        return null;
                    }

                    if (i > boundary && aliveAtBoundary && !changedAfterBoundary && positionAtBoundary is null)
                    {
                        // A scatter has no origin: the boundary position cannot be recovered.
                        return null;
                    }

                    position = (scattered.ToX, scattered.ToY);
                    break;
            }
        }

        // The simulated final liveness must agree with the end state, else an event is missing (fail closed).
        if (alive != (atEnd is not null))
        {
            return null;
        }

        var finalIncarnation = atEnd is not null && !changedAfterBoundary;
        if (positionAtBoundary is null)
        {
            // A created army that has not moved by the boundary and does not move after it either stands where
            // the round ends -- but only if it is the incarnation that survives to the end.
            if (!finalIncarnation)
            {
                return null;
            }

            positionAtBoundary = (atEnd!.X, atEnd.Y);
        }

        var basis = finalIncarnation
            ? atEnd!
            : initialIncarnation && atStart is not null
                ? atStart
                : new ArmyState(id, nation ?? string.Empty, 0, 0, 0, 0, 0, 0, null, null, ValueList<UnitSlot>.Empty);
        return basis with { X = positionAtBoundary.Value.X, Y = positionAtBoundary.Value.Y };
    }

    /// <summary>
    /// Whether the decision at <paramref name="decisionIndex"/> is superseded: a later decision for the same army from
    /// the same position is in the round's log, and the earlier target is unreachable from that position on
    /// <paramref name="boundaryState"/> (the state at the end of that nation's turn, see
    /// <see cref="TurnBoundaryState"/>), with the army placed at the decision's recorded position (it has moved
    /// since). An army absent from the boundary state is never exempt.
    /// </summary>
    internal static bool IsSuperseded(
        List<AiArmyTargetTree.DecisionRecord> decisions,
        int decisionIndex,
        GameState boundaryState,
        Ruleset ruleset,
        World world)
    {
        // By position in the log, never by value: DecisionRecord is a record, so IndexOf would find the first
        // value-equal copy and a final duplicate would count itself or an earlier copy as "later".
        var decision = decisions[decisionIndex];
        var later = decisions.Skip(decisionIndex + 1).Any(d =>
            string.Equals(d.NationId, decision.NationId, StringComparison.Ordinal)
            && string.Equals(d.ArmyId, decision.ArmyId, StringComparison.Ordinal)
            && d.ArmyX == decision.ArmyX
            && d.ArmyY == decision.ArmyY);
        if (!later || boundaryState.ArmyById(decision.ArmyId) is not { } army)
        {
            return false;
        }

        var attackCity = decision.Kind == AiArmyTargetTree.Kind.AttackCity;
        var targetX = attackCity ? decision.TargetCityX : decision.TargetArmyX;
        var targetY = attackCity ? decision.TargetCityY : decision.TargetArmyY;
        var view = new AiView(boundaryState, ruleset, world, decision.NationId);
        return !AiArmyTargetTree.IsReachable(view, army with { X = decision.ArmyX, Y = decision.ArmyY }, targetX, targetY);
    }

    private static int Distance(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

    private static string Describe(int round, AiArmyTargetTree.DecisionRecord d) =>
        $"round {round}: {d.NationId}/{d.ArmyId} at ({d.ArmyX},{d.ArmyY}) chose {d.Kind} "
        + $"{(d.Kind == AiArmyTargetTree.Kind.AttackCity ? d.TargetCityId + $" ({d.TargetCityX},{d.TargetCityY}) score {d.CityScore}" : d.TargetArmyId + $" ({d.TargetArmyX},{d.TargetArmyY}) score {d.ArmyScore}")}";
}
