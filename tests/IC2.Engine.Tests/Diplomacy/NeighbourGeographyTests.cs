using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Export;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// T82 (#359, bug #357). Checks <see cref="NeighbourGeography"/>'s derivation against the three nations
/// <c>decompiled-ai-offers-to-human-seats.md</c> §1a reports the original's own neighbour mask for --
/// see that type's own remarks for the search that produced the method and the two known, unresolved
/// spurious pairs (Rome ↔ Greece; Thracia ↔ Bithynia/Seleucid).
/// </summary>
public sealed class NeighbourGeographyTests
{
    private static readonly Lazy<World> LazyClassicalWorld = new(
        () => GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile));

    private static World ClassicalWorld => LazyClassicalWorld.Value;

    // ---- Full recall: every one of the report's nine confirmed pairs is reproduced ----

    [Theory]
    [InlineData("rome", "carthage")]
    [InlineData("rome", "gaul")]
    [InlineData("rome", "illyria")]
    [InlineData("carthage", "rome")]
    [InlineData("carthage", "ptolemaic")]
    [InlineData("carthage", "numidia")]
    [InlineData("carthage", "celtiberia")]
    [InlineData("thracia", "macedonia")]
    [InlineData("thracia", "dacia")]
    public void EveryConfirmedPairFromTheReportIsReproduced(string a, string b)
    {
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, a, b), $"'{a}' should border '{b}'.");
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, b, a), "The relation must be symmetric.");
    }

    // ---- Symmetry holds generally, not just for the confirmed pairs ----

    [Fact]
    public void TheRelationIsSymmetricForEveryNationPair()
    {
        var ids = ClassicalWorld.Nations.Select(n => n.Id).ToList();
        foreach (var a in ids)
        {
            foreach (var b in ids)
            {
                Assert.Equal(
                    NeighbourGeography.AreNeighbours(ClassicalWorld, a, b),
                    NeighbourGeography.AreNeighbours(ClassicalWorld, b, a));
            }
        }
    }

    [Fact]
    public void ANationNeverBordersItself()
    {
        Assert.False(NeighbourGeography.AreNeighbours(ClassicalWorld, "rome", "rome"));
    }

    // ---- NeighboursOf agrees with AreNeighbours ----

    [Fact]
    public void NeighboursOfAgreesWithAreNeighbours()
    {
        var romeNeighbours = NeighbourGeography.NeighboursOf(ClassicalWorld, "rome");
        Assert.Contains("carthage", romeNeighbours);
        Assert.Contains("gaul", romeNeighbours);
        Assert.Contains("illyria", romeNeighbours);
        Assert.DoesNotContain("rome", romeNeighbours);

        foreach (var other in romeNeighbours)
        {
            Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "rome", other));
        }
    }

    // ---- The two known, unresolved spurious pairs (documented, not asserted away) ----

    /// <summary>
    /// Rome ↔ Greece and Thracia ↔ {Bithynia, Seleucid} are not in the report's own three checked rows,
    /// but this derivation cannot rule them out either (see <see cref="NeighbourGeography"/>'s remarks,
    /// search step 4). Pinned so a future tightening of the method is a visible, deliberate change here,
    /// not a silent one.
    /// </summary>
    [Fact]
    public void TheTwoKnownSpuriousPairsAreStillPresent()
    {
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "rome", "greece"));
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "thracia", "bithynia"));
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "thracia", "seleucid"));
    }

    // ---- A thin sliver from Voronoi corner geometry does not count as a border ----

    [Fact]
    public void RomeDoesNotBorderPtolemaic()
    {
        // A 3-tile Voronoi sliver in the original whole-map derivation (this type's remarks, search
        // step 3 vs step 4) -- below MinimumBorderTiles, and Ptolemaic is not in Rome's confirmed row.
        Assert.False(NeighbourGeography.AreNeighbours(ClassicalWorld, "rome", "ptolemaic"));
    }

    // ---- A tiny hand-built world with two adjacent, single-city nations still borders ----

    [Fact]
    public void TwoAdjacentSingleCityNationsBorderOnATinyWorld()
    {
        var world = TinyWorld(("a", 0, 0), ("b", 1, 0));

        Assert.True(NeighbourGeography.AreNeighbours(world, "a", "b"));
    }

    /// <summary>
    /// Two single-city nations always split a whole map into two Voronoi half-planes and so always
    /// border each other, however far apart their cities are -- a third nation's territory sitting
    /// between two others is what actually severs a border. <c>a</c> and <c>c</c> sit at opposite edges
    /// of the world with <c>b</c> directly between them; <c>b</c>'s own territory should reach both
    /// edges, leaving <c>a</c> and <c>c</c> without a shared boundary.
    /// </summary>
    [Fact]
    public void TwoNationsSeparatedByAThirdDoNotBorderOnATinyWorld()
    {
        var world = TinyWorld(("a", 0, 10), ("b", 10, 10), ("c", 19, 10));

        Assert.True(NeighbourGeography.AreNeighbours(world, "a", "b"));
        Assert.True(NeighbourGeography.AreNeighbours(world, "b", "c"));
        Assert.False(NeighbourGeography.AreNeighbours(world, "a", "c"));
    }

    private static World TinyWorld(params (string NationId, int X, int Y)[] cities)
    {
        var nations = cities
            .Select(c => new NationDefinition(
                c.NationId, c.NationId, "#000000", c.NationId, c.NationId, 0, 500, 10000, 1000, 10, 0, 100))
            .ToArray();
        var cityDefs = cities
            .Select(c => new CityDefinition(
                c.NationId, c.NationId, c.X, c.Y, c.NationId, c.NationId, 80, 0, 50, 10, 10, 0,
                ValueList<UnitSlot>.Empty))
            .ToArray();

        return new World(
            GameDataSchema.CurrentVersion,
            "tiny-test-world",
            "Tiny test world",
            20,
            20,
            new TerrainGrid(TerrainEncoding.RunLength, Runs: ValueList<TerrainRun>.Of(new TerrainRun(2, 400))),
            ValueList<TileType>.Of(new TileType("plain", 2, "Plain", true, false)),
            ValueList<NationDefinition>.Of(nations),
            ValueList<CityDefinition>.Of(cityDefs),
            ValueList<StartingArmy>.Empty,
            ValueList<StartingFleet>.Empty,
            ValueList<string>.Of(cities.Select(c => c.NationId).ToArray()));
    }
}
