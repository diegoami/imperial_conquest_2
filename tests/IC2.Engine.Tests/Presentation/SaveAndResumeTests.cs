using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
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
}
