using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// The naval half of <c>docs/game-design.md</c> §AI phase 2, and specifically the rule the
/// <c>/code-review ultra</c> pass found a remark misdescribing: <strong>there is no hold rule</strong>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ProposeFleetMarches</c>' remarks used to claim a fleet already adjacent to an enemy "stays put
/// rather than being pulled at a second one". No such rule exists — the only guard is per target. The
/// remark was the defect and has been rewritten; this file pins the behaviour it misdescribed, using the
/// reviewer's own reachable case, so the claim cannot drift back in either direction.
/// </para>
/// <para>
/// The case, built from the toy world's actual sea lanes: column 0 is water for rows 0-4 and row 5 is
/// water end to end, so a fleet at <c>(0,2)</c> can be put in contact with one enemy at <c>(0,1)</c> and
/// three tiles from another at <c>(0,5)</c> without inventing terrain.
/// </para>
/// </remarks>
public sealed class AiMilitaryPhaseTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    /// <summary>
    /// The behaviour a hold rule would forbid, and which this AI deliberately allows: in contact with a
    /// fleet far too strong to attack, it sails at a weaker one three tiles away.
    /// </summary>
    [Fact]
    public void A_fleet_beside_a_stronger_enemy_sails_at_a_weaker_one_further_off()
    {
        var candidates = Military(FleetState());

        // It declines the fight it cannot win...
        Assert.DoesNotContain(candidates, c => c.Kind == "attack-fleet");

        // ...and goes after the one it might, rather than parking next to the stronger enemy.
        var sail = Assert.Single(candidates, c => c.Kind == AiCandidate.SailKind);
        Assert.Equal("own-fleet", sail.SubjectId);
        Assert.Contains("weak-enemy", sail.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// The per-target half that <em>is</em> real: no march is proposed at the fleet already being
    /// touched, because the walk could not enter its tile anyway.
    /// </summary>
    [Fact]
    public void No_march_is_proposed_at_the_fleet_already_in_contact()
    {
        var candidates = Military(FleetState());

        foreach (var candidate in candidates)
        {
            if (candidate.Kind == AiCandidate.SailKind)
            {
                Assert.DoesNotContain("strong-enemy", candidate.Rationale, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// The control for the first test: with the near enemy weak enough, the fleet attacks it instead of
    /// sailing anywhere. Without this, "it sails at the weaker one" would also be satisfied by an AI that
    /// never attacks fleets at all.
    /// </summary>
    [Fact]
    public void A_fleet_beside_a_weaker_enemy_attacks_it_rather_than_sailing_off()
    {
        var candidates = Military(FleetState(nearEnemyShips: 1));

        Assert.Contains(candidates, c => c.Kind == "attack-fleet");

        // And the attack outranks any sail, so it is what the turn would actually do.
        long bestAttack = 0;
        long bestSail = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Kind == "attack-fleet")
            {
                bestAttack = Math.Max(bestAttack, candidate.Score);
            }

            if (candidate.Kind == AiCandidate.SailKind)
            {
                bestSail = Math.Max(bestSail, candidate.Score);
            }
        }

        Assert.True(bestAttack > bestSail, $"attack {bestAttack} should outrank sail {bestSail}");
    }

    private static List<AiCandidate> Military(GameState state)
    {
        var view = new AiView(state, AiScriptedStates.Ruleset, AiScriptedStates.World, Acting);
        var candidates = new List<AiCandidate>();

        AiMilitaryPhase.Propose(
            view,
            AiPersonalityProfile.For(state.NationById(Acting)!),
            SplitMix64Rng.ForStream(1, "ai.turn"),
            Array.Empty<string>(),
            candidates);

        return candidates;
    }

    /// <summary>
    /// One own fleet at <c>(0,2)</c>, an enemy in contact at <c>(0,1)</c>, and a second enemy three tiles
    /// south at <c>(0,5)</c>. Both nations hold a city so neither is eliminated, and both cities sit
    /// inland and fully fortified so no land candidate competes.
    /// </summary>
    /// <param name="nearEnemyShips">
    /// The fleet in contact. 40 ships against the acting fleet's 10 puts the strength ratio far below any
    /// gate even at the most favourable random band, so no attack is proposed; 1 ship puts it far above.
    /// </param>
    private static GameState FleetState(int nearEnemyShips = 40)
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
                "ours", "Ours", 2, 1, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
            CaptureFixtures.City(
                "theirs", "Theirs", 5, 3, Other, Other, loyalty: 90, fortificationCode: 100,
                populationThousands: 200, maxPopulationThousands: 200, tribute: 10),
        };

        var fleets = new[]
        {
            BattleTestbed.Fleet("own-fleet", Acting, 0, 2, ships: 10, conditionPercent: 100),
            BattleTestbed.Fleet("strong-enemy", Other, 0, 1, ships: nearEnemyShips, conditionPercent: 100),
            BattleTestbed.Fleet("weak-enemy", Other, 0, 5, ships: 10, conditionPercent: 100),
        };

        var state = BattleCommandTestbed.StateWith(nations, cities, armies: null, fleets);
        return AiScriptedStates.WithActiveSeat(
            BattleCommandTestbed.AtWar(state, Acting, Other), Acting);
    }
}
