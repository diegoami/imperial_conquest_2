using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Movement;
using IC2.Slice.UI;
using Xunit;
using TravelKind = IC2.Engine.Movement.StrategicMarkerKind;

namespace IC2.Engine.Tests.Ui;

public sealed class StrategicTravelPlaybackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_partial_route_visits_exact_paid_cells_and_suppresses_destination(bool fleet)
    {
        var session = StrategicTravelObservationTests.Session(fleet, moves: 2);
        var output = session.Submit(fleet ? "move-fleet f 7 3" : "move a 7 3");
        var playback = new StrategicTravelPlayback { CellSeconds = 1 };
        var cells = new List<GridPoint>();
        playback.CellEntered += (_, p) => cells.Add(p);
        var completed = 0;
        Assert.True(playback.Begin(output.StrategicTimeline, "north", () => completed++));
        Assert.False(playback.CommandsAllowed);
        Assert.Equal(0, fleet ? playback.DisplayState!.Fleets[0].X : playback.DisplayState!.Armies[0].X);
        Assert.Equal(2, fleet ? session.State.Fleets[0].X : session.State.Armies[0].X);
        playback.Advance(.5);
        Assert.Equal((.5, 0d), playback.Position(fleet ? TravelKind.Fleet : TravelKind.Army, fleet ? "f" : "a"));
        Assert.Empty(cells);
        playback.Advance(.5);
        Assert.Equal(new[] { new GridPoint(1, 0) }, cells);
        Assert.Equal(1, fleet ? playback.DisplayState!.Fleets[0].X : playback.DisplayState!.Armies[0].X);
        playback.Advance(1);
        Assert.Equal(new[] { new GridPoint(1, 0), new GridPoint(2, 1) }, cells);
        Assert.Null(playback.DisplayState);
        Assert.True(playback.CommandsAllowed);
        playback.Skip(); playback.Advance(100);
        Assert.Equal(1, completed);
        Assert.False(playback.Begin(output.StrategicTimeline, "north", () => completed++));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Partition_fast_pause_skip_zero_and_offscreen_have_identical_traversal(int mode)
    {
        var session = StrategicTravelObservationTests.Session();
        var output = session.Submit("move a 5 2");
        var route = output.StrategicTimeline.Steps.SelectMany(s => s.Routes).Single();
        var playback = new StrategicTravelPlayback { CellSeconds = mode == 0 ? 0 : 1 };
        var cells = new List<GridPoint>();
        var completions = 0;
        playback.CellEntered += (_, p) => cells.Add(p);
        playback.Begin(output.StrategicTimeline, "north", () => completions++);
        playback.Paused = true; playback.Advance(1000);
        Assert.Empty(cells);
        playback.Paused = false;
        if (mode == 1) playback.Fast = true;
        if (mode == 2) playback.IntersectsViewport = _ => false;
        if (mode == 3) playback.Skip();
        else for (var i = 0; i < 1000 && playback.IsActive; i++) playback.Advance(.01);
        Assert.Equal(route.EnteredCells, cells);
        Assert.Equal(1, completions);
    }

    [Fact]
    public void Sequential_actions_preserve_order_boundaries_and_release_snapshots_on_close()
    {
        var session = StrategicTravelObservationTests.Session();
        var a = session.Submit("move a 2 0");
        var b = session.Submit("move a 4 1");
        var batch = new StrategicTravelBatch(b.StrategicTimeline.BatchId,
            ValueList.From(a.StrategicTimeline.Steps.Concat(b.StrategicTimeline.Steps)));
        var playback = new StrategicTravelPlayback { CellSeconds = 1 };
        var seen = new List<GridPoint>();
        var boundaries = new List<long>();
        playback.CellEntered += (_, p) => seen.Add(p);
        playback.BoundaryCompleted += s => boundaries.Add(s.StepId);
        var complete = 0;
        playback.Begin(batch, "north", () => complete++);
        playback.Advance(1000);
        Assert.Equal(batch.Steps.SelectMany(s => s.Routes).SelectMany(r => r.EnteredCells), seen);
        Assert.Equal(batch.Steps.Select(s => s.StepId), boundaries);
        Assert.Equal(1, complete);
        playback.Close(); playback.Skip();
        Assert.Null(playback.DisplayState); Assert.Null(playback.CurrentStep); Assert.Null(playback.CurrentRoute);
        Assert.False(playback.CommandsAllowed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removed_or_captured_entity_keeps_prior_identity_until_arrival(bool capture)
    {
        var session = StrategicTravelObservationTests.Session();
        var output = session.Submit("move a 3 0");
        var step = output.StrategicTimeline.Steps.Single(s => s.Routes.Count > 0);
        var after = step.After with { Armies = capture
            ? ValueList.Of(step.After.Armies[0] with { Nation = "south" }) : ValueList<ArmyState>.Empty };
        var batch = new StrategicTravelBatch(1, ValueList.Of(step with { After = after }));
        var playback = new StrategicTravelPlayback { CellSeconds = 1 };
        GameState? boundary = null;
        playback.BoundaryCompleted += _ => boundary = playback.DisplayState;
        playback.Begin(batch, "north", () => { });
        playback.Advance(1);
        Assert.Equal("north", playback.DisplayState!.Armies[0].Nation);
        Assert.Equal(1, playback.DisplayState.Armies[0].X);
        playback.Skip();
        Assert.Equal(after, boundary);
    }

    [Fact]
    public void Foreign_simulation_seat_does_not_become_information_viewer()
    {
        var session = StrategicTravelObservationTests.Session();
        var output = session.Submit("move a 3 0");
        var step = output.StrategicTimeline.Steps.Single(s => s.Routes.Count > 0);
        var south = step.Before.TurnOrder.ToList().IndexOf("south");
        step = step with { Before = step.Before with { ActiveSeatIndex = south }, After = step.After with { ActiveSeatIndex = south } };
        var playback = new StrategicTravelPlayback();
        playback.Begin(new(1, ValueList.Of(step)), "north", () => { });
        Assert.Equal("north", playback.DisplayState!.ActiveNationId);
        Assert.Equal("south", playback.CurrentStep!.Before.ActiveNationId);
        playback.Close();
    }

    [Fact]
    public void Fleet_cargo_stays_hidden_and_shadows_last_entered_cell()
    {
        var session = StrategicTravelObservationTests.Session(true, change: s => s with
        {
            Fleets = ValueList.Of(s.Fleets[0] with { CarriedArmyId = "cargo" }),
            Armies = ValueList.Of(new ArmyState("cargo", "north", 0, 0, 8, 80, 20, 20, null, "f", ValueList<UnitSlot>.Empty)),
        });
        var output = session.Submit("move-fleet f 3 0");
        var playback = new StrategicTravelPlayback { CellSeconds = 1 };
        playback.Begin(output.StrategicTimeline, "north", () => { });
        playback.Advance(1);
        var cargo = playback.DisplayState!.ArmyById("cargo")!;
        Assert.Equal("f", cargo.AboardFleetId); Assert.Equal(1, cargo.X);
        Assert.Equal(cargo.X, playback.DisplayState.Fleets[0].X);
    }

    [Fact]
    public void Empty_noop_closure_and_reentrant_completion_are_once_only()
    {
        var session = StrategicTravelObservationTests.Session();
        var noop = session.Submit("move a 0 0");
        var playback = new StrategicTravelPlayback();
        var count = 0;
        playback.Begin(noop.StrategicTimeline, "north", () => { count++; playback.Skip(); });
        Assert.Equal(1, count);
        var next = session.Submit("move a 3 0");
        playback.Begin(next.StrategicTimeline, "north", () => count++);
        playback.Close(); playback.Skip(); playback.Advance(1000);
        Assert.Equal(1, count);
        Assert.False(playback.Begin(new(999, ValueList<StrategicTravelStep>.Empty), "north", () => count++));
    }

    [Fact]
    public void Presentation_enabled_disabled_leave_identical_authoritative_state_events_costs_seed_and_saves()
    {
        var enabled = StrategicTravelObservationTests.Session();
        var disabled = StrategicTravelObservationTests.Session();
        var eventsA = new List<DomainEvent>(); var eventsB = new List<DomainEvent>();
        enabled.EventsPublished += eventsA.AddRange; disabled.EventsPublished += eventsB.AddRange;
        var playback = new StrategicTravelPlayback();
        foreach (var line in new[] { "move a 2 0", "move a 4 1", "move a 4 1", "end" })
        {
            var a = enabled.Submit(line); var b = disabled.Submit(line);
            playback.Begin(a.StrategicTimeline, "north", () => { }); playback.Skip();
            Assert.Equal(a.Lines, b.Lines); Assert.Equal(a.Battles, b.Battles);
            Assert.Equal(eventsA, eventsB); Assert.Equal(enabled.State, disabled.State);
            Assert.Equal(GameJson.Serialize(enabled.State), GameJson.Serialize(disabled.State));
        }
    }
}
