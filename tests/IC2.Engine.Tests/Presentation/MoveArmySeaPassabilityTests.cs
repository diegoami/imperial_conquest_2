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
    /// T237 (#1587): the toy world's row <c>y = 5</c> is sea across its whole width (<c>~ ~ ~ ~ ~ ~ ~ ~</c>).
    /// Before T237 an order to <c>(1, 5)</c> was accepted and the army walked
    /// <c>(3,2) -&gt; (2,3) -&gt; (2,4)</c> and stopped at the shore. The original's map click, and the
    /// remake's (<c>MapClickRules</c>, army to land only), do nothing for a sea tile, so the engine now
    /// refuses the order and the army stays where it is.
    /// </summary>
    [Fact]
    public void An_army_ordered_onto_open_sea_is_refused_and_does_not_move()
    {
        var dispatcher = Dispatcher();
        var state = CoreTestbed.InitialState();
        var army = state.ArmyById("north-army-1")!;

        var result = dispatcher.Dispatch(state, new MoveArmyCommand(state.ActiveNationId, army.Id, 1, 5));

        Assert.Equal(MoveArmyRejections.DestinationNotLand, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>
    /// The shore itself is still a legal destination, and from there one step down onto the sea is
    /// refused: the army stays on <c>(2, 4)</c>, a passable tile.
    /// </summary>
    [Fact]
    public void An_army_already_at_the_shore_cannot_step_directly_onto_open_sea()
    {
        var dispatcher = Dispatcher();
        var state = CoreTestbed.InitialState();
        var army = state.ArmyById("north-army-1")!;
        var world = CoreTestbed.Toy.World;

        // (3,2) -> (2,4): two plain steps, ending one step from row 5's sea.
        var moved = dispatcher.Dispatch(state, new MoveArmyCommand(state.ActiveNationId, army.Id, 2, 4));
        Assert.True(moved.IsAccepted, moved.ToString());
        var afterFirstMove = moved.State.ArmyById(army.Id)!;
        Assert.Equal(2, afterFirstMove.X);
        Assert.Equal(4, afterFirstMove.Y);
        var terrain = world.Terrain.Decode(world.Width, world.Height);
        Assert.True(world.TileTypeByCode(terrain[(4 * world.Width) + 2])!.PassableByArmies);

        var second = dispatcher.Dispatch(
            moved.State, new MoveArmyCommand(state.ActiveNationId, army.Id, 2, 5));

        Assert.Equal(MoveArmyRejections.DestinationNotLand, second.Code);
        Assert.Same(moved.State, second.State);
    }
}
