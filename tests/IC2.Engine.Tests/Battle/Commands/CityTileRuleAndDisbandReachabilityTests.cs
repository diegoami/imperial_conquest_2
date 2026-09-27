using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using Xunit;
using static IC2.Engine.Tests.Battle.Commands.BattleCommandTestbed;

namespace IC2.Engine.Tests.Battle.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> T54, Done-when 3: what a city tile means for an army.
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
/// <strong>T54's disband tests (Done-when 4, #215) moved out of this file, T66 (#221's note).</strong>
/// They lived here only because T54's Owns list granted no <c>tests/IC2.Engine.Tests/Armies/**</c>; T66
/// has that grant, so <c>DoD04_DisbandIsReachableThroughPlay_MoveThenDisband</c>,
/// <c>DoD04_NearMeansAnAdjoiningTile</c> and <c>DoD04_AdjoiningAForeignCityIsStillRefused</c> moved,
/// unchanged in body, to
/// <see cref="IC2.Engine.Tests.Armies.DisbandReachabilityThroughPlayTests"/>. What stays here —
/// <see cref="DoD03_AnArmyNeverEndsOnACityTile"/> — is a movement test, not a disband one: no
/// <c>tests/IC2.Engine.Tests/Movement/**</c> grant exists for T66 either, so it has nowhere else to go
/// and remains in <c>Battle/Commands</c> under the same reasoning T54 originally gave.
/// </para>
/// </remarks>
public sealed class CityTileRuleAndDisbandReachabilityTests
{
    private const string ArmyId = "north-column";

    /// <summary>
    /// One of north's armies at (4, 0) — two tiles from <c>arx</c> at (2, 1) and two from <c>portus</c>
    /// at (5, 2), so it starts near <em>neither</em> of north's cities.
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
}
