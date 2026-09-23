using IC2.Engine.Battle;
using IC2.Engine.Battle.Candidates;
using IC2.Engine.Battle.Candidates.Tournament;
using IC2.Engine.Core;
using IC2.Engine.Strength;
using Xunit;

namespace IC2.Engine.Tests.Battle.Candidates;

/// <summary>
/// T59 Done-when 3 (the tournament is deterministic: a fixed seed set reproduces the whole scorecard byte for
/// byte), §8.0's test armies, and the §6/§8 results that follow from a candidate's own arithmetic ("fixed by
/// construction") — asserted here so that the scorecard never presents them as discoveries.
/// </summary>
public class TournamentTests
{
    private const int ReducedSeeds = 2;

    [Fact]
    public void The_same_seed_set_reproduces_the_whole_scorecard_byte_for_byte()
    {
        var first = Scorecards(reverse: false);
        var second = Scorecards(reverse: true); // a different play order, as a parallel run would have

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], second[i]);
        }
    }

    [Fact]
    public void A_battle_replayed_on_a_fresh_generator_gives_the_same_canonical_record()
    {
        var harness = CandidateTestbed.Harness();
        var schedule = TournamentHarness.CompositionSchedule();
        foreach (var candidate in CandidateTestbed.All())
        {
            for (var i = 0; i < 20; i++)
            {
                var input = harness.Input(schedule[i], ArmyScale.Power);
                var a = TournamentHarness.CanonicalJson(candidate.Resolve(input, new SplitMix64Rng(schedule[i].Seed)));
                var b = TournamentHarness.CanonicalJson(candidate.Resolve(input, new SplitMix64Rng(schedule[i].Seed)));
                Assert.Equal(a, b);
            }
        }
    }

    [Fact]
    public void Section_8_0_builds_21_compositions_of_at_most_18_units_with_P_scale_armies_equal_in_the_baselines_measure()
    {
        var harness = CandidateTestbed.Harness();
        var ruleset = CandidateTestbed.Ruleset;
        Assert.Equal(21, TestArmies.Compositions.Count);
        foreach (var composition in TestArmies.Compositions)
        {
            Assert.Equal(100, composition.Shares.Sum());
            foreach (var scale in new[] { ArmyScale.Troops, ArmyScale.Power })
            {
                var army = harness.Army(composition.Index, scale, 100);
                Assert.InRange(army.Units.Count, 1, 18);
                Assert.All(army.Units, u => Assert.Equal(TestArmies.Quality, u.Quality));
                Assert.Equal(TestArmies.Morale, army.Morale);
            }

            // Every P-scale army has a merged armyPower of exactly 300 × M (§8.0).
            var power = harness.Army(composition.Index, ArmyScale.Power, 100);
            var slots = power.Units.Select((u, i) => new IC2.Engine.Model.UnitSlot(0, u.UnitTypeId, u.Troops, u.Quality, "u" + i));
            Assert.Equal(300 * TestArmies.Morale, ArmyPower.Compute(slots, power.Morale, ruleset));
        }

        // §8.0's one merged remainder: the uniform army at κ = 0.90 has light cavalry 7,200 = 7,000 + 200, and
        // 200 is below LC's floor of 280, so it is one 7,200-troop unit.
        var uniform90 = harness.Army(15, ArmyScale.Troops, 90);
        var lightCavalry = uniform90.Units.Where(u => u.UnitTypeId == "light_cavalry").Select(u => u.Troops).ToArray();
        Assert.Equal(new[] { 7_200 }, lightCavalry);
    }

    [Fact]
    public void C1_ties_every_power_matched_battle_so_the_defender_always_wins_and_CS_P_is_exactly_zero()
    {
        var harness = CandidateTestbed.Harness();
        var c1 = CandidateTestbed.C1();
        var schedule = TournamentHarness.CompositionSchedule(ReducedSeeds);
        var records = schedule.Select(b => harness.Play(c1, b, ArmyScale.Power)).ToList();
        Assert.All(records, r => Assert.False(r.AttackerWon));

        var card = CandidateScorecard.Compute("C1", "baseline", records, records, Array.Empty<BattleRecord>(), ReducedSeeds, false, false, true);
        Assert.Equal("0.0000", card.Clauses.First(c => c.Id == "CS-P").Value);
    }

    [Fact]
    public void C1_and_C3_decide_the_winner_without_a_draw_so_their_upset_rate_is_exactly_zero()
    {
        var harness = CandidateTestbed.Harness();
        foreach (var candidate in new IAutoResolveCandidate[] { CandidateTestbed.C1(), new TypeWeightedInstantCandidate() })
        {
            foreach (var battle in TournamentHarness.UpsetSchedule(ReducedSeeds))
            {
                var record = harness.Play(candidate, battle, ArmyScale.Troops);
                var weakerIsAttacker = battle.AttackerKappaPercent != 100;
                Assert.NotEqual(weakerIsAttacker, record.AttackerWon);
            }
        }
    }

    private static List<string> Scorecards(bool reverse)
    {
        var harness = CandidateTestbed.Harness();
        var composition = TournamentHarness.CompositionSchedule(ReducedSeeds);
        var upset = TournamentHarness.UpsetSchedule(ReducedSeeds);
        var cards = new List<string>();
        foreach (var candidate in CandidateTestbed.All())
        {
            var power = Play(harness, candidate, composition, ArmyScale.Power, reverse);
            var troops = Play(harness, candidate, composition, ArmyScale.Troops, reverse);
            var ur = Play(harness, candidate, upset, ArmyScale.Troops, reverse);
            var card = CandidateScorecard.Compute(
                candidate.Key, candidate.Name, troops, power, ur, ReducedSeeds,
                enApplies: candidate.Key is "C2" or "C4" or "C5",
                cascadeApplies: candidate.Key is "C2" or "C5",
                svApplies: true);
            cards.Add(card.ToJson());
        }

        return cards;
    }

    private static BattleRecord[] Play(TournamentHarness harness, IAutoResolveCandidate candidate, IReadOnlyList<ScheduledBattle> schedule, ArmyScale scale, bool reverse)
    {
        var records = new BattleRecord[schedule.Count];
        for (var n = 0; n < schedule.Count; n++)
        {
            var i = reverse ? schedule.Count - 1 - n : n;
            records[i] = harness.Play(candidate, schedule[i], scale);
        }

        return records;
    }
}
