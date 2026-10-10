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
            var captures = 0;
            var exceptions = new List<string>();
            var seatName = resolved.World.Nations.Single(n => string.Equals(n.Id, seat, StringComparison.Ordinal)).Name;

            for (var r = 1; r <= Rounds; r++)
            {
                round.Clear();
                log.Clear();
                session.Submit("end");

                captures += round.OfType<CityFallsToNation>().Count(c => c.NewOwner != seatName);
                var turns = SplitByNationTurn(round);
                attackDecisions += log.Count(d => d.Kind is AiArmyTargetTree.Kind.AttackCity or AiArmyTargetTree.Kind.AttackArmy);

                // EVERY attack decision the log records is owed a carried-out attack or march toward its
                // target (the review's R1/R2): an army that has spent its march is not decided again, so
                // there is no later decision to exempt.
                foreach (var decision in log
                    .Where(d => d.Kind is AiArmyTargetTree.Kind.AttackCity or AiArmyTargetTree.Kind.AttackArmy))
                {
                    checkedDecisions++;
                    skipped += decision.Skipped;
                    if (turns.TryGetValue(decision.NationId, out var carriedEvents) && IsCarriedOut(decision, carriedEvents))
                    {
                        carriedOut++;
                    }

                    if (!turns.TryGetValue(decision.NationId, out var turnEvents) || !IsCarriedOut(decision, turnEvents))
                    {
                        exceptions.Add(Describe(r, decision));
                    }
                }
            }

            output.WriteLine(
                $"seat {seat} seed {(seed is null ? "default" : seed.ToString())}: "
                + $"{attackDecisions} attack decisions, {checkedDecisions} checked, {captures} AI captures, {carriedOut} carried out, {skipped} candidates skipped by the reachability rule, "
                + $"{exceptions.Count} exceptions");
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

    private static int Distance(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

    private static string Describe(int round, AiArmyTargetTree.DecisionRecord d) =>
        $"round {round}: {d.NationId}/{d.ArmyId} at ({d.ArmyX},{d.ArmyY}) chose {d.Kind} "
        + $"{(d.Kind == AiArmyTargetTree.Kind.AttackCity ? d.TargetCityId + $" ({d.TargetCityX},{d.TargetCityY}) score {d.CityScore}" : d.TargetArmyId + $" ({d.TargetArmyX},{d.TargetArmyY}) score {d.ArmyScore}")}";
}
