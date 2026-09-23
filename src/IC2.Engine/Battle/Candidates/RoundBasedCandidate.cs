using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle.Candidates;

/// <summary>
/// C4 (§6.4): the EU4/CK shape. Rounds of fire (1–3, D30) then shock (D31), damage from the matrix and the
/// vulnerability weights against the enemy's mix, one army morale pool per side drained by casualties
/// (D32), an army-level break, and one pursuit round by the winner's cavalry (D33). No positions and no
/// unit-level break. Draws (variable): 2 per shooting unit per fire round, 2 per shock round, and 1 for
/// pursuit only when the winner has a live cavalry unit.
/// </summary>
/// <remarks>
/// Two readings this implementation had to make (recorded as findings): the pursuit round recomputes the
/// shares over the post-round live units (§6.4 "Shares … are recomputed over live units at the start of
/// every round", and the pursuit is one more round); and "distributed onto the loser as a shock round"
/// uses the shock round's own weights, <c>Xm_winner</c> over the winner's whole live mix.
/// </remarks>
public sealed class RoundBasedCandidate : IAutoResolveCandidate
{
    /// <inheritdoc />
    public string Key => "C4";

    /// <inheritdoc />
    public string Name => "round-based, EU4/CK lineage";

    /// <inheritdoc />
    public CandidateOutcome Resolve(CandidateBattle battle, IRng rng, bool recordEvents = false)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(rng);

        var tables = CandidateTables.For(battle.Ruleset);
        var rules = battle.Ruleset.Combat.DetailedResolver;
        var counting = new DrawCountingRng(rng);
        var events = recordEvents ? new List<CandidateEvent>() : null;
        long stated = 0;

        void Log(CandidateEventKind kind, int round, int side, int slot)
        {
            var e = new CandidateEvent(kind, round, side, slot);
            stated += e.StatedDraws;
            events?.Add(e);
        }

        var sides = new[]
        {
            TypeWeightedInstantCandidate.Side.From(battle.Attacker, tables),
            TypeWeightedInstantCandidate.Side.From(battle.Defender, tables),
        };
        var pool = new long[] { battle.Attacker.Morale, battle.Defender.Morale }; // P_S = M_S
        var start = new[] { Sum(sides[0].Troops), Sum(sides[1].Troops) };          // T0_S
        var shotsLeft = new int[2][];
        for (var s = 0; s < 2; s++)
        {
            shotsLeft[s] = new int[sides[s].Types.Length];
            for (var i = 0; i < shotsLeft[s].Length; i++)
            {
                shotsLeft[s][i] = tables.Shots[sides[s].Types[i]]; // K23
            }
        }

        var matrixDivisor = (long)rules.MeleePowerDivisor * CandidateConstants.SharePerMille;
        int winner = -1;
        var ending = CandidateEnding.Cap;
        var roundsFought = 0;

        for (var round = 1; round <= CandidateConstants.C4RoundCap; round++) // D33
        {
            roundsFought = round;
            var shares = new[]
            {
                tables.Shares1000(sides[0].Types, sides[0].Troops),
                tables.Shares1000(sides[1].Types, sides[1].Troops),
            };

            // Both sides' damage is computed from the start-of-round state, then applied together.
            var losses = new[] { new long[sides[0].Types.Length], new long[sides[1].Types.Length] };

            if (round <= CandidateConstants.C4FireRounds)
            {
                // D30: fire round.
                for (var s = 0; s < 2; s++)
                {
                    var o = 1 - s;
                    var vbar = tables.VulnerabilityBar1000(shares[o]);
                    long fire = 0;
                    var side = sides[s];
                    for (var i = 0; i < side.Types.Length; i++)
                    {
                        if (side.Troops[i] <= 0 || shotsLeft[s][i] <= 0)
                        {
                            continue;
                        }

                        var troops = side.Troops[i];

                        // base_i = troops × q × max(P, 0) × vbar1000_O / 1000 / (troops × 5 + 150000)   (K21, P for m)
                        var shotBase = troops * side.Quality[i] * Math.Max(pool[s], 0) * vbar / CandidateConstants.SharePerMille
                                       / ((troops * CandidateConstants.ShotTroopMultiplier) + CandidateConstants.ShotDenominatorConstant);
                        long draw = counting.NextInt(0, (int)(shotBase + 1));
                        draw += counting.NextInt(0, (int)(shotBase + 1));
                        Log(CandidateEventKind.FireVolley, round, s, i);
                        fire += Math.Min(troops / CandidateConstants.ShotShooterDivisor, draw);
                        shotsLeft[s][i]--;
                    }

                    // Each unit j of O loses F × troops_j × vuln[type_j] / Σ_k troops_k × vuln[type_k].
                    var target = sides[o];
                    long denominator = 0;
                    for (var j = 0; j < target.Types.Length; j++)
                    {
                        denominator += target.Troops[j] * tables.Vulnerability[target.Types[j]];
                    }

                    if (denominator > 0)
                    {
                        for (var j = 0; j < target.Types.Length; j++)
                        {
                            losses[o][j] += fire * target.Troops[j] * tables.Vulnerability[target.Types[j]] / denominator;
                        }
                    }
                }
            }
            else
            {
                // D31: shock round, one die per side, the attacker's first.
                var dice = new long[2];
                dice[0] = counting.NextInt(0, CandidateConstants.C4ShockDie);
                dice[1] = counting.NextInt(0, CandidateConstants.C4ShockDie);
                Log(CandidateEventKind.ShockRound, round, 0, -1);

                for (var s = 0; s < 2; s++)
                {
                    var o = 1 - s;
                    var hit = ShockSum(sides[s], shares[o], pool[s], tables, rules, matrixDivisor, cavalryOnly: false)
                              * (CandidateConstants.C4ShockDieBase + dice[s]) / CandidateConstants.C4ShockPace;
                    Distribute(hit, sides[o], shares[s], tables, losses[o]);
                }
            }

            // Apply both sides' losses (each clamped to the unit's troops); units at 0 are removed.
            var lost = new long[2];
            for (var s = 0; s < 2; s++)
            {
                for (var j = 0; j < sides[s].Troops.Length; j++)
                {
                    var applied = Math.Min(sides[s].Troops[j], losses[s][j]);
                    sides[s].Troops[j] -= applied;
                    lost[s] += applied;
                }

                pool[s] -= CandidateConstants.C4MoraleDamage * lost[s] / start[s]; // D32
            }

            var attackerEmpty = Sum(sides[0].Troops) == 0;
            var defenderEmpty = Sum(sides[1].Troops) == 0;
            if (attackerEmpty || defenderEmpty)
            {
                winner = attackerEmpty ? 1 : 0; // both at 0: the defender wins (K05)
                ending = CandidateEnding.Annihilation;
                break;
            }

            if (pool[0] <= 0 || pool[1] <= 0)
            {
                // The lower pool loses; a tie means the attacker loses (K05).
                winner = pool[1] < pool[0] ? 0 : 1;
                ending = CandidateEnding.Collapse;

                // D33 pursuit, only if the winner has a live cavalry unit (otherwise no draw, no loss).
                if (HasLiveCavalry(sides[winner], tables))
                {
                    var loserSide = 1 - winner;
                    var postShares = new[]
                    {
                        tables.Shares1000(sides[0].Types, sides[0].Troops),
                        tables.Shares1000(sides[1].Types, sides[1].Troops),
                    };
                    var cavalry = ShockSum(sides[winner], postShares[loserSide], pool[winner], tables, rules, matrixDivisor, cavalryOnly: true);
                    var die = counting.NextInt(0, CandidateConstants.C4ShockDie);
                    Log(CandidateEventKind.Pursuit, round, winner, -1);

                    // × 2 before / 60: one truncation, as §6.4 specifies.
                    var hit = cavalry * (CandidateConstants.C4ShockDieBase + die) * CandidateConstants.C4PursuitMultiplier
                              / CandidateConstants.C4ShockPace;
                    var pursuitLosses = new long[sides[loserSide].Types.Length];
                    Distribute(hit, sides[loserSide], postShares[winner], tables, pursuitLosses);
                    for (var j = 0; j < pursuitLosses.Length; j++)
                    {
                        sides[loserSide].Troops[j] -= Math.Min(sides[loserSide].Troops[j], pursuitLosses[j]);
                    }
                }

                break;
            }
        }

        if (winner < 0)
        {
            // After round 30: the lower pool loses (tie: the attacker loses); no pursuit.
            winner = pool[1] < pool[0] ? 0 : 1;
            ending = CandidateEnding.Cap;
        }

        var winnerAfter = ToInts(sides[winner].Troops);
        var survivors = battle.OnDefeat == DefeatOutcome.Scatter
            ? ToInts(sides[1 - winner].Troops)
            : new int[sides[1 - winner].Troops.Length];

        return new CandidateOutcome(
            winner == 0 ? BattleSide.Attacker : BattleSide.Defender,
            winnerAfter,
            survivors,
            ending,
            counting.Draws,
            counting.Draws,
            stated,
            CascadeBreak: false,
            roundsFought,
            (IReadOnlyList<CandidateEvent>?)events ?? Array.Empty<CandidateEvent>());
    }

    private static long ShockSum(
        TypeWeightedInstantCandidate.Side side,
        long[] enemyShares,
        long pool,
        CandidateTables tables,
        DetailedResolverRules rules,
        long matrixDivisor,
        bool cavalryOnly)
    {
        // a_i = mbar1000_i × troops_i × (q_i × 10 + max(P, 0)) / 2,000,000 + 12, over live units; summed first.
        long sum = 0;
        for (var i = 0; i < side.Types.Length; i++)
        {
            if (side.Troops[i] <= 0 || (cavalryOnly && !tables.IsCavalry[side.Types[i]]))
            {
                continue;
            }

            var mbar = tables.MeleeBar1000(side.Types[i], enemyShares);
            sum += (mbar * side.Troops[i] * ((side.Quality[i] * CandidateConstants.QualityWeight) + Math.Max(pool, 0)) / matrixDivisor)
                   + rules.MeleeBasePowerFloor;
        }

        return sum;
    }

    private static void Distribute(long hit, TypeWeightedInstantCandidate.Side target, long[] dealerShares, CandidateTables tables, long[] into)
    {
        // Each unit j loses H × troops_j × Xm_S(type_j) / Σ_k troops_k × Xm_S(type_k)   (D20)
        var exposure = new long[target.Types.Length];
        long denominator = 0;
        for (var j = 0; j < target.Types.Length; j++)
        {
            exposure[j] = tables.MeleeExposure1000(target.Types[j], dealerShares);
            denominator += target.Troops[j] * exposure[j];
        }

        if (denominator == 0)
        {
            return;
        }

        for (var j = 0; j < target.Types.Length; j++)
        {
            into[j] += hit * target.Troops[j] * exposure[j] / denominator;
        }
    }

    private static bool HasLiveCavalry(TypeWeightedInstantCandidate.Side side, CandidateTables tables)
    {
        for (var i = 0; i < side.Types.Length; i++)
        {
            if (side.Troops[i] > 0 && tables.IsCavalry[side.Types[i]])
            {
                return true;
            }
        }

        return false;
    }

    private static long Sum(long[] values)
    {
        long total = 0;
        foreach (var v in values)
        {
            total += v;
        }

        return total;
    }

    private static int[] ToInts(long[] values)
    {
        var result = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = (int)values[i];
        }

        return result;
    }
}
