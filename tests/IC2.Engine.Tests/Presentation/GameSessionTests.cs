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
    /// Done-when 2, second half: "A different <c>--seed</c> changes only what the RNG drives."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>T22 adds a third random consumer to this script, and the claim is widened by exactly that
    /// consumer and no further</strong> (<c>docs/build-process.md</c> §2.3: the list may grow when a task
    /// legitimately adds a draw, provided the new draw is <em>named</em> and the differing fields are
    /// still asserted to be <em>exactly</em> the known ones). The three, in draw order:
    /// </para>
    /// <list type="number">
    /// <item><see cref="Economy.WeatherEventSystem"/> — surfaced as a "  Weather: ..." line, dropped
    /// whole, since every character of one is random-driven and a fired event also shifts every later
    /// line's position.</item>
    /// <item><see cref="Economy.CityLoyaltyDraws"/> (T35) — its quarterly rise/fall rolls move the
    /// <c>loyalty NN</c> field of a "Cities:" line. Its rebellion-risk event never fires in this script,
    /// so the news log is unaffected either way.</item>
    /// <item><strong>New with T22:</strong> <see cref="Battle.BattleCasualties"/>'s per-unit casualty
    /// divisor, drawn inside <see cref="Battle.InstantBattleResolver"/>'s field resolution. The demo's
    /// <c>south</c> seat is AI-controlled, and the AI now attacks <c>north-army-1</c> rather than passing,
    /// so the winner's surviving troop count differs between seeds.</item>
    /// </list>
    /// <para>
    /// <strong>Only the third of those is a draw; two of the four fields it moves are arithmetic.</strong>
    /// This distinction matters, because widening the blank list by three fields when only one new draw
    /// exists would be the loosening §2.3 forbids. A surviving army's <c>supply NNt (NN%)</c> is a
    /// function of its troop count (capacity is <c>troops / armySupplyTonsPerTroops</c>, the percentage
    /// <c>supplies × supplyPercentNumerator / troops</c>), and the Southern League's <c>treasury</c> is a
    /// function of the same count through per-troop quarterly army upkeep. Both move because the casualty
    /// draw moved, not because anything else rolled.
    /// </para>
    /// <para>
    /// <strong>What is still asserted byte-for-byte across seeds</strong>, checked against 25 seeds before
    /// this list was written: every other line in the transcript; the surviving army's <em>unit count</em>,
    /// position, morale and moves; every city's supply tonnage and fortification percentage; every nation's
    /// unity and tax rate; and the whole news log, including the order of its entries.
    /// <see cref="The_fields_this_test_blanks_really_do_move_with_the_seed"/> is the other half of the
    /// claim: each blanked field has to genuinely vary, so the list cannot quietly grow to cover something
    /// that does not.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// The other half of the claim above: every field <see cref="WithoutRandomDrivenText"/> blanks has to
    /// <em>actually</em> vary with the seed. Without this, the blank list could be widened to cover a
    /// field nothing rolls, and the equality assertion would keep passing while checking less.
    /// </summary>
    [Fact]
    public void The_fields_this_test_blanks_really_do_move_with_the_seed()
    {
        var scriptLines = File.ReadAllLines(DemoScriptPath);

        // Twelve seeds, because two adjacent seeds are not guaranteed to differ in every blanked field --
        // a loyalty roll landing the same way twice is ordinary, not a defect.
        var transcripts = new List<string>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            transcripts.Add(RunTranscript(NewSession(seed), scriptLines));
        }

        // Over the played part of the transcript only -- the same slice WithoutRandomDrivenText blanks
        // in, so the control and the normalizer cannot disagree about what is covered.
        var played = new List<string>();
        foreach (var transcript in transcripts)
        {
            played.Add(AfterTheFirstTurn(transcript));
        }

        AssertVaries(played, WeatherLinePattern, "a Weather: line");
        AssertVaries(played, LoyaltyFieldPattern, "a city's loyalty");
        AssertVaries(played, ArmyTroopCountPattern, "a surviving army's troop count");
        AssertVaries(played, UnitSupplyFieldPattern, "a surviving army's supply");
        AssertVaries(played, TreasuryFieldPattern, "the Southern League's treasury");
    }

    /// <summary>
    /// Fails unless <paramref name="pattern"/> matches something, and unless <strong>every one of its
    /// occurrences</strong> takes more than one value across the runs.
    /// </summary>
    /// <remarks>
    /// Review round 1, F3: this used to join a run's matches into one string and compare those, which
    /// proved only "<em>at least one</em> occurrence varies" — one granularity looser than the claim it
    /// is here to defend. A pattern covering six treasury figures of which two varied would have passed
    /// while blanking four that are invariant, which is the quiet widening
    /// <c>docs/build-process.md</c> §2.3 forbids. Comparing per occurrence index closes that: an
    /// occurrence present in some runs and absent in others counts as varying, which is how the weather
    /// lines (whose count itself changes with the seed) are handled without a special case.
    /// </remarks>
    private static void AssertVaries(List<string> transcripts, Regex pattern, string what)
    {
        var perRun = new List<string[]>();
        foreach (var transcript in transcripts)
        {
            var matches = new List<string>();
            foreach (Match match in pattern.Matches(transcript))
            {
                matches.Add(match.Value);
            }

            perRun.Add(matches.ToArray());
        }

        var occurrences = 0;
        foreach (var matches in perRun)
        {
            occurrences = Math.Max(occurrences, matches.Length);
        }

        Assert.True(occurrences > 0, $"{what} never appears in the transcript at all");

        for (var i = 0; i < occurrences; i++)
        {
            var distinct = new List<string>();
            foreach (var matches in perRun)
            {
                var value = i < matches.Length ? matches[i] : "<absent>";
                if (!distinct.Contains(value))
                {
                    distinct.Add(value);
                }
            }

            Assert.True(
                distinct.Count > 1,
                $"{what}: occurrence {i + 1} of {occurrences} is blanked by this test but never varies "
                + $"with the seed (it reads \"{distinct[0]}\" in all {perRun.Count} runs). Either narrow "
                + "the pattern so it stops blanking an invariant field, or say which draw reaches it.");
        }
    }

    /// <summary>A whole weather line, for the varies-with-the-seed control.</summary>
    private static readonly Regex WeatherLinePattern =
        new(@"Weather: \S+ \(week \d+\)\.", RegexOptions.Compiled);

    /// <summary>Matches exactly the "loyalty NN" field <see cref="GameSessionRendering.RenderStatus"/> prints
    /// in a "Cities:" line -- the one field <see cref="Economy.CityLoyaltyDraws"/> can change.</summary>
    private static readonly Regex LoyaltyFieldPattern = new(@"loyalty \d+", RegexOptions.Compiled);

    /// <summary>
    /// The troop count of an "Armies:" line, and <em>only</em> the count: the "in N units" that follows is
    /// matched as a look-ahead and therefore left in place, so the unit count stays asserted exactly. This
    /// is the one field T22's new draw -- the battle casualty divisor -- actually moves.
    /// </summary>
    private static readonly Regex ArmyTroopCountPattern =
        new(@"\d+(?= troops in \d+ units)", RegexOptions.Compiled);

    /// <summary>
    /// A unit's "supply NNt (NN%)" -- arithmetic on the troop count above, not a draw of its own. The
    /// trailing percentage is what tells this apart from a <em>city's</em> "supply NNNNt", which carries no
    /// percentage and stays asserted exactly.
    /// </summary>
    private static readonly Regex UnitSupplyFieldPattern =
        new(@"supply \d+t \(\d+%\)", RegexOptions.Compiled);

    /// <summary>
    /// The <em>Southern League's</em> "treasury NNNN", and only its own: arithmetic on the surviving
    /// troop count, through per-troop quarterly army upkeep. Signed, because that nation ends this script
    /// in debt.
    /// </summary>
    /// <remarks>
    /// Review round 1, F3: this used to match every nation's treasury, six occurrences of which only the
    /// Southern League's two vary — the Northern League's reads 471 at every seed, because the battle
    /// destroys its army and an army it no longer has cannot bill it a varying upkeep. Blanking four
    /// invariant figures to reach two varying ones is a wider hole than the claim needs, so the lookbehind
    /// keeps the Northern League's treasury asserted exactly. <see cref="AssertVaries"/> now checks each
    /// occurrence separately, so this narrowing is enforced rather than merely intended.
    /// </remarks>
    private static readonly Regex TreasuryFieldPattern =
        new(@"(?<=\(south, \w+\): )treasury -?\d+", RegexOptions.Compiled);

    /// <summary>
    /// The echoed command that marks the first turn being played. Everything before it is output from a
    /// state no draw has touched yet.
    /// </summary>
    private const string FirstTurnMarker = "> end";

    /// <summary>
    /// The part of a transcript from the first played turn onwards — the only part any of the three
    /// random consumers can have reached.
    /// </summary>
    /// <remarks>
    /// Review round 1, F3 (second half). Tightening <see cref="AssertVaries"/> to per-occurrence
    /// granularity immediately caught a second over-blank: the transcript's opening <c>status</c> prints
    /// each city's loyalty <em>before any turn has been played</em>, so those values are identical at
    /// every seed by construction — no quarterly draw has happened yet. Blanking them hid three lines of
    /// starting state that ought to be pinned exactly. Splitting here keeps the whole pre-turn section —
    /// the opening <c>status</c>, the <c>map</c>, and the move results — asserted byte for byte.
    /// </remarks>
    private static string AfterTheFirstTurn(string transcript)
    {
        var index = transcript.IndexOf(FirstTurnMarker, StringComparison.Ordinal);
        return index < 0 ? transcript : transcript[index..];
    }

    /// <summary>
    /// Leaves everything before the first played turn untouched, then, in the rest, drops the weather
    /// lines entirely (every character of one is random-driven, and a fired event also shifts every later
    /// line) and blanks exactly the four fields the three named draws reach — and nothing else. Every
    /// other character of every other line still has to match byte for byte.
    /// </summary>
    private static string WithoutRandomDrivenText(string transcript)
    {
        var index = transcript.IndexOf(FirstTurnMarker, StringComparison.Ordinal);
        var head = index < 0 ? string.Empty : transcript[..index];
        var tail = index < 0 ? transcript : transcript[index..];

        return head + string.Join(
            '\n',
            tail.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("Weather:", StringComparison.Ordinal))
                .Select(line => LoyaltyFieldPattern.Replace(line, "loyalty ##"))
                .Select(line => ArmyTroopCountPattern.Replace(line, "####"))
                .Select(line => UnitSupplyFieldPattern.Replace(line, "supply ##t (##%)"))
                .Select(line => TreasuryFieldPattern.Replace(line, "treasury ####")));
    }

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
