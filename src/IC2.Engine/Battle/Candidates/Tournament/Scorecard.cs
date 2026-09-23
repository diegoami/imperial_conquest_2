using System.Globalization;
using System.Text;

namespace IC2.Engine.Battle.Candidates.Tournament;

/// <summary>One metric clause: the measured number and whether it lies in its pre-set band (§8).</summary>
/// <param name="Id">The clause id, e.g. <c>CS-T</c>, <c>UR(0.90)</c>, <c>SV-c</c>.</param>
/// <param name="Value">The measured number, formatted.</param>
/// <param name="Band">The band, as §8.9 states it.</param>
/// <param name="Applicable">False where §8 says the clause does not apply (recorded, not passed).</param>
/// <param name="Pass">Whether the value lies in the band (meaningless when not applicable).</param>
public sealed record MetricClause(string Id, string Value, string Band, bool Applicable, bool Pass)
{
    /// <summary>"pass", "FAIL" or "n/a".</summary>
    public string Verdict => !Applicable ? "n/a" : Pass ? "pass" : "FAIL";
}

/// <summary>
/// Every metric of §8.1–§8.4, §8.7 and §8.8 for one candidate, computed exactly as §8 defines it from the
/// battle records. DET (§8.5) and COST (§8.6) need separate processes and a wall clock, so the tool measures
/// those and adds them; everything here is a pure function of the records and therefore byte-reproducible.
/// </summary>
public sealed class CandidateScorecard
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private CandidateScorecard(string key, string name)
    {
        Key = key;
        Name = name;
    }

    /// <summary>C1–C5.</summary>
    public string Key { get; }

    /// <summary>The candidate's neutral name.</summary>
    public string Name { get; }

    /// <summary>Every clause, in §8's order.</summary>
    public List<MetricClause> Clauses { get; } = new();

    /// <summary>§8.1's <c>s(A)</c> per composition, T-scale.</summary>
    public double[] ScoreT { get; private set; } = Array.Empty<double>();

    /// <summary>§8.1's <c>s(A)</c> per composition, P-scale.</summary>
    public double[] ScoreP { get; private set; } = Array.Empty<double>();

    /// <summary>§8.2: the directed edges <c>A ▷ B</c> on the P-scale.</summary>
    public int Edges { get; private set; }

    /// <summary>§8.2: the number of directed 3-cycles.</summary>
    public int Cycles { get; private set; }

    /// <summary>§8.2: the dominant compositions, by label.</summary>
    public List<string> Dominant { get; } = new();

    /// <summary>§8.4 CD-b: per opponent, the clear-top type's label, or "none"/"no data".</summary>
    public string[] ClearTops { get; private set; } = Array.Empty<string>();

    /// <summary>§8.7: the fraction of P-scale battles per ending name.</summary>
    public SortedList<string, double> EndingFractions { get; } = new(StringComparer.Ordinal);

    /// <summary>§8.8's diagnostic: mean <c>σ</c> by ending, and the battle count behind each.</summary>
    public SortedList<string, (double MeanSigma, int Battles)> SigmaByEnding { get; } = new(StringComparer.Ordinal);

    /// <summary>Diagnostics with no band: draws, rounds, the draw-formula check over every battle.</summary>
    public SortedList<string, string> Diagnostics { get; } = new(StringComparer.Ordinal);

    /// <summary>Computes the scorecard.</summary>
    /// <param name="key">C1–C5.</param>
    /// <param name="name">The candidate's name.</param>
    /// <param name="troopScale">The CS-T schedule's records, in schedule order.</param>
    /// <param name="powerScale">The CS-P schedule's records, in schedule order.</param>
    /// <param name="upset">The UR schedule's records, in schedule order.</param>
    /// <param name="seedsPerMatchup">Seeds per matchup (200 in the tournament).</param>
    /// <param name="enApplies">§8.7 applies (C2, C4, C5).</param>
    /// <param name="cascadeApplies">§8.7 EN-c applies (C2, C5).</param>
    /// <param name="svApplies">§8.8 applies (every candidate under <c>scatter</c>).</param>
    public static CandidateScorecard Compute(
        string key,
        string name,
        IReadOnlyList<BattleRecord> troopScale,
        IReadOnlyList<BattleRecord> powerScale,
        IReadOnlyList<BattleRecord> upset,
        int seedsPerMatchup,
        bool enApplies,
        bool cascadeApplies,
        bool svApplies)
    {
        ArgumentNullException.ThrowIfNull(troopScale);
        ArgumentNullException.ThrowIfNull(powerScale);
        ArgumentNullException.ThrowIfNull(upset);

        var card = new CandidateScorecard(key, name);
        var n = TestArmies.Compositions.Count;

        // §8.1 CS-T and CS-P.
        var pT = PairProbabilities(troopScale, n, seedsPerMatchup);
        var pP = PairProbabilities(powerScale, n, seedsPerMatchup);
        card.ScoreT = Scores(pT, n);
        card.ScoreP = Scores(pP, n);
        var csT = card.ScoreT.Max() - card.ScoreT.Min();
        var csP = card.ScoreP.Max() - card.ScoreP.Min();
        card.Clauses.Add(new MetricClause("CS-T", F(csT), "≥ 0.20", true, csT >= 0.20));
        card.Clauses.Add(new MetricClause("CS-P", F(csP), "≥ 0.20", true, csP >= 0.20));

        // §8.2 NT on the P-scale.
        var edge = new bool[n, n];
        for (var a = 0; a < n; a++)
        {
            for (var b = 0; b < n; b++)
            {
                if (a != b && pP[a, b] >= 0.55)
                {
                    edge[a, b] = true;
                    card.Edges++;
                }
            }
        }

        var orderedCycles = 0;
        for (var a = 0; a < n; a++)
        {
            for (var b = 0; b < n; b++)
            {
                if (!edge[a, b])
                {
                    continue;
                }

                for (var c = 0; c < n; c++)
                {
                    if (c != a && c != b && edge[b, c] && edge[c, a])
                    {
                        orderedCycles++;
                    }
                }
            }
        }

        card.Cycles = orderedCycles / 3; // each cycle is counted once per rotation
        for (var a = 0; a < n; a++)
        {
            var dominant = true;
            for (var b = 0; b < n && dominant; b++)
            {
                if (a != b && !edge[a, b])
                {
                    dominant = false;
                }
            }

            if (dominant)
            {
                card.Dominant.Add(TestArmies.Compositions[a].Label);
            }
        }

        card.Clauses.Add(new MetricClause(
            "NT",
            $"{card.Cycles} three-cycles; {card.Edges} edges; dominant: {(card.Dominant.Count == 0 ? "none" : string.Join(", ", card.Dominant))}",
            "≥ 1 three-cycle and no dominant mix",
            true,
            card.Cycles >= 1 && card.Dominant.Count == 0));

        // §8.3 UR.
        (int Kappa, double Low, double High)[] bands = { (90, 0.10, 0.45), (80, 0.03, 0.35), (67, 0.005, 0.20), (50, 0.0, 0.08) };
        var ur = new double[bands.Length];
        var urPass = true;
        for (var i = 0; i < bands.Length; i++)
        {
            var total = 0;
            var weakerWins = 0;
            foreach (var r in upset)
            {
                var attackerWeaker = r.Battle.AttackerKappaPercent == bands[i].Kappa;
                var defenderWeaker = r.Battle.DefenderKappaPercent == bands[i].Kappa;
                if (!attackerWeaker && !defenderWeaker)
                {
                    continue;
                }

                total++;
                if (attackerWeaker == r.AttackerWon)
                {
                    weakerWins++;
                }
            }

            ur[i] = total == 0 ? 0 : (double)weakerWins / total;
            var inBand = ur[i] >= bands[i].Low && ur[i] <= bands[i].High;
            urPass &= inBand;
            card.Clauses.Add(new MetricClause(
                $"UR(0.{bands[i].Kappa.ToString("00", Inv)})",
                F(ur[i]) + $" ({weakerWins}/{total})",
                $"[{bands[i].Low.ToString("0.###", Inv)}, {bands[i].High.ToString("0.###", Inv)}]",
                true,
                inBand));
        }

        var monotone = true;
        for (var i = 1; i < ur.Length; i++)
        {
            monotone &= ur[i] <= ur[i - 1] + 0.02;
        }

        card.Clauses.Add(new MetricClause("UR-monotone", monotone ? "non-increasing" : "rises", "non-increasing as κ falls (±0.02)", true, monotone));
        card.Clauses.Add(new MetricClause("UR", urPass && monotone ? "all bands" : "outside a band", "every UR clause", true, urPass && monotone));

        // §8.4 CD.
        var spreads = new List<double>();
        foreach (var r in powerScale)
        {
            // The uniform mix (15) and the five one-heavy mixes (16–20) are the compositions with all five types.
            if (WinnerComposition(r) >= 15)
            {
                spreads.Add(Spread(r));
            }
        }

        var cdA = Median(spreads);
        card.Clauses.Add(new MetricClause("CD-a", F(cdA) + $" (median of {spreads.Count})", "≥ 0.15", true, cdA >= 0.15));

        const int uniform = 15;
        card.ClearTops = new string[n];
        var distinctTops = new SortedSet<string>(StringComparer.Ordinal);
        for (var x = 0; x < n; x++)
        {
            var sums = new double[5];
            var count = 0;
            foreach (var r in powerScale)
            {
                var uniformWonAttacking = r.Battle.AttackerComposition == uniform && r.Battle.DefenderComposition == x && r.AttackerWon;
                // Each record is visited once, so the mirror matchup (x = uniform) counts every one of its
                // battles exactly once, whichever seat won.
                var uniformWonDefending = r.Battle.DefenderComposition == uniform && r.Battle.AttackerComposition == x && !r.AttackerWon;
                if (!uniformWonAttacking && !uniformWonDefending)
                {
                    continue;
                }

                count++;
                for (var t = 0; t < 5; t++)
                {
                    sums[t] += Loss(r, t);
                }
            }

            if (count == 0)
            {
                card.ClearTops[x] = "no data";
                continue;
            }

            var order = Enumerable.Range(0, 5).OrderByDescending(t => sums[t]).ThenBy(t => t).ToArray();
            var top = sums[order[0]] / count;
            var second = sums[order[1]] / count;
            if (top - second >= 0.05)
            {
                card.ClearTops[x] = TestArmies.TypeLabels[order[0]];
                distinctTops.Add(TestArmies.TypeLabels[order[0]]);
            }
            else
            {
                card.ClearTops[x] = "none";
            }
        }

        card.Clauses.Add(new MetricClause(
            "CD-b",
            $"{distinctTops.Count} distinct clear-top types ({(distinctTops.Count == 0 ? "none" : string.Join(", ", distinctTops))})",
            "≥ 2 distinct clear-top types",
            true,
            distinctTops.Count >= 2));

        // §8.7 EN, over every P-scale battle.
        var endings = new SortedList<string, int>(StringComparer.Ordinal);
        foreach (var name2 in new[] { "annihilation", "cap", "collapse", "decided", "withdrawal" })
        {
            endings[name2] = 0;
        }

        var cascades = 0;
        foreach (var r in powerScale)
        {
            endings[TournamentHarness.EndingName(r.Ending)]++;
            if (r.CascadeBreak)
            {
                cascades++;
            }
        }

        foreach (var pair in endings)
        {
            card.EndingFractions[pair.Key] = (double)pair.Value / powerScale.Count;
        }

        var capFraction = card.EndingFractions["cap"];
        var collapseOrWithdrawal = card.EndingFractions["collapse"] + card.EndingFractions["withdrawal"];
        var cascadeFraction = (double)cascades / powerScale.Count;
        card.Clauses.Add(new MetricClause("EN-a", F(capFraction), "cap ≤ 0.05", enApplies, capFraction <= 0.05));
        card.Clauses.Add(new MetricClause("EN-b", F(collapseOrWithdrawal), "collapse + withdrawal in [0.10, 0.90]", enApplies,
            collapseOrWithdrawal >= 0.10 && collapseOrWithdrawal <= 0.90));
        card.Clauses.Add(new MetricClause("EN-c", F(cascadeFraction), "cascade in [0.10, 0.90]", cascadeApplies,
            cascadeFraction >= 0.10 && cascadeFraction <= 0.90));

        // §8.8 SV, over every decisive P-scale battle (every battle is decisive: ties go to the defender).
        var sigmas = new List<double>();
        var oneMinusSigmas = new List<double>();
        var omegas = new List<double>();
        var taus = new List<double>();
        double sigmaZ = 0, sigmaH = 0;
        int countZ = 0, countH = 0;
        var byEnding = new SortedList<string, (double Sum, int Count)>(StringComparer.Ordinal);
        foreach (var r in powerScale)
        {
            var loserStart = r.LoserStart.Sum();
            var survived = r.LoserSurvivors.Sum();
            var sigma = (double)survived / loserStart;
            var winnerStart = r.WinnerStart.Sum();
            var omega = (double)(winnerStart - r.WinnerEnd.Sum()) / winnerStart;
            sigmas.Add(sigma);
            oneMinusSigmas.Add(1 - sigma);
            omegas.Add(omega);
            if (survived > 0)
            {
                double tv = 0;
                for (var t = 0; t < 5; t++)
                {
                    tv += Math.Abs(((double)r.LoserSurvivors[t] / survived) - ((double)r.LoserStart[t] / loserStart));
                }

                taus.Add(tv / 2);
            }

            var shares = TestArmies.Compositions[WinnerComposition(r)].Shares;
            var cavalryShare = shares[3] + shares[4]; // P-scale shares ARE shares of weighted power
            if (cavalryShare >= 40)
            {
                sigmaH += sigma;
                countH++;
            }
            else if (cavalryShare == 0)
            {
                sigmaZ += sigma;
                countZ++;
            }

            var ending = TournamentHarness.EndingName(r.Ending);
            byEnding.TryGetValue(ending, out var acc);
            byEnding[ending] = (acc.Sum + sigma, acc.Count + 1);
        }

        foreach (var pair in byEnding)
        {
            card.SigmaByEnding[pair.Key] = (pair.Value.Sum / pair.Value.Count, pair.Value.Count);
        }

        var svA = Median(sigmas);
        var svB = Median(oneMinusSigmas) - Median(omegas);
        var svC = (countZ == 0 ? 0 : sigmaZ / countZ) - (countH == 0 ? 0 : sigmaH / countH);
        var svD = taus.Count == 0 ? double.NaN : taus.Average();
        card.Clauses.Add(new MetricClause("SV-a", F(svA) + " (median σ)", "≥ 0.10", svApplies, svA >= 0.10));
        card.Clauses.Add(new MetricClause("SV-b", F(svB) + $" (median 1−σ {F(Median(oneMinusSigmas))} − median ω {F(Median(omegas))})",
            "≥ 0.10", svApplies, svB >= 0.10));
        card.Clauses.Add(new MetricClause("SV-c", F(svC) + $" (σ(Z) {F(countZ == 0 ? 0 : sigmaZ / countZ)} over {countZ} − σ(H) {F(countH == 0 ? 0 : sigmaH / countH)} over {countH})",
            "≥ 0.05", svApplies, svC >= 0.05));
        card.Clauses.Add(new MetricClause("SV-d", (taus.Count == 0 ? "undefined" : F(svD)) + $" (mean τ over {taus.Count} battles with survivors)",
            "≥ 0.05", svApplies, taus.Count > 0 && svD >= 0.05));

        // Diagnostics, no band: the draw-formula check over EVERY battle, not only §8.5's 100.
        var all = troopScale.Concat(powerScale).Concat(upset).ToList();
        var mismatches = all.Count(r => r.TotalDraws != r.StatedDraws);
        card.Diagnostics["draw-formula mismatches (all battles)"] = $"{mismatches} of {all.Count}";
        card.Diagnostics["battle-phase draws per P-scale battle (mean)"] = F(powerScale.Average(r => (double)r.BattleDraws), "0.0");
        card.Diagnostics["battle-phase draws per P-scale battle (min..max)"] =
            $"{powerScale.Min(r => r.BattleDraws)}..{powerScale.Max(r => r.BattleDraws)}";
        card.Diagnostics["rounds per P-scale battle (mean)"] = F(powerScale.Average(r => (double)r.Rounds), "0.00");
        card.Diagnostics["attacker win rate, P-scale"] = F(powerScale.Count(r => r.AttackerWon) / (double)powerScale.Count);
        card.Diagnostics["attacker win rate, T-scale"] = F(troopScale.Count(r => r.AttackerWon) / (double)troopScale.Count);

        return card;
    }

    /// <summary>A deterministic JSON rendering of everything computed here.</summary>
    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"key\":").Append(Q(Key)).Append(",\"name\":").Append(Q(Name)).Append(",\"clauses\":[");
        for (var i = 0; i < Clauses.Count; i++)
        {
            var c = Clauses[i];
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append("{\"id\":").Append(Q(c.Id)).Append(",\"value\":").Append(Q(c.Value))
              .Append(",\"band\":").Append(Q(c.Band)).Append(",\"verdict\":").Append(Q(c.Verdict)).Append('}');
        }

        sb.Append("],\"scoreT\":[").Append(string.Join(",", ScoreT.Select(v => F(v)))).Append(']');
        sb.Append(",\"scoreP\":[").Append(string.Join(",", ScoreP.Select(v => F(v)))).Append(']');
        sb.Append(",\"clearTops\":[").Append(string.Join(",", ClearTops.Select(Q))).Append(']');
        sb.Append(",\"endings\":{").Append(string.Join(",", EndingFractions.Select(p => Q(p.Key) + ":" + F(p.Value)))).Append('}');
        sb.Append(",\"sigmaByEnding\":{")
          .Append(string.Join(",", SigmaByEnding.Select(p => Q(p.Key) + ":{\"meanSigma\":" + F(p.Value.MeanSigma) + ",\"battles\":" + p.Value.Battles.ToString(Inv) + "}")))
          .Append('}');
        sb.Append(",\"diagnostics\":{").Append(string.Join(",", Diagnostics.Select(p => Q(p.Key) + ":" + Q(p.Value)))).Append('}');
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Formats a fraction the one way every output uses.</summary>
    public static string F(double value, string format = "0.0000") =>
        double.IsNaN(value) ? "NaN" : value.ToString(format, Inv);

    private static string Q(string s) =>
        "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static int WinnerComposition(BattleRecord r) =>
        r.AttackerWon ? r.Battle.AttackerComposition : r.Battle.DefenderComposition;

    private static double Loss(BattleRecord r, int t) =>
        r.WinnerStart[t] == 0 ? 0 : (double)(r.WinnerStart[t] - r.WinnerEnd[t]) / r.WinnerStart[t];

    private static double Spread(BattleRecord r)
    {
        var max = double.MinValue;
        var min = double.MaxValue;
        for (var t = 0; t < 5; t++)
        {
            if (r.WinnerStart[t] == 0)
            {
                continue;
            }

            var l = Loss(r, t);
            max = Math.Max(max, l);
            min = Math.Min(min, l);
        }

        return max - min;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static double[,] PairProbabilities(IReadOnlyList<BattleRecord> records, int n, int seeds)
    {
        var wins = new int[n, n];
        foreach (var r in records)
        {
            if (r.AttackerWon)
            {
                wins[r.Battle.AttackerComposition, r.Battle.DefenderComposition]++;
            }
        }

        // p(A, B) = [w(A, B) + 1 − w(B, A)] / 2
        var p = new double[n, n];
        for (var a = 0; a < n; a++)
        {
            for (var b = 0; b < n; b++)
            {
                var wAB = (double)wins[a, b] / seeds;
                var wBA = (double)wins[b, a] / seeds;
                p[a, b] = (wAB + 1 - wBA) / 2;
            }
        }

        return p;
    }

    private static double[] Scores(double[,] p, int n)
    {
        var s = new double[n];
        for (var a = 0; a < n; a++)
        {
            double sum = 0;
            for (var b = 0; b < n; b++)
            {
                if (b != a)
                {
                    sum += p[a, b];
                }
            }

            s[a] = sum / (n - 1);
        }

        return s;
    }
}
