using System.Globalization;
using System.Text;
using IC2.Data;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Candidates;
using IC2.Engine.Battle.Candidates.Tournament;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace AutoResolveTournament;

/// <summary>
/// §9's smoke test: the observed tactical battles, each played for seeds k = 0 … 999 by every candidate.
/// It fails a candidate only on §9.4's conditions (exception, impossible troops, incomplete record, the first
/// 10 seeds not byte-identical). Everything else is reported beside the observation with no band, and NOTHING
/// is tuned against it (§9's warning).
/// </summary>
internal static class Smoke
{
    private const int Seeds = 1000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] TypeIds = { "light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry" };

    public static int Run(TournamentContext context, Options options)
    {
        var sb = new StringBuilder();
        var tables = context.Harness.Tables;

        // §9.1, [designed] inputs: the §8.0 split, quality 6, M = 59, Seleucid attacking.
        var seleucid = TestArmies.Split(new long[] { 27_300, 8_400, 5_200, 1_800, 2_400 }, tables);
        var ptolemaic = TestArmies.Split(new long[] { 21_300, 3_200, 0, 2_300, 900 }, tables);
        sb.AppendLine("## §9.1 Seleucid 45,100 v Ptolemaic 27,700 ([designed] rosters: §8.0 split, q 6, M 59, Seleucid attacks)");
        sb.AppendLine();
        sb.AppendLine("Seleucid units: " + Describe(seleucid) + "; Ptolemaic units: " + Describe(ptolemaic));
        sb.AppendLine();
        Instance(context, sb, seleucid, ptolemaic, "Seleucid", observedTotals: null);
        SeatSwap(context, sb, seleucid, ptolemaic, "Seleucid");

        // §9.2, read from the save.
        var corpus = options.Corpus ?? CorpusDirectory(context);
        sb.AppendLine("## §9.2 Rome v Gaul, `1_rome_270_winter_7.sav` (rosters read from the save; Rome attacks)");
        sb.AppendLine();
        var save92 = corpus is null ? null : Path.Combine(corpus, "saves-processed", "1_rome_270_winter_7.sav");
        if (save92 is null || !File.Exists(save92))
        {
            sb.AppendLine("NOT RUN: the save could not be resolved (no local asset corpus). Not run on substitute data (§9.2).");
        }
        else
        {
            var table = SaveArmyTable.Parse(File.ReadAllBytes(save92));
            var rome = table.Armies.FirstOrDefault(a => a.Index == 0 && a.X == 90 && a.Y == 28);
            var gaul = table.Armies.FirstOrDefault(a => a.Index == 13 && a.X == 88 && a.Y == 25);
            var failures = new List<string>();
            if (rome is null || gaul is null)
            {
                failures.Add("army 0 at (90,28) or army 13 at (88,25) not found");
            }
            else
            {
                Expect(failures, "Rome per-type totals", Totals(rome), new long[] { 45_087, 28_057, 16_856, 6_695, 3_187 });
                Expect(failures, "Gaul per-type totals", Totals(gaul), new long[] { 57_973, 7_635, 3_867, 10_899, 2_974 });
                var hc = rome.Units.Where(u => u.TypeCode == 4).Select(u => (long)u.Troops).OrderBy(t => t).ToArray();
                Expect(failures, "Rome's heavy-cavalry units", hc, new long[] { 755, 2_432 });
                if (!rome.Units.Any(u => u.Name.Contains("4th Bowmen", StringComparison.Ordinal) && u.Troops == 3_312))
                {
                    failures.Add("4th Bowmen with 3,312 troops not found");
                }

                var quality = new Dictionary<int, int> { [1] = 7, [2] = 7, [12] = 7, [4] = 9, [15] = 8 };
                foreach (var slot in new[] { 0, 6, 7, 8, 9, 13, 16, 17, 18 })
                {
                    quality[slot] = 6;
                }

                foreach (var slot in quality.Keys.OrderBy(k => k))
                {
                    var unit = rome.Units.FirstOrDefault(u => u.Slot == slot);
                    if (unit is null || unit.QualityCode != quality[slot])
                    {
                        failures.Add($"Rome slot {slot} quality {(unit is null ? "missing" : unit.QualityCode.ToString(Inv))}, expected {quality[slot]}");
                    }
                }

                if (rome.Morale != 68)
                {
                    failures.Add($"Rome M = {rome.Morale}, expected 68");
                }
            }

            if (failures.Count > 0)
            {
                sb.AppendLine("NOT RUN: roster assertion failed — " + string.Join("; ", failures) + ". Not run on substitute data (§9.2).");
            }
            else
            {
                var romeArmy = FromSave(rome!);
                var gaulArmy = FromSave(gaul!);
                sb.AppendLine($"Roster assertions: all passed (totals, HC 755 + 2,432, 4th Bowmen 3,312, slot qualities, Rome M = 68). Owners: army 0 {NationCatalog.Name(rome!.OwnerCode)}, army 13 {NationCatalog.Name(gaul!.OwnerCode)}. Gaul's +14 = {gaul!.Morale}.");
                sb.AppendLine();
                Instance(context, sb, romeArmy, gaulArmy, "Rome", observedTotals: new long[] { 63_282, 75_536 });
            }
        }

        // §9.3, the exact-total lookup in 7.sav.
        sb.AppendLine("## §9.3 Rome v Gaul, `7.sav → 8.sav`");
        sb.AppendLine();
        var save73 = corpus is null ? null : Path.Combine(corpus, "saves-processed", "7.sav");
        CandidateArmy? rome93 = null;
        CandidateArmy? gaul93 = null;
        if (save73 is not null && File.Exists(save73))
        {
            var table = SaveArmyTable.Parse(File.ReadAllBytes(save73));
            var romeMatches = table.Armies.Where(a => Totals(a).SequenceEqual(new long[] { 8_900, 34_700, 0, 2_400, 4_700 })).ToList();
            var gaulMatches = table.Armies.Where(a => Totals(a).SequenceEqual(new long[] { 55_518, 4_531, 0, 1_298, 555 })).ToList();
            sb.AppendLine($"Exact-total lookup in `7.sav`: {romeMatches.Count} army record(s) match Rome's start column, {gaulMatches.Count} match Gaul's.");
            if (romeMatches.Count == 1 && gaulMatches.Count == 1)
            {
                rome93 = FromSave(romeMatches[0]);
                gaul93 = FromSave(gaulMatches[0]);
                sb.AppendLine($"Path taken: **[confirmed by exact match]** — Rome's column matches army {romeMatches[0].Index} ({NationCatalog.Name(romeMatches[0].OwnerCode)}) at ({romeMatches[0].X},{romeMatches[0].Y}), M {romeMatches[0].Morale}; Gaul's matches army {gaulMatches[0].Index} ({NationCatalog.Name(gaulMatches[0].OwnerCode)}) at ({gaulMatches[0].X},{gaulMatches[0].Y}), M {gaulMatches[0].Morale}.");
            }
        }
        else
        {
            sb.AppendLine("`7.sav` could not be resolved.");
        }

        if (rome93 is null)
        {
            sb.AppendLine("Path taken: **fallback [designed]** — §8.0 split, quality 6, M = 59.");
            rome93 = TestArmies.Split(new long[] { 8_900, 34_700, 0, 2_400, 4_700 }, tables);
            gaul93 = TestArmies.Split(new long[] { 55_518, 4_531, 0, 1_298, 555 }, tables);
        }

        sb.AppendLine();
        Instance(context, sb, rome93, gaul93!, "Rome", observedTotals: new long[] { 39_941 });

        var outDir = options.Out ?? context.DefaultOut;
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "smoke.md"), sb.ToString(), new UTF8Encoding(false));
        Console.Write(sb.ToString());
        return 0;
    }

    private static void Instance(
        TournamentContext context,
        StringBuilder sb,
        CandidateArmy attacker,
        CandidateArmy defender,
        string attackerName,
        long[]? observedTotals)
    {
        var battle = new CandidateBattle(attacker, defender, context.Ruleset, DefeatOutcome.Scatter);
        var tables = context.Harness.Tables;
        var start = TestArmies.StartByType(attacker, tables);
        var startTotal = start.Sum();

        sb.Append("| Candidate | smoke verdict | ").Append(attackerName).Append(" (attacker) wins | attacker total loss p5 / p50 / p95 |");
        for (var t = 0; t < 5; t++)
        {
            if (start[t] > 0)
            {
                sb.Append(' ').Append(TestArmies.TypeLabels[t]).Append(" loss p5/p50/p95 |");
            }
        }

        sb.Append(" winner loses a whole arm (which) |");
        if (observedTotals is not null)
        {
            sb.Append(" observed final total(s): percentile in the attacker-won distribution |");
        }

        sb.AppendLine();
        sb.Append("| --- | --- | --- | --- |");
        for (var t = 0; t < 5; t++)
        {
            if (start[t] > 0)
            {
                sb.Append(" --- |");
            }
        }

        sb.Append(" --- |");
        if (observedTotals is not null)
        {
            sb.Append(" --- |");
        }

        sb.AppendLine();

        foreach (var candidate in context.Candidates)
        {
            var failures = new List<string>();
            var attackerWins = 0;
            var totalLoss = new List<double>();
            var typeLoss = Enumerable.Range(0, 5).Select(_ => new List<double>()).ToArray();
            var finals = new List<long>();
            var wholeArm = 0;
            var arms = new SortedDictionary<string, int>(StringComparer.Ordinal);

            for (var k = 0; k < Seeds; k++)
            {
                CandidateOutcome outcome;
                try
                {
                    outcome = candidate.Resolve(battle, new SplitMix64Rng((ulong)k));
                    if (k < 10)
                    {
                        var again = candidate.Resolve(battle, new SplitMix64Rng((ulong)k));
                        if (!string.Equals(TournamentHarness.CanonicalJson(outcome), TournamentHarness.CanonicalJson(again), StringComparison.Ordinal))
                        {
                            failures.Add($"seed {k} not byte-identical");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures.Add($"seed {k}: {ex.GetType().Name}");
                    continue;
                }

                var winner = outcome.Winner == BattleSide.Attacker ? attacker : defender;
                var loser = outcome.Winner == BattleSide.Attacker ? defender : attacker;
                if (outcome.WinnerAfter.Length != winner.Units.Count || outcome.LoserSurvivors.Length != loser.Units.Count)
                {
                    failures.Add($"seed {k}: incomplete record");
                    continue;
                }

                for (var i = 0; i < winner.Units.Count; i++)
                {
                    if (outcome.WinnerAfter[i] < 0 || outcome.WinnerAfter[i] > winner.Units[i].Troops)
                    {
                        failures.Add($"seed {k}: winner slot {i} has {outcome.WinnerAfter[i]} of {winner.Units[i].Troops}");
                    }
                }

                for (var i = 0; i < loser.Units.Count; i++)
                {
                    if (outcome.LoserSurvivors[i] < 0 || outcome.LoserSurvivors[i] > loser.Units[i].Troops)
                    {
                        failures.Add($"seed {k}: loser slot {i} has {outcome.LoserSurvivors[i]} of {loser.Units[i].Troops}");
                    }
                }

                // The winner's per-type losses (whole-arm check), and the attacker's own losses when it won.
                var winnerStart = TestArmies.StartByType(winner, tables);
                var winnerEnd = TestArmies.ByType(winner, outcome.WinnerAfter, tables);
                var lostArm = Enumerable.Range(0, 5).Where(t => winnerStart[t] > 0 && winnerEnd[t] == 0).ToList();
                if (lostArm.Count > 0)
                {
                    wholeArm++;
                    var key = string.Join("+", lostArm.Select(t => TestArmies.TypeLabels[t]));
                    arms[key] = arms.TryGetValue(key, out var n) ? n + 1 : 1;
                }

                if (outcome.Winner == BattleSide.Attacker)
                {
                    attackerWins++;
                    totalLoss.Add((double)(startTotal - winnerEnd.Sum()) / startTotal);
                    finals.Add(winnerEnd.Sum());
                    for (var t = 0; t < 5; t++)
                    {
                        if (start[t] > 0)
                        {
                            typeLoss[t].Add((double)(start[t] - winnerEnd[t]) / start[t]);
                        }
                    }
                }
            }

            sb.Append("| ").Append(candidate.Key).Append(" | ")
              .Append(failures.Count == 0 ? "pass" : "FAIL: " + string.Join("; ", failures.Take(3)))
              .Append(" | ").Append(((double)attackerWins / Seeds).ToString("0.000", Inv))
              .Append(" | ").Append(Percentiles(totalLoss)).Append(" |");
            for (var t = 0; t < 5; t++)
            {
                if (start[t] > 0)
                {
                    sb.Append(' ').Append(Percentiles(typeLoss[t])).Append(" |");
                }
            }

            sb.Append(' ').Append(((double)wholeArm / Seeds).ToString("0.000", Inv))
              .Append(arms.Count == 0 ? string.Empty : " (" + string.Join(", ", arms.Select(p => $"{p.Key} {p.Value}")) + ")")
              .Append(" |");
            if (observedTotals is not null)
            {
                var sorted = finals.OrderBy(v => v).ToArray();
                sb.Append(' ').Append(string.Join("; ", observedTotals.Select(o =>
                    $"{o.ToString("N0", Inv)}: {(sorted.Length == 0 ? "no attacker win" : ((double)sorted.Count(v => v <= o) / sorted.Length).ToString("0.000", Inv))}")))
                  .Append(sorted.Length == 0 ? string.Empty : $" (range {sorted[0].ToString("N0", Inv)}–{sorted[^1].ToString("N0", Inv)})")
                  .Append(" |");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
    }

    /// <summary>
    /// A measured seat effect, with no band: the same two armies with their seats swapped, the same seeds.
    /// </summary>
    private static void SeatSwap(TournamentContext context, StringBuilder sb, CandidateArmy army, CandidateArmy other, string name)
    {
        sb.AppendLine($"Seat swap (measured, no band): the same two rosters and seeds `k = 0 … 999`, {name} attacking and then defending.");
        sb.AppendLine();
        sb.AppendLine($"| Candidate | {name} wins attacking | {name} wins defending |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var candidate in context.Candidates)
        {
            var attacking = 0;
            var defending = 0;
            for (var k = 0; k < Seeds; k++)
            {
                if (candidate.Resolve(new CandidateBattle(army, other, context.Ruleset, DefeatOutcome.Scatter), new SplitMix64Rng((ulong)k)).Winner == BattleSide.Attacker)
                {
                    attacking++;
                }

                if (candidate.Resolve(new CandidateBattle(other, army, context.Ruleset, DefeatOutcome.Scatter), new SplitMix64Rng((ulong)k)).Winner == BattleSide.Defender)
                {
                    defending++;
                }
            }

            sb.AppendLine($"| {candidate.Key} | {((double)attacking / Seeds).ToString("0.000", Inv)} | {((double)defending / Seeds).ToString("0.000", Inv)} |");
        }

        sb.AppendLine();
    }

    private static string Percentiles(List<double> values)
    {
        if (values.Count == 0)
        {
            return "—";
        }

        var sorted = values.OrderBy(v => v).ToArray();
        string P(double q) => sorted[Math.Min(sorted.Length - 1, (int)Math.Floor(q * (sorted.Length - 1)))].ToString("0.000", Inv);
        return $"{P(0.05)} / {P(0.50)} / {P(0.95)}";
    }

    private static long[] Totals(ArmyRecord army)
    {
        var totals = new long[5];
        foreach (var unit in army.Units)
        {
            if (unit.TypeCode < 5)
            {
                totals[unit.TypeCode] += unit.Troops;
            }
        }

        return totals;
    }

    private static CandidateArmy FromSave(ArmyRecord army)
    {
        var units = army.Units
            .Where(u => u.Troops > 0)
            .OrderBy(u => u.Slot)
            .Select(u => new CandidateUnit(TypeIds[u.TypeCode], u.Troops, u.QualityCode, u.MercenaryLabel))
            .ToList();
        return new CandidateArmy(ValueList.From(units), army.Morale);
    }

    private static void Expect(List<string> failures, string what, long[] actual, long[] expected)
    {
        if (!actual.SequenceEqual(expected))
        {
            failures.Add($"{what} {string.Join("/", actual)} ≠ {string.Join("/", expected)}");
        }
    }

    private static string Describe(CandidateArmy army) =>
        string.Join(", ", army.Units.Select(u => $"{u.UnitTypeId} {u.Troops.ToString("N0", CultureInfo.InvariantCulture)}"));

    private static string? CorpusDirectory(TournamentContext context)
    {
        var ini = Path.Combine(context.RepositoryRoot, "assets.local.ini");
        var fallback = Path.Combine(Path.GetDirectoryName(context.RepositoryRoot) ?? string.Empty, "imperial_conquest_2", "assets.local.ini");
        foreach (var candidate in new[] { ini, fallback })
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                return AssetSettings.Load(candidate).DirectoryPath;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or ArgumentException)
            {
                continue;
            }
        }

        return Environment.GetEnvironmentVariable("IC2_FIXTURES_DIR");
    }
}
