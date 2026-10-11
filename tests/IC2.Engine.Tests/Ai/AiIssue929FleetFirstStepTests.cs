using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// Bug #929: <c>ProposeFleetMarches</c> checks terrain only on the first Bresenham step,
/// ignoring whether another launched fleet occupies it. A sail whose first step holds a
/// launched fleet is proposed anyway, accepted, and moves nothing. After #928 the no-op is
/// no longer counted as issued, but the AI's turn still ends on it and the next candidate
/// is never tried.
///
/// The fix mirrors the army fix in <c>AiMilitaryPhase.IsProposableMove</c>: after the terrain check,
/// reject a first step that <c>MoveFleetCommandHandler</c>'s <c>IsBlocked</c> would block
/// (another launched fleet at that position), so the next candidate is tried.
/// </summary>
public sealed class AiIssue929FleetFirstStepTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    /// <summary>
    /// A fleet whose best sail's first step holds a launched fleet proposes its next candidate
    /// instead. Two enemy fleets stand at the same distance (2); a third, launched fleet holds the
    /// first step toward one of them ("near-enemy"), so only the sail at the other ("far-enemy")
    /// is proposed.
    /// </summary>
    [Fact]
    public void A_fleet_whose_best_sail_first_step_is_blocked_proposes_the_next_candidate()
    {
        var state = BlockedFirstStepState();

        var candidates = Military(state);

        // The sail toward the near enemy (blocked at first step) must not appear.
        Assert.DoesNotContain(
            candidates,
            c => c.Kind == AiCandidate.SailKind && c.Rationale.Contains("near-enemy", StringComparison.Ordinal));

        // The sail toward the far enemy (first step is clear water) must appear.
        var farSail = Assert.Single(
            candidates,
            c => c.Kind == AiCandidate.SailKind && c.Rationale.Contains("far-enemy", StringComparison.Ordinal));
        Assert.Equal("own-fleet", farSail.SubjectId);
    }

    /// <summary>
    /// Own fleet at <c>(0,3)</c>. Two enemy fleets on row 5 (all water): near-enemy at <c>(2,5)</c>
    /// and far-enemy at <c>(0,5)</c>. Bresenham from (0,3) to (2,5): first step is (1,4) where a
    /// launched blocker fleet sits -- blocked. Bresenham from (0,3) to (0,5): first step is
    /// (0,4) -- not blocked. So only the far-enemy sail passes the first-step check.
    /// </summary>
    private static GameState BlockedFirstStepState()
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

        // Inland cities far from the sea lane so no economy candidate competes.
        var cities = new[]
        {
            CaptureFixtures.City(
                "ours", "Ours", 5, 5, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "theirs", "Theirs", 7, 1, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        // Row 4 (y=4) has water at columns 0-1 and 6-7, land at columns 2-5.
        // Row 5 (y=5) is all water.
        // Blocker at (1,4) blocks the diagonal path to near-enemy (first step (1,4))
        // but not the vertical path to far-enemy (first step (0,4)).
        var fleets = new[]
        {
            BattleTestbed.Fleet("own-fleet", Acting, 0, 3, ships: 10, conditionPercent: 100),
            BattleTestbed.Fleet("blocker", Other, 1, 4, ships: 40, conditionPercent: 100),
            BattleTestbed.Fleet("near-enemy", Other, 2, 5, ships: 10, conditionPercent: 100),
            BattleTestbed.Fleet("far-enemy", Other, 0, 5, ships: 10, conditionPercent: 100),
        };

        var atWar = BattleCommandTestbed.AtWar(
            BattleCommandTestbed.StateWith(nations, cities, fleets: fleets),
            Acting,
            Other);

        return AiScriptedStates.WithActiveSeat(atWar, Acting);
    }

    private static List<AiCandidate> Military(GameState state)
    {
        var view = new AiView(state, AiScriptedStates.Ruleset, AiScriptedStates.World, Acting);
        var candidates = new List<AiCandidate>();

        AiMilitaryPhase.Propose(
            view,
            AiPersonalityProfile.For(state.NationById(Acting)!, view.Ruleset),
            SplitMix64Rng.ForStream(1, "ai.turn"),
            Array.Empty<string>(),
            candidates);

        return candidates;
    }
}
