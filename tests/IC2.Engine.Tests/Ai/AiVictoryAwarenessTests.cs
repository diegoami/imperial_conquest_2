using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/game-design.md</c> §AI phase 4: "<em>nations close to the scenario's victory condition weight
/// their decisions toward securing it (e.g., a domination-victory AI prioritizes attacking weak
/// neighbors over turtling).</em>"
/// </summary>
/// <remarks>
/// <para>
/// Implemented as a multiplier on the score of every city-taking action, and <em>only</em> as that. It
/// changes what the AI prefers, never what it is willing to do: the attack gate stays a pure function of
/// aggression and the strength ratio, so <c>docs/task-catalogue.md</c> T22 Done-when 3's demonstration
/// tests one variable rather than two. <see cref="Victory_progress_never_moves_the_attack_gate"/> pins
/// that separation, which is the claim most worth breaking by accident.
/// </para>
/// </remarks>
public sealed class AiVictoryAwarenessTests
{
    [Theory]
    [InlineData(0, 1000)]
    [InlineData(500, 1500)]
    [InlineData(1000, 2000)]
    public void A_nation_closer_to_victory_scores_a_city_higher(long progressPermille, long expected)
    {
        Assert.Equal(expected, AiView.WithVictoryAwareness(1000, progressPermille));
    }

    /// <summary>
    /// Progress is the share of the map's cities held, counted with the engine's own
    /// <see cref="GameState.CountCitiesOwnedBy"/> — the same count
    /// <c>VictoryEvaluator.EvaluateTotalConquest</c> compares against <c>Cities.Count</c>.
    /// </summary>
    [Fact]
    public void Progress_is_the_share_of_the_maps_cities_the_nation_holds()
    {
        var state = FourCityState(ownedByActing: 3);

        var view = new AiView(
            state, AiScriptedStates.Ruleset, AiScriptedStates.World, AiScriptedStates.Attacker);

        Assert.Equal(750, view.VictoryProgressPermille());
    }

    /// <summary>
    /// The same march at the same city, scored by a nation holding three quarters of the map and by one
    /// holding a quarter of it: the leader wants it more. This is the behaviour the design line asks for,
    /// asserted as a comparison rather than as an absolute number, so retuning the weight does not
    /// invalidate it.
    /// </summary>
    [Fact]
    public void The_leading_nation_values_the_same_objective_more_than_the_trailing_one()
    {
        var leading = ApproachScore(FourCityState(ownedByActing: 3));
        var trailing = ApproachScore(FourCityState(ownedByActing: 1));

        Assert.True(
            leading > trailing,
            $"a nation near victory should value taking a city more: {leading} vs {trailing}");
    }

    /// <summary>
    /// Victory awareness is a preference, not a permission. A nation one city from winning still refuses
    /// an attack its aggression and the strength ratio do not justify.
    /// </summary>
    [Fact]
    public void Victory_progress_never_moves_the_attack_gate()
    {
        var timid = new AiPersonality(Aggression: 0.1, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5);
        var state = AiScriptedStates.TwoArmiesInContact(timid);

        // The acting nation now holds every city but one: maximum victory pressure.
        var nearVictory = state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                string.Equals(c.Id, "defender-city", StringComparison.Ordinal)
                    ? c
                    : c with { Owner = AiScriptedStates.Attacker })),
        };

        var driven = AiScriptedStates.DriveOneTurn(nearVictory);

        Assert.DoesNotContain("battle.attack-army", driven.IssuedKinds);
        Assert.Equal(0, driven.Outcome.CommandsRejected);
    }

    private static long ApproachScore(GameState state)
    {
        var view = new AiView(
            state, AiScriptedStates.Ruleset, AiScriptedStates.World, AiScriptedStates.Attacker);
        var candidates = new List<AiCandidate>();

        AiMilitaryPhase.Propose(
            view,
            AiPersonalityProfile.For(state.NationById(AiScriptedStates.Attacker)!),
            IC2.Engine.Core.SplitMix64Rng.ForStream(1, "ai.turn"),
            Array.Empty<string>(),
            candidates);

        long best = 0;
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.Kind, AiCandidate.ApproachKind, StringComparison.Ordinal))
            {
                best = Math.Max(best, candidate.Score);
            }
        }

        Assert.True(best > 0, "the fixture must offer a march at an enemy city");
        return best;
    }

    /// <summary>Four cities, <paramref name="ownedByActing"/> of them the acting nation's, and one army.</summary>
    private static GameState FourCityState(int ownedByActing)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(
                AiScriptedStates.Attacker, AiScriptedStates.DefaultPersonality, capitalCityId: "c0"),
            AiScriptedStates.AiNation(
                AiScriptedStates.Defender, AiScriptedStates.DefaultPersonality, capitalCityId: "c3"),
        };

        var cities = new List<CityState>();
        var positions = new[] { (0, 0), (7, 0), (0, 5), (7, 5) };
        for (var i = 0; i < positions.Length; i++)
        {
            var owner = i < ownedByActing ? AiScriptedStates.Attacker : AiScriptedStates.Defender;
            cities.Add(CaptureFixtures.City(
                "c" + i, "City " + i, positions[i].Item1, positions[i].Item2, owner, owner,
                loyalty: 50, fortificationCode: 50, populationThousands: 100,
                maxPopulationThousands: 200, tribute: 10));
        }

        var armies = new[]
        {
            CaptureFixtures.Army(
                    "marcher", AiScriptedStates.Attacker, 4, 3, morale: 60,
                    CaptureFixtures.Unit("light_infantry", 15000))
                with { Moves = 5 },
        };

        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.StateWith(nations, cities, armies), AiScriptedStates.Attacker);
    }
}
