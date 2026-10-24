using System.Globalization;
using System.Linq;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;

namespace IC2.Cli;

/// <summary>
/// A thin console wrapper around <see cref="GameSession"/> — <c>docs/task-catalogue.md</c> "T41 Thin CLI
/// demo on the toy world (a walking skeleton)". Holds no game rule and no parsing beyond its own eight
/// arguments (<c>--script</c>, <c>--seed</c>, <c>--turn-order</c> <c>fixed|shuffle</c>, <c>--scenario</c>,
/// <c>--ruleset</c>, <c>--seat</c>, <c>--load</c>, <c>--human-battles</c> <c>instant|tactical</c>): every
/// command line it reads is handed to <see cref="GameSession.Submit"/> verbatim, and every line it prints
/// to standard output is exactly what that call returned.
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
/// <remarks>
/// <strong><c>--seat &lt;nation&gt;</c> (<c>docs/tasks/T83.md</c>, bug #361).</strong> Makes that nation
/// human for the session (<see cref="GameSession"/>'s own constructor parameter carries the actual
/// override, since it also has to reach <see cref="IC2.Engine.Model.GameStateFactory.CreateInitial"/>'s
/// state, not just this class's bookkeeping) and rejects an unknown id with the list of ids, the same
/// shape <c>--scenario</c>/<c>--ruleset</c> already use above.
/// </remarks>
/// <remarks>
/// <strong><c>--load &lt;path&gt;</c> (<c>docs/tasks/T95.md</c>, #467).</strong> The <c>load &lt;path&gt;</c>
/// command alone cannot serve a cold start: it needs an already-constructed <see cref="GameSession"/>
/// whose World/Ruleset already happen to match the save (<see cref="SaveManager.LoadFile"/>'s own check),
/// which means the caller would first have to guess the right <c>--scenario</c>/<c>--ruleset</c> for a
/// save it has not opened yet. <c>--load</c> instead reads the save's own recorded ids first
/// (<see cref="SaveManager.PeekSummaryFile"/>), resolves World/Ruleset/Scenario from those, and builds
/// the session directly from the save through <see cref="GameSession"/>'s resume constructor — so
/// <c>--scenario</c>/<c>--ruleset</c> need never be given (and are ignored, with a note to standard
/// error, if they are). <c>--seed</c> and <c>--seat</c> are ignored the same way: Done-when 3's random
/// stream and Done-when 4's human seat both come from the save itself, never from a flag.
/// </remarks>
/// <remarks>
/// <strong><c>--human-battles &lt;instant|tactical&gt;</c> (<c>docs/tasks/T125.md</c>, #666).</strong> How a field
/// battle with a human side is fought: <c>instant</c> (the default, so every existing transcript is unchanged)
/// resolves it at once; <c>tactical</c> opens the battle on the board, fought with the <c>battle</c> verbs. A session
/// switch, never saved: <c>--load</c> takes the switch given here, and a save that holds an open battle reopens it
/// whatever the switch is.
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

        if (args.Contains("--export-sav"))
        {
            return ExportSav(args);
        }

        string? scriptPath = null;
        ulong? seed = null;
        var scenarioId = DefaultScenarioId;
        string? rulesetOverrideId = null;
        string? seatNationId = null;
        string? loadPath = null;
        NewGameTurnOrderPolicy? turnOrderOverride = null;
        var humanBattles = HumanBattleMode.Instant;

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

                case "--turn-order":
                    // T119: overrides the ruleset's newGameTurnOrder flag for this session only.
                    if (i + 1 >= args.Length
                        || !Enum.TryParse<NewGameTurnOrderPolicy>(args[i + 1], ignoreCase: true, out var parsedOrder)
                        || (parsedOrder != NewGameTurnOrderPolicy.Fixed && parsedOrder != NewGameTurnOrderPolicy.Shuffle))
                    {
                        Console.Error.WriteLine("--turn-order requires fixed or shuffle.");
                        return 1;
                    }

                    i++;
                    turnOrderOverride = parsedOrder;
                    break;

                case "--human-battles":
                    // T125: a session switch, never a rule; the default keeps every existing transcript.
                    if (i + 1 >= args.Length
                        || !Enum.TryParse<HumanBattleMode>(args[i + 1], ignoreCase: true, out var parsedMode)
                        || !Enum.IsDefined(parsedMode))
                    {
                        Console.Error.WriteLine("--human-battles requires instant or tactical.");
                        return 1;
                    }

                    i++;
                    humanBattles = parsedMode;
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

                case "--seat":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--seat requires a nation id.");
                        return 1;
                    }

                    seatNationId = args[++i];
                    break;

                case "--load":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--load requires a file path.");
                        return 1;
                    }

                    loadPath = args[++i];
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

            if (loadPath is not null)
            {
                if (scenarioId != DefaultScenarioId || rulesetOverrideId is not null || seatNationId is not null || seed is not null)
                {
                    Console.Error.WriteLine(
                        "--load resumes the save's own scenario, world, ruleset, seed and human seat; "
                        + "--scenario/--ruleset/--seat/--seed are ignored.");
                }

                var (loadedSession, loadError) = LoadSession(repository, loadPath, humanBattles);
                if (loadError is not null)
                {
                    Console.Error.WriteLine(loadError);
                    return 1;
                }

                session = loadedSession!;
            }
            else
            {
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

                if (turnOrderOverride is not null)
                {
                    resolved = resolved with
                    {
                        Ruleset = resolved.Ruleset with
                        {
                            Flags = resolved.Ruleset.Flags with { NewGameTurnOrder = turnOrderOverride.Value },
                        },
                    };
                }

                // docs/tasks/T83.md Done-when 1: "an unknown nation id is rejected with the list of ids" --
                // the same shape as --scenario/--ruleset above, checked against the resolved World (every
                // nation the world defines has exactly one seat -- GameStateFactory.CreateInitial's own
                // invariant -- so the world's own nation ids are the complete, authoritative list).
                if (seatNationId is not null && resolved.World.NationById(seatNationId) is null)
                {
                    var available = string.Join(
                        ", ", resolved.World.Nations.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal));
                    Console.Error.WriteLine($"Unknown nation '{seatNationId}'. Available nations: {available}.");
                    return 1;
                }

                Console.Error.WriteLine(
                    $"Loaded scenario '{resolved.Scenario.Id}': world '{resolved.World.Id}', ruleset '{resolved.Ruleset.Id}'.");

                session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, seed, seatNationId)
                {
                    HumanBattles = humanBattles,
                };
            }
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

    /// <summary>
    /// <c>--load &lt;path&gt;</c>'s own resolution: reads <paramref name="loadPath"/>'s own recorded
    /// scenario/world/ruleset ids (<see cref="SaveManager.PeekSummaryFile"/>), resolves them against
    /// <paramref name="repository"/>, and builds the session directly from the save through
    /// <see cref="GameSession"/>'s resume constructor — see this class's own <c>--load</c> remarks for why
    /// this cannot simply reuse the ordinary <c>--scenario</c>/<c>--ruleset</c> path above.
    /// </summary>
    /// <returns>
    /// The resumed session, or (<see langword="null"/>, a message already worded for standard error) on
    /// any of the three refusals <c>docs/tasks/T95.md</c> Done-when 5 names: a missing file, a malformed
    /// save, or a save recorded against a world/ruleset/scenario this build does not have.
    /// </returns>
    /// <summary>
    /// T185: the offline converter, an exclusive mode —
    /// <c>--export-sav &lt;native.json&gt; --output &lt;new.sav&gt; [--template &lt;original.sav&gt;] [--config &lt;assets.ini&gt;]</c>. Loads the
    /// native save through <see cref="SaveManager"/> (its migrations included), maps it with
    /// <see cref="OriginalSaveExporter"/>, takes only the interface settings from the template, and writes a new
    /// file without replacing anything. Every refusal is printed with its reason and nothing is written.
    /// </summary>
    private static int ExportSav(string[] args)
    {
        const string usage = "Usage: IC2.Cli --export-sav <native-save.json> --output <new.sav> [--template <original.sav>] [--config <assets.ini>]";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--export-sav" or "--output" or "--template" or "--config"))
            {
                Console.Error.WriteLine($"--export-sav takes only --output, --template and --config; '{args[i]}' is not allowed. {usage}");
                return 1;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"{args[i]} requires a path. {usage}");
                return 1;
            }

            if (!values.TryAdd(args[i], args[++i]))
            {
                Console.Error.WriteLine($"{args[i - 1]} is given twice. {usage}");
                return 1;
            }
        }

        if (!values.TryGetValue("--output", out var output))
        {
            Console.Error.WriteLine($"--export-sav needs --output. {usage}");
            return 1;
        }

        var input = values["--export-sav"];
        try
        {
            var repositoryRoot = FindRepositoryRoot();
            var templatePath = OriginalSaveExportTemplateResolver.Resolve(
                values.GetValueOrDefault("--template"),
                values.GetValueOrDefault("--config") ?? Path.Combine(repositoryRoot, "assets.local.ini"));
            var repository = GameDataRepository.Load(Path.Combine(repositoryRoot, "data"));
            var summary = SaveManager.PeekSummaryFile(input);
            var world = repository.WorldById(summary.WorldId);
            var ruleset = repository.RulesetById(summary.RulesetId);
            if (world is null || ruleset is null)
            {
                Console.Error.WriteLine($"Save '{input}' names a world or ruleset this build does not have.");
                return 1;
            }

            var inputHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input)));
            var save = SaveManager.LoadFile(input, world, ruleset);
            var written = OriginalSaveExporter.ExportFile(save, world, ruleset, templatePath, output);
            var result = written.Export;

            Console.WriteLine($"Exported '{input}' (SHA-256 {inputHash}) to '{written.OutputPath}'.");
            Console.WriteLine($"  output SHA-256 {written.OutputSha256}, {written.Length} bytes, profile {written.ProfileId}");
            Console.WriteLine($"  template '{templatePath}' SHA-256 {written.TemplateSha256} (interface settings only)");
            Console.WriteLine($"  {result.Document.Armies.Count} armies, {result.Document.Fleets.Count} fleets, {result.Document.News.Count} news lines, "
                + $"{summary.Label}, {save.State.Calendar.YearBc} BC");
            foreach (var alias in result.Report.Aliases)
                Console.WriteLine($"  alias: {alias.ArmyId} slot {alias.Slot} '{alias.NativeName}' written as '{alias.ExportedName}'");
            foreach (var omitted in result.Report.Omitted)
                Console.WriteLine($"  not in the original format: {omitted}");
            Console.WriteLine("  Opening the file in the original game is a separate check; this export does not claim it.");
            return 0;
        }
        catch (OriginalSaveExportException ex)
        {
            foreach (var refusal in ex.Refusals)
                Console.Error.WriteLine($"Refused: {refusal.Code} {refusal.Field}: {refusal.Detail}");
            return 2;
        }
        catch (Exception ex) when (ex is GameDataException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"Export failed, nothing written: {ex.Message}");
            return 2;
        }
    }

    private static (GameSession? Session, string? Error) LoadSession(
        GameDataRepository repository, string loadPath, HumanBattleMode humanBattles)
    {
        SaveSummary summary;
        try
        {
            summary = SaveManager.PeekSummaryFile(loadPath);
        }
        catch (GameDataException ex)
        {
            return (null, $"Could not read save '{loadPath}': {ex.Message}");
        }

        var world = repository.WorldById(summary.WorldId);
        var ruleset = repository.RulesetById(summary.RulesetId);
        var scenario = repository.ScenarioById(summary.ScenarioId);
        if (world is null || ruleset is null || scenario is null)
        {
            return (null,
                $"Save '{loadPath}' names a world/ruleset/scenario this build does not have "
                + $"(world '{summary.WorldId}', ruleset '{summary.RulesetId}', scenario '{summary.ScenarioId}').");
        }

        SaveGame save;
        try
        {
            save = SaveManager.LoadFile(loadPath, world, ruleset);
        }
        catch (GameDataException ex)
        {
            return (null, $"Could not load save '{loadPath}': {ex.Message}");
        }

        Console.Error.WriteLine(
            $"Loaded save '{summary.Label}': scenario '{scenario.Id}', world '{world.Id}', "
            + $"ruleset '{ruleset.Id}', turn {summary.TurnIndex}.");

        return (new GameSession(world, ruleset, scenario, save) { HumanBattles = humanBattles }, null);
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
