using System.Globalization;
using System.Linq;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;

namespace IC2.Cli;

/// <summary>
/// A thin console wrapper around <see cref="GameSession"/> — <c>docs/task-catalogue.md</c> "T41 Thin CLI
/// demo on the toy world (a walking skeleton)". Holds no game rule and no parsing beyond its own four
/// arguments (<c>--script</c>, <c>--seed</c>, <c>--scenario</c>, <c>--ruleset</c>): every command line it
/// reads is handed to <see cref="GameSession.Submit"/> verbatim, and every line it prints to standard
/// output is exactly what that call returned.
/// </summary>
/// <remarks>
/// <strong>DoD 5 (added to <c>docs/task-catalogue.md</c> T23 after PR #248's round-1 review).</strong>
/// Before this, <see cref="Main"/> hardcoded <c>repository.Resolve("toy-3city")</c>, so T29's 334-city
/// <c>classical-mediterranean</c> world and T36's <c>improved</c> ruleset preset were both exported and
/// loaded by <see cref="GameDataRepository"/> but reachable from nothing. <c>--scenario</c> and
/// <c>--ruleset</c> fix that without touching <c>GameDataRepository</c> (owned elsewhere, and unneeded —
/// it already exposes <see cref="GameDataRepository.Scenarios"/>/<see cref="GameDataRepository.Rulesets"/>
/// and <see cref="GameDataRepository.Resolve"/> returns the three parts separately): <c>--ruleset</c> is
/// a plain substitution on the already-resolved <see cref="ResolvedScenario"/> record, at the same call
/// site that already split the three apart for <see cref="GameSession"/>'s constructor. <c>--ruleset</c>
/// is the design, not a shortcut — <c>docs/game-design.md</c> §"Two shipped presets, not a pile of
/// independent flags" calls the <c>classical-faithful</c>/<c>improved</c> choice "a top-level product
/// decision … surfaced as a prominent choice at New Game rather than buried in scenario JSON", and this is
/// that choice's text-mode form, ahead of T24's graphical one.
/// </remarks>
/// <remarks>
/// <strong>Both flags default to today's values, so a no-flag invocation is unchanged.</strong> No new
/// line reaches standard output on the default path: the "which world/ruleset pair it loaded" line DoD 5
/// requires goes to standard <em>error</em>, precisely so <c>tests/fixtures/cli/demo.golden.txt</c> (which
/// only ever captures standard output) regenerates byte-identical. If it ever doesn't, that means the
/// default path moved — the bug is there, not in the golden.
/// </remarks>
/// <remarks>
/// <strong>The big world loads; rendering it is not this task's to fix.</strong>
/// <see cref="GameSessionRendering"/> is already world-size-agnostic, so <c>--scenario
/// classical-mediterranean</c> loads and runs, but its <c>map</c> command prints a 320-character-wide
/// grid and its legend emits one line per city for 334 cities, with markers colliding wholesale
/// (<c>city.Name[0]</c> is nowhere near unique at that scale). That is a viewport problem for a future
/// task, not a defect this one introduces or should paper over with ad hoc paging.
/// </remarks>
internal static class Program
{
    /// <summary>The scenario loaded when <c>--scenario</c> is not given — unchanged from before DoD 5.</summary>
    private const string DefaultScenarioId = "toy-3city";

    private static int Main(string[] args)
    {
        // A fixed, invariant culture and "\n" line endings, so the golden transcript this task ships
        // (tests/fixtures/cli/demo.golden.txt) is byte-stable across machines.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.Out.NewLine = "\n";

        string? scriptPath = null;
        ulong? seed = null;
        var scenarioId = DefaultScenarioId;
        string? rulesetOverrideId = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--script":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--script requires a file path.");
                        return 1;
                    }

                    scriptPath = args[++i];
                    break;

                case "--seed":
                    if (i + 1 >= args.Length
                        || !ulong.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeed))
                    {
                        Console.Error.WriteLine("--seed requires a non-negative integer.");
                        return 1;
                    }

                    i++;
                    seed = parsedSeed;
                    break;

                case "--scenario":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--scenario requires a scenario id.");
                        return 1;
                    }

                    scenarioId = args[++i];
                    break;

                case "--ruleset":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--ruleset requires a ruleset id.");
                        return 1;
                    }

                    rulesetOverrideId = args[++i];
                    break;

                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        if (scriptPath is not null && !File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"Could not find script file: {scriptPath}");
            return 1;
        }

        GameSession session;
        try
        {
            var repository = GameDataRepository.Load(Path.Combine(FindRepositoryRoot(), "data"));

            if (repository.ScenarioById(scenarioId) is null)
            {
                var available = string.Join(", ", repository.Scenarios.Select(s => s.Id).OrderBy(id => id, StringComparer.Ordinal));
                Console.Error.WriteLine($"Unknown scenario '{scenarioId}'. Available scenarios: {available}.");
                return 1;
            }

            var resolved = repository.Resolve(scenarioId);

            if (rulesetOverrideId is not null)
            {
                var overrideRuleset = repository.RulesetById(rulesetOverrideId);
                if (overrideRuleset is null)
                {
                    var available = string.Join(", ", repository.Rulesets.Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal));
                    Console.Error.WriteLine($"Unknown ruleset '{rulesetOverrideId}'. Available rulesets: {available}.");
                    return 1;
                }

                resolved = resolved with { Ruleset = overrideRuleset };
            }

            Console.Error.WriteLine(
                $"Loaded scenario '{resolved.Scenario.Id}': world '{resolved.World.Id}', ruleset '{resolved.Ruleset.Id}'.");

            session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, seed);
        }
        catch (GameDataException ex)
        {
            Console.Error.WriteLine($"Could not load scenario '{scenarioId}': {ex.Message}");
            return 1;
        }

        foreach (var inputLine in ReadLines(scriptPath))
        {
            var output = session.Submit(inputLine);
            foreach (var text in output.Lines)
            {
                Console.WriteLine(text);
            }

            if (output.ShouldExit)
            {
                return 0;
            }
        }

        return 0;
    }

    private static IEnumerable<string> ReadLines(string? scriptPath)
    {
        if (scriptPath is not null)
        {
            foreach (var scriptLine in File.ReadLines(scriptPath))
            {
                yield return scriptLine;
            }

            yield break;
        }

        string? consoleLine;
        while ((consoleLine = Console.In.ReadLine()) is not null)
        {
            yield return consoleLine;
        }
    }

    /// <summary>
    /// Finds the repository root by walking up from the running executable to the directory that holds
    /// <c>IC2.sln</c> — the same convention <c>tests/IC2.Engine.Tests/Model/TestPaths.cs</c> uses to find
    /// the shipped <c>data/</c> directory from a test run.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IC2.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find 'IC2.sln' above '{AppContext.BaseDirectory}'.");
    }
}
