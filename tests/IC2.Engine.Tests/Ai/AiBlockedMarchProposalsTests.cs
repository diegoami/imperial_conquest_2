using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// Issue <see href="https://github.com/diegoami/imperial_conquest_2/issues/907">#907</see>, the gate that
/// stops the AI re-proposing a march whose first step the walker is going to refuse.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the gate is its own test.</strong> Bug #907 reproduced with Gaul's army-9 at
/// <c>(92,34)</c> on <c>classical-mediterranean</c> proposing a march at Tarquinii
/// <c>(98,39)</c>: Bresenham's first step was <c>(93,35)</c>, Pisae's own tile, and
/// <see cref="Movement.Commands.MoveArmyCommandHandler.IsBlocked"/> refuses a city cell. Without this
/// gate the same inert candidate wins the score again the next turn, <see cref="AiTurn.Run"/> ends on
/// "changed nothing substantive", and "1 order issued" hides the stall from the soak's metric.
/// </para>
/// <para>
/// <strong>The test mirrors <see cref="Movement.Commands.MoveArmyCommandHandler"/>'s own
/// <c>IsBlocked</c> scan.</strong> <see cref="AiMilitaryPhase.ProposeMarches"/>'s pre-check decided, and
/// the brief's Owns list named, to use the same predicates the handler applies (city on the tile; other
/// army with non-null <see cref="ArmyState.CoveredTileCode"/>; non-under-construction fleet), so the
/// two checks cannot drift: a step the handler accepts is exactly a step the AI proposes.
/// </para>
/// <para>
/// <strong>Each test pins one of the three blockers.</strong> The terrain rule itself is the AI's
/// long-standing gate (it predates this issue); each new test pins one branch of the mirroring scan
/// added in this fix, and the negative control pins the regression case (a step with no occupant must
/// still pass).
/// </para>
/// </remarks>
public sealed class AiBlockedMarchProposalsTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    [Fact]
    public void A_march_whose_first_step_holds_an_enemy_city_is_not_proposed()
    {
        // The march under test is to "target-city" at (7,4); Bresenham's first step is (2,2), which
        // is the enemy city "blocker-city". The rationale format is "march at {city.Id} ...", so
        // "target-city" names the proposal and "blocker-city" names nothing — the assertion must
        // be on the proposal's own city id. The previous wording asserted on "blocker-city", which
        // no rationale ever contained; that version passed on main and passed again with the city
        // check removed (the assert was vacuously true). Asserting on the proposal's own id makes
        // this test fail when the blocker-city check is removed.
        var candidates = MarchCandidates(BlockerAtFirstStepCity());

        Assert.DoesNotContain(
            candidates,
            c => c.Kind == AiCandidate.ApproachKind
                 && c.Rationale.Contains("target-city", StringComparison.Ordinal));
    }

    [Fact]
    public void A_march_whose_first_step_holds_another_army_is_not_proposed()
    {
        var candidates = MarchCandidates(BlockerAtFirstStepArmy());

        Assert.DoesNotContain(
            candidates,
            c => c.Kind == AiCandidate.ApproachKind
                 && c.Rationale.Contains("target-city", StringComparison.Ordinal));
    }

    [Fact]
    public void A_march_whose_first_step_holds_a_fleet_is_not_proposed()
    {
        var candidates = MarchCandidates(BlockerAtFirstStepFleet());

        Assert.DoesNotContain(
            candidates,
            c => c.Kind == AiCandidate.ApproachKind
                 && c.Rationale.Contains("target-city", StringComparison.Ordinal));
    }

    /// <summary>
    /// A march with a clear first step still wins a candidate -- the new occupancy gate does not
    /// regress the existing terrain-passability gate or any march where no blocker stands at
    /// <c>path[1]</c>.
    /// </summary>
    [Fact]
    public void A_march_with_no_blocker_at_its_first_step_is_still_proposed()
    {
        var candidates = MarchCandidates(ClearFirstStep());

        var march = Assert.Single(
            candidates,
            c => c.Kind == AiCandidate.ApproachKind
                 && c.Rationale.Contains("target-city", StringComparison.Ordinal));
        Assert.True(march.Score > 0, $"the unobstructed march should score positive, got {march.Score}");
    }

    /// <summary>
    /// Coverage (R4): the blocker on <c>path[1]</c> is one of <em>this</em> nation's cities, not an
    /// enemy's. <see cref="AiMilitaryPhase.IsProposableMove"/>'s own occupancy scan treats every city
    /// on the tile as a blocker regardless of ownership, because
    /// <see cref="Movement.Commands.MoveArmyCommandHandler.IsBlocked"/> does the same — so a march
    /// that lands one step past an own city must also be filtered.
    /// </summary>
    [Fact]
    public void A_march_whose_first_step_holds_an_own_city_is_not_proposed()
    {
        var candidates = MarchCandidates(BlockerAtFirstStepOwnCity());

        Assert.DoesNotContain(
            candidates,
            c => c.Kind == AiCandidate.ApproachKind
                 && c.Rationale.Contains("target-city", StringComparison.Ordinal));
    }

    /// <summary>
    /// Coverage (R4): the blocker on <c>path[1]</c> is another of <em>this</em> nation's armies,
    /// not an enemy's. <see cref="AiMilitaryPhase.IsProposableMove"/>'s own occupancy scan ignores
    /// ownership of <c>other</c> armies — only <c>other.Id != army.Id</c> and a non-null
    /// <see cref="ArmyState.CoveredTileCode"/> matter — so a march past an own army is filtered
    /// the same way a march past an enemy army is.
    /// </summary>
    [Fact]
    public void A_march_whose_first_step_holds_another_own_army_is_not_proposed()
    {
        // The fixture has two of Acting's armies, both iterated by view.OwnArmies(): "ours-army" at
        // (1,2) is the one whose march at "target-city" is under test (its Bresenham first step is
        // (2,2) = blocker-army-ours); "blocker-army-ours" at (2,2) is itself clear of "target-city"
        // and so proposes its own unblocked march. Filter on the SubjectId the under-test march would
        // carry so the second army's own proposal does not false-positive this assertion.
        var candidates = MarchCandidates(BlockerAtFirstStepOwnArmy());

        Assert.DoesNotContain(
            candidates,
            c => c.Kind == AiCandidate.ApproachKind
                 && c.SubjectId == "ours-army"
                 && c.Rationale.Contains("target-city", StringComparison.Ordinal));
    }

    private static List<AiCandidate> MarchCandidates(GameState state)
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

    /// <summary>
    /// One own army at <c>(1,2)</c>, an enemy city at <c>(2,2)</c> on Bresenham's first step toward
    /// <c>(7,4)</c>, and the target city at <c>(7,4)</c>. The own capital at <c>(5,5)</c> sits
    /// inland and unthreatened so no reinforce candidate competes. Both nations are at war so the
    /// <c>target-city</c> march is the natural approach pick.
    /// </summary>
    private static GameState BlockerAtFirstStepCity()
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting, AiScriptedStates.DefaultPersonality, capitalCityId: "ours"),
            AiScriptedStates.AiNation(
                Other, AiScriptedStates.DefaultPersonality, capitalCityId: "theirs"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "ours", "Ours", 5, 5, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "blocker-city", "Blocker", 2, 2, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "target-city", "Target", 7, 4, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "ours-army", Acting, 1, 2, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 15000))
                with { Moves = 8 },
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(
                BattleCommandTestbed.StateWith(nations, cities, armies),
                Acting,
                Other),
            Acting);
    }

    /// <summary>
    /// Mirror of <see cref="BlockerAtFirstStepCity"/> but with another nation's army occupying
    /// <c>(2,2)</c> instead of a city. The blocker army has <c>CoveredTileCode = 2</c> so the AI's
    /// own occupancy predicate treats it as a real occupant, the same way
    /// <see cref="Movement.Commands.MoveArmyCommandHandler.IsBlocked"/> does.
    /// </summary>
    private static GameState BlockerAtFirstStepArmy()
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting, AiScriptedStates.DefaultPersonality, capitalCityId: "ours"),
            AiScriptedStates.AiNation(
                Other, AiScriptedStates.DefaultPersonality, capitalCityId: "theirs"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "ours", "Ours", 5, 5, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "target-city", "Target", 7, 4, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "ours-army", Acting, 1, 2, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 15000))
                with { Moves = 8 },
            CaptureFixtures.Army(
                    "blocker-army", Other, 2, 2, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 5000))
                with { Moves = 5, CoveredTileCode = 2 },
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(
                BattleCommandTestbed.StateWith(nations, cities, armies),
                Acting,
                Other),
            Acting);
    }

    /// <summary>
    /// Mirror of <see cref="BlockerAtFirstStepCity"/> but with an enemy fleet at <c>(2,2)</c>. A
    /// fleet on the Bresenham first step lands in the same place a city would for the land-march
    /// walker, and the same IsBlocked-style rule blocks either shape.
    /// </summary>
    private static GameState BlockerAtFirstStepFleet()
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting, AiScriptedStates.DefaultPersonality, capitalCityId: "ours"),
            AiScriptedStates.AiNation(
                Other, AiScriptedStates.DefaultPersonality, capitalCityId: "theirs"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "ours", "Ours", 5, 5, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "target-city", "Target", 7, 4, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "ours-army", Acting, 1, 2, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 15000))
                with { Moves = 8 },
        };

        var fleets = new[]
        {
            BattleTestbed.Fleet("blocker-fleet", Other, 2, 2, ships: 5, conditionPercent: 100),
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(
                BattleCommandTestbed.StateWith(nations, cities, armies, fleets),
                Acting,
                Other),
            Acting);
    }

    /// <summary>
    /// Bresenham from <c>(1,2)</c> to <c>(7,4)</c> puts the first step at <c>(2,2)</c>; that tile is
    /// plain terrain with no occupant, so the march at <c>"target-city"</c> is unobstructed. A unit
    /// test of the unobstructed path is the regression control for the new occupancy gate.
    /// </summary>
    private static GameState ClearFirstStep()
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting, AiScriptedStates.DefaultPersonality, capitalCityId: "ours"),
            AiScriptedStates.AiNation(
                Other, AiScriptedStates.DefaultPersonality, capitalCityId: "theirs"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "ours", "Ours", 5, 5, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "target-city", "Target", 7, 4, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "ours-army", Acting, 1, 2, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 15000))
                with { Moves = 8 },
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(
                BattleCommandTestbed.StateWith(nations, cities, armies),
                Acting,
                Other),
            Acting);
    }

    /// <summary>
    /// Own-city variant of <see cref="BlockerAtFirstStepCity"/>: the city on Bresenham's first step
    /// at <c>(2,2)</c> belongs to <paramref name="Acting"/>, not to the enemy. The IsProposableMove
    /// scan treats any city on the tile as a blocker, so this case must be filtered identically.
    /// </summary>
    private static GameState BlockerAtFirstStepOwnCity()
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting, AiScriptedStates.DefaultPersonality, capitalCityId: "ours"),
            AiScriptedStates.AiNation(
                Other, AiScriptedStates.DefaultPersonality, capitalCityId: "theirs"),
        };

        var cities = new[]
        {
            // "ours" at (5,5) is the own capital; the own blocker-city sits at (2,2) on Bresenham's
            // first step toward "target-city" at (7,4). All are active-owned.
            CaptureFixtures.City(
                "ours", "Ours", 5, 5, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "blocker-city", "Blocker", 2, 2, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "target-city", "Target", 7, 4, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "ours-army", Acting, 1, 2, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 15000))
                with { Moves = 8 },
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(
                BattleCommandTestbed.StateWith(nations, cities, armies),
                Acting,
                Other),
            Acting);
    }

    /// <summary>
    /// Own-army variant of <see cref="BlockerAtFirstStepArmy"/>: the army on Bresenham's first step
    /// at <c>(2,2)</c> belongs to <paramref name="Acting"/>, not to the enemy. The IsProposableMove
    /// scan checks only <c>other.Id != army.Id</c> on the occupant army, so an own-army blocker is
    /// filtered the same way an enemy-army blocker is.
    /// </summary>
    private static GameState BlockerAtFirstStepOwnArmy()
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting, AiScriptedStates.DefaultPersonality, capitalCityId: "ours"),
            AiScriptedStates.AiNation(
                Other, AiScriptedStates.DefaultPersonality, capitalCityId: "theirs"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "ours", "Ours", 5, 5, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "target-city", "Target", 7, 4, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "ours-army", Acting, 1, 2, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 15000))
                with { Moves = 8 },
            // The blocking army belongs to Acting (its id is "blocker-army-ours", distinct from
            // "ours-army"), so the IsProposableMove scan sees it as a real occupant.
            CaptureFixtures.Army(
                    "blocker-army-ours", Acting, 2, 2, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 5000))
                with { Moves = 5, CoveredTileCode = 2 },
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(
                BattleCommandTestbed.StateWith(nations, cities, armies),
                Acting,
                Other),
            Acting);
    }
}
