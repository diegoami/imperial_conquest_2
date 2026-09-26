using IC2.Engine.Battle;
using IC2.Engine.Battle.Candidates;
using IC2.Engine.Battle.Candidates.Tournament;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Battle.Candidates;

/// <summary>
/// T59 Done-when 1: every candidate behind one seam, C1 being the merged resolver wrapped unchanged; and the
/// §8.5 draw-count contract, checked on every battle of a reduced schedule rather than only on the 100 DET
/// battles.
/// </summary>
public class CandidateSeamTests
{
    [Theory]
    [InlineData(DefeatOutcome.Destroyed)]
    [InlineData(DefeatOutcome.Scatter)]
    public void C1_is_the_merged_ResolveField_unchanged(DefeatOutcome onDefeat)
    {
        var harness = CandidateTestbed.Harness(onDefeat);
        var ruleset = CandidateTestbed.Ruleset with
        {
            Flags = CandidateTestbed.Ruleset.Flags with { CombatOnDefeat = onDefeat },
        };
        var toy = CandidateTestbed.Repository.Resolve("toy-3city");

        // T88: forced AI, matching CandidateTestbed.C1()'s own fixture -- the toy scenario's "north" seat
        // is otherwise human, and InstantBattleResolver.ResolveField now reads SeatControl for its
        // post-battle treaty gate, so an unmodified baseState here would put "direct" on a different
        // (human-consent) path from "viaSeam"'s AI-forced C1, breaking the "wrapped UNCHANGED" comparison
        // this test exists to make for a reason that has nothing to do with what it is testing.
        var baseState = CandidateTestbed.AsAiControlled(CandidateTestbed.Repository.CreateInitialState("toy-3city"));
        var c1 = CandidateTestbed.C1();

        // T-scale pairs, so both seats win somewhere and the loser sometimes has survivors to scatter.
        var pairs = new[] { (15, 4), (4, 15), (0, 1), (2, 3), (17, 19), (5, 5) };
        foreach (var (a, d) in pairs)
        {
            for (ulong seed = 0; seed < 5; seed++)
            {
                var battle = harness.Input(new ScheduledBattle(a, d, 0, seed), ArmyScale.Troops);
                var viaSeam = c1.Resolve(battle, new SplitMix64Rng(seed));

                // The same battle, straight through the merged resolver, with no wrapper in between.
                var attacker = Army("x-att", baseState.Nations[0].Id, 3, 2, battle.Attacker);
                var defender = Army("x-def", baseState.Nations[1].Id, 4, 2, battle.Defender);
                var state = baseState with
                {
                    Armies = ValueList.Of(attacker, defender),
                    Fleets = ValueList<FleetState>.Empty,
                    Cities = ValueList<CityState>.Empty,
                };
                var counting = new DrawCountingRng(new SplitMix64Rng(seed));
                var direct = InstantBattleResolver.ResolveField(state, "x-att", "x-def", ruleset, toy.World, counting, NullEventSink.Instance);

                Assert.Equal(direct.Result.AttackerWon, viaSeam.Winner == BattleSide.Attacker);
                Assert.Equal(counting.Draws, viaSeam.TotalDraws);
                var winner = direct.State.ArmyById(direct.Result.AttackerWon ? "x-att" : "x-def");
                Assert.Equal(Troops(winner, viaSeam.WinnerAfter.Length), viaSeam.WinnerAfter);
                var loser = direct.State.ArmyById(direct.Result.AttackerWon ? "x-def" : "x-att");
                var expectedSurvivors = onDefeat == DefeatOutcome.Scatter && direct.Result.LoserFate == LoserFate.Scattered
                    ? Troops(loser, viaSeam.LoserSurvivors.Length)
                    : new int[viaSeam.LoserSurvivors.Length];
                Assert.Equal(expectedSurvivors, viaSeam.LoserSurvivors);
            }
        }
    }

    [Fact]
    public void Every_candidate_draws_exactly_the_count_its_section_states_on_every_battle()
    {
        var harness = CandidateTestbed.Harness();
        var composition = TournamentHarness.CompositionSchedule(seedsPerMatchup: 1);
        var upset = TournamentHarness.UpsetSchedule(seedsPerMatchup: 2);
        foreach (var candidate in CandidateTestbed.All())
        {
            foreach (var (schedule, scale) in new[] { (composition, ArmyScale.Power), (composition, ArmyScale.Troops), (upset, ArmyScale.Troops) })
            {
                foreach (var battle in schedule)
                {
                    var input = harness.Input(battle, scale);
                    var outcome = candidate.Resolve(input, new SplitMix64Rng(battle.Seed), recordEvents: true);
                    Assert.True(
                        outcome.TotalDraws == outcome.StatedDraws,
                        $"{candidate.Key} seed {battle.Seed}: counted {outcome.TotalDraws}, stated {outcome.StatedDraws}");

                    if (candidate.Key is "C2" or "C4" or "C5")
                    {
                        // §8.5: for the variable candidates, the formula evaluated on the battle's own event log.
                        Assert.Equal(outcome.TotalDraws, outcome.Events.Sum(e => (long)e.StatedDraws));
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(DefeatOutcome.Destroyed)]
    [InlineData(DefeatOutcome.Scatter)]
    public void Every_output_record_is_complete_and_no_unit_gains_troops(DefeatOutcome onDefeat)
    {
        var harness = CandidateTestbed.Harness(onDefeat);
        var schedule = TournamentHarness.CompositionSchedule(seedsPerMatchup: 1);
        foreach (var candidate in CandidateTestbed.All())
        {
            foreach (var battle in schedule)
            {
                var input = harness.Input(battle, ArmyScale.Power);
                var outcome = candidate.Resolve(input, new SplitMix64Rng(battle.Seed));
                var winner = outcome.Winner == BattleSide.Attacker ? input.Attacker : input.Defender;
                var loser = outcome.Winner == BattleSide.Attacker ? input.Defender : input.Attacker;

                Assert.Equal(winner.Units.Count, outcome.WinnerAfter.Length);
                Assert.Equal(loser.Units.Count, outcome.LoserSurvivors.Length);
                for (var i = 0; i < winner.Units.Count; i++)
                {
                    Assert.InRange(outcome.WinnerAfter[i], 0, winner.Units[i].Troops);
                }

                for (var i = 0; i < loser.Units.Count; i++)
                {
                    Assert.InRange(outcome.LoserSurvivors[i], 0, loser.Units[i].Troops);
                    if (onDefeat == DefeatOutcome.Destroyed)
                    {
                        Assert.Equal(0, outcome.LoserSurvivors[i]); // §3: discarded under destroy
                    }
                }

                // §6.2: C2 has no survivors, by construction.
                if (candidate.Key == "C2")
                {
                    Assert.All(outcome.LoserSurvivors, t => Assert.Equal(0, t));
                }

                // C1 and C3 are one-shot comparisons; C2, C4, C5 never end "decided".
                Assert.Equal(candidate.Key is "C1" or "C3", outcome.Ending == CandidateEnding.Decided);
            }
        }
    }

    [Fact]
    public void The_counting_wrapper_refuses_to_fork_a_stream()
    {
        var counting = new DrawCountingRng(new SplitMix64Rng(1));
        Assert.Throws<NotSupportedException>(() => counting.ForStream("anything"));
    }

    private static ArmyState Army(string id, string nation, int x, int y, CandidateArmy army)
    {
        var units = army.Units
            .Select((u, i) => new UnitSlot(u.MercenaryLabel, u.UnitTypeId, u.Troops, u.Quality, "slot-" + i))
            .ToArray();
        return new ArmyState(id, nation, x, y, Moves: 5, army.Morale, Money: 0, SupplyTons: 0, CoveredTileCode: 2, AboardFleetId: null, ValueList.From(units));
    }

    private static int[] Troops(ArmyState? army, int slots)
    {
        var troops = new int[slots];
        if (army is null)
        {
            return troops;
        }

        foreach (var unit in army.Units)
        {
            troops[int.Parse(unit.Name["slot-".Length..], System.Globalization.CultureInfo.InvariantCulture)] = unit.Troops;
        }

        return troops;
    }
}
