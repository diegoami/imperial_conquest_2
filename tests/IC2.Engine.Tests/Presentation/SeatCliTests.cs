using IC2.Engine.Model;
using IC2.Engine.Persistence;
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
    /// Forces <paramref name="mutate"/> onto <paramref name="nationId"/>'s <see cref="NationState"/> in
    /// <paramref name="session"/>'s live state, through <see cref="GameSession.State"/>'s own private
    /// setter — the same reflection the review's own N2/N3 probes used. There is no public way to reach
    /// this from outside <see cref="GameSession"/> by design (nothing but <see cref="GameSession.Submit"/>
    /// is supposed to move <see cref="GameSession.State"/> forward), so a real elimination or deposition
    /// would normally arrive through actual play; this reaches the exact same states directly and
    /// deterministically, without depending on the AI happening to eliminate or bankrupt a specific nation
    /// within a bounded number of rounds. When <paramref name="alsoReassignCitiesTo"/> is given, every
    /// city <paramref name="nationId"/> owns is handed to it too, keeping the state self-consistent the
    /// way an actual conquest would (a real elimination never leaves an "eliminated" nation still owning
    /// cities).
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

    /// <summary>
    /// Done-when 7: "If the <c>--seat</c> nation is eliminated ... the CLI prints that it has fallen and
    /// continues in watch mode: one round per <c>end</c>, no orders, and no seat played twice." Review
    /// round 1, N2's own probe (Carthage eliminated, then <c>end</c>) found the pre-Done-when-7 loop
    /// double-playing Seleucid and then pausing on Ptolemaic, an AI seat the CLI would accept orders for.
    /// </summary>
    [Fact]
    public void A_seat_eliminated_mid_game_falls_and_the_session_switches_to_watch_mode()
    {
        var session = NewClassicalSeatSession("carthage");
        ForceNationState(session, "carthage", n => n with { Eliminated = true }, alsoReassignCitiesTo: "rome");

        var output = session.Submit("end");

        Assert.Contains(
            output.Lines,
            line => line.Contains("Carthage", StringComparison.Ordinal)
                    && line.Contains("has fallen", StringComparison.Ordinal));

        // No seat played twice: one "takes its turn" line per seat that was not already eliminated when
        // this round started (Carthage itself never gets one -- it is gone before its own turn, and was
        // never re-added to the turn order to begin with).
        var turnLines = output.Lines
            .Where(line => line.Contains("takes its turn", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(turnLines.Count, turnLines.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(turnLines, line => line.StartsWith("Carthage", StringComparison.Ordinal));

        // Watch mode from here on: no orders, read-only verbs still work.
        var rejected = session.Submit("declare-war rome");
        Assert.Contains(rejected.Lines, line => line.Contains("--seat", StringComparison.Ordinal));
        var status = session.Submit("status");
        Assert.DoesNotContain(status.Lines, line => line.Contains("--seat", StringComparison.Ordinal));
    }

    /// <summary>
    /// Done-when 7: "or deposed and handed to the AI, the CLI prints that it has fallen and continues in
    /// watch mode". Review round 1, N3: deposition does not eliminate the nation, only flips
    /// <see cref="NationState.Control"/> to <see cref="SeatControl.Ai"/>, so <see cref="GameSession"/>'s
    /// pre-Done-when-7 pause condition (<c>ActiveNationId == _humanSeatNationId</c>) kept firing for it
    /// forever, letting the AI play a seat the CLI still offered orders for.
    /// </summary>
    [Fact]
    public void A_seat_deposed_to_ai_falls_and_the_session_switches_to_watch_mode()
    {
        var session = NewClassicalSeatSession("carthage");
        ForceNationState(session, "carthage", n => n with { Control = SeatControl.Ai });

        var output = session.Submit("end");

        Assert.Contains(
            output.Lines,
            line => line.Contains("Carthage", StringComparison.Ordinal)
                    && line.Contains("deposed", StringComparison.Ordinal));

        var turnLines = output.Lines
            .Where(line => line.Contains("takes its turn", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(turnLines.Count, turnLines.Distinct(StringComparer.Ordinal).Count());

        var rejected = session.Submit("declare-war rome");
        Assert.Contains(rejected.Lines, line => line.Contains("--seat", StringComparison.Ordinal));
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
    [Fact]
    public void The_round_footer_lists_every_news_line_written_even_once_the_ring_buffer_is_full()
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
