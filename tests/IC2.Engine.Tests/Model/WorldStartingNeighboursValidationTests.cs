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

    /// <summary>
    /// A minimal, valid three-nation world (no cities, no capitals) -- the toy world's own two nations
    /// cannot isolate "an omitted nation" from "an empty row" (review round 2, B3): with only two
    /// nations, any entry that survives symmetry with the other one omitted must itself be empty, so the
    /// two edges collapse into the same scenario there. A third nation lets one test give a non-empty,
    /// reciprocated row to two nations while omitting the third outright, with no entry anywhere empty.
    /// </summary>
    private static World ThreeNationWorld()
    {
        var nations = new[] { "a", "b", "c" }
            .Select(id => new NationDefinition(id, id, "#000000", id, null, 0, 500, 10000, 1000, 10, 0, 100))
            .ToArray();

        return new World(
            GameDataSchema.CurrentVersion,
            "three-nation-test-world",
            "Three-nation test world",
            3,
            1,
            new TerrainGrid(TerrainEncoding.RunLength, Runs: ValueList<TerrainRun>.Of(new TerrainRun(2, 3))),
            ValueList<TileType>.Of(new TileType("plain", 2, "Plain", true, false)),
            ValueList<NationDefinition>.Of(nations),
            ValueList<CityDefinition>.Empty,
            ValueList<StartingArmy>.Empty,
            ValueList<StartingFleet>.Empty,
            ValueList.Of("a", "b", "c"));
    }

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

    /// <summary>
    /// Review round 2, B3: <see cref="World.StartingNeighbours"/>' own doc comment claims validation
    /// "accepts an empty <see cref="NationNeighbours.NeighbourIds"/> row" -- nothing visited that edge
    /// before this test (the reviewer's mutation E1, making <c>ValidateStartingNeighboursShape</c> throw
    /// on an empty row, left all 2902 tests green). Both nations get an entry here, both with an empty
    /// list -- "this nation borders nobody" is a valid, vacuously symmetric statement on its own.
    /// </summary>
    [Fact]
    public void An_empty_neighbour_row_is_accepted()
    {
        var ids = ToyNationIds();
        var world = ToyWorld() with
        {
            StartingNeighbours = ValueList.Of(Entry(ids[0]), Entry(ids[1])),
        };

        GameDataValidation.Validate(DocumentPath, world);
    }

    /// <summary>
    /// Review round 2, B3: the same doc comment claims validation "accepts a nation omitted entirely" --
    /// the reviewer's mutation E2, throwing whenever the entry count is short of the world's own nation
    /// count, also left all 2902 tests green. Uses <see cref="ThreeNationWorld"/>, not the toy world:
    /// with only two nations, an entry that survives symmetry while the other is omitted collapses into
    /// <see cref="An_empty_neighbour_row_is_accepted"/>'s own shape (its one entry would have to be
    /// empty too). Here <c>a</c> and <c>b</c> reciprocate a genuinely non-empty row, and <c>c</c> is
    /// omitted with no entry at all -- distinct from the empty-row test above, and isolated from it.
    /// </summary>
    [Fact]
    public void A_nation_omitted_entirely_is_accepted()
    {
        var world = ThreeNationWorld() with
        {
            StartingNeighbours = ValueList.Of(Entry("a", "b"), Entry("b", "a")),
            // "c" gets no entry at all; neither "a" nor "b" names an empty row.
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
