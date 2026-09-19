using IC2.Engine.Battle.Commands;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using Xunit;
using static IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Battle.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> T54, Done-when 3 and Done-when 4: what a city tile means for an army,
/// and <c>armies.disband-army</c> becoming reachable through play again
/// (<see href="https://github.com/diegoami/imperial_conquest_2/issues/215">#215</see>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Done-when 3 is settled, and the answer is that the merged movement rule was right.</strong>
/// An army never enters a city's tile — not a foreign one and not its own. The original's movement walk
/// steps only cell codes <c>2..11</c>, and "codes ≥ 12 are not steppable at all by this walk… interactions
/// with those are handled at the destination"; a city is code <c>20..99</c>
/// <strong>[confirmed: terrain-move-cost-table-in-dat.md]</strong>. The user confirmed the same from
/// play: <em>"first you select the army, then the town, and if they are adjacent there is a siege action.
/// Same pattern for an army attacking an army, or a fleet attacking a fleet."</em>
/// <strong>[confirmed: attack-and-siege-are-adjacency-orders.md — direct user observation of the original game, 2026-09-19]</strong>. So
/// <c>MoveArmyCommandHandler.IsBlocked</c> is faithful as merged and <strong>is not changed by this
/// task</strong>; these tests pin what it already does, because the next reader of
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/190">#190</see>'s N5 — which reads
/// the opposite, from a test fixture's convention rather than from the game — will need it pinned.
/// </para>
/// <para>
/// <strong>Why these tests live in <c>Battle/Commands</c>.</strong> They belong with the movement and
/// army suites, but T54's Owns list grants neither <c>tests/IC2.Engine.Tests/Armies/**</c> nor (after the
/// Done-when 3 answer removed any reason to touch movement) anything under
/// <c>tests/IC2.Engine.Tests/Movement/**</c>. Rather than widen the diff past the granted paths they are
/// kept here, with this note, so a reviewer can see the choice instead of inferring it.
/// </para>
/// </remarks>
public sealed class CityTileRuleAndDisbandReachabilityTests
{
    private const string ArmyId = "north-column";

    /// <summary>
    /// One of north's armies at (4, 0) — two tiles from <c>arx</c> at (2, 1) and two from <c>portus</c>
    /// at (5, 2), so it starts near <em>neither</em> of north's cities. That matters:
    /// <see cref="DoD04_DisbandIsReachableThroughPlay_MoveThenDisband"/> would prove nothing if the
    /// starting tile were already near one.
    /// </summary>
    private static GameState Fixture(int x = 4, int y = 0, int money = 90, int supplyTons = 30) =>
        BattleTestbed.StateWith(armies: new[]
        {
            BattleTestbed.Army(
                ArmyId, NorthNationId, x, y, morale: 68, money: money, supplyTons: supplyTons,
                BattleTestbed.Unit("light_infantry", 4_000, 6, "1st Foot Battalion")),
        });

    /// <summary>
    /// Done-when 3: ordering a move onto a city's tile leaves the army <em>beside</em> it, never on it —
    /// for the mover's own nation's city exactly as for a foreign one.
    /// </summary>
    [Theory]
    [InlineData("arx", 2, 1, 4, 0)]       // north's own city: still impassable.
    [InlineData("meridia", 3, 4, 3, 2)]   // south's city.
    public void DoD03_AnArmyNeverEndsOnACityTile(string cityId, int cityX, int cityY, int fromX, int fromY)
    {
        var state = Fixture(fromX, fromY);
        Assert.Equal((cityX, cityY), (state.CityById(cityId)!.X, state.CityById(cityId)!.Y));

        var result = Dispatcher().Dispatch(state, new MoveArmyCommand(NorthNationId, ArmyId, cityX, cityY));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(ArmyId)!;
        Assert.NotEqual((cityX, cityY), (army.X, army.Y));
        Assert.True(
            AttackLegality.AreAdjacent(army.X, army.Y, cityX, cityY),
            $"The walk should stop on a tile adjoining ({cityX}, {cityY}), not at ({army.X}, {army.Y}).");
    }

    /// <summary>
    /// <strong>Done-when 4, the whole of #215 in one test.</strong> The army moves toward its own city,
    /// stops on the adjoining tile because the city's own tile is impassable, and disbands there — the
    /// sequence that was impossible while "near" meant "co-located", since the position it demanded
    /// cannot be reached.
    /// </summary>
    [Fact]
    public void DoD04_DisbandIsReachableThroughPlay_MoveThenDisband()
    {
        var state = Fixture();
        var dispatcher = Dispatcher();
        var arx = state.CityById("arx")!;
        var treasuryBefore = state.NationById(NorthNationId)!.Treasury;
        var citySupplyBefore = arx.SupplyTons;

        var moved = dispatcher.Dispatch(state, new MoveArmyCommand(NorthNationId, ArmyId, arx.X, arx.Y));
        Assert.True(moved.IsAccepted, moved.ToString());

        var disbanded = dispatcher.Dispatch(moved.State, new DisbandArmyCommand(NorthNationId, ArmyId));

        Assert.True(disbanded.IsAccepted, disbanded.ToString());
        Assert.Null(disbanded.State.ArmyById(ArmyId));
        Assert.Equal(treasuryBefore + 90, disbanded.State.NationById(NorthNationId)!.Treasury);
        Assert.Equal(citySupplyBefore + 30, disbanded.State.CityById("arx")!.SupplyTons);
    }

    /// <summary>
    /// Done-when 4's boundary, so "near" is a distance and not "anywhere on the map": every tile
    /// adjoining the city is near enough, and the next ring out is not.
    /// </summary>
    [Theory]
    [InlineData(2, 1, true)]   // arx's own tile: unreachable in play, still accepted.
    [InlineData(3, 1, true)]   // adjoining arx, orthogonally.
    [InlineData(3, 2, true)]   // adjoining arx, diagonally.
    [InlineData(4, 0, false)]  // two tiles from arx and two from portus.
    [InlineData(2, 3, false)]  // two tiles from arx; adjoining south's meridia, which does not count.
    public void DoD04_NearMeansAnAdjoiningTile(int x, int y, bool expectedAccepted)
    {
        var result = Dispatcher().Dispatch(Fixture(x, y), new DisbandArmyCommand(NorthNationId, ArmyId));

        Assert.Equal(expectedAccepted, result.IsAccepted);
        if (!expectedAccepted)
        {
            Assert.Equal(DisbandArmyRejections.NotNearOwnCity, result.Code);
        }
    }

    /// <summary>
    /// The widening does not make disband legal beside <em>someone else's</em> city — the rule is "near
    /// its own city", and only the owner test keeps that true now that distance no longer does.
    /// </summary>
    [Fact]
    public void DoD04_AdjoiningAForeignCityIsStillRefused()
    {
        // (3, 3) adjoins south's meridia at (3, 4) and is two tiles from north's own arx.
        var result = Dispatcher().Dispatch(Fixture(3, 3), new DisbandArmyCommand(NorthNationId, ArmyId));

        Assert.True(result.IsRejected);
        Assert.Equal(DisbandArmyRejections.NotNearOwnCity, result.Code);
    }
}
