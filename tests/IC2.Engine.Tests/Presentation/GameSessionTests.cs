using System.Text;
using System.Text.RegularExpressions;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy world", Done-when 1 and 2.
/// </summary>
public sealed class GameSessionTests
{
    private static readonly string DemoScriptPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "demo.txt");

    private static readonly string GoldenPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "demo.golden.txt");

    private static GameSession NewSession(ulong? seed = null) =>
        new(CoreTestbed.Toy.World, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.Scenario, seed);

    /// <summary>Runs every line through <paramref name="session"/> and renders exactly what the CLI would print.</summary>
    private static string RunTranscript(GameSession session, IEnumerable<string> scriptLines)
    {
        var builder = new StringBuilder();
        foreach (var line in scriptLines)
        {
            var output = session.Submit(line);
            foreach (var text in output.Lines)
            {
                builder.Append(text).Append('\n');
            }

            if (output.ShouldExit)
            {
                break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Done-when 1: "<c>dotnet run --project src/IC2.Cli -- --script tests/fixtures/cli/demo.txt</c> exits
    /// 0, and its output equals the committed <c>tests/fixtures/cli/demo.golden.txt</c> byte for byte. A
    /// test runs the same session in-process, through the Presentation session, and asserts the same."
    /// </summary>
    [Fact]
    public void The_demo_script_run_in_process_matches_the_committed_golden_transcript()
    {
        var scriptLines = File.ReadAllLines(DemoScriptPath);
        var session = NewSession();

        var transcript = RunTranscript(session, scriptLines);

        var golden = File.ReadAllText(GoldenPath);
        Assert.Equal(golden, transcript);
    }

    /// <summary>Done-when 2, first half: "The same seed gives an identical transcript twice."</summary>
    [Fact]
    public void The_same_seed_gives_an_identical_transcript_twice()
    {
        var scriptLines = File.ReadAllLines(DemoScriptPath);

        var first = RunTranscript(NewSession(20250913), scriptLines);
        var second = RunTranscript(NewSession(20250913), scriptLines);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Done-when 2, second half: "A different <c>--seed</c> changes only what the RNG drives." Two systems
    /// in this script read <c>SystemContext.Rng</c>, in draw order: <see cref="Economy.WeatherEventSystem"/>
    /// (<see cref="GameSession"/> surfaces it as a "  Weather: ..." line distinct from the news log, which
    /// this script never populates) and, once the script's <c>end</c> commands cross a quarter boundary,
    /// <see cref="Economy.CityLoyaltyDraws"/> (T35 — its rise/fall rolls change a city's <c>loyalty NN</c>
    /// field in the "Cities:" listing; its rebellion-risk event never fires in this script, so the news log
    /// stays empty either way). Nothing else in the transcript reads the RNG, so every other line, and every
    /// other field of the "Cities:" lines, is asserted byte-for-byte equal.
    /// </summary>
    [Fact]
    public void A_different_seed_changes_only_the_weather_and_loyalty_lines()
    {
        var scriptLines = File.ReadAllLines(DemoScriptPath);

        var first = RunTranscript(NewSession(1), scriptLines);
        var second = RunTranscript(NewSession(2), scriptLines);

        // The two seeds really do produce a different transcript -- otherwise the rest of this test would
        // be vacuously true.
        Assert.NotEqual(first, second);

        var firstNormalized = WithoutRandomDrivenText(first);
        var secondNormalized = WithoutRandomDrivenText(second);
        Assert.Equal(firstNormalized, secondNormalized);
    }

    /// <summary>Matches exactly the "loyalty NN" field <see cref="GameSessionRendering.RenderStatus"/> prints
    /// in a "Cities:" line -- the one field <see cref="Economy.CityLoyaltyDraws"/> can change.</summary>
    private static readonly Regex LoyaltyFieldPattern = new(@"loyalty \d+", RegexOptions.Compiled);

    /// <summary>
    /// Drops the weather lines entirely (every character of one is random-driven) and, in every remaining
    /// line, blanks out only the "loyalty NN" field (the one field the quarterly loyalty draws can change).
    /// Everything else in every line -- including the rest of each "Cities:" line: id, name, coordinates,
    /// owner, supply, fortification -- is left untouched, so it still has to match exactly.
    /// </summary>
    private static string WithoutRandomDrivenText(string transcript) =>
        string.Join(
            '\n',
            transcript.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("Weather:", StringComparison.Ordinal))
                .Select(line => LoyaltyFieldPattern.Replace(line, "loyalty ##")));

    /// <summary>
    /// Review round 1: "make sure no other session command can throw on bad input: unknown army or
    /// city ids, non-numeric or negative tons and coordinates, missing arguments." Every one of these
    /// must come back as an ordinary printed line, never an exception escaping <see cref="GameSession.Submit"/>.
    /// </summary>
    [Theory]
    [InlineData("move")]
    [InlineData("move north-army-1")]
    [InlineData("move north-army-1 4")]
    [InlineData("move north-army-1 abc 2")]
    [InlineData("move north-army-1 4 abc")]
    [InlineData("move north-army-1 999999999999999999999 2")]
    [InlineData("move ghost-army 4 2")]
    [InlineData("move north-army-1 -1 -1")]
    [InlineData("move north-army-1 999 999")]
    [InlineData("buy")]
    [InlineData("buy north-army-1")]
    [InlineData("buy north-army-1 arx")]
    [InlineData("buy north-army-1 arx abc")]
    [InlineData("buy north-army-1 arx -5")]
    [InlineData("buy north-army-1 arx 0")]
    [InlineData("buy ghost-army arx 5")]
    [InlineData("buy north-army-1 ghost-city 5")]
    [InlineData("buy north-army-1 arx 999999999999999999999")]
    [InlineData("gibberish")]
    [InlineData("")]
    [InlineData("   ")]
    public void Submit_never_throws_on_malformed_or_illegal_input(string line)
    {
        var session = NewSession();

        var thrown = Record.Exception(() => session.Submit(line));

        Assert.Null(thrown);
    }
}
