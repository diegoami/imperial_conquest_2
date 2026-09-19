using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/task-catalogue.md</c> T23, Done-when 4, folded follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/226">#226</see>:
/// <c>MoveArmyCommandHandler</c> never checked <see cref="TileType.PassableByArmies"/>, so an army could
/// be legally walked into the sea. <c>MoveFleetCommandHandler:93</c> checks its mirror
/// (<see cref="TileType.PassableByFleets"/>), so this was an asymmetry rather than a design.
/// </summary>
/// <remarks>
/// <strong>Why this test lives in <c>Presentation</c> rather than <c>Movement</c>.</strong> Same reasoning
/// as <c>Battle/Commands/CityTileRuleAndDisbandReachabilityTests</c>: T23's Owns list grants
/// <c>src/IC2.Engine/Movement/**</c> for this one terrain check only, and does not widen
/// <c>tests/IC2.Engine.Tests/Movement/**</c>. <c>tests/IC2.Engine.Tests/Presentation/**</c> is granted in
/// full, so the regression this task's own fix needs is pinned there instead of reaching outside Owns.
/// </remarks>
public sealed class MoveArmySeaPassabilityTests
{
    private static CommandDispatcher Dispatcher() => new(
        SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);

    /// <summary>
    /// The toy world's row <c>y = 5</c> is sea across its whole width (<c>~ ~ ~ ~ ~ ~ ~ ~</c>, confirmed
    /// by the golden transcript's own <c>map</c> rendering). North's army starts at <c>(3, 2)</c>, four
    /// tiles from open water it could otherwise walk straight into with its five starting moves.
    /// </summary>
    [Fact]
    public void An_army_ordered_into_open_sea_stops_at_the_shore_instead_of_entering_it()
    {
        var dispatcher = Dispatcher();
        var state = CoreTestbed.InitialState();
        var army = state.ArmyById("north-army-1")!;
        var world = CoreTestbed.Toy.World;

        // (3, 2) toward (3, 5): straight down column 3, crossing into row 5's sea.
        var result = dispatcher.Dispatch(state, new MoveArmyCommand(state.ActiveNationId, army.Id, 3, 5));

        Assert.True(result.IsAccepted, result.ToString());
        var updated = result.State.ArmyById(army.Id)!;

        var terrain = world.Terrain.Decode(world.Width, world.Height);
        var finalTileType = world.TileTypeByCode(terrain[(updated.Y * world.Width) + updated.X]);
        Assert.NotNull(finalTileType);
        Assert.True(finalTileType!.PassableByArmies, $"The army ended on '{finalTileType.Id}', which armies cannot pass.");
        Assert.NotEqual(5, updated.Y); // never reached the sea row.
    }

    /// <summary>
    /// The mirror of the fleet-side test this same class of bug would have caught earlier: a destination
    /// that is itself open sea is refused the same way an out-of-bounds one is, by simply never being
    /// reached — the walk stops on the last passable tile rather than throwing or teleporting.
    /// </summary>
    [Fact]
    public void An_army_already_at_the_shore_cannot_step_directly_onto_open_sea()
    {
        var dispatcher = Dispatcher();
        var state = CoreTestbed.InitialState();
        var army = state.ArmyById("north-army-1")!;
        var world = CoreTestbed.Toy.World;

        // One army-move short of the sea: (3,2) -> (3,4) leaves it one plain/river step from row 5.
        var moved = dispatcher.Dispatch(state, new MoveArmyCommand(state.ActiveNationId, army.Id, 3, 4));
        Assert.True(moved.IsAccepted, moved.ToString());

        var second = dispatcher.Dispatch(
            moved.State, new MoveArmyCommand(state.ActiveNationId, army.Id, 3, 5));
        Assert.True(second.IsAccepted, second.ToString());

        var updated = second.State.ArmyById(army.Id)!;
        var terrain = world.Terrain.Decode(world.Width, world.Height);
        var finalTileType = world.TileTypeByCode(terrain[(updated.Y * world.Width) + updated.X]);
        Assert.True(finalTileType!.PassableByArmies);
    }
}
