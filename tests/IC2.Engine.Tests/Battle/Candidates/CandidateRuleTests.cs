using IC2.Engine.Battle;
using IC2.Engine.Battle.Candidates;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Battle.Candidates;

/// <summary>
/// Each test visits ONE candidate rule directly, with constructed inputs rather than the tournament, so that
/// changing the rule fails a test (T59 review, B1): the one-level cascade (§5, K12–K14), both bounds of the
/// initial-morale clamp (D08), K27's +3 to both sides (the survey's C2/C5 placeholder), C4's round cap (D33),
/// and C3's strength floor (K08).
/// </summary>
public class CandidateRuleTests
{
    private const int Attacker = 0;
    private const int Defender = 1;

    [Theory]
    [InlineData(false)] // C2: the removed units go to troops 0
    [InlineData(true)]  // C5: the removed units flee
    public void The_cascade_removes_exactly_one_level_and_only_the_routed_unit_earns_the_enemy_its_plus_5(bool retreat)
    {
        var probe = TacticalProbe.AfterSetup(Battle(Army(59, Unit("light_infantry", 15_000), Unit("light_infantry", 15_000), Unit("light_infantry", 15_000)),
                                                    Army(59, Unit("light_infantry", 15_000), Unit("light_infantry", 15_000))), new FixedRng(0), retreat);
        probe.SetMorale(Attacker, 0, 10); // routs through the morale floor (K09): m ≤ 19, no draw
        probe.SetMorale(Attacker, 1, 32); // 32 − 6 = 26 < 30: removed by the cascade (K13)
        probe.SetMorale(Attacker, 2, 50); // 50 − 6 = 44: survives
        probe.SetMorale(Defender, 0, 70);
        probe.SetMorale(Defender, 1, 97);

        probe.RoutCheck(Attacker, 0);

        Assert.False(probe.IsLive(Attacker, 0));
        Assert.Equal(BreakCause.MoraleFloor, probe.Cause(Attacker, 0));
        Assert.False(probe.IsLive(Attacker, 1));
        Assert.Equal(BreakCause.Cascade, probe.Cause(Attacker, 1));

        // ONE level: the friend the cascade removed starts no cascade of its own, so the survivor lost 6 once
        // (a recursive cascade would take it to 38), and it earns the enemy no +5 (a second +5 would give 80).
        Assert.True(probe.IsLive(Attacker, 2));
        Assert.Equal(44, probe.Morale(Attacker, 2));
        Assert.Equal(75, probe.Morale(Defender, 0));
        Assert.Equal(99, probe.Morale(Defender, 1)); // capped at 99 (K14)
        Assert.Equal(2, probe.Events.Count(e => e.Kind == CandidateEventKind.Break));
        Assert.False(probe.Ended);

        if (retreat)
        {
            // C5: both flee and pay the 5% disorder cost (D40); the pursuing shots all draw 0 on this generator.
            Assert.True(probe.HasFled(Attacker, 0));
            Assert.True(probe.HasFled(Attacker, 1));
            Assert.Equal(14_250, probe.Troops(Attacker, 0));
            Assert.Equal(14_250, probe.Troops(Attacker, 1));
        }
        else
        {
            Assert.Equal(0, probe.Troops(Attacker, 0));
            Assert.Equal(0, probe.Troops(Attacker, 1));
        }
    }

    [Theory]
    [InlineData(10, 0, 60)]   // 0 + 10 + 3 = 13: the lower bound binds
    [InlineData(10, 35, 60)]  // the largest q = 9 draw, 35 + 13 = 48: still 60
    [InlineData(100, 0, 90)]  // 103: the upper bound binds
    [InlineData(80, 35, 90)]  // 118: the upper bound binds
    [InlineData(60, 10, 73)]  // 10 + 63 = 73: inside [60, 90], neither bound binds
    public void The_initial_tactical_morale_is_clamped_to_60_and_90(int strategicMorale, int draw, int expected)
    {
        var probe = TacticalProbe.AfterSetup(
            Battle(Army(strategicMorale, Unit("heavy_infantry", 6_000, quality: 9)), Army(strategicMorale, Unit("heavy_infantry", 6_000, quality: 9))),
            new FixedRng(draw),
            retreat: false);

        Assert.Equal(expected, probe.Morale(Attacker, 0));
        Assert.Equal(expected, probe.Morale(Defender, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void K27s_placeholder_adds_3_to_BOTH_sides_before_seeding_m(bool retreat)
    {
        // The survey's C2/C5 placeholder: +3 to both sides. (The original gives it to a computer side only.)
        var probe = TacticalProbe.AfterSetup(
            Battle(Army(65, Unit("light_cavalry", 7_000)), Army(70, Unit("light_cavalry", 7_000))), new FixedRng(5), retreat);

        Assert.Equal(5 + 65 + 3, probe.Morale(Attacker, 0));
        Assert.Equal(5 + 70 + 3, probe.Morale(Defender, 0));
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void C4_stops_at_round_30_exactly(ulong seed)
    {
        // Light infantry and archers score 0 against each other in the matrix (K15), so after three fire rounds
        // the shock rounds deal only the +12 floor and neither pool can break: the battle runs to the cap.
        var battle = Battle(Army(70, Unit("light_infantry", 15_000)), Army(70, Unit("archers", 15_000)));
        var outcome = new RoundBasedCandidate().Resolve(battle, new SplitMix64Rng(seed), recordEvents: true);

        Assert.Equal(CandidateEnding.Cap, outcome.Ending);
        Assert.Equal(30, outcome.Rounds);
        Assert.Equal(27, outcome.Events.Count(e => e.Kind == CandidateEventKind.ShockRound)); // rounds 4 … 30
        Assert.Equal(30, outcome.Events.Max(e => e.Round));
    }

    [Fact]
    public void C3_zeroes_a_winner_unit_left_below_its_strength_floor()
    {
        // The light-infantry unit starts 20 above its floor of 600 (K08). Its loss is at most
        // troops / 105 × 40 × W / Wbar — far less than 620 — so only the floor can take it to 0.
        var battle = Battle(
            Army(59, Unit("heavy_infantry", 6_000), Unit("light_infantry", 620)),
            Army(59, Unit("heavy_infantry", 5_000)));

        for (ulong seed = 0; seed < 5; seed++)
        {
            var outcome = new TypeWeightedInstantCandidate().Resolve(battle, new SplitMix64Rng(seed));
            Assert.Equal(BattleSide.Attacker, outcome.Winner);
            Assert.InRange(outcome.WinnerAfter[0], 1, 5_999);
            Assert.Equal(0, outcome.WinnerAfter[1]);
        }
    }

    private static CandidateBattle Battle(CandidateArmy attacker, CandidateArmy defender) =>
        new(attacker, defender, CandidateTestbed.Ruleset, DefeatOutcome.Scatter);

    private static CandidateArmy Army(int morale, params CandidateUnit[] units) => new(ValueList.From(units), morale);

    private static CandidateUnit Unit(string type, int troops, int quality = 6) => new(type, troops, quality);

    /// <summary>A generator whose every <c>NextInt</c> returns one fixed value (clamped into range), for exact expectations.</summary>
    private sealed class FixedRng : IRng
    {
        private readonly int _value;

        public FixedRng(int value) => _value = value;

        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64() => (ulong)_value;

        public int NextInt(int exclusiveUpperBound) => Math.Min(_value, exclusiveUpperBound - 1);

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) =>
            Math.Max(inclusiveLowerBound, Math.Min(_value, exclusiveUpperBound - 1));

        public bool NextChance(int numerator, int denominator) => false;

        public IRng ForStream(string streamName) => this;
    }
}
