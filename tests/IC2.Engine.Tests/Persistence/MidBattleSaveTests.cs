using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Presentation;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// <c>docs/tasks/T125.md</c> Done-when 4 and 5: a save made during a tactical battle carries the whole battle block
/// (save format 7), reloads into the battle resumed at the side to move's half-round setup (<c>FUN_00439968</c>, T123's
/// <c>Resume</c>) as the original resumes, and the exact-restore path rebuilds the block as saved without calling it.
/// </summary>
/// <remarks>
/// Not <c>tests/IC2.Data.Tests/MidBattleSaveTests.cs</c>, T123's data-level test of the same subject: this one runs the
/// session and the save format end to end.
/// </remarks>
public sealed class MidBattleSaveTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ic2-midbattle-").FullName;

    private static readonly string[] MidHalfRound =
    {
        "attack-army north-army-1 south-army-1",
        "battle place 1 1 2",
        "battle end",
        "battle move 0 8 5",
        "battle move 1 6 3",
        "battle end",
        "battle move 0 8 6",
        "battle move 1 8 5",
        "battle shoot 1 20",
        "battle end",
        "battle attack 0 20",   // a melee target set ...
        "battle shoot 1 20",    // ... and a shot taken: moves spent, a target pending, in the middle of the half-round
    };

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathFor(string name) => Path.Combine(_directory, name);

    private static GameSession NewSession(HumanBattleMode mode = HumanBattleMode.Tactical) =>
        TacticalSessionTestbed.ToySession(mode, armies: TacticalSessionTestbed.StrongAttacker());

    private static SaveGame Load(string path) =>
        SaveManager.LoadFile(path, TacticalSessionTestbed.Toy.World with { StartingArmies = ValueList.From(TacticalSessionTestbed.StrongAttacker()) }, TacticalSessionTestbed.Toy.Ruleset);

    private static GameSession Reload(SaveGame save, HumanBattleMode mode = HumanBattleMode.Tactical)
    {
        var toy = TacticalSessionTestbed.Toy;
        var world = toy.World with { StartingArmies = ValueList.From(TacticalSessionTestbed.StrongAttacker()) };
        return new GameSession(world, toy.Ruleset, toy.Scenario, save) { HumanBattles = mode };
    }

    private SaveGame SaveMidHalfRound(out GameSession session, out string path)
    {
        session = NewSession();
        TacticalSessionTestbed.Play(session, MidHalfRound);
        path = PathFor("mid.json");
        Assert.Equal($"Saved to '{path}'.", session.Submit($"save {path}").Lines[1]);
        return Load(path);
    }

    // ---- Done-when 4 ----

    [Fact]
    public void A_save_made_in_the_middle_of_a_half_round_round_trips_its_battle_section_field_for_field()
    {
        var saved = SaveMidHalfRound(out var session, out var path);
        var view = session.OpenTacticalBattle!;

        var block = Assert.IsType<SavedTacticalBattle>(saved.Battle);
        Assert.Equal(view.AttackerArmyId, block.AttackerArmyId);
        Assert.Equal(view.DefenderArmyId, block.DefenderArmyId);
        Assert.Equal(view.SideToMove, block.SideToMove);
        Assert.Equal(view.Placed, block.Placed);
        Assert.Equal(view.Counter, block.Counter);
        Assert.Equal(view.Grid, block.Grid);
        Assert.Equal(view.ComputerControlled, block.ComputerControlled);
        Assert.Equal(view.ComputerGeneral, block.ComputerGeneral);
        Assert.Equal(view.Selected, block.Selected);
        Assert.Equal(40, block.Slots.Count);
        Assert.Equal(168, block.Grid.Count);
        for (var s = 0; s < 40; s++)
        {
            var slot = view.Slots[s];
            var saved40 = block.Slots[s];
            Assert.Equal(
                (slot.X, slot.Y, slot.Origin, slot.Type, slot.Troops, slot.Quality, slot.Morale, slot.Moves, slot.Shots, slot.Target, slot.Name),
                (saved40.X, saved40.Y, saved40.Origin, saved40.Type, saved40.Troops, saved40.Quality, saved40.Morale, saved40.Moves, saved40.Shots, saved40.Target, saved40.Name));
        }

        // Mid half-round: a target is pending and a unit has spent moves, which the section keeps as they were.
        Assert.Contains(block.Slots, s => s.Target >= 0);
        Assert.Contains(block.Slots, s => s.Troops > 0 && s.Moves < 4);
        Assert.False(block.AtHalfRoundStart);
        Assert.NotEqual(0UL, block.DrawState);

        // The file's text round-trips to the same text.
        Assert.Equal(File.ReadAllText(path), SaveManager.Serialize(saved));
        Assert.Contains("\"saveFormatVersion\": 7", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Reloading_gives_the_state_Resume_gives_on_the_saved_block_not_the_unsaved_continuation()
    {
        var saved = SaveMidHalfRound(out var session, out _);
        var before = session.OpenTacticalBattle!;
        var context = TacticalContext.From(TacticalSessionTestbed.Toy.Ruleset);
        var expected = TacticalHalfRound.Resume(TacticalBattleSession.FromSaved(saved.Battle!), context);

        var reloaded = Reload(saved);
        var view = reloaded.OpenTacticalBattle!;

        // The half-round setup again: moves refilled, targets cleared, the counter incremented.
        Assert.Equal(expected.Slots, view.Slots);
        Assert.Equal(expected.Grid, view.Grid);
        Assert.Equal(expected.Counter, view.Counter);
        Assert.Equal(saved.Battle!.Counter + 1, view.Counter);
        Assert.Equal(expected.SideToMove, view.SideToMove);
        // The setup is the side to move's: its targets are cleared, the other side's stay as saved.
        Assert.All(view.Slots.Take(20), s => Assert.Equal(TacticalSlot.NoTarget, s.Target));
        Assert.Equal(saved.Battle.Slots.Skip(20).Select(s => s.Target), view.Slots.Skip(20).Select(s => s.Target));
        var ruleset = TacticalSessionTestbed.Toy.Ruleset;
        Assert.All(
            view.Slots.Take(20).Where(s => s.IsLive),
            s => Assert.Equal(ruleset.UnitTypes[s.Type].Moves, s.Moves));

        // The continuation the saving session would have had is a different state.
        Assert.Contains(before.Slots, s => s.Target >= 0);
        Assert.NotEqual(before.Slots, view.Slots);
        Assert.NotEqual(before.Counter, view.Counter);
    }

    [Fact]
    public void Two_reloads_of_one_save_play_to_byte_identical_ends()
    {
        var saved = SaveMidHalfRound(out _, out _);
        string[] rest =
        {
            "battle attack 0 20", "battle end",
            "battle attack 0 20", "battle end",
            "battle attack 0 20", "battle end",
            "battle attack 0 20", "battle end",
            "battle attack 0 20", "battle end",
            "battle attack 0 20", "battle end",
        };

        var first = TacticalSessionTestbed.Play(Reload(saved), rest);
        var second = TacticalSessionTestbed.Play(Reload(saved), rest);

        Assert.Equal(first.Transcript, second.Transcript);
        Assert.Equal(first.Session.State, second.Session.State);
        Assert.NotNull(first.Report);
        Assert.NotNull(second.Report);
        Assert.Equal(first.Report!.ExchangeLog, second.Report!.ExchangeLog);
    }

    [Fact]
    public void A_save_with_an_open_battle_reopens_it_whatever_the_switch_is_and_through_load()
    {
        var saved = SaveMidHalfRound(out _, out var path);

        // The resume constructor under Instant: the battle is already being fought.
        var instant = Reload(saved, HumanBattleMode.Instant);
        Assert.NotNull(instant.OpenTacticalBattle);
        var first = instant.Submit("status");
        Assert.Contains("A tactical battle is open again", string.Join('\n', first.Lines), StringComparison.Ordinal);
        Assert.NotNull(first.OpenBattleView);

        // The load command of a session with no battle open.
        var fresh = NewSession(HumanBattleMode.Instant);
        Assert.Null(fresh.OpenTacticalBattle);
        var load = fresh.Submit($"load {path}");
        Assert.NotNull(load.OpenBattleView);
        Assert.Contains("A tactical battle is open again", string.Join('\n', load.Lines), StringComparison.Ordinal);
        Assert.Equal(instant.OpenTacticalBattle, fresh.OpenTacticalBattle);
    }

    [Fact]
    public void A_version_6_save_loads_through_the_6_to_7_step_with_no_battle()
    {
        var session = NewSession();
        var path = PathFor("plain.json");
        session.Submit($"save {path}");
        var text = File.ReadAllText(path);
        var v6 = text.Replace("\"saveFormatVersion\": 7", "\"saveFormatVersion\": 6", StringComparison.Ordinal);
        Assert.NotEqual(text, v6);
        var v6Path = PathFor("plain.v6.json");
        File.WriteAllText(v6Path, v6);

        var loaded = Load(v6Path);

        Assert.Null(loaded.Battle);
        Assert.Equal(session.State, loaded.State);
        // Migrating a version 6 save and saving it again writes the version 7 text.
        Assert.Equal(text, SaveManager.Serialize(loaded));
        Assert.Null(Reload(loaded).OpenTacticalBattle);
    }

    [Fact]
    public void A_save_made_outside_a_battle_has_no_battle_section_and_equals_a_version_6_save_apart_from_the_version()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        var path = PathFor("outside.json");
        session.Submit($"save {path}");
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("\"battle\"", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split("\"saveFormatVersion\": 7").Length - 1);

        // A save made after a battle that ended has none either.
        var played = NewSession();
        TacticalSessionTestbed.Play(played, TacticalSessionTestbed.WonBattleScript);
        Assert.Null(played.OpenTacticalBattle);
        var afterPath = PathFor("after.json");
        played.Submit($"save {afterPath}");
        Assert.DoesNotContain("\"battle\"", File.ReadAllText(afterPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_works_during_a_battle()
    {
        var session = NewSession();
        session.Submit("attack-army north-army-1 south-army-1");
        var path = PathFor("during.json");

        var output = session.Submit($"save {path}");

        Assert.Equal($"Saved to '{path}'.", output.Lines[1]);
        Assert.NotNull(output.OpenBattleView);
        Assert.NotNull(Load(path).Battle);
    }

    // ---- Done-when 5: the exact-restore path ----

    [Fact]
    public void The_exact_restore_path_rebuilds_a_half_round_start_as_saved_and_plays_on_like_the_unsaved_battle()
    {
        var toy = TacticalSessionTestbed.Toy;
        var world = toy.World with { StartingArmies = ValueList.From(TacticalSessionTestbed.StrongAttacker()) };
        var game = GameStateFactory.CreateInitial(world, toy.Ruleset, toy.Scenario);
        var unsaved = TacticalBattleSession.Open(game, "north-army-1", "south-army-1", toy.Ruleset, out var opened);
        Assert.True(unsaved.EndHalfRound().Accepted);   // the attacker's placement ends; the attacker's movement begins
        var saved = unsaved.ToSaved();
        Assert.True(saved.AtHalfRoundStart);

        var restored = TacticalBattleSession.Reopen(opened, toy.Ruleset, saved, exactRestore: true);

        // As saved: the counter not incremented, moves and targets as they were, the claims and the stream too.
        var asSaved = TacticalBattleSession.FromSaved(saved);
        Assert.Equal(asSaved, restored.Battle);
        Assert.Equal(saved.Counter, restored.Battle.Counter);
        Assert.Equal(saved.DrawState, restored.DrawState);
        Assert.Equal(saved, restored.ToSaved());

        // Playing on gives the trace the unsaved battle gives.
        var logAtSave = unsaved.Battle.Log.Count;
        foreach (var battle in new[] { unsaved, restored })
        {
            Assert.True(battle.Move(0, 8, 5).Accepted);
            Assert.True(battle.Move(1, 6, 3).Accepted);
            Assert.True(battle.EndHalfRound().Accepted);
            Assert.True(battle.Move(0, 8, 6).Accepted);
            Assert.True(battle.Move(1, 8, 5).Accepted);
            Assert.True(battle.Shoot(1, 20).Accepted);
            Assert.True(battle.EndHalfRound().Accepted);
            Assert.True(battle.GeneralOn().Accepted);
        }

        Assert.True(unsaved.IsOver);
        Assert.True(restored.IsOver);
        Assert.Equal(unsaved.Battle.Log.Skip(logAtSave).ToList(), restored.Battle.Log.ToList());
        Assert.Equal(unsaved.Battle with { Log = ValueList<TacticalEvent>.Empty }, restored.Battle with { Log = ValueList<TacticalEvent>.Empty });
        Assert.Equal(unsaved.DrawState, restored.DrawState);
    }

    [Fact]
    public void Under_classical_faithful_every_reload_goes_through_Resume_at_a_half_round_start_or_not()
    {
        var toy = TacticalSessionTestbed.Toy;
        Assert.False(TacticalBattleSession.RulesetAsksForExactRestore(toy.Ruleset));

        // At a half-round's start, through the session's own load path: the counter rises by 1.
        var session = NewSession();
        TacticalSessionTestbed.Play(session, new[] { "attack-army north-army-1 south-army-1", "battle end" });
        var path = PathFor("start.json");
        session.Submit($"save {path}");
        var saved = Load(path);
        Assert.True(saved.Battle!.AtHalfRoundStart);
        Assert.Equal(saved.Battle.Counter + 1, Reload(saved).OpenTacticalBattle!.Counter);

        // Not at a half-round's start: it rises by 1 as well.
        var mid = SaveMidHalfRound(out _, out _);
        Assert.False(mid.Battle!.AtHalfRoundStart);
        Assert.Equal(mid.Battle.Counter + 1, Reload(mid).OpenTacticalBattle!.Counter);

        // Even asked for, exact restore needs the section to say so: a mid-half-round section is resumed.
        var game = GameStateFactory.CreateInitial(
            toy.World with { StartingArmies = ValueList.From(TacticalSessionTestbed.StrongAttacker()) }, toy.Ruleset, toy.Scenario);
        var asked = TacticalBattleSession.Reopen(game, toy.Ruleset, mid.Battle, exactRestore: true);
        Assert.Equal(mid.Battle.Counter + 1, asked.Battle.Counter);
    }
}
