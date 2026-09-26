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
/// any-nation's check says "yes" — the same distinction <see cref="CapitalOwnership"/>'s other four
/// callers already draw.
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

        Assert.True(staleCapitalTally.Proposed + staleCapitalTally.RejectedByRatio == 1,
            "the ratio gate must have been evaluated exactly once for this to be a comparable measurement");
        Assert.True(ordinaryTally.Proposed + ordinaryTally.RejectedByRatio == 1,
            "the ratio gate must have been evaluated exactly once for this to be a comparable measurement");

        Assert.True(
            staleCapitalTally.BestDefenderPower > ordinaryTally.BestDefenderPower,
            $"a city another nation's stale capital names ({staleCapitalTally.BestDefenderPower}) must "
            + $"defend harder than the same city when nobody's capital names it ({ordinaryTally.BestDefenderPower}) "
            + "-- otherwise #425's fix is not being read.");
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
