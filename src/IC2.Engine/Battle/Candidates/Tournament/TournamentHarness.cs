using System.Globalization;
using System.Text;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Candidates.Tournament;

/// <summary>One battle of a schedule: which armies, which seats, which seed.</summary>
/// <param name="AttackerComposition">Index into <see cref="TestArmies.Compositions"/>.</param>
/// <param name="DefenderComposition">Index into <see cref="TestArmies.Compositions"/>.</param>
/// <param name="Repetition">The repetition <c>k</c>.</param>
/// <param name="Seed">The seed passed to a fresh <see cref="IRng"/>.</param>
/// <param name="AttackerKappaPercent">§8.3's size factor for the attacker (100 = full).</param>
/// <param name="DefenderKappaPercent">§8.3's size factor for the defender (100 = full).</param>
public readonly record struct ScheduledBattle(
    int AttackerComposition,
    int DefenderComposition,
    int Repetition,
    ulong Seed,
    int AttackerKappaPercent = 100,
    int DefenderKappaPercent = 100);

/// <summary>What the metrics read from one battle, reduced to per-type troop sums.</summary>
public sealed record BattleRecord(
    ScheduledBattle Battle,
    bool AttackerWon,
    CandidateEnding Ending,
    bool CascadeBreak,
    long[] WinnerStart,
    long[] WinnerEnd,
    long[] LoserStart,
    long[] LoserSurvivors,
    long BattleDraws,
    long TotalDraws,
    long StatedDraws,
    int Rounds);

/// <summary>
/// Builds §8.0's schedules and plays single battles. Stateless and deterministic: a battle's record depends
/// only on the candidate, the ruleset and the scheduled battle, never on the order battles are played in, so
/// a caller may play a schedule in parallel and still get the same records.
/// </summary>
public sealed class TournamentHarness
{
    private static readonly string[] ExpectedTypeOrder =
    {
        "light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry",
    };

    private readonly CandidateArmy[] _troopScale;
    private readonly CandidateArmy[] _powerScale;
    private readonly CandidateArmy?[,] _kappaArmies;

    /// <summary>Creates the harness for a ruleset and <c>onDefeat</c>.</summary>
    public TournamentHarness(Ruleset ruleset, DefeatOutcome onDefeat)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        Ruleset = ruleset;
        OnDefeat = onDefeat;
        Tables = CandidateTables.For(ruleset);
        if (!Tables.TypeIds.SequenceEqual(ExpectedTypeOrder, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "§8.0's compositions are defined over LI, HI, A, LC, HC in that order; this ruleset's typeEffectivenessOrder differs.",
                nameof(ruleset));
        }

        var compositions = TestArmies.Compositions;
        _troopScale = new CandidateArmy[compositions.Count];
        _powerScale = new CandidateArmy[compositions.Count];
        for (var i = 0; i < compositions.Count; i++)
        {
            _troopScale[i] = TestArmies.Build(compositions[i], ArmyScale.Troops, Tables);
            _powerScale[i] = TestArmies.Build(compositions[i], ArmyScale.Power, Tables);
        }

        _kappaArmies = new CandidateArmy?[compositions.Count, TestArmies.UpsetKappaPercent.Count];
        foreach (var composition in TestArmies.UpsetCompositions)
        {
            for (var k = 0; k < TestArmies.UpsetKappaPercent.Count; k++)
            {
                _kappaArmies[composition, k] = TestArmies.Build(
                    compositions[composition], ArmyScale.Troops, Tables, TestArmies.UpsetKappaPercent[k]);
            }
        }
    }

    /// <summary>The ruleset every battle runs under.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>The <c>onDefeat</c> every battle runs under.</summary>
    public DefeatOutcome OnDefeat { get; }

    /// <summary>The ruleset's tables.</summary>
    public CandidateTables Tables { get; }

    /// <summary>§8.1's schedule: every ordered pair of Σ, mirrors included, 200 seeds each, in order.</summary>
    /// <param name="seedsPerMatchup">200 for the tournament; a test may pass fewer.</param>
    public static IReadOnlyList<ScheduledBattle> CompositionSchedule(int seedsPerMatchup = TestArmies.SeedsPerMatchup)
    {
        var n = TestArmies.Compositions.Count;
        var list = new List<ScheduledBattle>(n * n * seedsPerMatchup);
        for (var a = 0; a < n; a++)
        {
            for (var b = 0; b < n; b++)
            {
                var matchup = TestArmies.MatchupIndex(a, b);
                for (var k = 0; k < seedsPerMatchup; k++)
                {
                    list.Add(new ScheduledBattle(a, b, k, TestArmies.Seed(matchup, k)));
                }
            }
        }

        return list;
    }

    /// <summary>
    /// §8.3's schedule: each mirror composition, each <c>κ</c>, the weaker side attacking and then defending,
    /// 200 seeds each. The seed is §8.0's formula on the mirror's own matchup index (§8.0 says its seeds are
    /// shared by every metric; §8.3 names no other).
    /// </summary>
    public static IReadOnlyList<ScheduledBattle> UpsetSchedule(int seedsPerMatchup = TestArmies.SeedsPerMatchup)
    {
        var list = new List<ScheduledBattle>();
        foreach (var kappa in TestArmies.UpsetKappaPercent)
        {
            foreach (var c in TestArmies.UpsetCompositions)
            {
                var matchup = TestArmies.MatchupIndex(c, c);
                for (var k = 0; k < seedsPerMatchup; k++)
                {
                    list.Add(new ScheduledBattle(c, c, k, TestArmies.Seed(matchup, k), kappa, 100));
                }

                for (var k = 0; k < seedsPerMatchup; k++)
                {
                    list.Add(new ScheduledBattle(c, c, k, TestArmies.Seed(matchup, k), 100, kappa));
                }
            }
        }

        return list;
    }

    /// <summary>The army a scheduled battle's attacker or defender fields.</summary>
    public CandidateArmy Army(int composition, ArmyScale scale, int kappaPercent)
    {
        if (kappaPercent != 100)
        {
            for (var k = 0; k < TestArmies.UpsetKappaPercent.Count; k++)
            {
                if (TestArmies.UpsetKappaPercent[k] == kappaPercent)
                {
                    return _kappaArmies[composition, k]
                           ?? throw new ArgumentOutOfRangeException(nameof(composition), composition, "Not a §8.3 mirror composition.");
                }
            }

            throw new ArgumentOutOfRangeException(nameof(kappaPercent), kappaPercent, "Not one of §8.3's κ.");
        }

        return scale == ArmyScale.Troops ? _troopScale[composition] : _powerScale[composition];
    }

    /// <summary>The input record for a scheduled battle.</summary>
    public CandidateBattle Input(ScheduledBattle battle, ArmyScale scale) =>
        new(
            Army(battle.AttackerComposition, scale, battle.AttackerKappaPercent),
            Army(battle.DefenderComposition, scale, battle.DefenderKappaPercent),
            Ruleset,
            OnDefeat);

    /// <summary>Plays one scheduled battle on a fresh <see cref="SplitMix64Rng"/> on its seed.</summary>
    public BattleRecord Play(IAutoResolveCandidate candidate, ScheduledBattle battle, ArmyScale scale)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var input = Input(battle, scale);
        var outcome = candidate.Resolve(input, new SplitMix64Rng(battle.Seed));
        return Record(battle, input, outcome);
    }

    /// <summary>Reduces an outcome to a record.</summary>
    public BattleRecord Record(ScheduledBattle battle, CandidateBattle input, CandidateOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(outcome);
        var attackerWon = outcome.Winner == BattleSide.Attacker;
        var winner = attackerWon ? input.Attacker : input.Defender;
        var loser = attackerWon ? input.Defender : input.Attacker;
        return new BattleRecord(
            battle,
            attackerWon,
            outcome.Ending,
            outcome.CascadeBreak,
            TestArmies.StartByType(winner, Tables),
            TestArmies.ByType(winner, outcome.WinnerAfter, Tables),
            TestArmies.StartByType(loser, Tables),
            TestArmies.ByType(loser, outcome.LoserSurvivors, Tables),
            outcome.BattleDraws,
            outcome.TotalDraws,
            outcome.StatedDraws,
            outcome.Rounds);
    }

    /// <summary>
    /// §8.5's canonical JSON of an output record (§3: <c>winner</c>, <c>after</c>, <c>survivors</c>,
    /// <c>ending</c>, <c>draws</c>), keys sorted, no whitespace.
    /// </summary>
    public static string CanonicalJson(CandidateOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var sb = new StringBuilder();
        sb.Append("{\"after\":[");
        AppendInts(sb, outcome.WinnerAfter);
        sb.Append("],\"draws\":");
        sb.Append(outcome.TotalDraws.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"ending\":\"");
        sb.Append(EndingName(outcome.Ending));
        sb.Append("\",\"survivors\":[");
        AppendInts(sb, outcome.LoserSurvivors);
        sb.Append("],\"winner\":\"");
        sb.Append(outcome.Winner == BattleSide.Attacker ? "attacker" : "defender");
        sb.Append("\"}");
        return sb.ToString();
    }

    /// <summary>The lower-case ending name used in every output.</summary>
    public static string EndingName(CandidateEnding ending) => ending switch
    {
        CandidateEnding.Decided => "decided",
        CandidateEnding.Annihilation => "annihilation",
        CandidateEnding.Collapse => "collapse",
        CandidateEnding.Withdrawal => "withdrawal",
        CandidateEnding.Cap => "cap",
        _ => throw new ArgumentOutOfRangeException(nameof(ending)),
    };

    private static void AppendInts(StringBuilder sb, int[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }
    }
}
