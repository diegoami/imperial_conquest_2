using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Economy;
using IC2.Engine.Tests.Export;
using Xunit;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// T146 Done-when 3: the New Game draw — one <c>NextInt(12)</c> per pool-backed nation, in the world's
/// nation order, from its own <c>new-game.leaders</c> stream; nothing drawn on a world without a pool;
/// and through <see cref="GameSession"/> the draw is seed-stable and survives a save/resume without a
/// second draw.
/// </summary>
public sealed class NewGameLeadersTests
{
    private static (World World, Ruleset Ruleset, Scenario Scenario) Classical()
    {
        var world = GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile);
        var ruleset = GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.RulesetFile);
        var scenario = GameDataLoader.LoadFile<Scenario>(ExportedDataPaths.ScenarioFile);
        return (world, ruleset, scenario);
    }

    [Fact]
    public void Sixteen_scripted_draws_write_each_nations_pool_entry_in_world_order()
    {
        var (world, ruleset, scenario) = Classical();
        var state = GameStateFactory.CreateInitial(world, ruleset, scenario);

        var draws = Enumerable.Range(0, 12).Concat(new[] { 0, 1, 2, 3 }).ToArray();
        var rng = new ScriptedRng(
            nextIntDraws: draws,
            expectedNextIntBounds: Enumerable.Repeat(12, 16).ToArray());

        var after = NewGameLeaders.Apply(state, world, rng);
        rng.AssertAllDrawsConsumed();

        Assert.Equal(16, world.Nations.Count);
        for (var i = 0; i < world.Nations.Count; i++)
        {
            var pool = world.Nations[i].LeaderNames!;
            Assert.Equal(pool[draws[i]], after.Nations[i].LeaderName);
        }
    }

    [Fact]
    public void A_world_without_a_pool_draws_nothing_and_leaves_the_leaders_unchanged()
    {
        var toy = CoreTestbed.Toy;
        var before = CoreTestbed.InitialState();

        var after = NewGameLeaders.Apply(before, toy.World, new ScriptedRng());

        Assert.Equal(before.Nations, after.Nations);
    }

    [Fact]
    public void A_New_Game_session_draws_every_leader_from_its_pool_and_none_is_the_placeholder()
    {
        var (world, ruleset, scenario) = Classical();
        var session = new GameSession(world, ruleset, scenario, seedOverride: 12345);

        foreach (var nation in session.State.Nations)
        {
            var pool = world.NationById(nation.Id)!.LeaderNames!;
            Assert.Contains(nation.LeaderName, pool);
            Assert.DoesNotContain("unassigned", nation.LeaderName, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_sixteen_leaders()
    {
        var (world, ruleset, scenario) = Classical();

        var first = new GameSession(world, ruleset, scenario, seedOverride: 12345);
        var second = new GameSession(world, ruleset, scenario, seedOverride: 12345);

        Assert.Equal(
            first.State.Nations.Select(n => n.LeaderName).ToArray(),
            second.State.Nations.Select(n => n.LeaderName).ToArray());
    }

    [Fact]
    public void Two_named_seeds_give_different_leaders()
    {
        var (world, ruleset, scenario) = Classical();

        var first = new GameSession(world, ruleset, scenario, seedOverride: 12345);
        var second = new GameSession(world, ruleset, scenario, seedOverride: 999);

        Assert.NotEqual(
            first.State.Nations.Select(n => n.LeaderName).ToArray(),
            second.State.Nations.Select(n => n.LeaderName).ToArray());
    }

    [Fact]
    public void A_save_and_resume_keeps_the_leaders_and_draws_nothing()
    {
        var (world, ruleset, scenario) = Classical();
        var session = new GameSession(world, ruleset, scenario, seedOverride: 12345);
        var leaders = session.State.Nations.Select(n => n.LeaderName).ToArray();

        var save = new SaveGame(
            GameDataSchema.CurrentVersion, "t146-save", "T146", scenario.Id, world.Id, ruleset.Id, session.State);
        var loaded = SaveManager.Load("t146-save.json", SaveManager.Serialize(save), world, ruleset);
        var resumed = new GameSession(world, ruleset, scenario, loaded);

        Assert.Equal(leaders, resumed.State.Nations.Select(n => n.LeaderName).ToArray());
    }
}
