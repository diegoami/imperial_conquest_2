using System.Diagnostics;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T95.md</c> (#467): save and resume a game. One test group per Done-when line, plus the
/// field-inventory decisions <c>GameSession.ResumeFrom</c>'s own remarks make (which session-only fields
/// are rebuilt from the loaded state and which are left empty) proven here rather than only asserted in
/// the PR body — Done-when 3's byte-for-byte comparison is what actually exercises the random-stream
/// hazard, and Done-when 4's own test is what exercises the human-seat hazard (#382).
/// </summary>
public sealed class SaveAndResumeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ic2-save-resume-tests", Path.GetRandomFileName());

    public SaveAndResumeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leaked temp directory is a nuisance, not a test failure.
        }
    }

    private static readonly Lazy<ResolvedScenario> LazyToy = new(
        () => GameDataRepository.Load(TestPaths.DataRoot).Resolve("toy-3city"));

    private static ResolvedScenario Toy => LazyToy.Value;

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(TestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    // ---- Done-when 1: save ----

    [Fact]
    public void Save_writes_the_current_game_through_SaveManager()
    {
        var toy = Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        session.Submit("end");

        var path = PathFor("save1.json");
        var output = session.Submit($"save {path}");

        Assert.True(File.Exists(path));
        Assert.Contains(output.Lines, l => l.StartsWith("Saved to", StringComparison.Ordinal));

        var loaded = SaveManager.LoadFile(path, toy.World, toy.Ruleset);
        Assert.Equal(session.State, loaded.State);
    }

    [Fact]
    public void Save_from_the_CLI_seat_session_also_writes_a_loadable_file()
    {
        // Done-when 1: "from the CLI and from the Godot main game screen" -- both route through the same
        // GameSession.Submit("save <path>") command; this exercises the --seat construction path (the CLI's
        // own), the Godot path is identical code, only reached through MainGameScreen.OnSavePressed's own
        // SubmitForCheck (godot/UI/MainGameScreen.cs, outside plain xunit's reach -- see this task's PR body
        // for why the Godot-level check is not automated here).
        var classical = Classical;
        var session = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "rome");

        var path = PathFor("save-seat.json");
        session.Submit($"save {path}");

        var loaded = SaveManager.LoadFile(path, classical.World, classical.Ruleset);
        Assert.Equal("rome", loaded.State.NationById("rome")!.Id);
        Assert.Equal(SeatControl.Human, loaded.State.NationById("rome")!.Control);
    }

    // ---- B1 (rework round 1, the user's decision 2026-09-28): save refuses while a post-battle peace
    // treaty offer is pending, since GameSession.ResumeFrom's own remarks note _pendingPeaceTreatyOffers
    // has no counterpart in SaveGame/GameState -- a save taken mid-offer would otherwise resume into a
    // different game (the independent reviewer's own probe on PR #481, T88's OfferFixture below, copied
    // from PeaceTreatyOfferTests.cs so this file does not need to change that one). ----

    /// <summary>
    /// <see cref="PeaceTreatyOfferTests.OfferFixture"/>'s own fixture, copied here rather than exposed
    /// from that file (outside this task's "new tests" grant to modify): the shipped toy world/scenario,
    /// with the two threshold gates relaxed and the <c>Random(5)</c> draw always favourable so
    /// <c>south</c> (AI) reliably beats <c>north-army-1</c> and <c>north</c> (the toy scenario's own human
    /// seat) is offered the treaty. See that class's own remarks for why each override exists.
    /// </summary>
    private static GameSession OfferFixture()
    {
        var toy = CoreTestbed.Toy;
        var customRuleset = toy.Ruleset with
        {
            Combat = toy.Ruleset.Combat with
            {
                AutoPeaceChanceNumerator = toy.Ruleset.Combat.AutoPeaceChanceDenominator,
                AutoPeaceLoserUnityThreshold = -1,
                AutoPeaceLoserCityThreshold = 0,
            },
        };

        var reserve = new StartingArmy(
            "north-reserve", "north", X: 2, Y: 1, Morale: 1, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 480000, Quality: 5, Name: "Reserve")));

        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with { X = 4, Y = 2 };
        var customWorld = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a).Append(reserve)),
        };

        return new GameSession(customWorld, customRuleset, toy.Scenario);
    }

    [Fact]
    public void Save_is_refused_while_a_peace_treaty_offer_is_pending_and_writes_no_file()
    {
        var session = OfferFixture();
        session.Submit("attack-army north-army-1 south-army-1");

        var path = PathFor("save-pending-offer.json");
        var output = session.Submit($"save {path}");

        Assert.Contains(
            output.Lines,
            l => l.Contains("Answer the pending peace treaty offer", StringComparison.Ordinal)
                 && l.Contains("peace-yes", StringComparison.Ordinal)
                 && l.Contains("peace-no", StringComparison.Ordinal));
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// The other half of B1: once the offer is answered, <c>save</c> succeeds again, and the resumed game
    /// continues exactly like the uninterrupted one — the same byte-for-byte proof Done-when 3 uses, run
    /// past a real battle and a real peace this time (Done-when 3's own committed test never fights one).
    /// </summary>
    [Fact]
    public void Save_succeeds_after_answering_the_offer_and_the_resumed_game_continues_exactly_like_the_uninterrupted_one()
    {
        var follow = new[] { "end", "status" };

        // One shared "end" runs identically in both branches before either saves or diverges: a session's
        // very first "end" ever shows every news entry back to construction, not just that round's own
        // (T87's own _pendingNewsBaseline convention: "news written during the prelude must appear in the
        // first end's summary") -- a real but unrelated quirk of "was this the first end", not of save and
        // resume. Every other byte-for-byte test in this file (Done-when 3, B3) only ever saves after at
        // least one "end" has already consumed that baseline; this one battles and answers a peace treaty
        // before its own first "end", so it is given the same one here, in both branches, before the save
        // point -- keeping the comparison about B1's own claim (save/resume around a peace answer), not
        // about which side happens to still be showing prelude-era news.
        var uninterrupted = OfferFixture();
        uninterrupted.Submit("attack-army north-army-1 south-army-1");
        uninterrupted.Submit("peace-yes");
        uninterrupted.Submit("end");
        var uninterruptedTail = RunEach(uninterrupted, follow);

        var toSave = OfferFixture();
        toSave.Submit("attack-army north-army-1 south-army-1");
        toSave.Submit("peace-yes");
        toSave.Submit("end");

        var path = PathFor("save-after-offer-answered.json");
        var saveOutput = toSave.Submit($"save {path}");
        Assert.Contains(saveOutput.Lines, l => l.StartsWith("Saved to", StringComparison.Ordinal));
        Assert.True(File.Exists(path));

        var save = SaveManager.LoadFile(path, toSave.World, toSave.Ruleset);
        var resumed = new GameSession(toSave.World, toSave.Ruleset, toSave.Scenario, save);
        var resumedTail = RunEach(resumed, follow);

        Assert.Equal(string.Concat(uninterruptedTail), string.Concat(resumedTail));
    }

    // ---- Done-when 2: resume ----

    [Fact]
    public void A_session_built_from_a_loaded_save_continues_the_same_game()
    {
        var toy = Toy;
        var original = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        original.Submit("end");
        original.Submit("end");

        var path = PathFor("save2.json");
        original.Submit($"save {path}");

        var save = SaveManager.LoadFile(path, toy.World, toy.Ruleset);
        var resumed = new GameSession(toy.World, toy.Ruleset, toy.Scenario, save);

        // GameState is a fully value-equal record tree (Model/GameState.cs's own remarks): this one
        // Assert.Equal proves "the same state" in full, not a hand-picked subset of fields.
        Assert.Equal(original.State, resumed.State);
        Assert.Equal(original.State.ActiveNationId, resumed.State.ActiveNationId);
        Assert.Equal(original.State.Calendar, resumed.State.Calendar);
    }

    // ---- Done-when 3: deterministic continuation, byte for byte ----

    /// <summary>
    /// The uninterrupted game and the saved-then-resumed game are driven by the exact same script, so any
    /// difference between the two can only come from <see cref="GameSession"/>'s own bookkeeping around
    /// the save/resume boundary — chiefly whether the random stream (<see cref="GameState.RandomSeed"/>)
    /// resumed where it stopped. This is what a mutation proves: temporarily hard-coding
    /// <c>RandomSeed = 0</c> inside <c>GameSession.ResumeFrom</c> makes this test fail (see this task's
    /// PR body for the mutation record).
    /// </summary>
    [Fact]
    public void A_game_saved_and_resumed_continues_exactly_as_the_uninterrupted_game_does()
    {
        var classical = Classical;
        var script = new[] { "end", "end", "end", "end", "end", "end" };

        var uninterrupted = new GameSession(classical.World, classical.Ruleset, classical.Scenario);
        var fullBlocks = RunEach(uninterrupted, script);

        var toSave = new GameSession(classical.World, classical.Ruleset, classical.Scenario);
        RunEach(toSave, script[..3]);

        var path = PathFor("save3.json");
        var saveOutput = toSave.Submit($"save {path}");
        Assert.DoesNotContain(saveOutput.Lines, l => l.StartsWith("Could not", StringComparison.Ordinal));

        var save = SaveManager.LoadFile(path, classical.World, classical.Ruleset);
        var resumed = new GameSession(classical.World, classical.Ruleset, classical.Scenario, save);
        var afterBlocks = RunEach(resumed, script[3..]);

        Assert.Equal(string.Concat(fullBlocks[3..]), string.Concat(afterBlocks));
    }

    /// <summary>
    /// B3 (rework round 1, blocking): the test above uses <c>classical.Scenario</c> unmodified, whose own
    /// 16 seats are every one AI by default — so it only ever exercises a <em>watch-mode</em> resume, and
    /// a mutation that reads <c>_isWatchMode</c> from <c>Scenario.Seats</c> instead of the loaded state's
    /// own <see cref="Model.NationState.Control"/> (exactly the #382 hazard Done-when 4 forbids) passed
    /// the whole suite regardless (the independent reviewer's own M3b). This is the same byte-for-byte
    /// proof, seated (<c>--seat rome</c>) this time: under that mutation, the resumed session would
    /// recompute watch mode from the scenario's own all-AI seat list and stop pausing on rome at all, so
    /// <c>end</c> would play every AI seat in one call instead of stopping back at rome — a transcript
    /// that cannot match the uninterrupted, still-seated session's own.
    /// </summary>
    [Fact]
    public void A_seated_game_saved_and_resumed_continues_exactly_as_the_uninterrupted_game_does()
    {
        var classical = Classical;
        var script = new[] { "end", "end", "end", "end", "end", "end" };

        var uninterrupted = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "rome");
        var fullBlocks = RunEach(uninterrupted, script);

        var toSave = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "rome");
        RunEach(toSave, script[..3]);

        var path = PathFor("save-seated-b3.json");
        var saveOutput = toSave.Submit($"save {path}");
        Assert.DoesNotContain(saveOutput.Lines, l => l.StartsWith("Could not", StringComparison.Ordinal));

        var save = SaveManager.LoadFile(path, classical.World, classical.Ruleset);
        var resumed = new GameSession(classical.World, classical.Ruleset, classical.Scenario, save);
        var afterBlocks = RunEach(resumed, script[3..]);

        Assert.Equal(string.Concat(fullBlocks[3..]), string.Concat(afterBlocks));
    }

    /// <summary>
    /// B3's second test: a resumed <c>--seat carthage</c> save whose very first <c>end</c> must match the
    /// uninterrupted session's own first <c>end</c> — a narrower, more direct pin of the same watch-mode
    /// hazard than the byte-for-byte test above (M3b's own effect is visible on the very first command).
    /// Saved before any command is submitted, so both sessions start from an identical, freshly built
    /// state; if the resumed session recomputed watch mode from the scenario instead of the saved
    /// <see cref="Model.NationState.Control"/>, its "end" would play every AI seat in one lap (no pause on
    /// carthage) instead of ending only carthage's own turn and pausing there again.
    /// </summary>
    [Fact]
    public void A_resumed_seat_carthage_save_s_first_end_matches_the_uninterrupted_sessions_first_end()
    {
        var classical = Classical;

        // Carthage is not classical-mediterranean's own turn-order seat 0, so --seat carthage's own
        // construction fast-forwards past every AI seat before it (rome among them) and queues that
        // narration as a pending prelude, flushed onto the very first Submit call ever made -- whichever
        // command that happens to be. A plain "status" first consumes that flush harmlessly on both
        // branches (it does not touch State either way), so the "end" comparison below is not
        // contaminated by which session happens to be asked its first-ever question, only by whether
        // "end" itself pauses back on carthage the same way for both.
        var uninterrupted = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "carthage");
        uninterrupted.Submit("status");
        var uninterruptedFirstEnd = uninterrupted.Submit("end");

        var toSave = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "carthage");
        toSave.Submit("status");
        var path = PathFor("save-carthage-b3.json");
        var saveOutput = toSave.Submit($"save {path}");
        Assert.DoesNotContain(saveOutput.Lines, l => l.StartsWith("Could not", StringComparison.Ordinal));

        var save = SaveManager.LoadFile(path, classical.World, classical.Ruleset);
        var resumed = new GameSession(classical.World, classical.Ruleset, classical.Scenario, save);
        var resumedFirstEnd = resumed.Submit("end");

        Assert.Equal(
            string.Concat(uninterruptedFirstEnd.Lines.Select(l => l + "\n")),
            string.Concat(resumedFirstEnd.Lines.Select(l => l + "\n")));
    }

    private static List<string> RunEach(GameSession session, IEnumerable<string> lines)
    {
        var blocks = new List<string>();
        foreach (var line in lines)
        {
            var output = session.Submit(line);
            blocks.Add(string.Concat(output.Lines.Select(l => l + "\n")));
            if (output.ShouldExit)
            {
                break;
            }
        }

        return blocks;
    }

    // ---- Done-when 4: the human seat comes from the saved state (#382's hazard) ----

    /// <summary>
    /// A <c>--seat carthage</c> session is saved while carthage is still alive and human, then resumed
    /// with <strong>no <c>--seat</c> at all</strong> and against the <em>original, unmodified</em>
    /// scenario (every seat AI by default). Carthage's own fall is then forced directly on the
    /// <em>resumed</em> session's own state — the same reflection technique
    /// <c>SeatCliTests.ForceNationState</c> already uses — so this proves the resumed session still
    /// treats carthage as its one <c>--seat</c>-style seat (falling back to watch mode on its loss,
    /// <c>GameSession.AnnounceAndAdoptWatchModeIfSeatIsLost</c>) without ever having been told
    /// <c>--seat carthage</c> itself: if the human seat came from the scenario instead (no nation marked
    /// human there) or from a missing <c>--seat</c>, this session would instead be
    /// hotseat/watch-mode-from-the-start and would report "no human seat remains" (session over) the
    /// moment carthage's own control flips, not "watch mode from here on" (session continues).
    /// </summary>
    [Fact]
    public void The_human_seat_after_a_load_comes_from_the_saved_state_not_from_seat_or_scenario()
    {
        var classical = Classical;
        var original = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "carthage");
        Assert.Equal(SeatControl.Human, original.State.NationById("carthage")!.Control);
        Assert.Equal("carthage", original.State.ActiveNationId);

        var path = PathFor("save4.json");
        original.Submit($"save {path}");

        var save = SaveManager.LoadFile(path, classical.World, classical.Ruleset);

        // No --seat, and the ORIGINAL scenario object (whose own Seats mark no nation human) -- see this
        // test's own summary for why this is the whole point.
        var resumed = new GameSession(classical.World, classical.Ruleset, classical.Scenario, save);
        Assert.Equal(SeatControl.Human, resumed.State.NationById("carthage")!.Control);

        var mutated = resumed.State with
        {
            Nations = ValueList.From(resumed.State.Nations.Select(n =>
                string.Equals(n.Id, "carthage", StringComparison.Ordinal)
                    ? n with { Control = SeatControl.Ai }
                    : n)),
        };
        typeof(GameSession).GetProperty(nameof(GameSession.State))!.SetValue(resumed, mutated);

        var afterEnd = resumed.Submit("end");

        Assert.Contains(afterEnd.Lines, l =>
            l.Contains("has been deposed and handed to the AI", StringComparison.Ordinal)
            && l.Contains("Watch mode from here on", StringComparison.Ordinal));
        Assert.DoesNotContain(afterEnd.Lines, l => l.Contains("No human seat remains", StringComparison.Ordinal));
        Assert.False(afterEnd.ShouldExit);
    }

    /// <summary>
    /// The hotseat half of Done-when 4: two or more <see cref="SeatControl.Human"/> nations in the saved
    /// state resume as hotseat (<c>_humanSeatNationId</c> stays <see langword="null"/>), not as a single
    /// <c>--seat</c>-style seat — proven by <c>status mine</c> defaulting to whichever seat currently has
    /// the turn (<c>GameSession.DefaultViewNationId</c>'s own fallback), the same way an ordinary
    /// scenario-driven hotseat session already behaves.
    /// </summary>
    [Fact]
    public void A_resumed_hotseat_save_with_two_human_seats_still_rotates_between_both()
    {
        var toy = Toy;
        var hotseatScenario = toy.Scenario with
        {
            Seats = ValueList.From(toy.Scenario.Seats.Select(seat => seat with { Control = SeatControl.Human })),
        };

        var original = new GameSession(toy.World, toy.Ruleset, hotseatScenario);
        var firstSeat = original.State.ActiveNationId;

        var path = PathFor("save-hotseat.json");
        original.Submit($"save {path}");

        var save = SaveManager.LoadFile(path, toy.World, toy.Ruleset);
        var resumed = new GameSession(toy.World, toy.Ruleset, hotseatScenario, save);

        var afterEnd = resumed.Submit("end");
        var secondSeat = resumed.State.ActiveNationId;

        Assert.NotEqual(firstSeat, secondSeat);
        Assert.Equal(SeatControl.Human, resumed.State.NationById(secondSeat)!.Control);
        Assert.False(afterEnd.ShouldExit);
    }

    // ---- Done-when 5: errors ----

    [Fact]
    public void Load_of_a_missing_file_is_refused_and_the_session_keeps_running()
    {
        var toy = Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        var stateBefore = session.State;

        var output = session.Submit($"load {PathFor("does-not-exist.json")}");

        Assert.Contains(output.Lines, l => l.StartsWith("Could not load", StringComparison.Ordinal));
        Assert.Equal(stateBefore, session.State);
        Assert.False(output.ShouldExit);

        var status = session.Submit("status");
        Assert.DoesNotContain(status.Lines, l => l.StartsWith("Could not", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_of_a_malformed_file_is_refused_and_the_session_keeps_running()
    {
        var toy = Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        var stateBefore = session.State;

        var badPath = PathFor("malformed.json");
        File.WriteAllText(badPath, "not json at all");

        var output = session.Submit($"load {badPath}");

        Assert.Contains(output.Lines, l => l.StartsWith("Could not load", StringComparison.Ordinal));
        Assert.Equal(stateBefore, session.State);
        Assert.False(output.ShouldExit);
    }

    [Fact]
    public void Load_of_a_save_for_a_different_world_is_refused_and_the_session_keeps_running()
    {
        var toy = Toy;
        var classical = Classical;

        var classicalSession = new GameSession(classical.World, classical.Ruleset, classical.Scenario);
        var otherWorldPath = PathFor("classical.json");
        classicalSession.Submit($"save {otherWorldPath}");

        var toySession = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        var stateBefore = toySession.State;

        var output = toySession.Submit($"load {otherWorldPath}");

        Assert.Contains(output.Lines, l =>
            l.StartsWith("Could not load", StringComparison.Ordinal)
            && l.Contains("world", StringComparison.Ordinal));
        Assert.Equal(stateBefore, toySession.State);
        Assert.False(output.ShouldExit);
    }

    /// <summary>
    /// N3 (rework round 1, non-blocking): Done-when 5 reads "a different world <em>or ruleset</em>" — the
    /// world case above shares a save with a session under a different ruleset but the <em>same</em>
    /// world, closing the reading DoD 5's "a test for each" asks for.
    /// </summary>
    [Fact]
    public void Load_of_a_save_for_a_different_ruleset_is_refused_and_the_session_keeps_running()
    {
        var classical = Classical;
        var improvedRuleset = GameDataRepository.Load(TestPaths.DataRoot).RulesetById("improved")!;

        var classicalFaithfulSession = new GameSession(classical.World, classical.Ruleset, classical.Scenario);
        var path = PathFor("classical-faithful.json");
        classicalFaithfulSession.Submit($"save {path}");

        // Same world as the save (classical), a different ruleset (improved) -- the CLI's own --ruleset
        // override already proves this world/ruleset pairing is legal to construct directly.
        var improvedSession = new GameSession(classical.World, improvedRuleset, classical.Scenario);
        var stateBefore = improvedSession.State;

        var output = improvedSession.Submit($"load {path}");

        Assert.Contains(output.Lines, l =>
            l.StartsWith("Could not load", StringComparison.Ordinal)
            && l.Contains("ruleset", StringComparison.Ordinal));
        Assert.Equal(stateBefore, improvedSession.State);
        Assert.False(output.ShouldExit);
    }

    // ---- save/load command parsing ----

    [Fact]
    public void Save_with_no_path_argument_prints_usage_and_writes_nothing()
    {
        var toy = Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario);

        var output = session.Submit("save");

        Assert.Contains(output.Lines, l => l.Contains("Usage: save <path>", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Load_with_no_path_argument_prints_usage()
    {
        var toy = Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario);

        var output = session.Submit("load");

        Assert.Contains(output.Lines, l => l.Contains("Usage: load <path>", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_and_load_a_path_containing_spaces_round_trips()
    {
        var toy = Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        session.Submit("end");

        var path = PathFor("my save with spaces.json");
        var saveOutput = session.Submit($"save {path}");
        Assert.Contains(saveOutput.Lines, l => l.Contains(path, StringComparison.Ordinal));
        Assert.True(File.Exists(path));

        var loadOutput = session.Submit($"load {path}");
        Assert.Contains(loadOutput.Lines, l => l.StartsWith("Loaded", StringComparison.Ordinal));
    }

    // ---- B2 (rework round 1, blocking): DoD 2's CLI half -- load <path> mid-session and --load at
    // startup -- had no automated test that would fail if either stopped resuming. Mutation M5 (HandleLoad's
    // own ResumeFrom(save); replaced with _ = save;) left the entire engine suite green. ----

    /// <summary>
    /// <c>load &lt;path&gt;</c>, mid-session, on a session sitting on a <em>different</em> state (a
    /// different <c>--seat</c>, never played forward) — the independent reviewer's own suggested shape.
    /// Asserts <see cref="GameSession.State"/> equals the saved state exactly, then that the next commands
    /// match the uninterrupted session's own continuation byte for byte. Dies under M5: a no-op
    /// <c>ResumeFrom</c> would leave <c>differentSession</c> on carthage's own turn-0 state, matching
    /// neither the saved state nor the uninterrupted transcript.
    /// </summary>
    [Fact]
    public void The_load_command_mid_session_resumes_and_continues_exactly_like_the_uninterrupted_game()
    {
        var classical = Classical;
        var follow = new[] { "end", "status" };

        var toSave = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "rome");
        RunEach(toSave, new[] { "end", "end", "end" });

        var path = PathFor("load-command-b2.json");
        toSave.Submit($"save {path}");
        var uninterruptedTail = RunEach(toSave, follow);

        // A different session entirely: a fresh --seat carthage game, never played forward -- proving
        // "load <path>" replaces this session's own state and bookkeeping wholesale, not merely
        // continuing whatever it already had.
        var differentSession = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "carthage");
        var loadOutput = differentSession.Submit($"load {path}");
        Assert.Contains(loadOutput.Lines, l => l.StartsWith("Loaded", StringComparison.Ordinal));

        var save = SaveManager.LoadFile(path, classical.World, classical.Ruleset);
        Assert.Equal(save.State, differentSession.State);

        var resumedTail = RunEach(differentSession, follow);
        Assert.Equal(string.Concat(uninterruptedTail), string.Concat(resumedTail));
    }

    /// <summary>
    /// <c>--load &lt;path&gt;</c> at CLI startup (<c>src/IC2.Cli/Program.cs</c>), run through the real
    /// built executable — the same reasoning <c>CliProcessTests</c> gives for every test that spawns
    /// <c>IC2.Cli.dll</c> directly rather than only exercising <see cref="GameSession"/> in process:
    /// <c>--load</c>'s own resolution (<c>Program.LoadSession</c>) runs before any <see cref="GameSession"/>
    /// exists. Compares the CLI's own stdout, after <c>--load</c>, against the same continuation an
    /// in-process resumed session gives for the identical script.
    /// </summary>
    [Fact]
    public void The_load_flag_at_cli_startup_resumes_and_continues_exactly_like_the_uninterrupted_game()
    {
        var cliDll = FindCliDll();
        if (cliDll is null)
        {
            return;
        }

        var classical = Classical;
        var follow = new[] { "end", "status" };

        var toSave = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "rome");
        RunEach(toSave, new[] { "end", "end", "end" });

        var savePath = PathFor("cli-load-flag-b2.json");
        toSave.Submit($"save {savePath}");
        var uninterruptedTail = RunEach(toSave, follow);

        var scriptPath = PathFor("cli-load-flag-script-b2.txt");
        File.WriteAllText(scriptPath, "end\nstatus\nquit\n");

        using var process = StartCli(cliDll, $"--load \"{savePath}\" --script \"{scriptPath}\"");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Loaded save", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.Ordinal);

        // The CLI's own Main loop prints every SessionOutput.Lines entry via Console.WriteLine (Program.cs
        // sets Console.Out.NewLine = "\n"), one call per script line -- the identical shape RunEach builds
        // here in process. stdout after --load must therefore contain this exact continuation verbatim
        // (a leading substring check, not full equality: the script's own trailing "quit" adds a further
        // "> quit\nGoodbye.\n\n" block the in-process reference never runs).
        Assert.Contains(string.Concat(uninterruptedTail), stdout, StringComparison.Ordinal);
    }

    /// <summary>Starts the built CLI with the given argument string, redirecting both output streams —
    /// the same helper <c>CliProcessTests</c> uses, duplicated here rather than shared (neither file may
    /// add a third file this task does not own to hold it in common).</summary>
    private static Process StartCli(string cliDll, string arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet", $"\"{cliDll}\" {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = TestPaths.RepositoryRoot,
        };

        var process = Process.Start(startInfo);
        Assert.NotNull(process);
        return process!;
    }

    /// <summary>Finds the built <c>IC2.Cli.dll</c> next to this test assembly's own build output — see
    /// <c>CliProcessTests.FindCliDll</c>'s own remarks for why this is not shared.</summary>
    private static string? FindCliDll()
    {
        var testOutputDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var targetFramework = testOutputDir.Name;
        var configuration = testOutputDir.Parent?.Name;
        if (configuration is null)
        {
            return null;
        }

        var candidate = Path.Combine(
            TestPaths.RepositoryRoot, "src", "IC2.Cli", "bin", configuration, targetFramework, "IC2.Cli.dll");
        return File.Exists(candidate) ? candidate : null;
    }
}
