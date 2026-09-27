using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle;
using Xunit;
using static IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/task-catalogue.md</c> T54, Done-when 4: <c>armies.disband-army</c> becoming reachable through
/// play again (<see href="https://github.com/diegoami/imperial_conquest_2/issues/215">#215</see>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Relocated by T66 (#221's note), body unchanged.</strong> These three tests lived in
/// <c>tests/IC2.Engine.Tests/Battle/Commands/CityTileRuleAndDisbandReachabilityTests.cs</c> only because
/// T54's Owns list granted no <c>tests/IC2.Engine.Tests/Armies/**</c> — the implementer said so in that
/// file's own remarks and in its PR rather than leaving it to be inferred. T66 is the first task since
/// with that grant, so they move here, to sit with the rest of the disband suite
/// (<see cref="DisbandArmyCommandHandlerTests"/>). <see cref="DisbandArmyCommandHandlerTests"/> is left
/// alone: it is a distinct, already-merged suite, and this file adds to the directory rather than
/// merging into it. <c>DoD03_AnArmyNeverEndsOnACityTile</c> — the movement test in the same original
/// file — does <em>not</em> move here: it tests <c>MoveArmyCommandHandler</c>, not disband, and no
/// <c>tests/IC2.Engine.Tests/Movement/**</c> grant exists for this task either, exactly as the original
/// file's own remarks already said.
/// </para>
/// </remarks>
public sealed class DisbandReachabilityThroughPlayTests
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
