using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using DiplomacyFixtures = IC2.Engine.Tests.Diplomacy.DiplomacyTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T82 (#359, bug #357) Done-when 3, "War": "declared on <c>Random(10) == 0</c>" -- a gate, not a
/// certainty. Every other test that reaches <see cref="AiMilitaryPhase.ProposeOwnWarDeclaration"/>
/// (<c>AiOwnDiplomacyRuleTests.BestWarTarget</c>'s own tests, <c>AiCommandLegalityTests</c>' seed search)
/// proves the roll <em>can</em> hit; this proves it does not always -- deleting the
/// <c>rng.ForStream(...).NextChance(1, denominator)</c> check (or replacing it with an unconditional
/// <see langword="true"/>) would make every one of the seeds below declare war, and this test would
/// catch that.
/// </summary>
public sealed class AiWarDeclarationRollTests
{
    private const string Acting = "me";
    private const string Target = "them";

    [Fact]
    public void TheRollDoesNotAlwaysHit_AcrossManySeedsWithAnEligibleTarget()
    {
        var world = TwoNationWorld(Acting, Target);
        var ruleset = DiplomacyFixtures.Ruleset;

        var nations = new[]
        {
            // P(me) = (40000/20000)*(500/100) = 10; P(them) = (1000/20000)*(100/100) = 0 (int division);
            // ratio = 8*10/max(1,0) = 80, comfortably over the base -- an eligible target every seed.
            DiplomacyFixtures.Nation(Acting, "Me", wealth: 40000, unity: 500),
            DiplomacyFixtures.Nation(Target, "Them", wealth: 1000, unity: 100),
        };

        var cities = new[]
        {
            CaptureFixtures.City(
                "me-city", "Me City", 0, 0, Acting, Acting, loyalty: 90, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
            CaptureFixtures.City(
                "them-city", "Them City", 1, 1, Target, Target, loyalty: 90, fortificationCode: 100,
                populationThousands: 100, maxPopulationThousands: 100, tribute: 10),
        };

        var baseState = BattleCommandTestbed.StateWith(nations, cities);
        baseState = baseState with { TurnOrder = ValueList.Of(Acting, Target) };
        baseState = AiScriptedStates.WithActiveSeat(baseState, Acting);

        var hits = 0;
        var misses = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            // Each seed is its own turn (TurnIndex varies the roll's own stream key), so a fresh state
            // with that TurnIndex reproduces exactly what a real turn `seed` would see.
            var state = baseState with { Calendar = baseState.Calendar with { TurnIndex = (int)seed } };
            var view = new AiView(state, ruleset, world, Acting);
            var candidates = new List<AiCandidate>();

            AiMilitaryPhase.Propose(
                view,
                AiPersonalityProfile.For(state.NationById(Acting)!),
                SplitMix64Rng.ForStream(seed, "ai.turn"),
                Array.Empty<string>(),
                candidates);

            if (candidates.Any(c => c.Kind == "declare-war"))
            {
                hits++;
            }
            else
            {
                misses++;
            }
        }

        Assert.True(hits > 0, "expected at least one of 100 seeds to hit Random(10)");
        Assert.True(misses > 0, "expected at least one of 100 seeds to miss Random(10) -- the roll must gate the declaration, not wave it through");
    }

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
            "war-declaration-roll-test-world",
            "AiWarDeclarationRollTests world",
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
