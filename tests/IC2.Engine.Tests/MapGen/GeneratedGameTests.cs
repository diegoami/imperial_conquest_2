using IC2.Engine.MapGen;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.MapGen;

/// <summary>
/// T294f (#1847): the New Game flow's generated game â€” three candidate previews per (style, seed),
/// the seeded scenario a generated game starts from, and a session whose saves carry the generated
/// world and scenario so a reload never needs the generator, the data set or a preview cache.
/// </summary>
public sealed class GeneratedGameTests
{
    [Fact]
    public void Candidates_are_three_deterministic_distinct_worlds()
    {
        var a = GeneratedGame.Previews("mediterranean", 42);
        var b = GeneratedGame.Previews("mediterranean", 42);
        Assert.Equal(a.Count, b.Count);
        Assert.Equal(3, a.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(a[i].World, b[i].World);
            Assert.Equal(42ul + (ulong)i, a[i].Seed);
            Assert.Equal(GeneratedGame.ReleaseSize.Width, a[i].World.Width);
            Assert.Equal(GeneratedGame.ReleaseSize.Height, a[i].World.Height);
            Assert.Equal($"random-mediterranean-{42ul + (ulong)i}", a[i].World.Id);
        }

        Assert.NotEqual(a[0].World, a[1].World);
        Assert.NotEqual(a[1].World, a[2].World);
    }

    [Fact]
    public void An_unknown_style_is_refused()
    {
        Assert.Throws<ArgumentException>(() => GeneratedGame.Previews("atlantis", 1));
    }

    [Fact]
    public void The_generated_scenario_seats_every_nation_for_total_conquest()
    {
        var world = GeneratedGame.Previews("china", 7)[0].World;
        var scenario = GeneratedGame.ScenarioFor(world, "improved", 7);
        Assert.Equal(world.Id, scenario.Id);
        Assert.Equal(world.Id, scenario.WorldId);
        Assert.Equal("improved", scenario.RulesetId);
        Assert.Equal(7ul, scenario.RandomSeed);
        Assert.Equal(VictoryConditionType.TotalConquest, scenario.Victory.Type);
        Assert.Null(scenario.TurnLimit);
        Assert.Equal(
            world.Nations.Select(n => n.Id).Order(),
            scenario.Seats.Select(s => s.Nation).Order());
        Assert.All(scenario.Seats, s => Assert.Equal(SeatControl.Ai, s.Control));
    }

    [Fact]
    public void A_generated_session_saves_embed_the_world_and_scenario_and_reload_from_themselves()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var candidate = GeneratedGame.Previews("mediterranean", 11)[1];
        var scenario = GeneratedGame.ScenarioFor(candidate.World, "improved", candidate.Seed);
        var session = new GameSession(candidate.World, repository.RulesetById("improved")!, scenario,
            humanSeatNationId: scenario.Seats[0].Nation,
            embeddedWorld: candidate.World,
            embeddedScenario: scenario);

        var path = Path.Combine(Path.GetTempPath(), "ic2-t294f-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            session.Submit($"save {path}");
            var resolved = SaveManager.LoadFile(path, repository);
            Assert.Equal(candidate.World, resolved.World);
            Assert.Equal(scenario, resolved.Scenario);
            Assert.Equal("improved", resolved.Ruleset.Id);

            // The resumed session keeps riding the embedded records: its next save embeds them again.
            var resumed = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, resolved.Save);
            Assert.Equal(candidate.World, resumed.EmbeddedWorld);
            Assert.Equal(scenario, resumed.EmbeddedScenario);
            var path2 = path + ".again.json";
            resumed.Submit($"save {path2}");
            var resolvedAgain = SaveManager.LoadFile(path2, repository);
            Assert.Equal(candidate.World, resolvedAgain.World);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".again.json")) File.Delete(path + ".again.json");
        }
    }

    [Fact]
    public void A_shipped_session_saves_nothing_embedded()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var toy = repository.Resolve("toy-3city");
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario, humanSeatNationId: toy.Scenario.Seats[0].Nation);
        Assert.Null(session.EmbeddedWorld);
        Assert.Null(session.EmbeddedScenario);

        var path = Path.Combine(Path.GetTempPath(), "ic2-t294f-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            session.Submit($"save {path}");
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("embeddedWorld", json, StringComparison.Ordinal);
            Assert.DoesNotContain("embeddedScenario", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
