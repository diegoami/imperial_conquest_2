using System.Globalization;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;

namespace IC2.Cli;

/// <summary>
/// A thin console wrapper around <see cref="GameSession"/> — <c>docs/task-catalogue.md</c> "T41 Thin CLI
/// demo on the toy world (a walking skeleton)". Holds no game rule and no parsing beyond its own two
/// arguments (<c>--script</c>, <c>--seed</c>): every command line it reads is handed to
/// <see cref="GameSession.Submit"/> verbatim, and every line it prints is exactly what that call returned.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // A fixed, invariant culture and "\n" line endings, so the golden transcript this task ships
        // (tests/fixtures/cli/demo.golden.txt) is byte-stable across machines.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.Out.NewLine = "\n";

        string? scriptPath = null;
        ulong? seed = null;

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

                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        GameSession session;
        try
        {
            var repository = GameDataRepository.Load(Path.Combine(FindRepositoryRoot(), "data"));
            var resolved = repository.Resolve("toy-3city");
            session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, seed);
        }
        catch (GameDataException ex)
        {
            Console.Error.WriteLine($"Could not load the toy scenario: {ex.Message}");
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
