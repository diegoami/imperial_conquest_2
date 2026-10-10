using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// Bug #425 (T91's review N3 and S2): <c>AiMilitaryPhase.IsControllerCapital</c> now reads
/// <see cref="Cities.Capture.CapitalOwnership.IsAnyNationsCapital"/>, matching every other
/// capital-strength caller since T91, instead of only the besieged city's current owner's own capital.
/// </summary>
/// <remarks>
/// The reachable case the fix targets: a city another nation's stale <see cref="NationState.CapitalCityId"/>
/// still names, while its actual, current owner's own capital is a different city entirely (T90/#409 — a
/// nation's capital pointer never moves on its own). The owner-only check says "not a capital" here; the
/// any-nation's check says "yes" — rework round 1 N7 (caller count corrected): the same distinction
/// <see cref="IC2.Engine.Battle.InstantBattleResolver"/> and
/// <see cref="IC2.Engine.Cities.Capture.ConquestTrigger"/> already draw, each reading
/// <see cref="IC2.Engine.Cities.Capture.CapitalOwnership.IsAnyNationsCapital"/> directly.
/// </remarks>
public sealed class AiMilitaryPhaseCapitalOwnershipTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    /// <summary>
    /// Same siege, same troops, same everything except one nation's stale capital pointer: the besieged
    /// city's defender strength is higher when another nation's stale capital names it, because the
    /// resolver's own ×5/3 capital multiplier now applies. The owner-only reading (pre-#425) could not
    /// tell these two states apart at all, since the defending nation's own capital is identical in both.
    /// </summary>
    [Fact]
    public void A_city_another_nations_stale_capital_names_scores_a_higher_defender_strength()
    {
        var staleCapitalTally = ProposeAgainst(anotherNationsStaleCapitalNamesTheCity: true);
        var ordinaryTally = ProposeAgainst(anotherNationsStaleCapitalNamesTheCity: false);

        Assert.True(staleCapitalTally.Adjacent == 1,
            "the tree must have been evaluated exactly once for this to be a comparable measurement");
        Assert.True(ordinaryTally.Adjacent == 1,
            "the tree must have been evaluated exactly once for this to be a comparable measurement");

        // The capital multiplier (×5/3) lifts a city's defender strength; the city scorer divides that
        // strength into the army's own, so a higher defender strength means a *lower* city score. The
        // stale-capital city therefore scores *less* than the ordinary one — and the test reads that
        // inversion directly.
        Assert.True(
            staleCapitalTally.BestCityScore < ordinaryTally.BestCityScore,
            $"a city another nation's stale capital names ({staleCapitalTally.BestCityScore}) must "
            + $"score *less* on the tree's city scorer than the same city when nobody's capital names it "
            + $"({ordinaryTally.BestCityScore}) -- otherwise #425's fix is not being read.");
    }

    /// <summary>
    /// N5 (rework round 1): the siege-gate call above only reaches <c>IsControllerCapital</c> at
    /// <c>AiMilitaryPhase.cs</c>'s adjacent-army call site (<c>ProposeSieges</c>). The march-path call
    /// site (<c>ProposeMarches</c>'s <c>SiegeRatioAgainst</c>, used to score how close an army marching at
    /// a <em>non-adjacent</em> enemy city is to being able to take it) goes through the identical helper
    /// but was not covered by the first test, and the reviewer's M2b mutation -- reverting only that one
    /// call site to the owner-only reading -- survived the full suite because of it. This drives an army
    /// too far from the besieged city to besiege it (an "approach" candidate, not a "besiege" one) and
    /// shows the same defender-strength difference there.
    /// </summary>
    [Fact]
    public void An_approach_candidate_against_a_stale_capital_city_scores_a_weaker_march()
    {
        var staleCapitalWeakness = ApproachWeaknessAgainstDefenderCity(anotherNationsStaleCapitalNamesTheCity: true);
        var ordinaryWeakness = ApproachWeaknessAgainstDefenderCity(anotherNationsStaleCapitalNamesTheCity: false);

        Assert.True(ordinaryWeakness > 0, "the ordinary scenario's own city-score term must be strictly positive for this comparison to mean anything");
        Assert.True(
            staleCapitalWeakness < ordinaryWeakness,
            $"marching at a city another nation's stale capital names ({staleCapitalWeakness}) must score a "
            + $"weaker siege-ratio term than the same city when nobody's capital names it ({ordinaryWeakness}) "
            + "-- otherwise the march-path call to IsControllerCapital is not reading #425's fix.");
    }

    /// <summary>
    /// The "{4} tiles away, base score {4}, siege ratio {5} permille" line's own siege-ratio term
    /// (<c>AiMilitaryPhase.SiegeRatioAgainst</c>'s return value, folded into the approach candidate's
    /// score as "weakness") for the one approach candidate that targets <c>defender-city</c>.
    /// </summary>
    private static long ApproachWeaknessAgainstDefenderCity(bool anotherNationsStaleCapitalNamesTheCity)
    {
        var ruleset = AiScriptedStates.Ruleset;

        var nations = new[]
        {
            AiScriptedStates.AiNation(
                Acting,
                AiScriptedStates.DefaultPersonality,
                capitalCityId: anotherNationsStaleCapitalNamesTheCity ? "defender-city" : "acting-city"),
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, capitalCityId: "other-home"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "acting-city", "Acting City", 0, 0, Acting, Acting,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "defender-city", "Defender City", 7, 5, Other, Other,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "other-home", "Other Home", 9, 9, Other, Other,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            // Adjacent to defender-city (Chebyshev distance 1) so the tally records an adjacent pair
            // and the test reads the city's tree score directly off it.
            CaptureFixtures.Army("marcher", Acting, 7, 4, morale: 60, CaptureFixtures.Unit("archers", 40000))
                with { Moves = 5 },
        };

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(BattleCommandTestbed.StateWith(nations, cities, armies), Acting, Other),
            Acting);

        var view = new AiView(state, ruleset, AiScriptedStates.World, Acting);
        var tally = new AiSiegeGateTally();

        AiMilitaryPhase.Propose(
            view,
            AiPersonalityProfile.For(state.NationById(Acting)!, ruleset),
            SplitMix64Rng.ForStream(1, "ai.turn"),
            Array.Empty<string>(),
            new List<AiCandidate>(),
            tally);

        // Post-T156: the AI's tree scorer reads the city's defender strength through
        // CompleteDefenderStrength, and the tallied BestCityScore is the city's score from the tree's
        // city scorer. A capital's ×5/3 multiplier lifts the city's defender strength, which divides
        // the army's own score, so the stale-capital city scores *less* than the ordinary one.
        return tally.BestCityScore;
    }

    private static AiSiegeGateTally ProposeAgainst(bool anotherNationsStaleCapitalNamesTheCity)
    {
        var ruleset = AiScriptedStates.Ruleset;

        var nations = new[]
        {
            // Acting's own capital: "acting-city" ordinarily, or a stale pointer at the besieged city
            // itself in the other scenario -- Acting never owns "defender-city", so either value is
            // equally "stale" from Acting's side; only the besieged city's OWNER's capital (below) is
            // the one the pre-#425 owner-only check actually consulted.
            AiScriptedStates.AiNation(
                Acting,
                AiScriptedStates.DefaultPersonality,
                capitalCityId: anotherNationsStaleCapitalNamesTheCity ? "defender-city" : "acting-city"),
            // Other's own capital is a THIRD city, never "defender-city" -- so the owner-only check
            // (owner.CapitalCityId == city.Id) reads "not a capital" in both scenarios, and only the
            // any-nation's check can tell them apart.
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, capitalCityId: "other-home"),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "acting-city", "Acting City", 1, 0, Acting, Acting,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "defender-city", "Defender City", 7, 5, Other, Other,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "other-home", "Other Home", 9, 9, Other, Other,
                loyalty: 90, fortificationCode: 100, populationThousands: 200,
                maxPopulationThousands: 200, tribute: 10),
        };

        var armies = new[]
        {
            CaptureFixtures.Army("besieger", Acting, 6, 5, morale: 60, CaptureFixtures.Unit("archers", 40000))
                with { Moves = 5 },
        };

        var state = AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(BattleCommandTestbed.StateWith(nations, cities, armies), Acting, Other),
            Acting);

        var view = new AiView(state, ruleset, AiScriptedStates.World, Acting);
        var candidates = new List<AiCandidate>();
        var tally = new AiSiegeGateTally();

        AiMilitaryPhase.Propose(
            view,
            AiPersonalityProfile.For(state.NationById(Acting)!, ruleset),
            SplitMix64Rng.ForStream(1, "ai.turn"),
            Array.Empty<string>(),
            candidates,
            tally);

        return tally;
    }
}
