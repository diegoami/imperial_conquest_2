using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// T85 Done-when 2: loading a world rejects, with a typed error, an asymmetric
/// <see cref="World.StartingNeighbours"/> pair, a nation that neighbours itself, an unknown nation id,
/// and a duplicated entry — one test per shape, each through <see cref="GameDataValidation.Validate"/>,
/// the exact call <see cref="GameDataLoader.Load{T}(string,string)"/> itself makes after
/// deserializing, mirroring <see cref="WorldStartingDataValidationTests"/>'s own pattern for
/// <see cref="World.StartingRelations"/>.
/// </summary>
public class WorldStartingNeighboursValidationTests
{
    private static World ToyWorld() => GameDataLoader.LoadFile<World>(ModelTestPaths.ToyWorldFile);

    private static string[] ToyNationIds() => ToyWorld().Nations.Select(n => n.Id).ToArray();

    private const string DocumentPath = "world-under-test.json (in-memory)";

    private static NationNeighbours Entry(string nationId, params string[] neighbourIds) =>
        new(nationId, ValueList.Of(neighbourIds));

    /// <summary>A world with no starting neighbours at all validates as a no-op.</summary>
    [Fact]
    public void A_world_with_no_starting_neighbours_validates_as_a_no_op()
    {
        var world = ToyWorld();
        Assert.Null(world.StartingNeighbours);

        GameDataValidation.Validate(DocumentPath, world);
    }

    /// <summary>A well-formed, symmetric adjacency list over the world's own two nations passes.</summary>
    [Fact]
    public void A_well_formed_symmetric_adjacency_list_passes()
    {
        var ids = ToyNationIds();
        var world = ToyWorld() with
        {
            StartingNeighbours = ValueList.Of(
                Entry(ids[0], ids[1]),
                Entry(ids[1], ids[0])),
        };

        GameDataValidation.Validate(DocumentPath, world);
    }

    [Fact]
    public void An_asymmetric_pair_is_rejected()
    {
        var ids = ToyNationIds();
        // ids[0] lists ids[1] as a neighbour, but ids[1] lists nobody back.
        var world = ToyWorld() with
        {
            StartingNeighbours = ValueList.Of(
                Entry(ids[0], ids[1]),
                Entry(ids[1])),
        };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("symmetric", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_nation_that_neighbours_itself_is_rejected()
    {
        var ids = ToyNationIds();
        var world = ToyWorld() with
        {
            StartingNeighbours = ValueList.Of(Entry(ids[0], ids[0])),
        };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("itself", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An unknown nation id is a typed reference failure, not a generic malformed one — the
    /// same shape <see cref="WorldStartingDataValidationTests.A_nation_id_not_in_the_world_is_rejected"/>
    /// asserts for <see cref="World.StartingRelations"/>.</summary>
    [Fact]
    public void An_unknown_nation_id_is_rejected()
    {
        var world = ToyWorld() with
        {
            StartingNeighbours = ValueList.Of(Entry("not-a-real-nation", "also-not-real")),
        };

        var ex = Assert.Throws<UnresolvedReferenceException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Equal("nation", ex.Kind);
        Assert.Equal("not-a-real-nation", ex.Id);
    }

    /// <summary>An unknown neighbour id (the entry's own nation is real, but a listed neighbour is not) is
    /// rejected the same way.</summary>
    [Fact]
    public void An_unknown_neighbour_id_is_rejected()
    {
        var ids = ToyNationIds();
        var world = ToyWorld() with
        {
            StartingNeighbours = ValueList.Of(Entry(ids[0], "not-a-real-nation")),
        };

        var ex = Assert.Throws<UnresolvedReferenceException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Equal("nation", ex.Kind);
        Assert.Equal("not-a-real-nation", ex.Id);
    }

    [Fact]
    public void A_duplicated_entry_is_rejected()
    {
        var ids = ToyNationIds();
        var world = ToyWorld() with
        {
            StartingNeighbours = ValueList.Of(
                Entry(ids[0], ids[1]),
                Entry(ids[1], ids[0]),
                Entry(ids[0]) /* ids[0] again, a second entry for the same nation */),
        };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("more than one entry", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A duplicated neighbour id within one entry's own list is rejected too.</summary>
    [Fact]
    public void A_duplicated_neighbour_id_within_one_entry_is_rejected()
    {
        var ids = ToyNationIds();
        var world = ToyWorld() with
        {
            StartingNeighbours = ValueList.Of(
                Entry(ids[0], ids[1], ids[1]),
                Entry(ids[1], ids[0])),
        };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("twice", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
