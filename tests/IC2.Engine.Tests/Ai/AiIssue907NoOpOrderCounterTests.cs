using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// Bug #907's second half: even after
/// <see cref="AiMilitaryPhase.IsProposableMove"/> filters a march whose first step the walker will
/// refuse, the <see cref="AiTurn"/> loop's counter must still hold the same line, because a candidate
/// can still slip through (today via <see cref="AiMilitaryPhase.ProposeFleetMarches"/>, which
/// checks terrain only on <c>path[1]</c>). When the engine accepts a command and the substantive
/// state is untouched, the command must not be counted as issued.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a fleet-march fixture.</strong> This task's Owns list does not extend to
/// <see cref="AiMilitaryPhase.ProposeFleetMarches"/>'s proposed occupancy check -- the brief scoped
/// the fix to the army march -- so a fleet sail at an enemy fleet whose Bresenham first step is
/// itself a launched enemy fleet is the natural pin for the AiTurn counter: the proposal loop sees
/// a flat, passable first step (water, no city), the handler accepts the order, the walker stops at
/// the obstacle and the army sits where it started. With the old counter that accepted the no-op as
/// "1 order issued", the soak's stall metric never saw the stall; with the new counter the dispatch
/// still happens but the count stays at zero.
/// </para>
/// <para>
/// <strong>Two asserts to make the property explicit.</strong> The state substantively did not
/// change (a defensive regression check for the test's own setup, so a future fix to the fleet
/// occupancy that handles the no-op at proposal time rather than at dispatch can keep this contract)
/// and <see cref="AiTurnOutcome.CommandsIssued"/> is zero. The candidate's <c>"sail"</c> line is
/// still recorded in the log so a stall in this turn still reports the no-op, just not as an
/// issued command.
/// </para>
/// </remarks>
public sealed class AiIssue907NoOpOrderCounterTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    [Fact]
    public void A_blocked_fleet_sail_that_the_engine_accepts_does_not_count_as_issued()
    {
        var state = BlockedFleetSailState();

        var driven = AiScriptedStates.DriveOneTurn(state);

        Assert.True(
            AiSubstantiveState.AreEquivalent(state, driven.Outcome.State),
            "the fleet sail's blocked walk should leave the substantive state untouched, "
            + "so this test's fixture is actually exercising the no-op case");

        Assert.Equal(
            0,
            driven.Outcome.CommandsIssued);

        Assert.Contains(
            driven.Outcome.Log,
            line => line.Contains("last action changed nothing substantive", StringComparison.Ordinal));
    }

    /// <summary>
    /// Own fleet at <c>(0,2)</c> sailing at the enemy fleet at <c>(0,5)</c>: Bresenham's first step is
    /// <c>(0,3)</c>, where a launched enemy fleet blocks the walk. The terrain rule alone
    /// (<see cref="AiView.IsFleetPassable"/>) sees water, so the proposal loop still proposes the
    /// sail -- exactly the no-op accepted-and-counted case the brief asks the AiTurn counter to stop.
    /// </summary>
    private static GameState BlockedFleetSailState()
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting,
                AiScriptedStates.DefaultPersonality,
                treasury: 0,
                capitalCityId: "ours"),
            AiScriptedStates.AiNation(
                Other,
                AiScriptedStates.DefaultPersonality,
                capitalCityId: "theirs"),
        };

        // Far inland so no economy candidate races with the fleet sail.
        var cities = new[]
        {
            CaptureFixtures.City(
                "ours", "Ours", 5, 5, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "theirs", "Theirs", 7, 1, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        // The blocker fleet is larger than the sail target so an attack is not proposed alongside
        // the march (the existing fleet-march test fixture in AiMilitaryPhaseTests uses the same
        // trick to keep ProposeFleetAttacks out of the picture).
        var fleets = new[]
        {
            BattleTestbed.Fleet("own-fleet", Acting, 0, 2, ships: 10, conditionPercent: 100),
            BattleTestbed.Fleet("blocker", Other, 0, 3, ships: 40, conditionPercent: 100),
            BattleTestbed.Fleet("target", Other, 0, 5, ships: 10, conditionPercent: 100),
        };

        var atWar = BattleCommandTestbed.AtWar(
            BattleCommandTestbed.StateWith(nations, cities, fleets: fleets),
            Acting,
            Other);

        return AiScriptedStates.WithActiveSeat(atWar, Acting);
    }
}
