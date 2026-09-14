using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Movement.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy world", Done-when 3: "<c>MoveArmy</c> is
/// rejected with a typed rejection for an unknown army, another nation's army, or no moves left. A legal
/// move spends exactly the walker's cost and ends where the walker stops." Dispatched through the real
/// <see cref="CommandDispatcher"/> over the real engine assembly — this is the command layer, not
/// <see cref="MovementWalker"/> itself, which <c>tests/IC2.Engine.Tests/Movement/MovementWalkerTests.cs</c>
/// already covers in full.
/// </summary>
public sealed class MoveArmyCommandHandlerTests
{
    private static CommandDispatcher Dispatcher(IEventSink? sink = null) => new(
        SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink ?? NullEventSink.Instance);

    private static GameState WithArmy(GameState state, ArmyState updated) =>
        state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, updated.Id, StringComparison.Ordinal) ? updated : a)),
        };

    [Fact]
    public void Unknown_army_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var before = CoreTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new MoveArmyCommand(before.ActiveNationId, "no-such-army", 0, 0));

        Assert.Equal(MoveArmyRejections.UnknownArmy, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void Another_nations_army_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var before = CoreTestbed.InitialState();

        // The toy scenario's active seat is "north"; "south-army-1" belongs to "south".
        Assert.Equal("north", before.ActiveNationId);
        var target = before.ArmyById("south-army-1")!;

        var result = dispatcher.Dispatch(
            before, new MoveArmyCommand(before.ActiveNationId, target.Id, target.X, target.Y));

        Assert.Equal(MoveArmyRejections.NotYourArmy, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_army_with_no_moves_left_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var initial = CoreTestbed.InitialState();
        var army = initial.ArmyById("north-army-1")!;
        var before = WithArmy(initial, army with { Moves = 0 });

        var result = dispatcher.Dispatch(before, new MoveArmyCommand(before.ActiveNationId, army.Id, 4, 2));

        Assert.Equal(MoveArmyRejections.NoMovesLeft, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void A_legal_move_spends_exactly_the_walkers_cost_and_ends_where_it_stops()
    {
        var sink = new RecordingEventSink();
        var dispatcher = Dispatcher(sink);
        var before = CoreTestbed.InitialState();
        var army = before.ArmyById("north-army-1")!;
        Assert.Equal((3, 2), (army.X, army.Y));

        // Row y=2 from (3,2) to (4,2) is one Plain step, cost 1 -- the same tile MovementWalkerTests uses.
        var result = dispatcher.Dispatch(before, new MoveArmyCommand(before.ActiveNationId, army.Id, 4, 2));

        Assert.True(result.IsAccepted);
        var moved = Assert.IsType<ArmyMoved>(Assert.Single(sink.Events));
        Assert.Equal(1, moved.MovesSpent);
        Assert.Equal((3, 2), (moved.FromX, moved.FromY));
        Assert.Equal((4, 2), (moved.ToX, moved.ToY));

        var updated = result.State.ArmyById(army.Id)!;
        Assert.Equal(4, updated.X);
        Assert.Equal(2, updated.Y);
        Assert.Equal(army.Moves - moved.MovesSpent, updated.Moves);
    }

    [Fact]
    public void A_move_blocked_by_a_city_marker_stops_before_it_and_charges_nothing_for_it()
    {
        // Row y=2, (3,2) toward (6,2), straight through Portus at (5,2) -- MovementWalkerTests documents
        // the same path: only (4,2) (Plain, cost 1) is entered before the block.
        var dispatcher = Dispatcher();
        var before = CoreTestbed.InitialState();
        var army = before.ArmyById("north-army-1")!;
        var portus = before.CityById("portus")!;
        Assert.Equal((5, 2), (portus.X, portus.Y));

        var result = dispatcher.Dispatch(before, new MoveArmyCommand(before.ActiveNationId, army.Id, 6, 2));

        Assert.True(result.IsAccepted);
        var updated = result.State.ArmyById(army.Id)!;
        Assert.Equal(4, updated.X);
        Assert.Equal(2, updated.Y);
        Assert.Equal(army.Moves - 1, updated.Moves);
    }
}
