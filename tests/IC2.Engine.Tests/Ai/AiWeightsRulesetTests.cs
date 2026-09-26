using System.Text.Json.Nodes;
using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/task-catalogue.md</c> T79 (#355): the AI's weights are ruleset data now, in one new
/// <see cref="AiWeightsRules"/> block (<see cref="Ruleset.Ai"/>). Two things a soak comparison cannot
/// show on its own: that the loader actually enforces the block is present (Done-when 1's "the loader
/// rejects a ruleset missing the block"), and that a weight genuinely drives a decision rather than being
/// inert data nobody reads (Done-when 4).
/// </summary>
public sealed class AiWeightsRulesetTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    /// <summary>Well above the cheapest battalion's cost even at the 10% floor, so affordability never decides this test.</summary>
    private const int Treasury = 5000;

    /// <summary>
    /// Done-when 1 (§2.6): a ruleset document with no <c>"ai"</c> key is rejected at load time, exactly
    /// like every other required block — never defaulted, never silently accepted.
    /// </summary>
    [Fact]
    public void The_loader_rejects_a_ruleset_missing_the_ai_block()
    {
        var document = LoadToyRulesetNode();
        document.Remove("ai");

        var error = Assert.Throws<MissingRequiredFieldException>(
            () => GameDataLoader.Load<Ruleset>("toy-ruleset.json", document.ToJsonString()));

        Assert.Equal("ai", error.FieldPath);
        Assert.Equal(nameof(Ruleset), error.DeclaringType);
    }

    /// <summary>
    /// Done-when 4: a test ruleset that lowers one weight (<c>recruitBaseScore</c>, the example the task
    /// entry names) changes which candidate the economy phase ranks highest, over the identical state.
    /// </summary>
    /// <remarks>
    /// At <c>expansionDrive = 1.0</c> (permille 1000) and no threat: the shipped ruleset scores a
    /// recruitment order at <c>recruitBaseScore · 1000/1000 = 1000</c> and a fortification order at
    /// <c>fortifyBaseScore · (2000-1000)/1000 = 800</c>, so recruiting wins. Halving
    /// <c>recruitBaseScore</c> to 500 drops the recruit score to 500, below fortify's unchanged 800, so
    /// fortifying wins instead — the same state, the same personality, only the ruleset's own weight
    /// changed, and the highest-scoring candidate (what <see cref="AiTurn"/>'s own selection would issue)
    /// flips from one action kind to the other.
    /// </remarks>
    [Fact]
    public void Lowering_the_recruit_base_score_flips_the_economy_phases_highest_scoring_candidate()
    {
        var state = OneCityFullAmbitionState();

        var defaultRuleset = AiScriptedStates.Ruleset;
        Assert.Equal(1000, defaultRuleset.Ai.RecruitBaseScore);
        Assert.Equal(800, defaultRuleset.Ai.FortifyBaseScore);

        var loweredRuleset = defaultRuleset with
        {
            Ai = defaultRuleset.Ai with { RecruitBaseScore = 500 },
        };

        var defaultChoice = HighestScoringKind(Propose(state, defaultRuleset));
        var loweredChoice = HighestScoringKind(Propose(state, loweredRuleset));

        Assert.Equal("recruit", defaultChoice);
        Assert.Equal("fortify", loweredChoice);
    }

    private static List<AiCandidate> Propose(GameState state, Ruleset ruleset)
    {
        var view = new AiView(state, ruleset, AiScriptedStates.World, Acting);
        var candidates = new List<AiCandidate>();
        AiEconomyPhase.Propose(view, AiPersonalityProfile.For(state.NationById(Acting)!, ruleset), candidates);
        return candidates;
    }

    private static string HighestScoringKind(List<AiCandidate> candidates)
    {
        AiCandidate? best = null;
        foreach (var candidate in candidates)
        {
            if (best is null || candidate.Score > best.Score)
            {
                best = candidate;
            }
        }

        Assert.NotNull(best);
        return best!.Kind;
    }

    /// <summary>One AI-owned city, unthreatened, at full expansion drive: a plain recruit-vs-fortify contest.</summary>
    private static GameState OneCityFullAmbitionState()
    {
        var personality = new AiPersonality(Aggression: 0.5, ExpansionDrive: 1.0, LoyaltyToAlliances: 0.5);

        var nations = new[]
        {
            AiScriptedStates.AiNation(Acting, personality, treasury: Treasury, capitalCityId: "home"),
            AiScriptedStates.AiNation(Other, AiScriptedStates.DefaultPersonality, treasury: Treasury),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "home", "Home", 2, 2, Acting, Acting, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "theirs", "Theirs", 7, 5, Other, Other, loyalty: 80, fortificationCode: 20,
                populationThousands: 10, maxPopulationThousands: 100, tribute: 10),
        };

        // Far from "home" and few enough troops that HostileArmyAdjacent.IsThreatened never fires --
        // this test is about the ambition half of the sentence, not the threat bonus.
        var distant = CaptureFixtures.Army(
            "distant", Other, 7, 5, morale: 60, CaptureFixtures.Unit("light_infantry", 5000));

        var state = BattleCommandTestbed.StateWith(nations, cities, new[] { distant });
        return AiScriptedStates.WithActiveSeat(BattleCommandTestbed.AtWar(state, Acting, Other), Acting);
    }

    private static JsonObject LoadToyRulesetNode() =>
        JsonNode.Parse(File.ReadAllText(ModelTestPaths.ToyRulesetFile))!.AsObject();
}
