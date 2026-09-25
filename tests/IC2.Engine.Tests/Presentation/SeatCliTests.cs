using System.Reflection;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
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
    /// Done-when 1, the user's decision of 2026-09-25 on PR #375's review (N8), replacing round 1's own
    /// "additive" choice: "<c>--seat</c> makes its nation the ONLY human seat, and the scenario's other
    /// human seats are played by the AI." <c>toy-3city</c>'s own <c>north</c> seat is human by scenario
    /// default, with no <c>--seat</c> flag involved at all; naming <c>south</c> instead must hand
    /// <c>north</c> to the AI, not leave it a second, unplayed human seat (round 1's own N8: an
    /// AI-skipped, never-played human seat printed "0 orders issued" forever).
    /// </summary>
    [Fact]
    public void Seat_makes_its_nation_the_only_human_seat_and_hands_any_other_to_the_ai()
    {
        var toy = CoreTestbed.Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario, seedOverride: null, humanSeatNationId: "south");

        Assert.Equal(SeatControl.Ai, session.State.NationById("north")!.Control);
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

        // Review round 1, N1: "never played by the AI" is asserted directly on Carthage's own armies, not
        // only by the absence of a rendered "takes its turn" line -- a mutation that deleted the pause
        // condition entirely (so Carthage got AI-played every round, exactly like every other seat) would
        // still print no such line for Carthage specifically (it would print "Carthage ends its turn"
        // instead, from HandleEndSeated's own unconditional first RunTurn call), so the line-absence check
        // alone cannot tell "paused correctly" apart from "played as AI under a different label". Carthage
        // starts with exactly two armies (army-2 and army-3); an AI-controlled Carthage would very likely
        // move at least one of them within three rounds (16-nation classical-mediterranean's own AI is far
        // from idle -- see the seat-rome golden's own "orders issued" counts), so their positions staying
        // fixed is the AI-was-never-in-control claim itself, not a proxy for it.
        var startingPositions = session.State.Armies
            .Where(a => string.Equals(a.Nation, "carthage", StringComparison.Ordinal))
            .ToDictionary(a => a.Id, a => (a.X, a.Y));
        Assert.Equal(2, startingPositions.Count);

        for (var round = 0; round < 3; round++)
        {
            var output = session.Submit("end");

            Assert.Equal("carthage", session.State.ActiveNationId);
            Assert.DoesNotContain(
                output.Lines,
                line => line.Contains("Carthage (carthage) takes its turn", StringComparison.Ordinal));

            foreach (var army in session.State.Armies)
            {
                if (startingPositions.TryGetValue(army.Id, out var startingPosition))
                {
                    Assert.Equal(startingPosition, (army.X, army.Y));
                }
            }
        }
    }

    /// <summary>
    /// Review round 1, B1 (blocking): the construction-time prelude (AI seats played before the CLI's own
    /// <c>--seat</c> nation is reached in round 1 — <see cref="AdvanceToHumanSeat"/>) was flushed onto the
    /// first <see cref="GameSession.Submit"/> call with no test visiting it at all: a mutation that turned
    /// that flush into a no-op left every other test green. This asserts the flush's exact shape: the
    /// first <c>Submit</c>'s lines begin with the prelude's own "takes its turn" line, then a blank
    /// separator, then the echoed input — and the <em>second</em> <c>Submit</c> carries no prelude at all,
    /// since it was already consumed. Carthage (turn-order seat 1) is used deliberately, the same reason
    /// the seat-rome golden uses Rome (seat 0, no prelude) instead: this is the one case a prelude exists
    /// to test.
    /// </summary>
    [Fact]
    public void The_first_submit_flushes_the_construction_time_prelude_and_the_second_does_not()
    {
        var session = NewClassicalSeatSession("carthage");

        var first = session.Submit("end");

        Assert.True(first.Lines.Count >= 3, "Expected at least a prelude line, a blank separator and the echoed input.");
        Assert.StartsWith("Rome (rome) takes its turn: ", first.Lines[0]);
        Assert.EndsWith(" issued.", first.Lines[0]);
        Assert.Equal(string.Empty, first.Lines[1]);
        Assert.Equal("> end", first.Lines[2]);

        var second = session.Submit("end");

        // "Rome takes its turn" legitimately reappears in every round's own AI loop (Rome plays right
        // before the lap returns to Carthage each time), so the discriminator is not that line's absence
        // but where the transcript starts: a real per-round line always follows "Carthage ends its turn",
        // never opens the transcript the way the one-off prelude flush does.
        Assert.Equal("> end", second.Lines[0]);
        Assert.Equal("Carthage (carthage) ends its turn.", second.Lines[1]);
    }

    /// <summary>
    /// Review round 2, B4 (blocking): <c>_pendingNewsBaseline</c> (round 1's own N4 fix, "news written
    /// during the prelude must appear in the first <c>end</c>'s summary") had no test that fails when it
    /// is deleted. Thracia is <c>classical-mediterranean</c>'s own last turn-order seat, so its
    /// construction-time prelude plays all 15 other seats before Thracia's own first turn — including
    /// Seleucid, which forms an alliance with Thracia during that prelude, on this fixed seed. Without the
    /// baseline fix, that line would only ever surface through the standalone <c>news</c> command, never
    /// through any <c>end</c>'s own footer (confirmed against the real CLI, matching the review's own
    /// probe exactly).
    /// </summary>
    [Fact]
    public void The_first_rounds_footer_includes_news_the_construction_time_prelude_itself_produced()
    {
        var session = NewClassicalSeatSession("thracia");

        var output = session.Submit("end");

        Assert.Contains(
            output.Lines,
            l => l.Contains("Seleucid forms an alliance with Thracia.", StringComparison.Ordinal));
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
    /// Review round 2, B2 (blocking regression): every one of the 28 mutating verbs <see cref="Submit"/>
    /// recognises, gated in watch mode, and proved by more than a rendered rejection line — the state
    /// itself must come out byte-for-byte unchanged (<see cref="GameState"/>'s own record equality, which
    /// <see cref="ValueList{T}"/> backs with sequence equality, not reference equality). This is what
    /// actually catches B2's own regression: <c>attack-army</c> and <c>besiege-city</c> are given a target
    /// nation still at peace, so if <see cref="GameSession.Commands.ComposeDeclareWarIfNeeded"/>'s own gate
    /// were ever lost again, the composed <c>diplomacy.declare-war</c> would flip a relation cell — a real
    /// state change the rendered "rejected" line for the attack/siege itself would never reveal, since
    /// that composed declaration is a <em>second</em>, separate dispatch outside the one
    /// <see cref="IssueCommand"/> gates. <see cref="AllMutatingVerbLines"/>'s own count is asserted too, so
    /// this test cannot silently stop covering a verb <see cref="Submit"/> gains later.
    /// </summary>
    private static readonly (string Verb, string Line)[] AllMutatingVerbLines =
    {
        ("move", "move north-army-1 5 5"),
        ("buy", "buy north-army-1 arx 5"),
        ("attack-army", "attack-army north-army-1 south-army-1"),
        ("besiege-city", "besiege-city north-army-1 meridia"),
        ("attack-fleet", "attack-fleet north-fleet-1 south-fleet-1"),
        ("disband-army", "disband-army north-army-1"),
        ("join-armies", "join-armies north-army-1 south-army-1"),
        ("join-units", "join-units north-army-1 0 1"),
        ("split-army", "split-army north-army-1 north-army-2 0"),
        ("order-city", "order-city arx fortify 1"),
        ("declare-war", "declare-war south"),
        ("make-peace", "make-peace south"),
        ("propose-alliance", "propose-alliance south"),
        ("propose-trade", "propose-trade south"),
        ("accept-offer", "accept-offer"),
        ("mobilize", "mobilize 0 north-recruit-army"),
        ("hire-mercenary", "hire-mercenary north-army-1 0"),
        ("recruit-standing", "recruit-standing arx heavy_infantry 100"),
        ("move-fleet", "move-fleet north-fleet-1 5 5"),
        ("order-fleet", "order-fleet arx 1 north-fleet-2"),
        ("repair-fleet", "repair-fleet north-fleet-1 1"),
        ("scuttle-fleet", "scuttle-fleet north-fleet-1"),
        ("split-fleet", "split-fleet north-fleet-1 north-fleet-2 1"),
        ("join-fleets", "join-fleets north-fleet-1 south-fleet-1"),
        ("embark-army", "embark-army north-army-1 north-fleet-1"),
        ("disembark-army", "disembark-army north-army-1"),
        ("buy-fleet-supply", "buy-fleet-supply north-fleet-1 arx 1"),
        ("fleet-transfer", "fleet-transfer north-fleet-1 north-fleet-2 1 1 1"),
    };

    /// <summary>
    /// Every id above is <c>toy-3city</c>'s own: <c>north-army-1</c>/<c>south-army-1</c>,
    /// <c>north-fleet-1</c>/<c>south-fleet-1</c>, <c>arx</c> (north's own city) and <c>meridia</c> (south's
    /// only city, adjacent enough for a plausible siege). North and south start at peace, so
    /// <c>attack-army</c>/<c>besiege-city</c> are exactly the B2 repro shape. Each command's own engine-side
    /// legality is irrelevant here — some of these would be refused even outside watch mode (a fresh
    /// <c>north-army-1</c> has only one unit, so <c>join-units</c> fails its own gate regardless) — what
    /// matters is that <em>none</em> of them ever reach the dispatcher at all: the message names
    /// <c>--seat</c>, and the state is the literal same value, not merely "no obvious side effect".
    /// </summary>
    private static void AssertEveryMutatingVerbIsRejectedWithNoStateChange(GameSession session)
    {
        Assert.Equal(28, AllMutatingVerbLines.Length);

        foreach (var (verb, line) in AllMutatingVerbLines)
        {
            var before = session.State;

            var output = session.Submit(line);

            Assert.DoesNotContain(
                output.Lines, l => l.Contains("accepted", StringComparison.Ordinal));
            Assert.Contains(
                output.Lines, l => l.Contains("--seat", StringComparison.Ordinal));
            Assert.Equal(before, session.State);
        }
    }

    /// <summary>
    /// <c>[Fact(Timeout = ...)]</c> needs an <see langword="async"/> test to actually enforce the timeout
    /// (a synchronous one throws "Tests marked with Timeout are only supported for async tests" before
    /// ever running) — <see cref="Task.Run(Action)"/> gives every timeout-guarded test here a background
    /// thread xUnit can actually stop waiting on.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task Watch_mode_rejects_every_mutating_verb_and_leaves_state_untouched()
    {
        await Task.Run(() => AssertEveryMutatingVerbIsRejectedWithNoStateChange(NewWatchModeSession()));
    }

    /// <summary>
    /// The same sweep, once the CLI's own <c>--seat</c> nation has fallen — <c>_seatLost</c> flipped
    /// directly (the field <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/> sets), so every one of
    /// north's armies, fleets and cities is still exactly as the scenario started it. That isolates the
    /// gate itself from elimination's own side effects (a real loss can disband the loser's armies, T84),
    /// so the same <see cref="AllMutatingVerbLines"/> ids stay valid without a second, elimination-specific
    /// set.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task A_lost_seat_rejects_every_mutating_verb_and_leaves_state_untouched()
    {
        await Task.Run(() =>
        {
            var toy = CoreTestbed.Toy;
            var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario, seedOverride: null, humanSeatNationId: "north");
            typeof(GameSession).GetField("_seatLost", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(session, true);

            AssertEveryMutatingVerbIsRejectedWithNoStateChange(session);
        });
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

    // ---- Hazard: is the --seat human flag saved with the state, or session-only? ----

    /// <summary>
    /// <c>docs/tasks/T83.md</c>'s own hazard: "If a <c>--seat</c> session is saved, record whether the
    /// seat's human flag is saved with it or is session-only, and test a reload." It is saved with it:
    /// <c>--seat</c> writes straight into <see cref="NationState.Control"/> (see
    /// <see cref="Seat_marks_the_named_nation_human_in_the_state_the_engine_reads"/> above), which is part
    /// of <see cref="GameState.Nations"/> and therefore part of every <see cref="SaveGame"/> built from
    /// <see cref="GameSession.State"/> — nothing <c>--seat</c>-specific needed adding to T20's own save
    /// format (<c>src/IC2.Engine/Persistence/**</c>, outside this task's Owns list) for this to round-trip;
    /// it already carries every nation's <c>Control</c> field. Proven the same way <c>FieldBattleTests</c>'s
    /// own delete-class round-trip check does: through <see cref="SaveManager.Serialize"/>/
    /// <see cref="SaveManager.Load"/>, not just re-reading the in-memory record.
    /// </summary>
    [Fact]
    public void The_seat_human_flag_is_saved_with_the_state_not_session_only()
    {
        var session = NewClassicalSeatSession("carthage");
        var save = new SaveGame(
            SchemaVersion: GameDataSchema.CurrentVersion,
            Id: "t83-seat-probe",
            Label: "T83 --seat probe",
            ScenarioId: session.State.ScenarioId,
            WorldId: session.State.WorldId,
            RulesetId: session.State.RulesetId,
            State: session.State);

        var json = SaveManager.Serialize(save);
        var reloaded = SaveManager.Load("t83-seat-probe.json", json, Classical.World, Classical.Ruleset);

        Assert.Equal(SeatControl.Human, reloaded.State.NationById("carthage")!.Control);
        Assert.Equal(json, SaveManager.Serialize(reloaded));
    }

    /// <summary>
    /// Done-when 4 ("<c>help</c> lists them"), as the user decided on PR #375's review: listed in a
    /// <c>--seat</c> session and in watch mode, but not in a session with a scenario-assigned human seat
    /// and no <c>--seat</c> (exactly <c>toy-3city</c>'s own default, which keeps
    /// <c>tests/fixtures/cli/demo.golden.txt</c>'s <c>help</c> line unchanged — Done-when 5).
    /// </summary>
    [Fact]
    public void Help_lists_the_compact_views_in_a_seat_session_and_in_watch_mode_but_not_in_plain_hotseat()
    {
        var plainHotseat = NewToySession().Submit("help");
        Assert.DoesNotContain(plainHotseat.Lines, line => line.Contains("status mine", StringComparison.Ordinal));

        var toy = CoreTestbed.Toy;
        var seated = new GameSession(toy.World, toy.Ruleset, toy.Scenario, seedOverride: null, humanSeatNationId: "south");
        var withSeat = seated.Submit("help");
        Assert.Contains(withSeat.Lines, line => line.Contains("status mine", StringComparison.Ordinal));
        Assert.Contains(withSeat.Lines, line => line.Contains("armies [nation]", StringComparison.Ordinal));
        Assert.Contains(withSeat.Lines, line => line.Contains("cities [nation]", StringComparison.Ordinal));

        var watchMode = NewWatchModeSession().Submit("help");
        Assert.Contains(watchMode.Lines, line => line.Contains("status mine", StringComparison.Ordinal));
        Assert.Contains(watchMode.Lines, line => line.Contains("armies [nation]", StringComparison.Ordinal));
        Assert.Contains(watchMode.Lines, line => line.Contains("cities [nation]", StringComparison.Ordinal));
    }

    // ---- Done-when 7 (user decision, 2026-09-25, PR #375 review N2/N3): the seat is lost ----

    /// <summary>
    /// The same scripted world <c>GameSessionCommandsTests.HandleEnd_prints_the_dash_wrapped_elimination_…</c>
    /// builds: north's one starting army is replaced with a 400,000-archer force next to south's only
    /// city, and both treasuries are zeroed so the AI's economy phase proposes nothing (only military and
    /// diplomacy compete). Review round 2, B3: this is a <em>real</em> elimination through actual play, not
    /// <see cref="NationState"/> set by reflection — the reviewer's own repro. <paramref name="turnOrder"/>
    /// picks which seat plays first, so the same fixture proves the loss is caught whether it happens
    /// during <see cref="GameSession"/>'s own construction-time prelude or inside a later <c>end</c>.
    /// </summary>
    private static GameSession NewEliminationFixtureSession(ValueList<string> turnOrder, string humanSeatNationId)
    {
        var toy = CoreTestbed.Toy;
        var world = toy.World with
        {
            Nations = ValueList.Of(
                toy.World.NationById("north")! with { Treasury = 0 },
                toy.World.NationById("south")! with { Treasury = 0 }),
            StartingArmies = ValueList.Of(
                new StartingArmy(
                    "north-overwhelming-army", "north", X: 3, Y: 2, Morale: 60, Money: 0, SupplyTons: 0,
                    Moves: 1, Units: ValueList.Of(CaptureFixtures.Unit("archers", 400_000)))),
            TurnOrder = turnOrder,
        };
        var scenario = toy.Scenario with
        {
            Seats = ValueList.Of(
                new Seat("south", SeatControl.Ai),
                new Seat(
                    "north", SeatControl.Ai,
                    new AiPersonality(Aggression: 0.5, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5))),
        };
        return new GameSession(world, toy.Ruleset, scenario, seedOverride: null, humanSeatNationId);
    }

    /// <summary>
    /// Done-when 7: "If the <c>--seat</c> nation is eliminated ... the CLI prints that it has fallen and
    /// continues in watch mode: one round per <c>end</c>, no orders, and no seat played twice." South is
    /// turn-order seat 0 here (no prelude), so the fixture's own "two rounds, not one" timing applies
    /// exactly as it does in <c>GameSessionCommandsTests</c>: round 1 is the approach march only, round 2
    /// is the siege, capture and elimination.
    /// </summary>
    [Fact]
    public void A_seat_eliminated_by_real_play_falls_when_it_plays_first_in_turn_order()
    {
        var session = NewEliminationFixtureSession(ValueList.Of("south", "north"), "south");
        Assert.Equal("south", session.State.ActiveNationId); // turn-order seat 0: no prelude.

        var round1 = session.Submit("end");
        Assert.False(session.State.NationById("south")!.Eliminated, "round 1 is only the approach march");
        Assert.DoesNotContain(round1.Lines, l => l.Contains("has fallen", StringComparison.Ordinal));

        var round2 = session.Submit("end");
        Assert.True(session.State.NationById("south")!.Eliminated);
        Assert.Contains(
            round2.Lines,
            l => l.Contains("Southern League", StringComparison.Ordinal)
                 && l.Contains("has fallen", StringComparison.Ordinal));

        // Watch mode from here on: exactly one "takes its turn" line (north, the sole survivor), never
        // played twice, and orders are refused.
        var round3 = session.Submit("end");
        var turnLines = round3.Lines.Where(l => l.Contains("takes its turn", StringComparison.Ordinal)).ToList();
        Assert.Single(turnLines);
        Assert.StartsWith("Northern League (north)", turnLines[0]);

        var rejected = session.Submit("move north-overwhelming-army 1 1");
        Assert.Contains(rejected.Lines, l => l.Contains("--seat", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same real elimination, with south turn-order seat 1 instead — the construction-time prelude
    /// plays north's round-1 march silently (still just an approach, not yet adjacent), so south's own
    /// <em>first</em> <c>Submit("end")</c> is the round that plays north a second time, now adjacent, and
    /// catches the loss right after that AI turn — proving <see cref="AnnounceAndAdoptWatchModeIfSeatIsLost"/>
    /// fires from inside <see cref="PlayUntilOneFullLapOrRepeat"/>'s own loop, not only from the check
    /// <see cref="HandleEndSeated"/> runs right after the seat's own turn.
    /// </summary>
    [Fact]
    public void A_seat_eliminated_by_real_play_falls_on_its_own_first_end_when_it_plays_second_in_turn_order()
    {
        var session = NewEliminationFixtureSession(ValueList.Of("north", "south"), "south");
        Assert.Equal("south", session.State.ActiveNationId); // the prelude already played north's round 1.
        Assert.False(session.State.NationById("south")!.Eliminated);

        var output = session.Submit("end");

        Assert.True(session.State.NationById("south")!.Eliminated);
        Assert.Contains(
            output.Lines,
            l => l.Contains("Southern League", StringComparison.Ordinal)
                 && l.Contains("has fallen", StringComparison.Ordinal));
    }

    /// <summary>
    /// Forces <paramref name="mutate"/> onto <paramref name="nationId"/>'s <see cref="NationState"/> in
    /// <paramref name="session"/>'s live state, through <see cref="GameSession.State"/>'s own private
    /// setter. Kept only for <see cref="Watch_mode_completes_one_lap_without_hanging_when_a_non_active_nation_is_eliminated_mid_lap"/>
    /// below (review round 2, B3: "keep the reflection tests only if they add something") — that test
    /// isolates <c>PlayUntilOneFullLapOrRepeat</c>'s own repeat-detection on a 16-seat map, which a real
    /// siege on <c>classical-mediterranean</c> has no cheap, deterministic way to set up. The two Done-when
    /// 7 tests above use real play instead, exactly as the review asked.
    /// </summary>
    private static void ForceNationState(
        GameSession session, string nationId, Func<NationState, NationState> mutate,
        string? alsoReassignCitiesTo = null)
    {
        var current = session.State;
        var newState = current with
        {
            Nations = ValueList.From(current.Nations.Select(n =>
                string.Equals(n.Id, nationId, StringComparison.Ordinal) ? mutate(n) : n)),
            Cities = alsoReassignCitiesTo is null
                ? current.Cities
                : ValueList.From(current.Cities.Select(c =>
                    string.Equals(c.Owner, nationId, StringComparison.Ordinal)
                        ? c with { Owner = alsoReassignCitiesTo }
                        : c)),
        };

        typeof(GameSession).GetProperty(nameof(GameSession.State))!.SetValue(session, newState);
    }

    private static GameSession NewClassicalWatchModeSession() =>
        new(Classical.World, Classical.Ruleset, Classical.Scenario);

    /// <summary>
    /// Review round 2, B3 (blocking): <c>PlayUntilOneFullLapOrRepeat</c>'s own <c>!playedThisRound.Contains(…)</c>
    /// check, and the loss-check inside its loop, were both unpinned — the reviewer's own probe found that
    /// deleting the repeat check makes this exact scenario <strong>hang forever</strong> (killed after 9
    /// minutes): once a non-active nation is eliminated mid-lap, <c>Count &lt; TurnOrder.Count</c> alone
    /// never trips, because the eliminated seat is skipped by rotation rather than counted, so the "already
    /// played" set stops growing before the guard's raw count does, and <c>RunTurn</c> repeats the seat
    /// after it forever. Carthage is eliminated here while Rome (turn-order seat 0) is active, so the loop
    /// must run the rest of the lap — 15 of the 16 seats — and stop exactly there, never repeating one.
    /// <c>[Fact(Timeout = …)]</c> turns that specific regression into a fast, clean failure instead of a
    /// hang.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task Watch_mode_completes_one_lap_without_hanging_when_a_non_active_nation_is_eliminated_mid_lap()
    {
        await Task.Run(() =>
        {
            var session = NewClassicalWatchModeSession();
            Assert.Equal("rome", session.State.ActiveNationId);
            ForceNationState(session, "carthage", n => n with { Eliminated = true }, alsoReassignCitiesTo: "rome");

            var output = session.Submit("end");

            var turnLines = output.Lines
                .Where(l => l.Contains("takes its turn", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(15, turnLines.Count);
            Assert.Equal(turnLines.Count, turnLines.Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(turnLines, l => l.StartsWith("Carthage", StringComparison.Ordinal));
            Assert.Equal("rome", session.State.ActiveNationId);
        });
    }

    /// <summary>
    /// Done-when 7: "or deposed and handed to the AI, the CLI prints that it has fallen and continues in
    /// watch mode". Review round 2, B3 ("replace the reflection-based deposition test with a real one"):
    /// a deep negative starting <see cref="NationState.Treasury"/>, given as <see cref="World"/> input,
    /// trips <see cref="Economy.Deposition.InDebt"/> (<c>treasury &lt; DebtTreasuryFloor</c>) for real, and
    /// <c>HumanDepositionSystem</c> (<c>TurnPhase.SeatStart</c>) deposes Carthage during its own very first
    /// turn. That first <c>end</c> plays only Carthage — the loss is caught immediately after its own
    /// <c>RunTurn</c>, before <see cref="PlayUntilOneFullLapOrRepeat"/>'s loop ever starts (N10: correct,
    /// not a finding), so it carries no "takes its turn" line of its own; the round after that is a full
    /// 16-seat watch-mode lap, Carthage now played as AI among the rest.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task A_seat_deposed_by_real_debt_falls_and_the_session_switches_to_watch_mode()
    {
        await Task.Run(() =>
        {
            var world = Classical.World with
            {
                Nations = ValueList.From(Classical.World.Nations.Select(n =>
                    string.Equals(n.Id, "carthage", StringComparison.Ordinal)
                        ? n with { Treasury = -10_000_000 }
                        : n)),
            };
            var session = new GameSession(
                world, Classical.Ruleset, Classical.Scenario, seedOverride: null, humanSeatNationId: "carthage");

            var firstEnd = session.Submit("end");

            Assert.Contains(
                firstEnd.Lines,
                l => l.Contains("Carthage", StringComparison.Ordinal)
                     && l.Contains("deposed", StringComparison.Ordinal));
            Assert.Equal(SeatControl.Ai, session.State.NationById("carthage")!.Control);

            var laterEnd = session.Submit("end");
            var turnLines = laterEnd.Lines
                .Where(l => l.Contains("takes its turn", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(16, turnLines.Count);
            Assert.Equal(turnLines.Count, turnLines.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(turnLines, l => l.StartsWith("Carthage", StringComparison.Ordinal));

            var rejected = session.Submit("declare-war rome");
            Assert.Contains(rejected.Lines, l => l.Contains("--seat", StringComparison.Ordinal));
        });
    }

    // ---- Review round 1, N4 / bug #376: the round footer under-counts once the news ring buffer is full ----

    /// <summary>
    /// Bug #376, found in PR #375's review (N4): <c>NewsLog.Slots.Count</c> saturates at the ruleset's
    /// 40-slot ring buffer capacity, so a plain count subtraction reads zero new entries once the buffer
    /// is full, even though real new entries landed (evicting old ones). <c>classical-mediterranean</c>'s
    /// own starting news log is 27 of 40 slots (the DAT's own scripted 272-271 BC history) — padded here,
    /// through the real <see cref="NewsLog.Append"/>, to exactly 40/40 before the round runs, so this
    /// round's own genuine appends are guaranteed to evict something and exercise the saturated path.
    /// </summary>
    /// <remarks>
    /// <strong>Review round 2, N9 (naming/wording correction — this test's name and this comment
    /// previously overclaimed):</strong> this does <em>not</em> prove the footer lists every line a round
    /// writes, full stop — round 1 on <c>seat-rome.golden.txt</c>'s own fixed seed genuinely writes more
    /// than 40 entries (the reviewer counted 44), and the newest 40 is still all the 40-slot ring can ever
    /// show in one footer, faithful to the original's own ring buffer (<c>docs/design-audit.md</c>'s
    /// confirmed behaviour). What this test actually proves is narrower and is the whole of bug #376: the
    /// count is no longer silently <em>zero</em> merely because the buffer started this round already
    /// full — <see cref="CountNewsAppendedSince"/> reports real news up to the ring's own capacity, not
    /// nothing.
    /// </remarks>
    [Fact]
    public void The_round_footer_reports_news_even_when_the_ring_buffer_started_this_round_already_full()
    {
        var session = NewClassicalSeatSession("rome");
        Assert.Equal(27, session.State.NewsLog.Slots.Count);

        var padded = session.State.NewsLog;
        var fillerIndex = 0;
        while (padded.Slots.Count < session.Ruleset.NewsLog.RingBufferSlots)
        {
            padded = padded.Append(new NewsEntry($"Filler entry {fillerIndex++}."), session.Ruleset.NewsLog);
        }

        Assert.Equal(session.Ruleset.NewsLog.RingBufferSlots, padded.Slots.Count);
        var newStateWithPaddedLog = session.State with { NewsLog = padded };
        typeof(GameSession).GetProperty(nameof(GameSession.State))!.SetValue(session, newStateWithPaddedLog);

        var slotsBeforeRound = session.State.NewsLog.Slots.Count;
        var output = session.Submit("end");

        // The log is still exactly at capacity afterwards (every append evicted one) -- proof the round
        // really did write through the saturated buffer, not merely append into remaining headroom.
        Assert.Equal(slotsBeforeRound, session.State.NewsLog.Slots.Count);

        // At least one of the genuine alliance-formation lines the unpadded seat-rome golden shows for
        // round 1 on this same fixed seed is listed -- proof the footer did not fall back to reporting
        // zero once saturated.
        Assert.Contains(output.Lines, line => line.Contains("forms an alliance with", StringComparison.Ordinal));
        Assert.DoesNotContain(output.Lines, line => line.Contains("Filler entry", StringComparison.Ordinal));
    }
}
