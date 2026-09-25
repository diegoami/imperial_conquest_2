using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T83.md</c> (bug #361): <c>--seat</c>, the steady turn loop it needs, watch mode for the
/// no-seat all-AI case, and the compact views a big scenario needs to be playable at all.
/// </summary>
public sealed class SeatCliTests
{
    private static readonly string SeatRomeScriptPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "seat-rome.txt");

    private static readonly string SeatRomeGoldenPath =
        Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "cli", "seat-rome.golden.txt");

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static GameSession NewClassicalSeatSession(string nation, ulong? seed = null) =>
        new(Classical.World, Classical.Ruleset, Classical.Scenario, seed, nation);

    private static string RunTranscript(GameSession session, IEnumerable<string> scriptLines)
    {
        var builder = new System.Text.StringBuilder();
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

    // ---- Done-when 5: the new --seat golden, on the scenario's own fixed (committed) seed ----

    /// <summary>
    /// Mirrors <c>GameSessionTests.The_demo_script_run_in_process_matches_the_committed_golden_transcript</c>
    /// exactly, for the new script: <c>--seat rome</c> over two rounds on <c>classical-mediterranean</c>,
    /// under the scenario's own committed <c>randomSeed</c> (270) rather than an override — the same
    /// "fixed seed" <c>demo.golden.txt</c> itself relies on (no <c>--seed</c> flag either). Rome is
    /// <c>classical-mediterranean</c>'s own turn-order seat 0, so this golden never exercises the
    /// construction-time fast-forward (<see cref="GameSession"/>'s own prelude) — that is what
    /// <see cref="Three_ends_in_a_row_on_carthage_each_return_to_carthage_and_carthage_is_never_ai_played"/>
    /// below is for, deliberately using a seat that is <em>not</em> first in turn order.
    /// </summary>
    [Fact]
    public void The_seat_rome_script_run_in_process_matches_the_committed_golden_transcript()
    {
        var scriptLines = File.ReadAllLines(SeatRomeScriptPath);
        var session = NewClassicalSeatSession("rome");

        var transcript = RunTranscript(session, scriptLines);

        var golden = File.ReadAllText(SeatRomeGoldenPath);
        Assert.Equal(golden, transcript);
    }

    /// <summary>Done-when 5's "reproduces byte for byte", proven directly rather than only inferred from matching one golden once.</summary>
    [Fact]
    public void The_seat_rome_script_gives_an_identical_transcript_twice()
    {
        var scriptLines = File.ReadAllLines(SeatRomeScriptPath);

        var first = RunTranscript(NewClassicalSeatSession("rome"), scriptLines);
        var second = RunTranscript(NewClassicalSeatSession("rome"), scriptLines);

        Assert.Equal(first, second);
    }

    // ---- Done-when 1: --seat marks the nation human in the state the engine reads ----

    /// <summary>
    /// The hazard this task's brief names explicitly: the AI's own checks (T82) read
    /// <see cref="NationState.Control"/>, so <c>--seat</c> has to flip that field, not just remember the
    /// id CLI-side. Checked directly against <see cref="GameSession.State"/> rather than through any
    /// rendered text, so this stays pinned even if a display label changes later.
    /// </summary>
    [Fact]
    public void Seat_marks_the_named_nation_human_in_the_state_the_engine_reads()
    {
        var session = NewClassicalSeatSession("carthage");

        Assert.Equal(SeatControl.Human, session.State.NationById("carthage")!.Control);

        // Every other nation is untouched -- --seat adds a human seat, it does not touch anyone else's.
        Assert.Equal(SeatControl.Ai, session.State.NationById("rome")!.Control);
    }

    /// <summary>
    /// Done-when 1: "<c>--seat</c> on a scenario that already has human seats adds this one as human" --
    /// <c>toy-3city</c>'s own <c>north</c> seat is human by scenario default, with no <c>--seat</c> flag
    /// involved at all; naming <c>south</c> on top of it must add a second human seat, not replace the
    /// first.
    /// </summary>
    [Fact]
    public void Seat_is_additive_when_the_scenario_already_seats_a_human()
    {
        var toy = CoreTestbed.Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario, seedOverride: null, humanSeatNationId: "south");

        Assert.Equal(SeatControl.Human, session.State.NationById("north")!.Control);
        Assert.Equal(SeatControl.Human, session.State.NationById("south")!.Control);
    }

    /// <summary>Done-when 1: "an unknown nation id is rejected" -- at the engine boundary, a typed exception rather than a silent no-op or a KeyNotFoundException from somewhere deep inside <see cref="GameStateFactory"/>.</summary>
    [Fact]
    public void Seat_naming_an_unknown_nation_throws_instead_of_silently_doing_nothing()
    {
        var toy = CoreTestbed.Toy;

        var thrown = Record.Exception(() =>
            new GameSession(toy.World, toy.Ruleset, toy.Scenario, seedOverride: null, humanSeatNationId: "atlantis"));

        Assert.IsType<ArgumentException>(thrown);
    }

    // ---- Done-when 2: the turn loop is steady ----

    /// <summary>
    /// Done-when 2, verbatim: "With <c>--seat carthage</c> on <c>classical-mediterranean</c>, three
    /// <c>end</c>s in a row each return to Carthage, and Carthage is never played by the AI." Carthage is
    /// turn-order seat 1 (Rome is seat 0), so the session's own construction-time fast-forward has
    /// already played Rome once before this test's first assertion -- proving the pause point holds from
    /// the very first round, not just once the loop has "warmed up".
    /// </summary>
    [Fact]
    public void Three_ends_in_a_row_on_carthage_each_return_to_carthage_and_carthage_is_never_ai_played()
    {
        var session = NewClassicalSeatSession("carthage");
        Assert.Equal("carthage", session.State.ActiveNationId);

        for (var round = 0; round < 3; round++)
        {
            var output = session.Submit("end");

            Assert.Equal("carthage", session.State.ActiveNationId);
            Assert.DoesNotContain(
                output.Lines,
                line => line.Contains("Carthage (carthage) takes its turn", StringComparison.Ordinal));
        }
    }

    // ---- Done-when 3: all-AI without --seat is watch mode ----

    /// <summary>A local copy of every seat flipped to AI, mirroring <c>GameSessionCommandsTests.AllSeatsHuman</c>'s own pattern in the opposite direction.</summary>
    private static Scenario AllSeatsAi(Scenario scenario) =>
        scenario with
        {
            Seats = ValueList.From(scenario.Seats.Select(seat => seat with { Control = SeatControl.Ai })),
        };

    private static GameSession NewWatchModeSession()
    {
        var toy = CoreTestbed.Toy;
        return new GameSession(toy.World, toy.Ruleset, AllSeatsAi(toy.Scenario));
    }

    [Fact]
    public void Watch_mode_rejects_an_order_naming_seat()
    {
        var session = NewWatchModeSession();
        var startingX = session.State.ArmyById("north-army-1")!.X;

        var output = session.Submit("move north-army-1 4 2");

        Assert.Contains(output.Lines, line => line.Contains("--seat", StringComparison.Ordinal));
        // Refused before dispatch -- the army never actually moved.
        Assert.Equal(startingX, session.State.ArmyById("north-army-1")!.X);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("news")]
    [InlineData("map")]
    [InlineData("help")]
    [InlineData("armies")]
    [InlineData("cities")]
    public void Watch_mode_still_allows_every_read_only_verb(string verb)
    {
        var session = NewWatchModeSession();

        var output = session.Submit(verb);

        Assert.DoesNotContain(output.Lines, line => line.Contains("--seat", StringComparison.Ordinal));
    }

    /// <summary>
    /// Done-when 3: "Each <c>end</c> plays exactly one full round and returns." Before T83, a scenario
    /// with no human seat at all replayed the round-starting seat a second time (bug #361) because the
    /// old loop's only stopping condition was a guard count, never a check that the lap had actually
    /// closed. Proven here the same way the manual transcript in this task's PR proves it for
    /// <c>--seat carthage</c>: the active seat after one <c>end</c> is exactly the seat that was active
    /// before it, never anyone played twice along the way.
    /// </summary>
    [Fact]
    public void Watch_mode_end_plays_exactly_one_full_round()
    {
        var session = NewWatchModeSession();
        var startingSeat = session.State.ActiveNationId;

        var output = session.Submit("end");

        Assert.Equal(startingSeat, session.State.ActiveNationId);

        // Exactly one "takes its turn" line per seat in the turn order -- north and south, no repeats.
        var turnLines = output.Lines.Where(line => line.Contains("takes its turn", StringComparison.Ordinal)).ToList();
        Assert.Equal(session.State.TurnOrder.Count, turnLines.Count);
    }

    // ---- Done-when 4: compact views ----

    private static GameSession NewToySession() => new(CoreTestbed.Toy.World, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.Scenario);

    [Fact]
    public void Status_mine_first_line_is_the_active_seats_own_nation_summary()
    {
        var session = NewToySession();

        var output = session.Submit("status mine");

        Assert.Equal("> status mine", output.Lines[0]);
        Assert.Equal(
            "Nation: Northern League (north, human): treasury 500, unity 600, tax 15%", output.Lines[1]);
    }

    [Fact]
    public void Armies_view_first_line_names_the_filtered_nation_and_excludes_the_other()
    {
        var session = NewToySession();

        var output = session.Submit("armies");

        Assert.Equal("Armies (Northern League (north)):", output.Lines[1]);
        Assert.DoesNotContain(output.Lines, line => line.Contains("south-army-1", StringComparison.Ordinal));
    }

    [Fact]
    public void Cities_view_first_line_names_the_filtered_nation_and_excludes_the_other()
    {
        var session = NewToySession();

        var output = session.Submit("cities");

        Assert.Equal("Cities (Northern League (north)):", output.Lines[1]);
        Assert.DoesNotContain(output.Lines, line => line.Contains("meridia", StringComparison.Ordinal));
    }

    /// <summary>An explicit <c>[nation]</c> argument overrides the "mine" default, regardless of who is currently active.</summary>
    [Fact]
    public void Armies_view_with_an_explicit_nation_argument_filters_to_that_nation_instead()
    {
        var session = NewToySession();

        var output = session.Submit("armies south");

        Assert.Equal("Armies (Southern League (south)):", output.Lines[1]);
        Assert.Contains(output.Lines, line => line.Contains("south-army-1", StringComparison.Ordinal));
        Assert.DoesNotContain(output.Lines, line => line.Contains("north-army-1", StringComparison.Ordinal));
    }

    [Fact]
    public void Cities_view_with_an_unknown_nation_argument_is_rejected_not_thrown()
    {
        var session = NewToySession();
        SessionOutput? output = null;

        var thrown = Record.Exception(() => output = session.Submit("cities atlantis"));

        Assert.Null(thrown);
        Assert.Contains(output!.Lines, line => line.Contains("Unknown nation 'atlantis'", StringComparison.Ordinal));
    }

    /// <summary>Done-when 4: "<c>help</c> lists them" -- only in a <c>--seat</c> session (this task's own "Decision for the user", see the PR body): listing them unconditionally would move <c>tests/fixtures/cli/demo.golden.txt</c>'s own <c>help</c> line, which Done-when 5 forbids.</summary>
    [Fact]
    public void Help_lists_the_compact_views_only_in_a_seat_session()
    {
        var withoutSeat = NewToySession().Submit("help");
        Assert.DoesNotContain(withoutSeat.Lines, line => line.Contains("status mine", StringComparison.Ordinal));

        var toy = CoreTestbed.Toy;
        var seated = new GameSession(toy.World, toy.Ruleset, toy.Scenario, seedOverride: null, humanSeatNationId: "south");
        var withSeat = seated.Submit("help");
        Assert.Contains(withSeat.Lines, line => line.Contains("status mine", StringComparison.Ordinal));
        Assert.Contains(withSeat.Lines, line => line.Contains("armies [nation]", StringComparison.Ordinal));
        Assert.Contains(withSeat.Lines, line => line.Contains("cities [nation]", StringComparison.Ordinal));
    }
}
