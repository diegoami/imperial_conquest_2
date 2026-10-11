using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
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
                        if (IsSuperseded(roundDecisions, decisionIndex, TurnBoundaryState(session.State, round, decision.NationId), resolved.Ruleset, resolved.World))
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
    /// The state at the end of <paramref name="nationId"/>'s turn, rebuilt from the end-of-round state and the
    /// round's events (the session exposes no per-turn snapshot), preserving each army's lifetime.
    /// </summary>
    /// <remarks>
    /// An army's position at the boundary is its last move's destination before the nation's
    /// <see cref="AiTurnDecided"/>, else its first later move's origin, else where it ends the round. An army
    /// missing from the end-of-round state was destroyed in the round (<see cref="BattleResolved"/> with a
    /// destroyed loser); it is rebuilt, as a bare record, only when that happened AFTER the boundary and it
    /// moved (so its boundary position is known). One destroyed before the boundary is not rebuilt. One that never
    /// moved and was destroyed after the boundary cannot be rebuilt (no position is recorded); that only ever
    /// narrows the exemption.
    /// </remarks>
    internal static GameState TurnBoundaryState(GameState endOfRound, List<DomainEvent> round, string nationId)
    {
        var boundary = round.FindIndex(e => e is AiTurnDecided decided
            && string.Equals(decided.NationId, nationId, StringComparison.Ordinal));
        if (boundary < 0)
        {
            return endOfRound;
        }

        var destroyedAt = new Dictionary<string, int>(StringComparer.Ordinal);
        var destructions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < round.Count; i++)
        {
            if (round[i] is BattleResolved { Result.LoserFate: LoserFate.Destroyed } battle)
            {
                destroyedAt[battle.Result.LoserId] = i;
                destructions[battle.Result.LoserId] = destructions.GetValueOrDefault(battle.Result.LoserId) + 1;
            }
        }

        // An id that was destroyed in the round and is also alive at its end was reused (mobilisation takes the
        // lowest unused id), and one destroyed twice was reused in between. The round's events cannot tell the
        // old army's moves from the new one's, so such an id is ambiguous: it is left out of the boundary state
        // altogether (fail closed -- as the deciding army it makes the decision not exempt, as a blocker it blocks
        // nothing).
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, count) in destructions)
        {
            if (count > 1 || endOfRound.ArmyById(id) is not null)
            {
                ambiguous.Add(id);
            }
        }

        var positions = new Dictionary<string, (int X, int Y, string Nation)>(StringComparer.Ordinal);
        for (var i = round.Count - 1; i > boundary; i--)
        {
            if (round[i] is ArmyMoved later)
            {
                positions[later.ArmyId] = (later.FromX, later.FromY, later.NationId);
            }
        }

        for (var i = 0; i < boundary; i++)
        {
            if (round[i] is ArmyMoved earlier)
            {
                positions[earlier.ArmyId] = (earlier.ToX, earlier.ToY, earlier.NationId);
            }
        }

        var armies = new List<ArmyState>();
        foreach (var army in endOfRound.Armies)
        {
            if (ambiguous.Contains(army.Id))
            {
                continue;
            }

            armies.Add(positions.Remove(army.Id, out var at) ? army with { X = at.X, Y = at.Y } : army);
        }

        foreach (var (id, at) in positions)
        {
            if (!ambiguous.Contains(id) && destroyedAt.TryGetValue(id, out var when) && when > boundary)
            {
                armies.Add(new ArmyState(id, at.Nation, at.X, at.Y, 0, 0, 0, 0, null, null, ValueList<UnitSlot>.Empty));
            }
        }

        return endOfRound with { Armies = ValueList.From(armies) };
    }

    /// <summary>
    /// Whether <paramref name="decision"/> is superseded: a later decision for the same army from the same
    /// position is in the round's log, and the earlier target is unreachable from that position on the end-of-round
    /// state, with the army placed at the decision's recorded position (it has moved since).
    /// </summary>
    internal static bool IsSuperseded(
        List<AiArmyTargetTree.DecisionRecord> decisions,
        int decisionIndex,
        GameState endState,
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
        if (!later || endState.ArmyById(decision.ArmyId) is not { } army)
        {
            return false;
        }

        var attackCity = decision.Kind == AiArmyTargetTree.Kind.AttackCity;
        var targetX = attackCity ? decision.TargetCityX : decision.TargetArmyX;
        var targetY = attackCity ? decision.TargetCityY : decision.TargetArmyY;
        var view = new AiView(endState, ruleset, world, decision.NationId);
        return !AiArmyTargetTree.IsReachable(view, army with { X = decision.ArmyX, Y = decision.ArmyY }, targetX, targetY);
    }

    private static int Distance(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

    private static string Describe(int round, AiArmyTargetTree.DecisionRecord d) =>
        $"round {round}: {d.NationId}/{d.ArmyId} at ({d.ArmyX},{d.ArmyY}) chose {d.Kind} "
        + $"{(d.Kind == AiArmyTargetTree.Kind.AttackCity ? d.TargetCityId + $" ({d.TargetCityX},{d.TargetCityY}) score {d.CityScore}" : d.TargetArmyId + $" ({d.TargetArmyX},{d.TargetArmyY}) score {d.ArmyScore}")}";
}
