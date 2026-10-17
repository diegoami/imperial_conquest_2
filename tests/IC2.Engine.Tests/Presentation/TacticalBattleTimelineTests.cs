using System.Reflection;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Battle.Tactical;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class TacticalBattleTimelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Opening_captures_copy_in_setup_automatic_placement_and_both_human_sides(bool hotseat)
    {
        var armies = TacticalSessionTestbed.DefaultArmies();
        armies = armies.Select(a => a with
        {
            Units = ValueList.From(a.Units.Select(u => u with { Name = "duplicate" })),
        }).ToList();
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: armies, hotseat: hotseat);
        var opening = session.Submit("attack-army north-army-1 south-army-1");
        var frames = opening.TacticalTimeline;
        Assert.Equal(TacticalFrameKind.Initial, frames[0].Kind);
        Assert.All(frames[0].After.View.Slots, s => Assert.Equal(0, s.Troops));
        var copied = frames.Where(f => f.Event is TacticalCopiedInEvent).ToArray();
        Assert.Equal(3, copied.Length);
        Assert.Equal(3, copied.Select(f => f.Actor).Distinct().Count());
        Assert.All(copied, f =>
        {
            var id = Assert.IsType<TacticalUnitIdentity>(f.Actor);
            var slot = f.After.View.Slots[id.Slot];
            Assert.Equal("duplicate", slot.Name);
            Assert.True(slot.Troops > 0);
            Assert.Equal(0, f.Before.View.Slots[id.Slot].Troops);
            Assert.Equal(TacticalTestbed.Context.ShotsOf(slot.Type), slot.Shots);
            Assert.InRange(slot.Morale, session.Ruleset.Combat.Tactical.MoraleMin, session.Ruleset.Combat.Tactical.MoraleMax);
            Assert.Equal(f.After.View.UnitTypeIds[slot.Type], id.TypeId);
            Assert.Equal(id.Side == 0 ? "north" : "south", id.NationId);
        });
        Assert.Contains(frames, f => f.Kind == TacticalFrameKind.Setup);
        Assert.Equal(opening.OpenBattleView, frames[^1].After.View);
        if (hotseat)
        {
            Assert.Equal(1, opening.OpenBattleView!.SideToMove);
            var defender = session.Submit("battle place 20 4 10");
            Assert.Single(defender.TacticalTimeline, f => f.Event is TacticalPlacedEvent);
            var handoff = session.Submit("battle end");
            Assert.Contains(handoff.TacticalTimeline, f => f.Event is TacticalHalfRoundEndedEvent { NextSide: 0 });
            var attacker = session.Submit("battle place 0 4 1");
            Assert.Single(attacker.TacticalTimeline, f => f.Event is TacticalPlacedEvent);
            Assert.Equal(0, attacker.OpenBattleView!.SideToMove);
            var movementPhase = session.Submit("battle end");
            Assert.Contains(movementPhase.TacticalTimeline, f => f.Event is TacticalHalfRoundEndedEvent { Placed: true, NextSide: 0 });
            Assert.Contains(movementPhase.TacticalTimeline, f => f.Kind == TacticalFrameKind.Setup && f.After.View.Placed);
            var nextSide = session.Submit("battle end");
            Assert.Contains(nextSide.TacticalTimeline, f => f.Event is TacticalHalfRoundEndedEvent { NextSide: 1 });
        }
        else
        {
            var staged = Assert.Single(frames, f => f.Kind == TacticalFrameKind.Placement && f.Event is null && f.After.PendingPlacementSlots.Count > 0);
            Assert.Empty(staged.Before.PendingPlacementSlots);
            Assert.Equal(new[] { 20 }, staged.After.PendingPlacementSlots);
            var waiting = staged.After.View.Slots[20];
            Assert.True(waiting.IsLive);
            Assert.Equal(TacticalIcon.Empty, staged.After.View.Grid[(waiting.X * staged.After.Height) + waiting.Y]);
            var placed = Assert.Single(frames, f => f.Event is TacticalPlacedEvent);
            Assert.Equal(new[] { 20 }, placed.Before.PendingPlacementSlots);
            Assert.Empty(placed.After.PendingPlacementSlots);
            Assert.Contains(frames, f => f.Event is TacticalPlacedEvent);
            Assert.Contains(frames, f => f.Kind == TacticalFrameKind.HalfRound);
            Assert.Equal(0, opening.OpenBattleView!.SideToMove);
        }
        AssertCoherent(frames);
    }

    [Fact]
    public void Computer_movement_precedes_projectile_and_melee_at_the_actual_destination()
    {
        var context = TacticalContext.From(TacticalTestbed.ToyRuleset);
        var timeline = Attach(context);
        var state = TacticalTestbed.Arena(
            new[] { (0, TacticalTestbed.Unit(TacticalTestbed.AR, 2, 2, 1800, moves: 2)),
                    (20, TacticalTestbed.Unit(TacticalTestbed.HI, 5, 2, 2200)) }, attackerComputer: true);
        var draws = new SeededDraws(741);
        state = TacticalMovement.MoveUnit(state, 0, 5, 2, context, draws);
        var frames = timeline.Drain();
        var shot = Assert.Single(frames, f => f.Kind == TacticalFrameKind.Shot);
        var projectile = Assert.Single(frames, f => f.Kind == TacticalFrameKind.Projectile);
        var moved = frames.Last(f => f.Kind == TacticalFrameKind.Movement);
        Assert.True(moved.FrameId < projectile.FrameId && projectile.FrameId < shot.FrameId);
        Assert.Equal(3, moved.After.View.Slots[0].X);
        Assert.Equal(moved.After.View.Slots[0].X, shot.After.View.Slots[0].X);
        Assert.Equal(projectile.Before.View.Slots[0].Shots - 1, projectile.After.View.Slots[0].Shots);
        Assert.Equal(projectile.Before.View.Slots[0].Moves - 1, projectile.After.View.Slots[0].Moves);
        Assert.Equal(0, projectile.Actor!.Slot);
        Assert.Equal(20, projectile.Target!.Slot);
        AssertShot(shot);
        AssertCoherent(frames);

        var meleeState = TacticalTestbed.Arena(
            (0, TacticalTestbed.Unit(TacticalTestbed.HI, 2, 2, 2000, moves: 2)),
            (20, TacticalTestbed.Unit(TacticalTestbed.HI, 4, 2, 2000)));
        var meleeTimeline = Attach(context);
        meleeState = TacticalMovement.MoveUnit(meleeState, 0, 3, 2, context, draws);
        meleeState = TacticalOrders.SetTarget(meleeState, 0, 20, context).State;
        meleeState = TacticalHalfRound.End(meleeState, context, draws);
        var exchanges = meleeTimeline.Drain();
        var melee = Assert.Single(exchanges, f => f.Kind == TacticalFrameKind.Melee);
        Assert.Equal(3, melee.Before.View.Slots[0].X);
        Assert.True(exchanges.First(f => f.Kind == TacticalFrameKind.Movement).FrameId < melee.FrameId);
        AssertMelee(melee);
        AssertCoherent(exchanges);
        Assert.Equal(meleeState.Slots, exchanges[^1].After.View.Slots);
    }

    [Fact]
    public void Rout_captures_every_cascade_victim_and_morale_before_the_terminal_state()
    {
        var context = TacticalContext.From(TacticalTestbed.ToyRuleset);
        var timeline = Attach(context);
        var state = TacticalTestbed.Arena(
            (0, TacticalTestbed.Unit(TacticalTestbed.LI, 1, 1, 500, morale: 10)),
            (1, TacticalTestbed.Unit(TacticalTestbed.LI, 2, 1, 500, morale: 31)),
            (2, TacticalTestbed.Unit(TacticalTestbed.LI, 3, 1, 500, morale: 32)),
            (20, TacticalTestbed.Unit(TacticalTestbed.HI, 4, 1, 500, morale: 60, target: 0)));
        var ended = TacticalRout.Rout(state, 0, context, NoDraws.Instance);
        var frames = timeline.Drain();
        Assert.Equal(new[] { 0, 1, 2 }, frames.Where(f => f.Kind is TacticalFrameKind.Rout or TacticalFrameKind.Cascade)
            .Select(f => f.Actor!.Slot));
        foreach (var frame in frames.Where(f => f.Kind is TacticalFrameKind.Rout or TacticalFrameKind.Cascade))
        {
            var unit = frame.After.View.Slots[frame.Actor!.Slot];
            Assert.Equal(0, unit.Troops);
            Assert.Equal(TacticalIcon.Empty, frame.After.View.Grid[(unit.X * frame.After.Height) + unit.Y]);
        }
        Assert.True(ended.IsOver);
        Assert.Equal(state.Slots[20].Morale + context.Rules.RoutEnemyBonus, ended.Slots[20].Morale);
        Assert.Equal(TacticalSlot.NoTarget, ended.Slots[20].Target);
        Assert.Equal(TacticalFrameKind.Terminal, frames[^1].Kind);
        Assert.Equal(ended.Slots, frames[^1].After.View.Slots);
        AssertCoherent(frames);
    }

    [Fact]
    public void Surrender_batch_survives_close_and_later_commands_never_replay_it()
    {
        var session = TacticalSessionTestbed.HotseatSession(HumanBattleMode.Tactical);
        var opening = session.Submit("attack-army north-army-1 south-army-1");
        foreach (var line in new[] { "battle show", "battle place 0 4 1", "battle place 20 4 1", "help", "battle surrender" })
            Assert.Empty(session.Submit(line).TacticalTimeline);
        var ended = session.Submit("battle surrender yes");
        Assert.Null(ended.OpenBattleView);
        Assert.NotNull(ended.EndedBattleReport);
        Assert.Contains(ended.TacticalTimeline, f => f.Kind == TacticalFrameKind.Terminal);
        var removal = Assert.Single(ended.TacticalTimeline, f => f.Kind == TacticalFrameKind.Removal && f.Actor is not null);
        Assert.Equal(20, removal.Actor!.Slot);
        Assert.Equal(0, removal.After.View.Slots[20].Troops);
        Assert.Equal(TacticalFrameKind.Reconciliation, ended.TacticalTimeline[^1].Kind);
        Assert.True(ended.TacticalTimeline[^1].After.View.IsOver);
        Assert.Equal(ended.EndedBattleReport!.LoserFinishTotal, ended.TacticalTimeline[^1].After.View.Slots.Skip(20).Sum(s => s.Troops));
        Assert.Equal(opening.TacticalTimeline[^1].FrameId + 1, ended.TacticalTimeline[0].FrameId);
        Assert.Empty(session.Submit("battle show").TacticalTimeline);
        Assert.Empty(session.Submit("help").TacticalTimeline);
        AssertCoherent(ended.TacticalTimeline);
    }

    [Fact]
    public void Cap_withdrawal_retains_live_units_and_reconciles_without_inventing_removals()
    {
        var ruleset = TacticalSessionTestbed.Toy.Ruleset;
        ruleset = ruleset with { Combat = ruleset.Combat with { Tactical = ruleset.Combat.Tactical with { HalfRoundCap = 2 } } };
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, ruleset);
        var opening = session.Submit("attack-army north-army-1 south-army-1");
        var ended = session.Submit("battle end");
        Assert.Null(ended.OpenBattleView);
        Assert.True(ended.EndedBattleReport!.Capped);
        var cap = Assert.Single(ended.TacticalTimeline, f => f.Kind == TacticalFrameKind.CapWithdrawal);
        Assert.False(cap.Before.Capped);
        Assert.True(cap.After.Capped);
        Assert.Contains(cap.After.View.Slots.Take(20), s => s.IsLive);
        Assert.Equal(TacticalFrameKind.Reconciliation, ended.TacticalTimeline[^1].Kind);
        Assert.Equal(opening.TacticalTimeline[0].BattleId, cap.BattleId);
        AssertCoherent(ended.TacticalTimeline);
    }

    [Fact]
    public void Consecutive_general_half_rounds_have_each_transition_and_immutable_prior_snapshots()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.StrongAttacker());
        var opening = session.Submit("attack-army north-army-1 south-army-1");
        var savedOpening = GameJson.Serialize(opening.TacticalTimeline);
        var end = session.Submit("battle general on");
        Assert.Null(end.OpenBattleView);
        Assert.Contains(end.TacticalTimeline, f => f.Event is TacticalComputerGeneralEvent { On: true });
        Assert.True(end.TacticalTimeline.Count(f => f.Kind == TacticalFrameKind.HalfRound) > 2);
        Assert.Contains(end.TacticalTimeline, f => f.Kind == TacticalFrameKind.Setup && f.After.View.Placed);
        Assert.Contains(end.TacticalTimeline, f => f.Kind == TacticalFrameKind.Terminal);
        var formation = Assert.Single(end.TacticalTimeline, f => f.Event is null && f.Kind == TacticalFrameKind.Placement && f.After.PendingPlacementSlots.Count == 2);
        Assert.Equal(new[] { 0, 1 }, formation.After.PendingPlacementSlots);
        var placements = end.TacticalTimeline.Where(f => f.Event is TacticalPlacedEvent).ToArray();
        Assert.Equal(2, placements.Length);
        Assert.Equal(2, placements[0].Before.PendingPlacementSlots.Count);
        Assert.Single(placements[0].After.PendingPlacementSlots);
        Assert.Empty(placements[1].After.PendingPlacementSlots);
        Assert.Empty(end.TacticalTimeline[^1].After.PendingPlacementSlots);
        var final = end.TacticalTimeline[^1].After.View;
        var report = Assert.IsType<TacticalBattleReport>(end.EndedBattleReport);
        var slotsPerSide = final.Slots.Count / 2;
        Assert.Equal(report.WinnerFinishTotal, final.Slots.Skip(report.WinnerSide * slotsPerSide).Take(slotsPerSide).Sum(s => s.Troops));
        Assert.All(end.TacticalTimeline, f => Assert.Equal(1, f.CommandId));
        Assert.Equal(savedOpening, GameJson.Serialize(opening.TacticalTimeline));
        AssertCoherent(opening.TacticalTimeline.Concat(end.TacticalTimeline));
        Assert.DoesNotContain("tacticalTimeline", GameJson.Serialize(end), StringComparison.Ordinal);
        foreach (var frame in end.TacticalTimeline.Where(f => f.Kind == TacticalFrameKind.Shot)) AssertShot(frame);
        foreach (var frame in end.TacticalTimeline.Where(f => f.Kind == TacticalFrameKind.Melee)) AssertMelee(frame);
    }

    [Fact]
    public void Same_seeded_copy_in_and_commands_with_collector_attached_or_absent_have_identical_results_and_draws()
    {
        var game = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical).State;
        var ruleset = TacticalSessionTestbed.Toy.Ruleset;
        var capturedContext = TacticalContext.From(ruleset);
        var absentContext = TacticalContext.From(ruleset);
        var timeline = Attach(capturedContext);
        var capturedDraws = new SeededDraws(721);
        var absentDraws = new SeededDraws(721);
        var captured = TacticalCopyIn.Start(game, "north-army-1", "south-army-1", capturedContext, capturedDraws);
        var absent = TacticalCopyIn.Start(game, "north-army-1", "south-army-1", absentContext, absentDraws);
        Assert.Equal(absent.Game, captured.Game);
        Assert.Equal(absent.Battle, captured.Battle);
        var a = TacticalDriver.Run(captured.Battle, capturedContext, capturedDraws, new ComputerGeneral());
        var b = TacticalDriver.Run(absent.Battle, absentContext, absentDraws, new ComputerGeneral());
        a = TacticalOrders.EndHalfRound(a, capturedContext, capturedDraws, new ComputerGeneral()).State;
        b = TacticalOrders.EndHalfRound(b, absentContext, absentDraws, new ComputerGeneral()).State;
        a = TacticalOrders.ComputerGeneralOn(a, capturedContext, capturedDraws, new ComputerGeneral()).State;
        b = TacticalOrders.ComputerGeneralOn(b, absentContext, absentDraws, new ComputerGeneral()).State;
        Assert.True(a.IsOver);
        Assert.Equal(b, a);
        Assert.Equal(b.Log, a.Log);
        Assert.Equal(absentDraws.Calls, capturedDraws.Calls);
        Assert.Equal(absentDraws.State, capturedDraws.State);
        Assert.Equal(GameJson.Serialize(b), GameJson.Serialize(a));
        var batch = timeline.Drain();
        Assert.NotEmpty(batch);
        Assert.Empty(timeline.Drain());
        Assert.Equal(a.Log.Where(e => e is not TacticalDrawEvent), batch.Where(f => f.Event is not null).Select(f => f.Event));
        Assert.Equal(a.Slots, batch[^1].After.View.Slots);
        AssertCoherent(batch);
    }

    [Fact]
    public void Same_session_commands_with_observer_attached_or_absent_preserve_CLI_saves_log_and_writeback()
    {
        var captured = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.StrongAttacker());
        var absent = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.StrongAttacker());
        foreach (var line in TacticalSessionTestbed.WonBattleScript)
        {
            var a = captured.Submit(line);
            var b = absent.Submit(line);
            Assert.Equal(b.Lines, a.Lines);
            Assert.Equal(absent.State, captured.State);
            Assert.Equal(b.EndedBattleReport, a.EndedBattleReport);
            if (Battle(captured) is { } capturedBattle && Battle(absent) is { } absentBattle)
            {
                Assert.Equal(absentBattle.Battle.Log, capturedBattle.Battle.Log);
                Assert.Equal(absentBattle.ToSaved(), capturedBattle.ToSaved());
                Assert.Equal(GameJson.Serialize(absentBattle.ToSaved()), GameJson.Serialize(capturedBattle.ToSaved()));
                // Test-only observer detachment: no gameplay option or alternative resolver invocation.
                Context(absentBattle).Observer = null;
            }
        }
        Assert.Null(captured.OpenTacticalBattle);
    }

    [Fact]
    public void Setup_and_controller_off_expose_the_actual_moves_targets_and_claims_transition()
    {
        var context = TacticalContext.From(TacticalTestbed.ToyRuleset);
        var timeline = Attach(context);
        var state = TacticalTestbed.Arena(
            new[] { (0, TacticalTestbed.Unit(TacticalTestbed.AR, 2, 2, 1000, moves: 0, shots: 9, target: 20)),
                    (20, TacticalTestbed.Unit(TacticalTestbed.HI, 8, 8, 1000)) }, attackerGeneral: true);
        state = state with { GeneralClaims = ValueList.From(Enumerable.Repeat(7, state.Slots.Count)) };
        state = TacticalOrders.ComputerGeneralOff(state, 0, context).State;
        state = TacticalHalfRound.Setup(state, context);
        var frames = timeline.Drain();
        var controller = Assert.Single(frames, f => f.Kind == TacticalFrameKind.Controller);
        Assert.True(controller.Before.View.ComputerGeneral[0]);
        Assert.False(controller.After.View.ComputerGeneral[0]);
        var setup = Assert.Single(frames, f => f.Kind == TacticalFrameKind.Setup);
        Assert.Equal(0, setup.Before.View.Slots[0].Moves);
        Assert.Equal(context.MovesOf(TacticalTestbed.AR), setup.After.View.Slots[0].Moves);
        Assert.Equal(20, setup.Before.View.Slots[0].Target);
        Assert.Equal(TacticalSlot.NoTarget, setup.After.View.Slots[0].Target);
        Assert.Equal(9, setup.After.View.Slots[0].Shots);
        Assert.Equal(setup.Before.View.Counter + 1, setup.After.View.Counter);
        Assert.All(setup.Before.GeneralClaims, claim => Assert.Equal(7, claim));
        Assert.All(setup.After.GeneralClaims, claim => Assert.Equal(0, claim));
        AssertCoherent(frames);
    }

    [Fact]
    public void Loading_battle_or_plain_saves_replaces_history_and_assigns_a_new_battle_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ic2-timeline", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var plain = Path.Combine(directory, "plain.json");
            var midBattle = Path.Combine(directory, "battle.json");
            var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
            Assert.Contains(session.Submit($"save {plain}").Lines, l => l.StartsWith("Saved to", StringComparison.Ordinal));
            var first = session.Submit("attack-army north-army-1 south-army-1");
            Assert.Empty(session.Submit($"save {midBattle}").TacticalTimeline);
            session.Submit("battle surrender yes");
            var reopened = session.Submit($"load {midBattle}");
            Assert.Contains(reopened.Lines, l => l.StartsWith("Loaded", StringComparison.Ordinal));
            Assert.NotNull(reopened.OpenBattleView);
            Assert.Equal(TacticalFrameKind.Initial, reopened.TacticalTimeline[0].Kind);
            Assert.Equal(1, reopened.TacticalTimeline[0].FrameId);
            Assert.NotEqual(first.TacticalTimeline[0].BattleId, reopened.TacticalTimeline[0].BattleId);
            Assert.Equal(reopened.OpenBattleView, reopened.TacticalTimeline[^1].After.View);
            Assert.Empty(session.Submit("battle show").TacticalTimeline);
            session.Submit("battle surrender yes");
            var loadedPlain = session.Submit($"load {plain}");
            Assert.Contains(loadedPlain.Lines, l => l.StartsWith("Loaded", StringComparison.Ordinal));
            Assert.Null(loadedPlain.OpenBattleView);
            Assert.Empty(loadedPlain.TacticalTimeline);
            var next = session.Submit("attack-army north-army-1 south-army-1");
            Assert.NotEqual(first.TacticalTimeline[0].BattleId, next.TacticalTimeline[0].BattleId);
            Assert.Equal(1, next.TacticalTimeline[0].FrameId);
            AssertCoherent(reopened.TacticalTimeline);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Direct_session_open_order_and_terminal_results_transfer_executable_batches_once()
    {
        var game = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical).State;
        var battle = TacticalBattleSession.Open(game, "north-army-1", "south-army-1", TacticalSessionTestbed.Toy.Ruleset, out _);
        var opening = battle.DrainTimeline();
        Assert.NotEmpty(opening);
        Assert.Empty(battle.DrainTimeline());
        var order = battle.Place(0, 4, 1);
        Assert.True(order.Accepted);
        Assert.Single(order.Timeline, f => f.Kind == TacticalFrameKind.Placement);
        Assert.All(order.Timeline, f => Assert.Equal(1, f.CommandId));
        Assert.Empty(battle.DrainTimeline());
        Assert.Empty(battle.Place(20, 4, 1).Timeline);
        var terminal = battle.Surrender();
        Assert.True(terminal.Accepted);
        Assert.Contains(terminal.Timeline, f => f.Kind == TacticalFrameKind.Terminal);
        Assert.Equal(new[] { 0, 1 }, terminal.Timeline.Where(f => f.Kind == TacticalFrameKind.Removal && f.Actor is not null).Select(f => f.Actor!.Slot));
        Assert.All(terminal.Timeline, f => Assert.Equal(3, f.CommandId));
        Assert.Empty(battle.DrainTimeline());
        Assert.Equal(battle.View(), terminal.Timeline[^1].After.View);
        AssertCoherent(opening.Concat(order.Timeline).Concat(terminal.Timeline));
    }

    [Fact]
    public void AI_phase_opening_and_terminal_commands_deliver_their_own_complete_batches()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.WeakAttacker());
        session.Submit("declare-war south");
        var opening = session.Submit("end");
        Assert.NotNull(opening.OpenBattleView);
        Assert.Equal("north", opening.OpenBattleView!.DefenderNationId);
        Assert.Equal(TacticalFrameKind.Initial, opening.TacticalTimeline[0].Kind);
        Assert.Contains(opening.TacticalTimeline, f => f.Event is TacticalCopiedInEvent);
        Assert.Contains(opening.TacticalTimeline, f => f.Kind == TacticalFrameKind.Setup);
        Assert.All(opening.TacticalTimeline, f => Assert.Equal(0, f.CommandId));
        Assert.Equal(opening.OpenBattleView, opening.TacticalTimeline[^1].After.View);
        Assert.Empty(session.Submit("battle show").TacticalTimeline);
        var ended = session.Submit("battle general on");
        Assert.Null(ended.OpenBattleView);
        Assert.NotNull(ended.EndedBattleReport);
        Assert.Contains(ended.TacticalTimeline, f => f.Kind == TacticalFrameKind.Terminal);
        Assert.Equal(opening.TacticalTimeline[0].BattleId, ended.TacticalTimeline[^1].BattleId);
        Assert.Equal(TacticalFrameKind.Reconciliation, ended.TacticalTimeline[^1].Kind);
        AssertCoherent(opening.TacticalTimeline.Concat(ended.TacticalTimeline));
        Assert.Empty(session.Submit("help").TacticalTimeline);
    }

    [Fact]
    public void Handoff_lines_only_human_adapter_keeps_the_opening_batch_and_clears_on_every_later_submission()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        Assert.Empty(LastTimeline(session));
        Assert.Empty(InitialTimeline(session));
        // The real map adapter's transport: retain rendered lines, discard SessionOutput.
        var lines = SubmitLines(session, "attack-army north-army-1 south-army-1");
        Assert.Contains(lines, l => l.Contains("the battle opens on the board", StringComparison.Ordinal));
        Assert.NotNull(session.OpenTacticalBattle);
        var opening = LastTimeline(session);
        Assert.NotEmpty(opening);
        Assert.Equal(TacticalFrameKind.Initial, opening[0].Kind);
        Assert.Equal(session.OpenTacticalBattle, opening[^1].After.View);
        Assert.Empty(InitialTimeline(session));
        var archived = GameJson.Serialize(opening);
        foreach (var command in new[] { "battle show", "battle place 20 4 1", "help", "", "  " })
        {
            var output = session.Submit(command);
            Assert.Empty(output.TacticalTimeline);
            Assert.Empty(LastTimeline(session));
            Assert.Empty(InitialTimeline(session));
        }
        session.Submit("battle surrender yes");
        Assert.Equal(archived, GameJson.Serialize(opening));
    }

    [Fact]
    public void Handoff_lines_only_AI_adapter_keeps_opening_and_terminal_batches()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: TacticalSessionTestbed.WeakAttacker());
        SubmitLines(session, "declare-war south");
        SubmitLines(session, "end");
        var opening = LastTimeline(session);
        Assert.NotEmpty(opening);
        Assert.Equal("north", session.OpenTacticalBattle!.DefenderNationId);
        Assert.Equal(session.OpenTacticalBattle, opening[^1].After.View);
        Assert.Empty(InitialTimeline(session));
        SubmitLines(session, "battle general on");
        var terminal = LastTimeline(session);
        Assert.Null(session.OpenTacticalBattle);
        Assert.Contains(terminal, f => f.Kind == TacticalFrameKind.Terminal);
        Assert.Equal(opening[0].BattleId, terminal[^1].BattleId);
        Assert.Empty(InitialTimeline(session));
        Assert.Empty(session.Submit("help").TacticalTimeline);
        Assert.Empty(LastTimeline(session));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Handoff_reopened_constructor_exposes_opening_before_any_command_without_draining_it(bool automatic)
    {
        var source = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        if (automatic)
        {
            source.Submit("declare-war south");
            source.Submit("end");
        }
        else source.Submit("attack-army north-army-1 south-army-1");
        var saved = SaveOf(source);
        if (automatic) saved = saved with { Battle = saved.Battle! with { SideToMove = 0 } };
        var session = new GameSession(source.World, source.Ruleset, source.Scenario, saved);
        Assert.NotNull(session.OpenTacticalBattle);
        Assert.Empty(LastTimeline(session));
        var battleBeforeRead = Battle(session)!.ToSaved();
        var initial = InitialTimeline(session);
        Assert.NotEmpty(initial);
        Assert.Equal(TacticalFrameKind.Initial, initial[0].Kind);
        Assert.All(initial, f => Assert.Equal(0, f.CommandId));
        Assert.Equal(session.OpenTacticalBattle, initial[^1].After.View);
        Assert.Equal(initial, InitialTimeline(session));
        Assert.Equal(battleBeforeRead, Battle(session)!.ToSaved());
        if (automatic)
        {
            Assert.Contains(initial, f => f.Kind == TacticalFrameKind.Movement);
            Assert.Contains(initial, f => f.Event is TacticalPlacedEvent);
        }
        var archived = GameJson.Serialize(initial);
        var first = session.Submit("battle show");
        Assert.Equal(initial, first.TacticalTimeline);
        Assert.Same(first.TacticalTimeline, LastTimeline(session));
        Assert.Empty(InitialTimeline(session));
        Assert.Empty(session.Submit("battle show").TacticalTimeline);
        Assert.Empty(LastTimeline(session));
        session.Submit("battle surrender yes");
        Assert.Equal(archived, GameJson.Serialize(initial));
    }

    [Fact]
    public void Handoff_peeking_before_first_mutating_order_preserves_the_existing_output_batch_and_save()
    {
        var source = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        source.Submit("attack-army north-army-1 south-army-1");
        var saved = SaveOf(source);
        var peeked = new GameSession(source.World, source.Ruleset, source.Scenario, saved);
        var untouched = new GameSession(source.World, source.Ruleset, source.Scenario, saved);
        var initial = InitialTimeline(peeked);
        Assert.NotEmpty(initial);
        Assert.Equal(initial, InitialTimeline(peeked));
        var actual = peeked.Submit("battle place 0 4 1");
        var expected = untouched.Submit("battle place 0 4 1");
        Assert.Equal(expected.Lines, actual.Lines);
        Assert.Equal(expected.OpenBattleView, actual.OpenBattleView);
        Assert.Equal(expected.EndedBattleReport, actual.EndedBattleReport);
        Assert.Equal(expected.TacticalTimeline, actual.TacticalTimeline);
        Assert.Equal(initial, ValueList.From(actual.TacticalTimeline.Take(initial.Count)));
        Assert.Same(actual.TacticalTimeline, LastTimeline(peeked));
        Assert.Equal(Battle(untouched)!.ToSaved(), Battle(peeked)!.ToSaved());
        Assert.Empty(InitialTimeline(peeked));
        Assert.Empty(peeked.Submit("battle show").TacticalTimeline);
        Assert.Empty(LastTimeline(peeked));
    }

    [Fact]
    public void Handoff_loading_and_second_battles_replace_stale_batches_without_retaining_full_history()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ic2-handoff", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
            var plain = Path.Combine(directory, "plain.json");
            var middle = Path.Combine(directory, "middle.json");
            session.Submit($"save {plain}");
            var first = session.Submit("attack-army north-army-1 south-army-1");
            Assert.Same(first.TacticalTimeline, LastTimeline(session));
            session.Submit($"save {middle}");
            Assert.Empty(LastTimeline(session));
            var closed = session.Submit("battle surrender yes");
            Assert.Null(closed.OpenBattleView);
            Assert.Same(closed.TacticalTimeline, LastTimeline(session));
            var restored = session.Submit($"load {middle}");
            Assert.NotEmpty(restored.TacticalTimeline);
            Assert.Same(restored.TacticalTimeline, LastTimeline(session));
            Assert.Empty(InitialTimeline(session));
            Assert.NotEqual(first.TacticalTimeline[0].BattleId, restored.TacticalTimeline[0].BattleId);
            Assert.All(LastTimeline(session), f => Assert.Equal(restored.TacticalTimeline[0].BattleId, f.BattleId));
            session.Submit("battle surrender yes");
            var loaded = session.Submit($"load {plain}");
            Assert.Empty(loaded.TacticalTimeline);
            Assert.Empty(LastTimeline(session));
            Assert.Empty(InitialTimeline(session));
            var next = session.Submit("attack-army north-army-1 south-army-1");
            Assert.Same(next.TacticalTimeline, LastTimeline(session));
            Assert.NotEqual(restored.TacticalTimeline[0].BattleId, next.TacticalTimeline[0].BattleId);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static IReadOnlyList<string> SubmitLines(GameSession session, string command) => session.Submit(command).Lines;

    // Baseline probe: an older producer has no route for these batches, so the adapter observes no frames.
    private static ValueList<TacticalBattleFrame> LastTimeline(GameSession session) =>
        (ValueList<TacticalBattleFrame>?)typeof(GameSession).GetProperty("LastTacticalTimeline")?.GetValue(session) ?? ValueList<TacticalBattleFrame>.Empty;

    private static ValueList<TacticalBattleFrame> InitialTimeline(GameSession session) =>
        (ValueList<TacticalBattleFrame>?)typeof(GameSession).GetProperty("InitialTacticalTimeline")?.GetValue(session) ?? ValueList<TacticalBattleFrame>.Empty;

    private static SaveGame SaveOf(GameSession session) => new(
        GameDataSchema.CurrentVersion, "handoff", "Handoff", session.Scenario.Id, session.World.Id,
        session.Ruleset.Id, session.State, Battle: Battle(session)!.ToSaved());

    private static TacticalBattleSession? Battle(GameSession session) =>
        (TacticalBattleSession?)typeof(GameSession).GetField("_battle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);

    private static TacticalContext Context(TacticalBattleSession session) =>
        (TacticalContext)typeof(TacticalBattleSession).GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;

    private static TacticalBattleTimeline Attach(TacticalContext context)
    {
        var timeline = new TacticalBattleTimeline(context, "test-battle", "north", "south");
        context.Observer = timeline.Observe;
        return timeline;
    }

    private static void AssertShot(TacticalBattleFrame frame)
    {
        var shot = Assert.IsType<TacticalShotEvent>(frame.Event);
        Assert.Equal(shot.Shooter, frame.Actor!.Slot);
        Assert.Equal(shot.Target, frame.Target!.Slot);
        Assert.Equal(shot.Loss, frame.Before.View.Slots[shot.Target].Troops - frame.After.View.Slots[shot.Target].Troops);
        Assert.Equal(shot.TargetTroops, frame.After.View.Slots[shot.Target].Troops);
        Assert.Equal(shot.TargetMorale, frame.After.View.Slots[shot.Target].Morale);
    }

    private static void AssertMelee(TacticalBattleFrame frame)
    {
        var melee = Assert.IsType<TacticalMeleeEvent>(frame.Event);
        Assert.Equal(melee.Attacker, frame.Actor!.Slot);
        Assert.Equal(melee.Defender, frame.Target!.Slot);
        Assert.Equal(melee.AttackerLoss, frame.Before.View.Slots[melee.Attacker].Troops - frame.After.View.Slots[melee.Attacker].Troops);
        Assert.Equal(melee.DefenderLoss, frame.Before.View.Slots[melee.Defender].Troops - frame.After.View.Slots[melee.Defender].Troops);
        Assert.Equal(melee.AttackerTroops, frame.After.View.Slots[melee.Attacker].Troops);
        Assert.Equal(melee.DefenderTroops, frame.After.View.Slots[melee.Defender].Troops);
        Assert.Equal(melee.AttackerMorale, frame.After.View.Slots[melee.Attacker].Morale);
        Assert.Equal(melee.DefenderMorale, frame.After.View.Slots[melee.Defender].Morale);
    }

    private static void AssertCoherent(IEnumerable<TacticalBattleFrame> source)
    {
        var frames = source.ToArray();
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            if (i > 0)
            {
                Assert.Equal(frames[i - 1].FrameId + 1, frame.FrameId);
                Assert.Equal(frames[i - 1].After, frame.Before);
                Assert.True(frame.CommandId >= frames[i - 1].CommandId);
            }
            foreach (var snapshot in new[] { frame.Before, frame.After })
            {
                Assert.Equal(snapshot.Width * snapshot.Height, snapshot.View.Grid.Count);
                for (var slot = 0; slot < snapshot.View.Slots.Count; slot++)
                {
                    var unit = snapshot.View.Slots[slot];
                    if (!unit.IsLive || snapshot.PendingPlacementSlots.Contains(slot)) continue;
                    Assert.InRange(unit.X, 0, snapshot.Width - 1);
                    Assert.InRange(unit.Y, 0, snapshot.Height - 1);
                    Assert.Equal(TacticalTestbed.IconOf(slot, unit), snapshot.View.Grid[(unit.X * snapshot.Height) + unit.Y]);
                }
            }
        }
    }

    private sealed class SeededDraws(ulong seed) : IBattleDraws
    {
        private readonly SplitMix64Rng _rng = new(seed);
        public List<(int Bound, int Value)> Calls { get; } = new();
        public ulong State => _rng.State;
        public int Random(int n)
        {
            var value = n == 0 ? ConsumeZero() : _rng.NextInt(n);
            Calls.Add((n, value));
            return value;
        }

        private int ConsumeZero() { _rng.NextUInt64(); return 0; }
    }
}
