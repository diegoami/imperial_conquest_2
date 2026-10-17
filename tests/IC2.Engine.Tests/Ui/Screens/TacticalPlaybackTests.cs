using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Core;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Presentation;
using IC2.Engine.Tests.Battle.Tactical;
using IC2.Slice.Screens;
using Xunit;

namespace IC2.Engine.Tests.Ui.Screens;

public sealed class TacticalPlaybackTests
{
    private static (GameSession Session, IReadOnlyList<TacticalBattleFrame> Opening, SessionOutput Ending) Battle()
    {
        var armies = TacticalSessionTestbed.StrongAttacker().Select(a => a with
        {
            Units = ValueList.From(a.Units.Select(u => u with { Name = "duplicate" })),
        }).ToList();
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: armies);
        var opening = session.Submit("attack-army north-army-1 south-army-1").TacticalTimeline;
        return (session, opening, session.BattleGeneral(true));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void Every_frame_and_typed_exchange_once_in_order_before_completion_including_null_final_view(double delay, bool skip)
    {
        var (session, opening, ending) = Battle();
        Assert.Null(ending.OpenBattleView);
        var authoritative = session.State;
        var all = opening.Concat(ending.TacticalTimeline).ToArray();
        var seen = new List<TacticalBattleFrame>();
        var cues = new List<TacticalBattleFrame>();
        var queue = new TacticalPlayback { ShotDelay = delay, AttackDelay = delay };
        queue.FrameEntered += seen.Add;
        queue.ExchangeEntered += cues.Add;
        var completions = 0;
        queue.Begin(opening);
        queue.Finish();
        // The producer hands the same batch out via peek/Last/output; no repeated notifications.
        queue.Begin(opening);
        queue.Finish();
        queue.Begin(ending.TacticalTimeline, () =>
        {
            Assert.Equal(all[^1], queue.CurrentFrame);
            Assert.False(queue.IsPlaying);
            Assert.Equal(TacticalFrameKind.Reconciliation, queue.CurrentFrame!.Kind);
            completions++;
        });
        if (skip) queue.Finish();
        else for (var i = 0; queue.IsPlaying && i < 100000; i++) queue.Advance(0.1);
        queue.Finish();
        queue.Advance(100);
        Assert.False(queue.IsPlaying);
        Assert.Equal(1, completions);
        Assert.Equal(all, seen);
        Assert.Equal(all.Where(f => f.Event is TacticalShotEvent or TacticalMeleeEvent), cues);
        Assert.NotEmpty(cues);
        Assert.Same(authoritative, session.State); // traversal never submits an order
    }

    [Fact]
    public void Half_round_does_not_release_controller_before_post_melee_setup_and_history_clears_per_command_and_battle()
    {
        var (_, opening, ending) = Battle();
        var queue = new TacticalPlayback();
        var handoff = false;
        var meleeDuringHandoff = false;
        queue.FrameEntered += f =>
        {
            if (f.Kind == TacticalFrameKind.HalfRound) { Assert.True(queue.ResolvingHalfRound); handoff = true; }
            if (handoff && f.Kind == TacticalFrameKind.Melee) { Assert.True(queue.ResolvingHalfRound); meleeDuringHandoff = true; }
            if (f.Kind == TacticalFrameKind.Setup) { Assert.False(queue.ResolvingHalfRound); handoff = false; }
        };
        queue.Begin(opening);
        queue.Begin(ending.TacticalTimeline);
        Assert.True(meleeDuringHandoff);
        Assert.NotNull(queue.PreviousExchange);
        var final = ending.TacticalTimeline[^1];
        queue.Begin(new[] { final with { FrameId = final.FrameId + 1, CommandId = final.CommandId + 1 } });
        Assert.Null(queue.CurrentExchange);
        Assert.Null(queue.PreviousExchange);
        queue.Begin(new[] { opening[0] with { BattleId = "next-battle" } });
        Assert.Null(queue.CurrentExchange);
        Assert.Null(queue.PreviousExchange);
        Assert.False(queue.ResolvingHalfRound);
    }

    [Fact]
    public void Advance_carries_elapsed_time_across_waits_and_finish_uses_same_exchange_entry_path()
    {
        var (_, _, ending) = Battle();
        var exchanges = ending.TacticalTimeline.Where(f => f.Event is TacticalShotEvent or TacticalMeleeEvent).Take(3).ToArray();
        Assert.Equal(3, exchanges.Length);
        var queue = new TacticalPlayback { ShotDelay = 1, AttackDelay = 1 };
        var cues = new List<TacticalBattleFrame>();
        queue.ExchangeEntered += cues.Add;
        queue.Begin(exchanges);
        Assert.Single(cues);
        queue.Advance(0.5);
        Assert.Single(cues);
        queue.Advance(1.6);
        Assert.Equal(3, cues.Count);
        Assert.Equal(exchanges[2], queue.CurrentExchange);
        Assert.Equal(exchanges[1], queue.PreviousExchange);
        queue.Finish();
        Assert.Equal(exchanges, cues);
    }

    [Fact]
    public void Movement_precedes_shot_at_destination_and_all_rout_cascade_victims_disappear_in_displayed_snapshot()
    {
        var context = TacticalContext.From(TacticalTestbed.ToyRuleset);
        var timeline = new TacticalBattleTimeline(context, "movement", "north", "south");
        context.Observer = timeline.Observe;
        var arena = TacticalTestbed.Arena(new[]
        {
            (0, TacticalTestbed.Unit(TacticalTestbed.AR, 2, 2, 1800, moves: 2)),
            (20, TacticalTestbed.Unit(TacticalTestbed.HI, 5, 2, 2200)),
        }, attackerComputer: true);
        TacticalMovement.MoveUnit(arena, 0, 5, 2, context, new SeededDraws(741));
        var queue = new TacticalPlayback();
        var visited = new List<TacticalBattleFrame>();
        queue.FrameEntered += visited.Add;
        queue.Begin(timeline.Drain());
        var shot = Assert.Single(visited, f => f.Kind == TacticalFrameKind.Shot);
        var moved = visited.Last(f => f.Kind == TacticalFrameKind.Movement);
        Assert.True(moved.FrameId < shot.FrameId);
        Assert.Equal(3, shot.After.View.Slots[0].X);
        Assert.Equal(moved.After.View.Slots[0].X, shot.After.View.Slots[0].X);
        Assert.Equal(((TacticalShotEvent)shot.Event!).TargetTroops, shot.After.View.Slots[20].Troops);

        timeline = new TacticalBattleTimeline(context, "cascade", "north", "south");
        context.Observer = timeline.Observe;
        arena = TacticalTestbed.Arena(
            (0, TacticalTestbed.Unit(TacticalTestbed.LI, 1, 1, 500, morale: 10)),
            (1, TacticalTestbed.Unit(TacticalTestbed.LI, 2, 1, 500, morale: 31)),
            (2, TacticalTestbed.Unit(TacticalTestbed.LI, 3, 1, 500, morale: 32)),
            (20, TacticalTestbed.Unit(TacticalTestbed.HI, 4, 1, 500, morale: 60, target: 0)));
        TacticalRout.Rout(arena, 0, context, NoDraws.Instance);
        var removed = new List<int>();
        queue.FrameEntered += frame =>
        {
            if (frame.Kind is not (TacticalFrameKind.Rout or TacticalFrameKind.Cascade)) return;
            var slot = frame.Actor!.Slot;
            removed.Add(slot);
            var display = new BattleDisplay("North", "South", frame.After.View.UnitTypeIds, frame.After.Width, frame.After.Height);
            var model = TacticalBattleViewModel.FromSnapshot(frame.After, display, frame: frame, perspectiveSide: 0);
            foreach (var victim in removed)
            {
                Assert.DoesNotContain(model.Cells, cell => cell.Slot == victim);
                Assert.DoesNotContain(model.RosterRows(0), row => row.Slot == victim);
                Assert.Contains(model.ParticipantLines(victim, "removed"), line => line.Contains("0 troops") && line.Contains("removed"));
            }
        };
        queue.Begin(timeline.Drain());
        Assert.Equal(new[] { 0, 1, 2 }, removed);
        Assert.Equal(arena.Slots[20].Morale + context.Rules.RoutEnemyBonus, queue.CurrentFrame!.After.View.Slots[20].Morale);
    }
}
