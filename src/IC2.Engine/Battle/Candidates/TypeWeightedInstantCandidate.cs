using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Candidates;

/// <summary>
/// C3 (§6.3): C1's one-shot shape, with <c>armyPower</c> replaced by an effective power that reads the
/// matrix against the enemy's actual mix (D22), and the winner's casualties distributed by each type's
/// exposure to that mix (D20, D21). Unit-level break: the K08 strength floor only. No rounds, no tactical
/// morale. Draws: <c>nW</c>, plus <c>nL</c> under <c>scatter</c> — fixed per pair of armies.
/// </summary>
public sealed class TypeWeightedInstantCandidate : IAutoResolveCandidate
{
    /// <inheritdoc />
    public string Key => "C3";

    /// <inheritdoc />
    public string Name => "type-weighted instant resolver";

    /// <inheritdoc />
    public CandidateOutcome Resolve(CandidateBattle battle, IRng rng, bool recordEvents = false)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(rng);

        var tables = CandidateTables.For(battle.Ruleset);
        var combat = battle.Ruleset.Combat;
        var counting = new DrawCountingRng(rng);
        var events = recordEvents ? new List<CandidateEvent>() : null;

        var a = Side.From(battle.Attacker, tables);
        var d = Side.From(battle.Defender, tables);
        var sharesA = tables.Shares1000(a.Types, a.Troops);
        var sharesD = tables.Shares1000(d.Types, d.Troops);

        var powerA = EffectivePower(a, sharesD, tables, combat.DetailedResolver);
        var powerD = EffectivePower(d, sharesA, tables, combat.DetailedResolver);

        // K05: a strict less-than, ties to the defender.
        var attackerWon = powerD < powerA;
        var winner = attackerWon ? a : d;
        var loser = attackerWon ? d : a;
        var winnerPower = attackerWon ? powerA : powerD;
        var loserPower = attackerWon ? powerD : powerA;
        var winnerShares = attackerWon ? sharesA : sharesD;
        var loserShares = attackerWon ? sharesD : sharesA;

        // K03: R = E_loser × 40 / E_winner, so R ≤ 40.
        var ratio = loserPower * combat.WinnerCasualtyNumerator / winnerPower;
        var winnerAfter = Casualties(winner, ratio, loserShares, tables, combat, counting, events, attackerWon ? 0 : 1);

        var survivors = new int[loser.Troops.Length];
        if (battle.OnDefeat == DefeatOutcome.Scatter)
        {
            // K06's mirrored numerator, each loser unit's W against the winner, the same expression and floor.
            var mirrored = winnerPower * combat.ScatteredDefeat.SurvivorCasualtyNumerator / loserPower;
            survivors = Casualties(loser, mirrored, winnerShares, tables, combat, counting, events, attackerWon ? 1 : 0);
        }

        long stated = winner.Troops.Length + (battle.OnDefeat == DefeatOutcome.Scatter ? loser.Troops.Length : 0);

        return new CandidateOutcome(
            attackerWon ? BattleSide.Attacker : BattleSide.Defender,
            winnerAfter,
            survivors,
            CandidateEnding.Decided,
            counting.Draws,
            counting.Draws,
            stated,
            CascadeBreak: false,
            Rounds: 0,
            (IReadOnlyList<CandidateEvent>?)events ?? Array.Empty<CandidateEvent>());
    }

    /// <summary>§6.3's <c>E_S = Σ_i (Mel_i + Fire_i)</c> (D22, λ = 1), against the enemy's shares.</summary>
    internal static long EffectivePower(Side side, long[] enemyShares, CandidateTables tables, DetailedResolverRules rules)
    {
        var vbar = tables.VulnerabilityBar1000(enemyShares);
        var matrixDivisor = (long)rules.MeleePowerDivisor * CandidateConstants.SharePerMille; // 2,000,000
        long total = 0;
        for (var i = 0; i < side.Types.Length; i++)
        {
            var type = side.Types[i];
            var troops = side.Troops[i];
            var mbar = tables.MeleeBar1000(type, enemyShares);

            // Mel_i = mbar1000_i × troops_i × (q_i × 10 + M_S) / 2,000,000 + 12   (K16's shape, K18 with M)
            var mel = (mbar * troops * ((side.Quality[i] * CandidateConstants.QualityWeight) + side.Morale) / matrixDivisor)
                      + rules.MeleeBasePowerFloor;

            // Fire_i = shots × (troops × q × M × vbar1000_O / 1000) / (troops × 5 + 150000)   (K21 un-doubled, × K23)
            var fire = tables.Shots[type]
                       * (troops * side.Quality[i] * side.Morale * vbar / CandidateConstants.SharePerMille)
                       / ((troops * CandidateConstants.ShotTroopMultiplier) + CandidateConstants.ShotDenominatorConstant);

            total += mel + fire;
        }

        return total;
    }

    private static int[] Casualties(
        Side side,
        long ratio,
        long[] enemyShares,
        CandidateTables tables,
        CombatRules combat,
        IRng rng,
        List<CandidateEvent>? events,
        int sideIndex)
    {
        // W_i = Xm_E(type_i) + Xf_E(type_i) (D20, D21), ×1000; Wbar = Σ troops × W / Σ troops.
        var w = new long[side.Types.Length];
        long weighted = 0;
        long troopsTotal = 0;
        for (var i = 0; i < w.Length; i++)
        {
            w[i] = tables.MeleeExposure1000(side.Types[i], enemyShares) + tables.FireExposure1000(side.Types[i], enemyShares);
            weighted += side.Troops[i] * w[i];
            troopsTotal += side.Troops[i];
        }

        var wbar = weighted / troopsTotal;
        var after = new int[w.Length];
        for (var i = 0; i < w.Length; i++)
        {
            var troops = side.Troops[i];

            // K04's divisor draw, the merged BattleCasualties form: Random(15) + 105.
            var divisor = rng.NextInt(combat.CasualtyDivisorRandomSpan) + combat.CasualtyDivisorBase;
            events?.Add(new CandidateEvent(CandidateEventKind.CasualtyDivisor, 0, sideIndex, i));

            // K04 order: divide first. loss = min(troops, (troops / div) × R × W_i / Wbar)
            var loss = Math.Min(troops, (troops / divisor) * ratio * w[i] / wbar);
            troops -= loss;
            if (troops < tables.RoutFloor[side.Types[i]])
            {
                troops = 0; // K08, the strength floor only
            }

            after[i] = (int)troops;
        }

        return after;
    }

    internal sealed class Side
    {
        private Side(int[] types, long[] troops, int[] quality, int morale)
        {
            Types = types;
            Troops = troops;
            Quality = quality;
            Morale = morale;
        }

        public int[] Types { get; }

        public long[] Troops { get; }

        public int[] Quality { get; }

        public int Morale { get; }

        public static Side From(CandidateArmy army, CandidateTables tables)
        {
            var n = army.Units.Count;
            var types = new int[n];
            var troops = new long[n];
            var quality = new int[n];
            for (var i = 0; i < n; i++)
            {
                types[i] = tables.IndexOf(army.Units[i].UnitTypeId);
                troops[i] = army.Units[i].Troops;
                quality[i] = army.Units[i].Quality;
            }

            return new Side(types, troops, quality, army.Morale);
        }
    }
}
