using System.Text.Json;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class TacticalUndoTests
{
    private static GameSession Open(bool enabled = true)
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical,
            armies: TacticalSessionTestbed.StrongAttacker());
        session.TacticalUndoEnabled = enabled;
        session.Submit(TacticalSessionTestbed.WonBattleScript[0]);
        Assert.NotNull(session.OpenTacticalBattle);
        return session;
    }

    private static GameSession ReadyToShoot()
    {
        var session = Open();
        foreach (var line in TacticalSessionTestbed.WonBattleScript.Skip(1).Take(7)) session.Submit(line);
        Assert.NotNull(session.OpenTacticalBattle);
        return session;
    }

    [Fact]
    public void Placement_rewinds_exact_arena_campaign_rng_and_uses_a_fresh_presentation_epoch()
    {
        var session = Open();
        var before = session.OpenTacticalBattle;
        var campaign = session.State;
        var draw = session.TacticalDrawState;
        var placed = session.BattlePlace(1, 1, 2);
        Assert.True(session.CanUndoTactical);
        var output = session.BattleUndo();
        Assert.True(output.TacticalRewound);
        Assert.Equal(before, session.OpenTacticalBattle);
        Assert.Same(campaign, session.State);
        Assert.Equal(draw, session.TacticalDrawState);
        Assert.NotEmpty(output.TacticalTimeline);
        Assert.NotEqual(placed.TacticalTimeline[0].BattleId, output.TacticalTimeline[0].BattleId);
        Assert.False(session.CanUndoTactical);
    }

    [Fact]
    public void Refused_and_read_only_orders_do_not_add_history()
    {
        var session = Open();
        session.BattlePlace(1, 1, 2);
        session.Submit("battle show");
        session.BattlePlace(20, 5, 2); // Enemy placement is refused.
        session.BattleUndo();
        Assert.False(session.CanUndoTactical);
        var before = session.State;
        var view = session.OpenTacticalBattle;
        var draw = session.TacticalDrawState;
        session.BattleUndo();
        Assert.Same(before, session.State);
        Assert.Equal(view, session.OpenTacticalBattle);
        Assert.Equal(draw, session.TacticalDrawState);
    }

    [Fact]
    public void Undo_shot_restores_losses_shots_targets_and_random_stream_without_reroll()
    {
        var session = ReadyToShoot();
        var before = session.OpenTacticalBattle;
        var drawBefore = session.TacticalDrawState;
        var first = session.BattleShoot(1, 20);
        var after = session.OpenTacticalBattle;
        var drawAfter = session.TacticalDrawState;
        Assert.NotEqual(drawBefore, drawAfter);
        session.BattleUndo();
        Assert.Equal(before, session.OpenTacticalBattle);
        Assert.Equal(drawBefore, session.TacticalDrawState);
        var repeated = session.BattleShoot(1, 20);
        Assert.Equal(after, session.OpenTacticalBattle);
        Assert.Equal(drawAfter, session.TacticalDrawState);
        Assert.Equal(first.TacticalTimeline.Where(f => f.Event is not null).Select(f => f.Event),
            repeated.TacticalTimeline.Where(f => f.Event is not null).Select(f => f.Event));
    }

    [Fact]
    public void End_turn_cannot_undo_the_opponents_turn_and_the_next_human_turn_has_fresh_history()
    {
        var session = Open();
        session.BattlePlace(1, 1, 2);
        Assert.True(session.CanUndoTactical);
        session.BattleEnd();
        Assert.False(session.CanUndoTactical);
        var start = session.OpenTacticalBattle;
        session.BattleMove(0, 8, 5);
        Assert.True(session.CanUndoTactical);
        session.BattleUndo();
        Assert.Equal(start, session.OpenTacticalBattle);
        Assert.False(session.CanUndoTactical);
    }

    [Fact]
    public void Own_terminal_action_is_provisional_and_undo_restores_it_before_acknowledgement()
    {
        var session = Open();
        var before = session.OpenTacticalBattle;
        var campaign = session.State;
        var draw = session.TacticalDrawState;
        var first = session.BattleSurrender(true);
        Assert.NotNull(first.EndedBattleReport);
        Assert.True(session.BattleResultAwaitingAcknowledgement);
        Assert.True(session.CanUndoTactical);
        Assert.Same(campaign, session.State);
        session.BattleUndo();
        Assert.False(session.BattleResultAwaitingAcknowledgement);
        Assert.Equal(before, session.OpenTacticalBattle);
        Assert.Equal(draw, session.TacticalDrawState);
        Assert.Same(campaign, session.State);
        var repeated = session.BattleSurrender(true);
        Assert.Equal(JsonSerializer.Serialize(first.EndedBattleReport, GameJson.Options),
            JsonSerializer.Serialize(repeated.EndedBattleReport, GameJson.Options));
    }

    [Fact]
    public void Acknowledgement_commits_the_cached_result_once_and_matches_the_default_immediate_path()
    {
        var session = Open();
        var baseline = Open(enabled: false);
        baseline.BattleSurrender(true);
        session.BattleSurrender(true);
        var accepted = session.AcknowledgeBattleResult();
        Assert.Null(accepted.EndedBattleReport); // The preview must not be presented twice.
        Assert.Null(session.OpenTacticalBattle);
        Assert.False(session.CanUndoTactical);
        Assert.False(session.BattleResultAwaitingAcknowledgement);
        Assert.Equal(baseline.State, session.State);
        var committed = session.State;
        session.AcknowledgeBattleResult();
        Assert.Same(committed, session.State);
    }

    [Fact]
    public void Saving_a_provisional_result_refuses_before_touching_a_file()
    {
        var session = Open();
        session.BattleSurrender(true);
        var before = session.State;
        var output = session.Submit("save undo-pending-must-not-write.json");
        Assert.Contains(output.Lines, line => line == "Acknowledge or Undo the battle result before saving.");
        Assert.Same(before, session.State);
        Assert.True(session.BattleResultAwaitingAcknowledgement);
        Assert.True(session.CanUndoTactical);
    }

    [Fact]
    public void Repeated_undo_reaches_the_beginning_of_the_current_human_turn()
    {
        var session = Open();
        session.BattlePlace(1, 1, 2);
        session.BattleEnd();
        var start = session.OpenTacticalBattle;
        var draw = session.TacticalDrawState;
        session.BattleMove(0, 8, 5);
        session.BattleMove(1, 6, 3);
        session.BattleUndo();
        session.BattleUndo();
        Assert.Equal(start, session.OpenTacticalBattle);
        Assert.Equal(draw, session.TacticalDrawState);
        Assert.False(session.CanUndoTactical);
    }

    [Fact]
    public void AI_phase_continuation_waits_for_result_acknowledgement_and_matches_immediate_commit()
    {
        GameSession Build(bool undo)
        {
            var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical,
                armies: TacticalSessionTestbed.WeakAttacker());
            session.TacticalUndoEnabled = undo;
            session.Submit("declare-war south");
            session.Submit("end");
            Assert.NotNull(session.OpenTacticalBattle);
            Assert.Equal("south", session.State.ActiveNationId);
            return session;
        }
        var session = Build(true);
        var baseline = Build(false);
        var opened = session.State;
        session.BattleSurrender(true);
        Assert.Same(opened, session.State);
        Assert.True(session.BattleResultAwaitingAcknowledgement);
        session.BattleUndo();
        Assert.Same(opened, session.State);
        session.BattleSurrender(true);
        baseline.BattleSurrender(true);
        session.AcknowledgeBattleResult();
        Assert.Equal(baseline.State, session.State);
    }

    [Fact]
    public void Native_save_load_retains_the_existing_format_but_not_session_undo_history()
    {
        var toy = TacticalSessionTestbed.Toy;
        var rules = toy.Ruleset with { Combat = toy.Ruleset.Combat with { Tactical = toy.Ruleset.Combat.Tactical with
            { MidBattleSave = "anyTime" } } };
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, rules,
            armies: TacticalSessionTestbed.StrongAttacker());
        session.TacticalUndoEnabled = true;
        session.Submit(TacticalSessionTestbed.WonBattleScript[0]);
        session.BattlePlace(1, 1, 2);
        Assert.True(session.CanUndoTactical);
        var path = Path.Combine(Path.GetTempPath(), $"ic2-tactical-undo-{Guid.NewGuid():N}.json");
        try
        {
            session.Submit("save " + path);
            Assert.True(File.Exists(path));
            var saved = SaveManager.LoadFile(path, session.World, rules);
            var resumed = new GameSession(session.World, rules, session.Scenario, saved) { TacticalUndoEnabled = true };
            Assert.NotNull(resumed.OpenTacticalBattle);
            Assert.False(resumed.CanUndoTactical);
            Assert.DoesNotContain("tacticalUndo", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Target_assignment_can_be_undone_without_spending_or_rerolling()
    {
        var session = Open();
        foreach (var line in TacticalSessionTestbed.WonBattleScript.Skip(1).Take(9)) session.Submit(line);
        var before = session.OpenTacticalBattle;
        var draw = session.TacticalDrawState;
        session.BattleAttack(0, 20);
        Assert.True(session.CanUndoTactical);
        session.BattleUndo();
        Assert.Equal(before, session.OpenTacticalBattle);
        Assert.Equal(draw, session.TacticalDrawState);
    }

    [Fact]
    public void A_terminal_old_side_melee_remains_undoable_before_any_opponent_controller_handoff()
    {
        var session = Open();
        SessionOutput? ending = null;
        foreach (var line in TacticalSessionTestbed.WonBattleScript.Skip(1))
        {
            ending = session.Submit(line);
            if (ending.EndedBattleReport is not null) break;
        }
        Assert.NotNull(ending?.EndedBattleReport);
        Assert.True(session.BattleResultAwaitingAcknowledgement);
        Assert.True(session.CanUndoTactical);
        session.BattleUndo();
        Assert.False(session.OpenTacticalBattle!.IsOver);
        Assert.False(session.BattleResultAwaitingAcknowledgement);
    }

    [Fact]
    public void General_command_that_has_played_opponent_turns_cannot_be_undone()
    {
        var session = Open();
        session.BattleGeneral(true);
        Assert.True(session.BattleResultAwaitingAcknowledgement);
        Assert.False(session.CanUndoTactical);
    }

    [Fact]
    public void Disabling_history_cannot_reenable_an_older_turns_checkpoints()
    {
        var session = Open();
        session.BattlePlace(1, 1, 2);
        Assert.True(session.CanUndoTactical);
        session.TacticalUndoEnabled = false;
        session.BattleEnd();
        session.TacticalUndoEnabled = true;
        Assert.False(session.CanUndoTactical);
    }
}
