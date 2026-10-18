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
        queue.Begin(ending.TacticalTimeline, () => completions++);
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
        TacticalMovement.MoveUnit(arena, 0, 5, 2, context, new RngBattleDraws(new SplitMix64Rng(741)));
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

    private static TacticalBattleViewModel Model(TacticalPlayback queue)
    {
        var snapshot = queue.Snapshot!;
        return TacticalBattleViewModel.FromSnapshot(snapshot,
            new BattleDisplay("North", "South", snapshot.View.UnitTypeIds, snapshot.Width, snapshot.Height),
            selectedSlot: 0, frame: queue.CurrentFrame, currentExchange: queue.CurrentExchange,
            previousExchange: queue.PreviousExchange, perspectiveSide: 0, resolvingHalfRound: queue.ResolvingHalfRound,
            pose: queue.Pose, stage: queue.Stage);
    }

    [Theory]
    [InlineData(0.07)]
    [InlineData(1000)]
    public void Both_sides_travel_start_interior_arrival_before_combat_with_one_shared_snapshot_and_pose(double delta)
    {
        var (_, opening, ending) = Battle();
        var queue = new TacticalPlayback { ShotDelay = 1, AttackDelay = 1 };
        var stages = new Dictionary<long, HashSet<string>>();
        var sides = new HashSet<int>();
        var announced = new HashSet<int>();
        queue.Begin(opening.Concat(ending.TacticalTimeline));
        for (var i = 0; queue.IsPlaying && i < 100000; i++)
        {
            var frame = queue.CurrentFrame!;
            var model = Model(queue);
            Assert.Same(queue.Snapshot!.View, model.View);
            if (queue.Stage == TacticalPlaybackStage.Announcement)
            {
                Assert.False(queue.ResolvingHalfRound);
                announced.Add(model.View.SideToMove);
                Assert.Contains("Half-round", model.ControllerText);
            }
            if (queue.Pose is { Departing: false } pose)
            {
                sides.Add(pose.Glyph.Side);
                Assert.Contains(pose.Glyph.Side, announced);
                if (!stages.TryGetValue(frame.FrameId, out var visited)) stages[frame.FrameId] = visited = new();
                var unit = Assert.Single(model.VisualUnits, u => u.Cell.Slot == pose.Glyph.Slot);
                Assert.Equal(pose.X, unit.X);
                Assert.Equal(pose.Y, unit.Y);
                Assert.Contains(model.PositionText(pose.Glyph.Slot), string.Join(" ", model.ParticipantLines(pose.Glyph.Slot, "unit")));
                Assert.Contains(model.RosterRows(pose.Glyph.Side), r => r.Slot == pose.Glyph.Slot && r.Cells.Contains(model.PositionText(pose.Glyph.Slot)));
                if (queue.Stage == TacticalPlaybackStage.Travel)
                {
                    Assert.Same(frame.Before, queue.Snapshot);
                    if (queue.Progress == 0) visited.Add("start");
                    else
                    {
                        visited.Add("interior");
                        Assert.InRange(queue.Progress, double.Epsilon, 0.999999);
                        var end = frame.After.View.Slots[pose.Glyph.Slot];
                        Assert.Equal(pose.Glyph.X + (end.X - pose.Glyph.X) * queue.Progress, pose.X, 8);
                        Assert.Equal(pose.Glyph.Y + (end.Y - pose.Glyph.Y) * queue.Progress, pose.Y, 8);
                    }
                }
                if (queue.Stage == TacticalPlaybackStage.Arrival)
                {
                    Assert.Same(frame.After, queue.Snapshot);
                    Assert.Equal(frame.After.View.Slots[pose.Glyph.Slot].X, pose.X);
                    visited.Add("arrival");
                }
            }
            queue.Advance(delta);
        }
        Assert.False(queue.IsPlaying);
        Assert.Equal(new[] { 0, 1 }, sides.Order());
        Assert.NotEmpty(stages);
        Assert.All(stages.Values, stagesSeen => Assert.True(stagesSeen.SetEquals(new[] { "start", "interior", "arrival" })));
        Assert.Null(queue.Pose);
    }

    [Fact]
    public void Routed_exchange_keeps_losses_roles_and_last_glyph_through_hold_fade_then_atomic_removal()
    {
        var (_, opening, ending) = Battle();
        var queue = new TacticalPlayback { ShotDelay = 1, AttackDelay = 1 };
        queue.Begin(opening.Concat(ending.TacticalTimeline));
        var holds = 0;
        var fades = 0;
        var removals = 0;
        TacticalBattleFrame? fading = null;
        for (var i = 0; queue.IsPlaying && i < 100000; i++)
        {
            var frame = queue.CurrentFrame!;
            var model = Model(queue);
            if (queue.Stage is TacticalPlaybackStage.RoutHold or TacticalPlaybackStage.RoutFade)
            {
                Assert.Same(frame.Before, queue.Snapshot);
                Assert.Contains(model.CurrentExchangeLines, l => l.Contains(frame.Event is TacticalRoutedEvent ? "UNIT ROUTED" : "ROUT CASCADE REMOVAL"));
                Assert.Contains(model.CurrentExchangeLines, l => l.Contains($"#{frame.Actor!.Slot}"));
                if (queue.CurrentExchange is { } exchange)
                {
                    Assert.Equal(exchange.Actor?.Slot ?? -1, model.ActorSlot);
                    Assert.Equal(exchange.Target?.Slot ?? -1, model.TargetSlot);
                    Assert.Contains(model.CurrentExchangeLines, l => l.Contains("LOSSES"));
                }
                if (queue.Stage == TacticalPlaybackStage.RoutHold) holds++;
                if (queue.Pose is { } pose)
                {
                    Assert.True(pose.Departing);
                    Assert.Equal(pose.Glyph.X, pose.X);
                    Assert.Equal(pose.Glyph.Y, pose.Y);
                    var glyph = Assert.Single(model.VisualUnits, u => u.Cell.Slot == frame.Actor!.Slot);
                    Assert.Equal(pose.Opacity, glyph.Opacity);
                    Assert.Equal(-1, model.SlotAt(pose.Glyph.X, pose.Glyph.Y));
                    Assert.Contains(model.ParticipantLines(pose.Glyph.Slot, "unit"), l => l.Contains("departing"));
                    if (queue.Stage == TacticalPlaybackStage.RoutFade && queue.Progress > 0)
                    {
                        Assert.InRange(pose.Opacity, double.Epsilon, 0.999999);
                        fades++;
                        fading = frame;
                    }
                }
            }
            if (fading == frame && queue.Stage == TacticalPlaybackStage.Frame)
            {
                Assert.Same(frame.After, queue.Snapshot);
                Assert.Null(queue.Pose);
                Assert.DoesNotContain(model.VisualUnits, u => u.Cell.Slot == frame.Actor!.Slot);
                Assert.DoesNotContain(model.RosterRows(frame.Actor!.Side), r => r.Slot == frame.Actor.Slot);
                removals++;
                fading = null;
            }
            queue.Advance(1000);
        }
        Assert.True(holds > 0);
        Assert.True(fades > 0);
        Assert.True(removals > 0);
    }

    [Theory]
    [InlineData(TacticalPlaybackStage.Announcement)]
    [InlineData(TacticalPlaybackStage.Travel)]
    [InlineData(TacticalPlaybackStage.Arrival)]
    [InlineData(TacticalPlaybackStage.RoutHold)]
    [InlineData(TacticalPlaybackStage.RoutFade)]
    public void Finish_at_each_protected_stage_consumes_terminal_housekeeping_and_notifications_once(TacticalPlaybackStage stage)
    {
        var (_, opening, ending) = Battle();
        var frames = opening.Concat(ending.TacticalTimeline).ToArray();
        var queue = new TacticalPlayback { ShotDelay = 1, AttackDelay = 1 };
        var entries = new List<TacticalBattleFrame>();
        var cues = new List<TacticalBattleFrame>();
        var completions = 0;
        queue.FrameEntered += entries.Add;
        queue.ExchangeEntered += cues.Add;
        queue.Begin(frames, () => { Assert.Equal(frames[^1], queue.CurrentFrame); Assert.Null(queue.Pose); completions++; });
        for (var i = 0; queue.IsPlaying && (queue.Stage != stage || stage == TacticalPlaybackStage.RoutFade && queue.Progress == 0) && i < 100000; i++) queue.Advance(0.1);
        Assert.True(queue.IsPlaying);
        Assert.Equal(stage, queue.Stage);
        var progress = queue.Progress;
        queue.Advance(0);
        Assert.Equal(progress, queue.Progress);
        queue.Finish();
        queue.Finish();
        queue.Begin(frames, () => completions++);
        Assert.Equal(1, completions);
        Assert.Equal(frames, entries);
        Assert.Equal(frames.Where(f => f.Event is TacticalShotEvent or TacticalMeleeEvent), cues);
        Assert.Same(frames[^1].After, queue.Snapshot);
    }

    [Fact]
    public void Rout_without_prior_display_or_exchange_has_notice_hold_and_no_phantom_glyph()
    {
        var (_, _, ending) = Battle();
        var rout = ending.TacticalTimeline.First(f => f.Event is TacticalRoutedEvent);
        var queue = new TacticalPlayback { AttackDelay = 1 };
        queue.Begin(new[] { rout });
        Assert.Equal(TacticalPlaybackStage.RoutHold, queue.Stage);
        Assert.Null(queue.Pose);
        Assert.Null(queue.CurrentExchange);
        Assert.Contains(Model(queue).CurrentExchangeLines, l => l.Contains("UNIT ROUTED"));
        queue.Finish();
        Assert.Same(rout.After, queue.Snapshot);
        Assert.Null(queue.Pose);
    }

    [Theory]
    [InlineData(0.1, 0.2, 0.1)]
    [InlineData(1, 0.6, 0.4)]
    [InlineData(3, 0.6, 0.4)]
    public void Display_only_transition_pacing_is_bounded_and_arrival_has_its_own_wait(double delay, double travelSeconds, double arrivalSeconds)
    {
        var (_, _, ending) = Battle();
        var move = ending.TacticalTimeline.First(f => f.Event is TacticalMovedEvent e && e.FromX != e.ToX
            && f.Before.View.Slots[e.Slot].IsLive && !f.Before.PendingPlacementSlots.Contains(e.Slot));
        var queue = new TacticalPlayback { ShotDelay = delay };
        queue.Begin(new[] { move });
        Assert.Equal(TacticalPlaybackStage.Travel, queue.Stage);
        queue.Advance(travelSeconds * 0.25);
        Assert.Equal(0.25, queue.Progress, 8);
        queue.Advance(travelSeconds);
        Assert.Equal(TacticalPlaybackStage.Arrival, queue.Stage);
        Assert.Equal(0, queue.Progress);
        queue.Advance(arrivalSeconds * 0.5);
        Assert.Equal(TacticalPlaybackStage.Arrival, queue.Stage);
        Assert.Equal(0.5, queue.Progress, 8);
        queue.Finish();
        Assert.Same(move.After, queue.Snapshot);
    }

    [Fact]
    public void Every_cascade_victim_gets_its_own_ordered_hold_fade_and_removal_without_new_exchange()
    {
        var context = TacticalContext.From(TacticalTestbed.ToyRuleset);
        var timeline = new TacticalBattleTimeline(context, "cascade-stages", "north", "south");
        context.Observer = timeline.Observe;
        var arena = TacticalTestbed.Arena(
            (0, TacticalTestbed.Unit(TacticalTestbed.LI, 1, 1, 500, morale: 10)),
            (1, TacticalTestbed.Unit(TacticalTestbed.LI, 2, 1, 500, morale: 31)),
            (2, TacticalTestbed.Unit(TacticalTestbed.LI, 3, 1, 500, morale: 32)),
            (20, TacticalTestbed.Unit(TacticalTestbed.HI, 4, 1, 500, morale: 60, target: 0)));
        timeline.Observe(arena, null);
        TacticalRout.Rout(arena, 0, context, NoDraws.Instance);
        var queue = new TacticalPlayback { AttackDelay = 1 };
        var cues = 0;
        queue.ExchangeEntered += _ => cues++;
        queue.Begin(timeline.Drain());
        var held = new List<int>();
        var faded = new List<int>();
        var removed = new List<int>();
        for (var i = 0; queue.IsPlaying && i < 1000; i++)
        {
            var frame = queue.CurrentFrame!;
            if (queue.Stage == TacticalPlaybackStage.RoutHold)
            {
                held.Add(frame.Actor!.Slot);
                Assert.NotNull(queue.Pose);
                Assert.Equal(frame.Actor.Slot == 0 ? "UNIT ROUTED" : "ROUT CASCADE REMOVAL", Model(queue).DepartureLines[0].Split(" · ")[0]);
            }
            if (queue.Stage == TacticalPlaybackStage.RoutFade && queue.Progress > 0) faded.Add(frame.Actor!.Slot);
            if (frame.Event is TacticalRoutedEvent or TacticalCascadeRemovedEvent && queue.Stage == TacticalPlaybackStage.Frame)
            {
                removed.Add(frame.Actor!.Slot);
                Assert.Null(queue.Pose);
                Assert.Same(frame.After, queue.Snapshot);
                foreach (var slot in removed) Assert.DoesNotContain(Model(queue).VisualUnits, u => u.Cell.Slot == slot);
            }
            queue.Advance(1000);
        }
        Assert.Equal(new[] { 0, 1, 2 }, held);
        Assert.Equal(held, faded);
        Assert.Equal(held, removed);
        Assert.Equal(0, cues);
    }

    [Fact]
    public void Off_grid_pending_copy_in_and_no_distance_endpoints_never_fabricate_travel()
    {
        var (_, _, ending) = Battle();
        var move = ending.TacticalTimeline.First(f => f.Event is TacticalMovedEvent);
        var e = (TacticalMovedEvent)move.Event!;
        foreach (var frame in new[]
        {
            move with { Event = e with { FromX = -1 } },
            move with { Event = e with { ToX = e.FromX, ToY = e.FromY } },
            move with { Kind = TacticalFrameKind.Placement, Event = new TacticalCopiedInEvent(e.Slot, e.ToX, e.ToY, 60) },
            move with { Kind = TacticalFrameKind.Placement, Event = new TacticalPlacedEvent(e.Slot, e.FromX, e.FromY, e.ToX, e.ToY),
                Before = move.Before with { PendingPlacementSlots = ValueList.Of(e.Slot) } },
        })
        {
            var queue = new TacticalPlayback { ShotDelay = 1 };
            queue.Begin(new[] { frame });
            Assert.Null(queue.Pose);
            Assert.NotEqual(TacticalPlaybackStage.Travel, queue.Stage);
            queue.Finish();
            Assert.Same(frame.After, queue.Snapshot);
        }
    }

    [Fact]
    public void Overkill_rout_fades_the_last_actually_displayed_glyph_despite_negative_current_icon()
    {
        var toy = TacticalSessionTestbed.Toy;
        var world = toy.World with
        {
            StartingArmies = ValueList.From(toy.World.StartingArmies.Select(a => a.Id == "south-army-1"
                ? a with { X = 4, Y = 2, Units = ValueList.Of(new UnitSlot(0, "heavy_infantry", 50_000, 6, "Guards")) }
                : a)),
        };
        var session = new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Tactical };
        session.Submit("declare-war south");
        var opening = session.Submit("end");
        var ending = session.BattleGeneral(true);
        var queue = new TacticalPlayback { AttackDelay = 1 };
        queue.Begin(opening.TacticalTimeline.Concat(ending.TacticalTimeline));
        var overkill = 0;
        for (var i = 0; queue.IsPlaying && i < 100000; i++)
        {
            if (queue.Stage == TacticalPlaybackStage.RoutHold && queue.CurrentFrame is { } frame
                && frame.Before.View.Slots[frame.Actor!.Slot].Troops < 0)
            {
                overkill++;
                var pose = Assert.IsType<TacticalPose>(queue.Pose);
                Assert.Equal(frame.Actor.Slot, pose.Glyph.Slot);
                Assert.InRange(pose.Glyph.Code, 0, TacticalIcon.DefenderOffset + 14);
                Assert.Equal(frame.Before.View.Slots[frame.Actor.Slot].X, pose.X);
                Assert.Single(Model(queue).VisualUnits, u => u.Cell.Slot == frame.Actor.Slot);
                Assert.Contains(Model(queue).CurrentExchangeLines, l => l.Contains("LOSSES"));
            }
            queue.Advance(1000);
        }
        Assert.True(overkill > 0);
    }
}
