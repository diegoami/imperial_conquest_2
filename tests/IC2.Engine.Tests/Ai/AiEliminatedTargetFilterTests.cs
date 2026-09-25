using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/tasks/T84.md</c> Done-when 6: neither <c>AiMilitaryPhase.ProposeArmyAttacks</c> nor
/// <c>ProposeFleetAttacks</c> proposes a candidate against an eliminated nation's forces, even though
/// nothing else in a hand-built state stops them being adjacent — matching
/// <c>decompiled-elimination-cleanup.md</c> §5's "every targeting path requires the target's unity &gt; 0"
/// <c>[confirmed]</c> for diplomacy, <c>[derived]</c> here for the AI's own military candidates.
/// </summary>
/// <remarks>
/// Reuses the same idea <see cref="AiCommandLegalityTests"/>'s own <c>"eliminated-neighbour"</c> scripted
/// state already exercises (flip <see cref="AiScriptedStates.Defender"/>'s <see cref="NationState.Eliminated"/>
/// flag on a state that would otherwise produce an attack) — that file is a fixed battery this task may not
/// edit, so this is its own fixture, built the same way, calling <see cref="AiMilitaryPhase.Propose"/>
/// directly so the candidates themselves are visible rather than only "zero rejected commands".
/// </remarks>
public sealed class AiEliminatedTargetFilterTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    private static readonly AiPersonality AggressivePersonality =
        new(Aggression: 0.9, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5);

    /// <summary>
    /// The control: with <see cref="Other"/> not eliminated, the same contact state the eliminated variant
    /// below starts from really does propose an attack-army candidate against it — so the next test's
    /// silence is the filter, not an accident of the fixture.
    /// </summary>
    [Fact]
    public void A_live_neighbours_army_is_a_valid_attack_target()
    {
        var candidates = Military(AiScriptedStates.TwoArmiesInContact(AggressivePersonality));

        Assert.Contains(candidates, c => c.Kind == "attack-army" && c.SubjectId == "attacker-army");
    }

    /// <summary>Done-when 6, army half: the same state, with <see cref="Other"/> eliminated, proposes no attack-army candidate at all.</summary>
    [Fact]
    public void An_eliminated_neighbours_army_is_never_proposed_as_an_attack_target()
    {
        var live = AiScriptedStates.TwoArmiesInContact(AggressivePersonality);
        var eliminated = live with
        {
            Nations = ValueList.From(live.Nations.Select(n =>
                string.Equals(n.Id, Other, StringComparison.Ordinal) ? n with { Eliminated = true } : n)),
        };

        var candidates = Military(eliminated);

        Assert.DoesNotContain(candidates, c => c.Kind == "attack-army");
    }

    /// <summary>The fleet-half control, mirroring the army-half control above.</summary>
    [Fact]
    public void A_live_neighbours_fleet_is_a_valid_attack_target()
    {
        var candidates = Military(FleetContactState(eliminateOther: false));

        Assert.Contains(candidates, c => c.Kind == "attack-fleet");
    }

    /// <summary>Done-when 6, fleet half: with the neighbour eliminated, no attack-fleet candidate is proposed against it.</summary>
    [Fact]
    public void An_eliminated_neighbours_fleet_is_never_proposed_as_an_attack_target()
    {
        var candidates = Military(FleetContactState(eliminateOther: true));

        Assert.DoesNotContain(candidates, c => c.Kind == "attack-fleet");
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
    /// One own fleet in contact with a weak enemy fleet -- weak enough (1 ship against 10) that the attack
    /// clears the ratio gate at every random band, so a live neighbour always yields a candidate and an
    /// eliminated one isolates the filter rather than a strength shortfall. Both nations hold an inland,
    /// fully-fortified city so neither starts eliminated by <see cref="Cities.Capture.NationElimination"/>
    /// and no land candidate competes -- the same shape <c>AiMilitaryPhaseTests.FleetState</c> uses.
    /// </summary>
    private static GameState FleetContactState(bool eliminateOther)
    {
        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, AggressivePersonality, capitalCityId: "ours"),
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, capitalCityId: "theirs") with
            {
                Eliminated = eliminateOther,
            },
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
            BattleTestbed.Fleet("their-fleet", Other, 0, 1, ships: 1, conditionPercent: 100),
        };

        var state = BattleCommandTestbed.StateWith(nations, cities, armies: null, fleets);
        return AiScriptedStates.WithActiveSeat(BattleCommandTestbed.AtWar(state, Acting, Other), Acting);
    }
}
