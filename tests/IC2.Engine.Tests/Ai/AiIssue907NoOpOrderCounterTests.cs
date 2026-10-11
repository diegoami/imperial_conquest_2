using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// Bug #907's second half: the <see cref="AiTurn"/> loop's counter must not count a command that
/// does not change the substantive state. After bug #929's fix, a fleet sail whose first step
/// holds a launched fleet is no longer proposed at all (the fix intercepts it at proposal time),
/// so the no-op path in <see cref="AiTurn"/> (about line 277) is no longer reached by this
/// fixture. The first two assertions still prove the property: the turn issues nothing and changes
/// nothing. The third assertion (the log line) was removed because the sail is not proposed, so
/// no walk occurs and the "last action changed nothing substantive" path is never hit.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a fleet-march fixture.</strong> The original fixture used a fleet sail at an enemy
/// fleet whose Bresenham first step was itself a launched enemy fleet. With #929's fix, that sail
/// is now rejected at proposal time, so the AiTurn counter's no-op path is never reached. The
/// test now pins that the turn still issues zero commands and leaves the substantive state
/// unchanged -- the same property #907 guarantees, just exercised through a different code path.
/// </para>
/// <para>
/// <strong>Two assertions to make the property explicit.</strong> The state substantively did not
/// change and <see cref="AiTurnOutcome.CommandsIssued"/> is zero. These two asserts are the
/// substantive regression check; the third assert from the original test was removed because
/// #929 stops the no-op sail at proposal time, so the counter's no-op logging path is never
/// reached. <see cref="AiTurn"/> (about line 277) remains as a defence that no current proposal
/// reaches.
/// </para>
/// </remarks>
public sealed class AiIssue907NoOpOrderCounterTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    [Fact]
    public void A_fleet_sail_onto_a_launched_fleet_is_not_proposed_and_nothing_is_issued()
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
