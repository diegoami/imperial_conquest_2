using IC2.Engine.Model;
using IC2.Engine.Presentation;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class StrategicUndoTests
{
    private static GameSession Session(HumanBattleMode mode = HumanBattleMode.Instant)
    {
        var session = TacticalSessionTestbed.ToySession(mode);
        session.Submit("status");
        session.StrategicUndoEnabled = true;
        return session;
    }

    [Fact]
    public void Move_undo_and_replay_restore_the_exact_campaign_and_random_seed()
    {
        var session = Session();
        var before = session.State;
        session.Submit("move north-army-1 3 3");
        var after = session.State;
        Assert.NotEqual(before, after);
        Assert.True(session.CanUndoStrategic);
        Assert.Equal(1, session.StrategicUndoCount);
        session.StrategicUndo();
        Assert.Equal(before, session.State);
        Assert.False(session.CanUndoStrategic);
        session.Submit("move north-army-1 3 3");
        Assert.Equal(after, session.State);
    }

    [Fact]
    public void Repeated_undo_includes_campaign_metadata_and_stops_at_the_turn_start()
    {
        var session = Session();
        var before = session.State;
        session.Submit("set-tax 10");
        session.SetArmyTravelTarget(new("north-army-1", "north", 3, 3));
        session.SetRallyPoint(new("north", 4, 3));
        Assert.Equal(3, session.StrategicUndoCount);
        session.StrategicUndo();
        Assert.Empty(session.NationRallyPoints);
        Assert.Single(session.ArmyTravelTargets);
        session.StrategicUndo();
        Assert.Empty(session.ArmyTravelTargets);
        session.StrategicUndo();
        Assert.Equal(before, session.State);
        Assert.False(session.CanUndoStrategic);
    }

    [Fact]
    public void A_group_restores_the_command_and_related_metadata_as_one_action()
    {
        var session = Session();
        session.SetArmyTravelTarget(new("north-army-1", "north", 5, 3));
        session.ClearStrategicUndo();
        var before = session.State;
        using (session.BeginStrategicUndoAction())
        {
            session.Submit("move north-army-1 3 3");
            session.ClearArmyTravelTarget("north-army-1");
        }
        Assert.Equal(1, session.StrategicUndoCount);
        using (session.BeginStrategicUndoAction()) session.StrategicUndo();
        Assert.Equal(before, session.State);
        Assert.Single(session.ArmyTravelTargets);
        Assert.Equal(0, session.StrategicUndoCount);
    }

    [Fact]
    public void Refusals_and_read_only_commands_do_not_create_history()
    {
        var session = Session();
        var before = session.State;
        session.Submit("move missing 3 3");
        session.Submit("status"); session.Submit("help"); session.Submit("news");
        Assert.Equal(before, session.State);
        Assert.Equal(0, session.StrategicUndoCount);
    }

    [Fact]
    public void Confirmed_End_turn_clears_history_before_the_opponent_acts()
    {
        var session = Session();
        session.Submit("set-tax 10");
        Assert.True(session.CanUndoStrategic);
        session.Submit("end");
        var after = session.State;
        Assert.Equal(0, session.StrategicUndoCount);
        session.StrategicUndo();
        Assert.Equal(after, session.State);
    }

    [Theory]
    [InlineData(HumanBattleMode.Instant)]
    [InlineData(HumanBattleMode.Tactical)]
    public void Player_initiated_battle_is_one_strategic_action_with_exact_replay(HumanBattleMode mode)
    {
        var session = Session(mode);
        var before = session.State;
        var result = session.Submit("attack-army north-army-1 south-army-1");
        if (mode == HumanBattleMode.Tactical)
        {
            Assert.NotNull(result.OpenBattleView);
            Assert.False(session.CanUndoStrategic);
            session.Submit("battle general on");
        }
        Assert.Null(session.OpenTacticalBattle);
        var after = session.State;
        Assert.NotEqual(before, after);
        Assert.Equal(1, session.StrategicUndoCount);
        session.StrategicUndo();
        Assert.Equal(before, session.State);
        session.Submit("attack-army north-army-1 south-army-1");
        if (mode == HumanBattleMode.Tactical) session.Submit("battle general on");
        Assert.Equal(after, session.State);
    }

    [Fact]
    public void A_staged_dialog_is_one_undo_action_and_preview_or_cancel_is_not_history()
    {
        var session = Session();
        var before = session.State;
        var draft = session.BeginDialogEdit(DialogEditKind.Army);
        Assert.True(draft.Stage("rename-unit north-army-1 0 Renamed").Accepted);
        Assert.Equal(before, session.State);
        Assert.Equal(0, session.StrategicUndoCount);
        Assert.True(draft.Stage("rename-unit north-army-1 1 Bowmen").Accepted);
        Assert.True(draft.Commit().Accepted);
        Assert.Equal(1, session.StrategicUndoCount);
        session.StrategicUndo();
        Assert.Equal(before, session.State);
        var cancelled = session.BeginDialogEdit(DialogEditKind.Army);
        Assert.True(cancelled.Stage("rename-unit north-army-1 0 Other").Accepted);
        cancelled.Cancel();
        Assert.Equal(0, session.StrategicUndoCount);
    }

    [Fact]
    public void Native_load_clears_history_without_changing_the_save_format()
    {
        var session = Session();
        var before = session.State;
        var path = Path.Combine(Path.GetTempPath(), "ic2-undo-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            session.Submit($"save {path}");
            Assert.True(File.Exists(path));
            session.Submit("set-tax 10");
            Assert.True(session.CanUndoStrategic);
            session.Submit($"load {path}");
            Assert.Equal(before, session.State);
            Assert.Equal(0, session.StrategicUndoCount);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Hotseat_End_turn_does_not_expose_the_previous_players_history()
    {
        var session = TacticalSessionTestbed.HotseatSession(HumanBattleMode.Instant, TacticalSessionTestbed.DefaultArmies());
        session.StrategicUndoEnabled = true;
        var first = session.State.ActiveNationId;
        session.Submit("set-tax 10");
        Assert.True(session.CanUndoStrategic);
        session.Submit("end");
        Assert.NotEqual(first, session.State.ActiveNationId);
        Assert.False(session.CanUndoStrategic);
        session.Submit("set-tax 12");
        Assert.Equal(1, session.StrategicUndoCount);
        session.StrategicUndo();
        Assert.Equal(0, session.StrategicUndoCount);
    }

    [Fact]
    public void Undoing_support_restores_resources_but_keeps_the_runtime_global_preference()
    {
        var session = Session();
        var before = session.State;
        session.ConfigureHumanSupport(new IC2.Engine.Economy.HumanSupportOptions { SupplyArmies = true });
        Assert.NotEqual(before, session.State);
        Assert.True(session.CanUndoStrategic);
        session.StrategicUndo();
        Assert.Equal(before, session.State);
        Assert.True(session.HumanSupport!.SupplyArmies);
    }

    [Fact]
    public void Undo_restores_a_peace_offer_answer_then_the_whole_initiating_battle()
    {
        var f = IC2.Engine.Tests.Core.CoreTestbed.Toy;
        var reserve = new StartingArmy("south-reserve", "south", 0, 5, 1, 0, 0, 5,
            ValueList.Of(new UnitSlot(0, "heavy_infantry", 480000, 5, "Reserve")));
        var world = f.World with { StartingArmies = ValueList.From(f.World.StartingArmies.Select(a => a.Id switch {
            "north-army-1" => a with { Units = ValueList.Of(new UnitSlot(0, "heavy_infantry", 6000, 6, "Guards")) },
            "south-army-1" => a with { X = 4, Y = 2, Units = ValueList.Of(new UnitSlot(0, "light_infantry", 15000, 6, "Foot")) },
            _ => a }).Append(reserve)) };
        var rules = f.Ruleset with { Combat = f.Ruleset.Combat with {
            AutoPeaceChanceNumerator = f.Ruleset.Combat.AutoPeaceChanceDenominator,
            AutoPeaceLoserUnityThreshold = -1, AutoPeaceLoserCityThreshold = 0 } };
        var session = new GameSession(world, rules, f.Scenario);
        session.Submit("declare-war south");
        session.StrategicUndoEnabled = true;
        var before = session.State;
        session.Submit("attack-army north-army-1 south-army-1");
        Assert.True(session.HasPendingPeaceOffers);
        var afterBattle = session.State;
        session.Submit("peace-no");
        Assert.False(session.HasPendingPeaceOffers);
        session.StrategicUndo();
        Assert.True(session.HasPendingPeaceOffers);
        Assert.Equal(afterBattle, session.State);
        session.StrategicUndo();
        Assert.False(session.HasPendingPeaceOffers);
        Assert.Equal(before, session.State);
    }
}
