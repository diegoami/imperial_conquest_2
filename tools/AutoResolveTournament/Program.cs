// T59 — the auto-resolve tournament runner (docs/tasks/T59.md; the specification is
// docs/investigations/auto-resolve-approaches.md). A measurement tool: nothing here is gameplay, and the one
// wall-clock use (Stopwatch, COST §8.6) measures time — it never feeds a candidate.
//
//   dotnet run -c Release --project tools/AutoResolveTournament -- run   [--seeds N] [--out DIR] [--no-timing] [--no-det] [--e0 S] [--b N]
//   dotnet run -c Release --project tools/AutoResolveTournament -- det   --candidate C2 --out FILE
//   dotnet run -c Release --project tools/AutoResolveTournament -- soak  [--out DIR]
//   dotnet run -c Release --project tools/AutoResolveTournament -- smoke [--out DIR]
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AutoResolveTournament;

var command = args.Length == 0 ? "run" : args[0];
var options = Options.Parse(args.Skip(1).ToArray());
var context = TournamentContext.Load();

return command switch
{
    "run" => Commands.Run(context, options),
    "det" => Commands.Det(context, options),
    "soak" => Commands.Soak(context, options),
    "smoke" => Smoke.Run(context, options),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: run | det --candidate Cn --out FILE | soak | smoke");
    return 2;
}

namespace AutoResolveTournament
{
    using IC2.Engine.Ai;
    using IC2.Engine.Battle;
    using IC2.Engine.Battle.Candidates;
    using IC2.Engine.Battle.Candidates.Tournament;
    using IC2.Engine.Core;
    using IC2.Engine.Model;
    using IC2.Engine.Serialization;
    using IC2.Engine.Victory;

    internal sealed class Options
    {
        public int Seeds { get; private set; } = TestArmies.SeedsPerMatchup;

        public string? Out { get; private set; }

        public string? Candidate { get; private set; }

        public bool Timing { get; private set; } = true;

        public bool Det { get; private set; } = true;

        public double? E0 { get; private set; }

        public long? B { get; private set; }

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seeds": o.Seeds = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--out": o.Out = args[++i]; break;
                    case "--candidate": o.Candidate = args[++i]; break;
                    case "--no-timing": o.Timing = false; break;
                    case "--no-det": o.Det = false; break;
                    case "--e0": o.E0 = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--b": o.B = long.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException("unknown option " + args[i]);
                }
            }

            return o;
        }
    }

    internal sealed class TournamentContext
    {
        public const string RulesetId = "classical-faithful";

        private TournamentContext(string root, GameDataRepository repository)
        {
            RepositoryRoot = root;
            Repository = repository;
            Ruleset = repository.RulesetById(RulesetId) ?? throw new InvalidOperationException("no " + RulesetId);
            Toy = repository.Resolve("toy-3city");
            Harness = new TournamentHarness(Ruleset, DefeatOutcome.Scatter);

            // C1's battlefield: two adjacent plain tiles of the toy world, (3,2) and (4,2), with no city, fleet or
            // other army on the map, so a scattered loser always has a tile (MergedInstantCandidate remarks).
            var toyState = repository.CreateInitialState("toy-3city");
            Candidates = new IAutoResolveCandidate[]
            {
                new MergedInstantCandidate(toyState, Toy.World, 3, 2, 4, 2),
                new HeadlessTacticalCandidate(),
                new TypeWeightedInstantCandidate(),
                new RoundBasedCandidate(),
                new MoraleRetreatCandidate(),
            };
        }

        public string RepositoryRoot { get; }

        public GameDataRepository Repository { get; }

        public Ruleset Ruleset { get; }

        public ResolvedScenario Toy { get; }

        public TournamentHarness Harness { get; }

        public IReadOnlyList<IAutoResolveCandidate> Candidates { get; }

        public string DefaultOut => Path.Combine(RepositoryRoot, "tools", "AutoResolveTournament", "results");

        public static TournamentContext Load()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IC2.sln")))
            {
                dir = dir.Parent;
            }

            if (dir is null)
            {
                throw new InvalidOperationException("Run from inside the repository: IC2.sln not found above " + AppContext.BaseDirectory);
            }

            return new TournamentContext(dir.FullName, GameDataRepository.Load(Path.Combine(dir.FullName, "data")));
        }

        public IAutoResolveCandidate CandidateByKey(string key) =>
            Candidates.First(c => string.Equals(c.Key, key, StringComparison.Ordinal));
    }

    internal static class Commands
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Run(TournamentContext context, Options options)
        {
            var outDir = options.Out ?? context.DefaultOut;
            Directory.CreateDirectory(outDir);
            var harness = context.Harness;
            var scheduleCs = TournamentHarness.CompositionSchedule(options.Seeds);
            var scheduleUr = TournamentHarness.UpsetSchedule(options.Seeds);
            var cards = new List<CandidateScorecard>();
            var costs = new List<(string Key, double MeanMs, double P99Ms, double MaxMs, int Battles)>();

            foreach (var candidate in context.Candidates)
            {
                var clock = Stopwatch.StartNew();
                Console.WriteLine($"[{candidate.Key}] {candidate.Name}");

                BattleRecord[] power;
                if (options.Timing)
                {
                    // §8.6: Release build, 1,000 warm-up battles, then every battle of the CS-P schedule, one
                    // thread, timed one by one.
                    for (var i = 0; i < 1000; i++)
                    {
                        harness.Play(candidate, scheduleCs[i % scheduleCs.Count], ArmyScale.Power);
                    }

                    power = new BattleRecord[scheduleCs.Count];
                    var ticks = new long[scheduleCs.Count];
                    for (var i = 0; i < scheduleCs.Count; i++)
                    {
                        var t0 = Stopwatch.GetTimestamp();
                        power[i] = harness.Play(candidate, scheduleCs[i], ArmyScale.Power);
                        ticks[i] = Stopwatch.GetTimestamp() - t0;
                    }

                    var ms = ticks.Select(t => t * 1000.0 / Stopwatch.Frequency).OrderBy(v => v).ToArray();
                    var p99 = ms[(int)Math.Ceiling(0.99 * ms.Length) - 1];
                    costs.Add((candidate.Key, ms.Average(), p99, ms[^1], ms.Length));
                }
                else
                {
                    power = PlayAll(harness, candidate, scheduleCs, ArmyScale.Power);
                }

                var troops = PlayAll(harness, candidate, scheduleCs, ArmyScale.Troops);
                var upset = PlayAll(harness, candidate, scheduleUr, ArmyScale.Troops);

                var enApplies = candidate.Key is "C2" or "C4" or "C5";
                var cascadeApplies = candidate.Key is "C2" or "C5";
                var card = CandidateScorecard.Compute(
                    candidate.Key, candidate.Name, troops, power, upset, options.Seeds, enApplies, cascadeApplies, svApplies: true);
                cards.Add(card);
                Console.WriteLine($"[{candidate.Key}] {power.Length + troops.Length + upset.Length} battles in {clock.Elapsed.TotalSeconds:F1}s");
            }

            var scorecard = new StringBuilder();
            scorecard.Append("{\"ruleset\":\"").Append(TournamentContext.RulesetId).Append("\",\"onDefeat\":\"scatter\",\"seedsPerMatchup\":")
                .Append(options.Seeds.ToString(Inv)).Append(",\"candidates\":[")
                .Append(string.Join(",", cards.Select(c => c.ToJson()))).Append("]}\n");
            var scorecardPath = Path.Combine(outDir, "scorecard.json");
            File.WriteAllText(scorecardPath, scorecard.ToString(), new UTF8Encoding(false));
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(scorecardPath)));
            Console.WriteLine($"scorecard.json sha256 {hash}");

            var det = new List<(string Key, string Result)>();
            if (options.Det)
            {
                foreach (var candidate in context.Candidates)
                {
                    det.Add((candidate.Key, DetTwice(candidate.Key, outDir)));
                    Console.WriteLine($"[{candidate.Key}] DET {det[^1].Result}");
                }
            }

            var cost = new StringBuilder();
            cost.AppendLine("{");
            cost.AppendLine($"  \"machine\": \"{Environment.MachineName}\", \"processors\": {Environment.ProcessorCount}, \"runtime\": \"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\",");
            cost.AppendLine($"  \"e0Seconds\": {(options.E0?.ToString("0.00", Inv) ?? "null")}, \"fieldBattlesB\": {(options.B?.ToString(Inv) ?? "null")},");
            cost.AppendLine("  \"candidates\": [");
            var t1 = costs.FirstOrDefault(c => c.Key == "C1").MeanMs;
            for (var i = 0; i < costs.Count; i++)
            {
                var c = costs[i];
                string projection = "null";
                if (options.E0 is { } e0 && options.B is { } b)
                {
                    projection = (e0 + (b * (c.MeanMs - t1) / 1000.0)).ToString("0.00", Inv);
                }

                cost.AppendLine($"    {{\"key\": \"{c.Key}\", \"battles\": {c.Battles}, \"meanMs\": {c.MeanMs.ToString("0.00000", Inv)}, \"p99Ms\": {c.P99Ms.ToString("0.00000", Inv)}, \"maxMs\": {c.MaxMs.ToString("0.000", Inv)}, \"projectedSoakSeconds\": {projection}}}{(i < costs.Count - 1 ? "," : string.Empty)}");
            }

            cost.AppendLine("  ],");
            cost.AppendLine("  \"det\": {" + string.Join(", ", det.Select(d => $"\"{d.Key}\": \"{d.Result}\"")) + "}");
            cost.AppendLine("}");
            File.WriteAllText(Path.Combine(outDir, "cost-and-det.json"), cost.ToString(), new UTF8Encoding(false));

            File.WriteAllText(Path.Combine(outDir, "scorecard.md"), Markdown.Render(cards), new UTF8Encoding(false));
            return 0;
        }

        private static BattleRecord[] PlayAll(TournamentHarness harness, IAutoResolveCandidate candidate, IReadOnlyList<ScheduledBattle> schedule, ArmyScale scale)
        {
            // Parallel is safe here and only here: every record is a pure function of (candidate, battle), written
            // to its own index, so the array is the same whatever the thread interleaving.
            var records = new BattleRecord[schedule.Count];
            Parallel.For(0, schedule.Count, i => records[i] = harness.Play(candidate, schedule[i], scale));
            return records;
        }

        /// <summary>§8.5: the first 100 CS-P battles, twice, in two separate processes; byte-compared.</summary>
        private static string DetTwice(string key, string outDir)
        {
            var first = Path.Combine(outDir, $"det-{key}-1.txt");
            var second = Path.Combine(outDir, $"det-{key}-2.txt");
            Spawn(key, first);
            Spawn(key, second);
            var a = File.ReadAllBytes(first);
            var b = File.ReadAllBytes(second);
            var identical = a.AsSpan().SequenceEqual(b);
            var lines = File.ReadAllLines(first);
            var summary = lines.Last();
            File.Delete(second);
            return (identical ? "100/100 byte-identical across two processes" : "DIFFERENT across two processes") + "; " + summary;
        }

        private static void Spawn(string key, string outFile)
        {
            var self = Environment.ProcessPath!;
            var start = new ProcessStartInfo(self) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add(typeof(Commands).Assembly.Location);
            }

            foreach (var arg in new[] { "det", "--candidate", key, "--out", outFile })
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start)!;
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"det subprocess for {key} exited {process.ExitCode}");
            }
        }

        public static int Det(TournamentContext context, Options options)
        {
            var candidate = context.CandidateByKey(options.Candidate ?? throw new ArgumentException("--candidate"));
            var harness = context.Harness;
            var schedule = TournamentHarness.CompositionSchedule(TestArmies.SeedsPerMatchup);
            var sb = new StringBuilder();
            var drawOk = 0;
            for (var i = 0; i < 100; i++)
            {
                var input = harness.Input(schedule[i], ArmyScale.Power);
                var outcome = candidate.Resolve(input, new SplitMix64Rng(schedule[i].Seed), recordEvents: true);

                // C1 and C3: the fixed formula from the unit counts (the candidate's StatedDraws). C2, C4, C5: the
                // per-event formula evaluated on this battle's own event log.
                var stated = outcome.Events.Count > 0 && candidate.Key is "C2" or "C4" or "C5"
                    ? outcome.Events.Sum(e => (long)e.StatedDraws)
                    : outcome.StatedDraws;
                if (stated == outcome.TotalDraws)
                {
                    drawOk++;
                }

                sb.Append(TournamentHarness.CanonicalJson(outcome)).Append('\n');
            }

            sb.Append($"draw count as stated: {drawOk}/100\n");
            File.WriteAllText(options.Out ?? throw new ArgumentException("--out"), sb.ToString(), new UTF8Encoding(false));
            return 0;
        }

        /// <summary>
        /// §8.6's soak baseline, reproduced: the 50 fixed seeds of AiSoakTests (seeds 1…50, SoakTurnCap 1200, the
        /// toy scenario with both seats AI and the north seat on AiTestbed.NorthPersonality), timed as the soak times
        /// them; then replayed turn by turn to count the field battles B from the BattleResolved events, and each
        /// replay's final state hash checked against the timed run's so B belongs to the same games.
        /// </summary>
        public static int Soak(TournamentContext context, Options options)
        {
            var toy = context.Toy;
            var scenario = AllAiScenario(toy.Scenario);
            const int turnCap = 1200;
            const int seedCount = 50;

            var finals = new string[seedCount];
            var clock = Stopwatch.StartNew();
            for (var s = 0; s < seedCount; s++)
            {
                finals[s] = GameStateHash.Compute(AiGameRunner.Run(toy.World, toy.Ruleset, scenario, (ulong)(s + 1), turnCap).FinalState);
            }

            clock.Stop();

            long field = 0, siege = 0, naval = 0;
            var mismatched = 0;
            for (var s = 0; s < seedCount; s++)
            {
                var registry = SystemRegistry.FromEngineAssembly();
                var dispatcher = new CommandDispatcher(registry, toy.Ruleset, toy.World, NullEventSink.Instance);
                var coordinator = new TurnCoordinator(registry, toy.Ruleset, toy.World, NullEventSink.Instance, dispatcher);
                var state = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, scenario) with { RandomSeed = (ulong)(s + 1) };
                for (var turn = 0; turn < turnCap; turn++)
                {
                    var result = coordinator.RunTurn(state);
                    state = result.State;
                    var over = false;
                    foreach (var e in result.Events)
                    {
                        switch (e)
                        {
                            case BattleResolved { Result.Kind: BattleKind.Field }: field++; break;
                            case BattleResolved { Result.Kind: BattleKind.Siege }: siege++; break;
                            case BattleResolved { Result.Kind: BattleKind.Naval }: naval++; break;
                            case GameWon: over = true; break;
                            case GameExpired: over = true; break;
                            default: break;
                        }
                    }

                    if (over)
                    {
                        break;
                    }
                }

                if (!string.Equals(GameStateHash.Compute(state), finals[s], StringComparison.Ordinal))
                {
                    mismatched++;
                }
            }

            var line = string.Format(
                Inv,
                "soak reproduction: 50 seeds, cap {0}: {1:F2}s elapsed (E0, tool-measured); field battles B = {2}; sieges {3}; naval {4}; replay/final-state mismatches {5}",
                turnCap, clock.Elapsed.TotalSeconds, field, siege, naval, mismatched);
            Console.WriteLine(line);
            var outDir = options.Out ?? context.DefaultOut;
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "soak-baseline.txt"), line + "\n", new UTF8Encoding(false));
            return mismatched == 0 ? 0 : 1;
        }

        private static Scenario AllAiScenario(Scenario scenario)
        {
            // AiTestbed.AllAiScenario, reproduced: both seats AI, the human seat given NorthPersonality
            // (tests/IC2.Engine.Tests/Ai/AiTestbed.cs: Aggression 0.7, ExpansionDrive 0.6, LoyaltyToAlliances 0.3).
            var north = new AiPersonality(Aggression: 0.7, ExpansionDrive: 0.6, LoyaltyToAlliances: 0.3);
            var seats = scenario.Seats.Select(s => s.Control == SeatControl.Ai ? s : s with { Control = SeatControl.Ai, Personality = north }).ToArray();
            return scenario with { Id = scenario.Id + "-all-ai", Seats = ValueList<Seat>.Of(seats) };
        }
    }
}
