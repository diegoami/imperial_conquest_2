using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using DiplomacyFixtures = IC2.Engine.Tests.Diplomacy.DiplomacyTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T82 (#359, bug #357) Done-when 4: "The AI's war targeting excludes any ally that holds a city. Its
/// units attack only nations it is already at war with, so an attack is never an implicit declaration.
/// A test gives an AI an allied, city-holding neighbour next to an attackable army and shows no
/// declaration and no attack."
/// </summary>
/// <remarks>
/// <c>me</c>'s own ally is the "ally" <see cref="AiOwnDiplomacyRule.IsProtected"/> checks for: the
/// formula is <c>cities[candidate] + cities[a] &gt; cities[reference]</c> with no special case excluding
/// <c>a == reference</c> (the report's own pseudocode has none either), so a neighbour allied directly
/// to the acting AI is protected by that same alliance as long as it holds at least one city --
/// <c>cities[ally] + cities[me] &gt; cities[me]</c> is just <c>cities[ally] &gt; 0</c>. This is the
/// ordinary case Done-when 4 asks for, not an edge case needing separate code.
/// </remarks>
public sealed class AiNoWarOnAnAllyTests
{
    private const string Acting = "me";
    private const string Ally = "ally";

    [Fact]
    public void AnAlliedCityHoldingNeighbourWithAnAttackableArmy_IsNeverDeclaredWarOnOrAttacked()
    {
        var world = TwoNationWorld(Acting, Ally);
        var ruleset = DiplomacyFixtures.Ruleset;

        var nations = new[]
        {
            DiplomacyFixtures.Nation(Acting, "Me", wealth: 40000, unity: 500),
            // Weak enough that, if it were not protected, it would clear the war-target ratio gate --
            // and its army below is weak enough to clear the attack-ratio gate too -- so the absence of
            // both candidates is the protection/attack-filter working, not a strength shortfall.
            DiplomacyFixtures.Nation(Ally, "Ally", wealth: 1000, unity: 100),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "me-city", "Me City", 0, 0, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            // The ally "holds a city" -- Done-when 4's own wording, and IsProtected's own condition.
            CaptureFixtures.City(
                "ally-city", "Ally City", 1, 1, Ally, Ally, loyalty: 90, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
        };

        var armies = new[]
        {
            BattleCommandTestbedArmy("me-army", Acting, 0, 0, troops: 400_000),
            // Adjacent to me-army, and weak -- an attack on it would clear the ratio gate if attacking an
            // ally were ever offered as a candidate at all.
            BattleCommandTestbedArmy("ally-army", Ally, 0, 1, troops: 1_000),
        };

        var state = BattleCommandTestbed.StateWith(nations, cities, armies);
        state = state with
        {
            TurnOrder = ValueList.Of(Acting, Ally),
            Relations = state.Relations.WithRelation(Acting, Ally, ruleset.Diplomacy.StateCodes.Alliance),
        };
        state = AiScriptedStates.WithActiveSeat(state, Acting);

        var view = new AiView(state, ruleset, world, Acting);
        var candidates = new List<AiCandidate>();
        AiMilitaryPhase.Propose(
            view,
            AiPersonalityProfile.For(state.NationById(Acting)!),
            SplitMix64Rng.ForStream(1, "ai.turn"),
            Array.Empty<string>(),
            candidates);

        Assert.DoesNotContain(candidates, c => c.Kind == "declare-war");
        Assert.DoesNotContain(candidates, c => c.Kind == "attack-army");

        // The rule-level claim behind it: the ally never even qualifies as a war target.
        Assert.Null(AiOwnDiplomacyRule.BestWarTarget(state, ruleset, world, Acting));
        Assert.True(AiOwnDiplomacyRule.IsProtected(state, ruleset, Ally, Acting));
    }

    private static ArmyState BattleCommandTestbedArmy(string id, string nation, int x, int y, int troops) =>
        CaptureFixtures.Army(id, nation, x, y, morale: 60, CaptureFixtures.Unit("archers", troops)) with { Moves = 5 };

    /// <summary>A small world giving exactly two nations a real, wide (14-tile) shared border.</summary>
    private static World TwoNationWorld(string a, string b)
    {
        const int Width = 20;
        const int Height = 14;
        var ids = new[] { a, b };
        var nations = ids
            .Select(id => new NationDefinition(id, id, "#000000", id, id, 0, 500, 10000, 1000, 10, 0, 100))
            .ToArray();
        var cities = ids
            .Select((id, band) => new CityDefinition(
                id, id, (band * (Width / 2)) + (Width / 4), Height / 2, id, id,
                80, 0, 50, 10, 10, 0, ValueList<UnitSlot>.Empty))
            .ToArray();

        return new World(
            GameDataSchema.CurrentVersion,
            "no-war-on-ally-test-world",
            "AiNoWarOnAnAllyTests world",
            Width,
            Height,
            new TerrainGrid(TerrainEncoding.RunLength, Runs: ValueList<TerrainRun>.Of(new TerrainRun(2, Width * Height))),
            ValueList<TileType>.Of(new TileType("plain", 2, "Plain", true, false)),
            ValueList<NationDefinition>.Of(nations),
            ValueList<CityDefinition>.Of(cities),
            ValueList<StartingArmy>.Empty,
            ValueList<StartingFleet>.Empty,
            ValueList<string>.Of(ids));
    }
}
