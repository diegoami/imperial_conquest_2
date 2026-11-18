using IC2.Engine.MapGen;
using IC2.Engine.MapGen.Styles;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.MapGen;

/// <summary>
/// T294e (#1846): a generated world (and the scenario a generated game started from) rides inside
/// its save, so a save reloads without the generator, without the shipped data set and without any
/// preview cache — the world comes back exactly as written, and its provenance stays informational.
/// </summary>
public sealed class SaveEmbeddingTests
{
    private static readonly MapSize Size = new(280, 180);

    private static (World World, Scenario Scenario, GameState State) NewGeneratedGame(
        GameDataRepository repository, string scenarioId, string worldId)
    {
        var generated = MapGenerator.Generate("t294e-test", new MediterraneanStyle(), 5, Size,
            worldId, "Generated round trip");
        var world = worldId == generated.World.Id ? generated.World : generated.World with { Id = worldId };
        var ruleset = repository.RulesetById("improved")
                      ?? throw new InvalidOperationException("the shipped data set has no improved ruleset");
        var scenario = new Scenario(
            GameDataSchema.CurrentVersion,
            scenarioId,
            "Generated round trip",
            world.Id,
            ruleset.Id,
            new ValueList<Seat>(world.Nations.Select(n => new Seat(n.Id, SeatControl.Ai)).ToList()),
            new VictoryCondition(VictoryConditionType.Domination),
            TurnLimit: 200,
            BlindHotseat: false,
            RandomSeed: 1234);
        var state = GameStateFactory.CreateInitial(world, ruleset, scenario);
        return (world, scenario, state);
    }

    private static SaveGame BuildSave(World world, Scenario scenario, GameState state, bool embed) => new(
        GameDataSchema.CurrentVersion,
        "save-roundtrip",
        "Round trip",
        scenario.Id,
        world.Id,
        scenario.RulesetId,
        state,
        EmbeddedWorld: embed ? world : null,
        EmbeddedScenario: embed ? scenario : null);

    [Fact]
    public void A_generated_save_resolves_its_world_and_scenario_from_itself()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var (world, scenario, state) = NewGeneratedGame(repository, "generated-roundtrip", "generated-roundtrip");
        var save = BuildSave(world, scenario, state, embed: true);

        var path = Path.Combine(Path.GetTempPath(), "ic2-t294e-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SaveManager.WriteFile(path, save);
            var resolved = SaveManager.LoadFile(path, repository);

            // The generated ids ship in no data directory, so both records can only come from the save.
            Assert.Null(repository.WorldById(world.Id));
            Assert.Null(repository.ScenarioById(scenario.Id));
            Assert.Equal(world, resolved.World);
            Assert.Equal(scenario, resolved.Scenario);
            Assert.Equal("improved", resolved.Ruleset.Id);
            Assert.Equal(save, resolved.Save);
            Assert.Equal(SaveManager.Serialize(save), SaveManager.Serialize(resolved.Save));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_embedded_world_that_disagrees_with_the_save_is_refused()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var (world, scenario, state) = NewGeneratedGame(repository, "generated-mismatch", "generated-mismatch");
        var save = BuildSave(world, scenario, state, embed: true) with { EmbeddedWorld = world with { Id = "other-world" } };

        var failure = Assert.Throws<MalformedGameDataException>(() => SaveManager.Load("save.json", SaveManager.Serialize(save)));
        Assert.Contains("other-world", failure.Message);
    }

    [Fact]
    public void An_embedded_scenario_referencing_another_world_or_ruleset_is_refused()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var (world, scenario, state) = NewGeneratedGame(repository, "generated-mismatch-2", "generated-mismatch-2");
        var save = BuildSave(world, scenario, state, embed: true) with { EmbeddedScenario = scenario with { WorldId = "elsewhere" } };

        var failure = Assert.Throws<MalformedGameDataException>(() => SaveManager.Load("save.json", SaveManager.Serialize(save)));
        Assert.Contains("embedded scenario", failure.Message);
    }

    [Fact]
    public void A_save_naming_an_unknown_world_without_embedding_cannot_resolve()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var (world, scenario, state) = NewGeneratedGame(repository, "generated-orphan", "missing-world");
        var save = BuildSave(world, scenario, state, embed: false);

        var path = Path.Combine(Path.GetTempPath(), "ic2-t294e-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SaveManager.WriteFile(path, save);
            var failure = Assert.Throws<UnresolvedReferenceException>(() => SaveManager.LoadFile(path, repository));
            Assert.Equal("world", failure.Kind);
            Assert.Equal("missing-world", failure.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_shipped_save_still_resolves_from_the_repository_without_embedding()
    {
        // The repository-first rule keeps shipped campaigns exactly on their old path.
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var toy = repository.Resolve("toy-3city");
        var state = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);
        var save = BuildSave(toy.World, toy.Scenario, state, embed: false);

        var path = Path.Combine(Path.GetTempPath(), "ic2-t294e-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SaveManager.WriteFile(path, save);
            var resolved = SaveManager.LoadFile(path, repository);
            Assert.Same(toy.World, resolved.World);
            Assert.Same(toy.Scenario, resolved.Scenario);
            Assert.Null(resolved.Save.EmbeddedWorld);
            Assert.Null(resolved.Save.EmbeddedScenario);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
