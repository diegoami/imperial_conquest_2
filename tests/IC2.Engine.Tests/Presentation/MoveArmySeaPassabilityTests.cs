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
/// <para>
/// <strong>Why this test lives in <c>Presentation</c> rather than <c>Movement</c>.</strong> Same reasoning
/// as <c>Battle/Commands/CityTileRuleAndDisbandReachabilityTests</c>: T23's Owns list grants
/// <c>src/IC2.Engine/Movement/**</c> for this one terrain check only, and does not widen
/// <c>tests/IC2.Engine.Tests/Movement/**</c>. <c>tests/IC2.Engine.Tests/Presentation/**</c> is granted in
/// full, so the regression this task's own fix needs is pinned there instead of reaching outside Owns.
/// </para>
/// <para>
/// <strong>Why the walk no longer goes down column <c>x = 3</c>.</strong> PR #248's round-1 review
/// (<see href="https://github.com/diegoami/imperial_conquest_2/pull/248#issuecomment-5745380469"/>) found
/// both tests vacuous: Southern League's capital, Meridia, sits at <c>(3, 4)</c> — <c>data/scenarios/
/// toy-3city.json</c>'s <c>cities[2]</c> — directly in that column, and <c>MoveArmyCommandHandler</c>'s
/// occupancy check (a city always blocks, regardless of owner) stops the army at <c>(3, 3)</c> before the
/// sea-passability branch is ever reached. Both tests below now route down column <c>x = 1</c>/<c>x = 2</c>
/// instead, which the toy world's terrain grid (<c>data/worlds/toy-3city.json</c>) confirms is clear of
/// every city, army and fleet start position (Arx (2,1), Portus (5,2), Meridia (3,4), north-fleet-1 (0,3),
/// south-army-1 (4,4), south-fleet-1 (7,4)) all the way to row <c>y = 5</c>'s open sea. Both were confirmed
/// to actually exercise the fix by the mandatory mutation cycle in build-process.md's task-catalogue T23
/// entry: with <c>TileType.PassableByArmies</c> disabled in <c>MoveArmyCommandHandler</c> and a
/// <c>--no-incremental</c> rebuild, <c>An_army_ordered_into_open_sea_stops_at_the_shore_instead_of_entering_it</c>
/// fails on its <c>PassableByArmies</c> assertion (the army walks onto <c>(1, 5)</c>, sea) and restoring the
/// check and rebuilding brings both back to green.
/// </para>
/// </remarks>
public sealed class MoveArmySeaPassabilityTests
{
    private static CommandDispatcher Dispatcher() => new(
        SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);

    /// <summary>
    /// The toy world's row <c>y = 5</c> is sea across its whole width (<c>~ ~ ~ ~ ~ ~ ~ ~</c>, confirmed
    /// by the golden transcript's own <c>map</c> rendering). North's army starts at <c>(3, 2)</c>; ordering
    /// it to <c>(1, 5)</c> traces the straight-line path <c>(3,2) -&gt; (2,3) -&gt; (2,4) -&gt; (1,5)</c>
    /// (<c>BresenhamPath.Trace</c>), two plain steps and then open sea, with no city, army or fleet on any
    /// of those cells to stop it early the way Meridia used to.
    /// </summary>
    [Fact]
    public void An_army_ordered_into_open_sea_stops_at_the_shore_instead_of_entering_it()
    {
        var dispatcher = Dispatcher();
        var state = CoreTestbed.InitialState();
        var army = state.ArmyById("north-army-1")!;
        var world = CoreTestbed.Toy.World;

        // (3, 2) toward (1, 5): two plain steps down through (2,3) and (2,4), then open sea at (1,5).
        var result = dispatcher.Dispatch(state, new MoveArmyCommand(state.ActiveNationId, army.Id, 1, 5));

        Assert.True(result.IsAccepted, result.ToString());
        var updated = result.State.ArmyById(army.Id)!;

        var terrain = world.Terrain.Decode(world.Width, world.Height);
        var finalTileType = world.TileTypeByCode(terrain[(updated.Y * world.Width) + updated.X]);
        Assert.NotNull(finalTileType);
        Assert.True(finalTileType!.PassableByArmies, $"The army ended on '{finalTileType.Id}', which armies cannot pass.");
        Assert.NotEqual(5, updated.Y); // never reached the sea row.
        Assert.Equal(2, updated.X);
        Assert.Equal(4, updated.Y);
    }

    /// <summary>
    /// The mirror of the fleet-side test this same class of bug would have caught earlier: a destination
    /// that is itself open sea is refused the same way an out-of-bounds one is, by simply never being
    /// reached — the walk stops on the last passable tile rather than throwing or teleporting. The army
    /// first walks the clean <c>(3,2) -&gt; (2,3) -&gt; (2,4)</c> path (no Meridia in the way) to stand one
    /// step from row 5's sea, then is ordered one cell further, straight down, onto <c>(2, 5)</c>.
    /// </summary>
    [Fact]
    public void An_army_already_at_the_shore_cannot_step_directly_onto_open_sea()
    {
        var dispatcher = Dispatcher();
        var state = CoreTestbed.InitialState();
        var army = state.ArmyById("north-army-1")!;
        var world = CoreTestbed.Toy.World;

        // One army-move short of the sea: (3,2) -> (2,4) leaves it one plain step from row 5.
        var moved = dispatcher.Dispatch(state, new MoveArmyCommand(state.ActiveNationId, army.Id, 2, 4));
        Assert.True(moved.IsAccepted, moved.ToString());
        var afterFirstMove = moved.State.ArmyById(army.Id)!;
        Assert.Equal(2, afterFirstMove.X);
        Assert.Equal(4, afterFirstMove.Y);

        var second = dispatcher.Dispatch(
            moved.State, new MoveArmyCommand(state.ActiveNationId, army.Id, 2, 5));
        Assert.True(second.IsAccepted, second.ToString());

        var updated = second.State.ArmyById(army.Id)!;
        var terrain = world.Terrain.Decode(world.Width, world.Height);
        var finalTileType = world.TileTypeByCode(terrain[(updated.Y * world.Width) + updated.X]);
        Assert.True(finalTileType!.PassableByArmies, $"The army ended on '{finalTileType!.Id}', which armies cannot pass.");
        Assert.NotEqual(5, updated.Y); // still didn't step onto the sea.
    }
}
