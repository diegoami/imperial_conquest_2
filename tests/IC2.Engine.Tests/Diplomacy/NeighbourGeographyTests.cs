using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Export;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// T82 (#359, bug #357), rework round 1 (N1/N2). Checks <see cref="NeighbourGeography"/>'s derivation
/// against the DAT's own neighbour mask, transcribed as data from <c>dat-neighbour-mask.md</c> §2 -- not
/// just the three rows (Rome, Carthage, Thracia) the earlier <c>decompiled-ai-offers-to-human-seats.md</c>
/// §1a sample could confirm, but all sixteen. See <see cref="NeighbourGeography"/>'s own remarks for the
/// search that produced the derivation method and the full account of the six pairs it gets wrong.
/// </summary>
public sealed class NeighbourGeographyTests
{
    private static readonly Lazy<World> LazyClassicalWorld = new(
        () => GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile));

    private static World ClassicalWorld => LazyClassicalWorld.Value;

    /// <summary>
    /// The DAT's own 24 neighbour pairs, transcribed verbatim from <c>dat-neighbour-mask.md</c> §2's own
    /// table (nation-record <c>+0x2B</c>, confirmed against the DAT's bytes and all 101 local saves).
    /// Each pair is listed once, in the report's own row order.
    /// </summary>
    private static readonly (string A, string B)[] DatConfirmedPairs =
    {
        ("rome", "carthage"), ("rome", "gaul"), ("rome", "illyria"),
        ("carthage", "ptolemaic"), ("carthage", "numidia"), ("carthage", "celtiberia"),
        ("seleucid", "ptolemaic"), ("seleucid", "bithynia"), ("seleucid", "galatia"),
        ("seleucid", "armenia"), ("seleucid", "media"),
        ("macedonia", "greece"), ("macedonia", "illyria"), ("macedonia", "dacia"), ("macedonia", "thracia"),
        ("gaul", "celtiberia"), ("gaul", "illyria"), ("gaul", "dacia"),
        ("greece", "illyria"), ("illyria", "dacia"), ("dacia", "thracia"),
        ("bithynia", "galatia"), ("bithynia", "armenia"), ("armenia", "media"),
    };

    /// <summary>
    /// The six pairs this derivation adds that the DAT's own mask does not have
    /// (<c>dat-neighbour-mask.md</c> §6, engine run against the DAT) -- three already known from the
    /// three-row sample (Rome ↔ Greece; Thracia ↔ Bithynia/Seleucid), three more only the full DAT mask
    /// revealed (Seleucid ↔ Macedonia/Greece; Ptolemaic ↔ Greece).
    /// </summary>
    private static readonly (string A, string B)[] KnownFalsePositives =
    {
        ("rome", "greece"),
        ("thracia", "bithynia"), ("thracia", "seleucid"),
        ("seleucid", "macedonia"), ("seleucid", "greece"),
        ("ptolemaic", "greece"),
    };

    // ---- Full recall: every one of the DAT's 24 confirmed pairs is reproduced ----

    public static IEnumerable<object[]> DatConfirmedPairsData() =>
        DatConfirmedPairs.Select(p => new object[] { p.A, p.B });

    [Theory]
    [MemberData(nameof(DatConfirmedPairsData))]
    public void EveryDatConfirmedPairIsReproduced(string a, string b)
    {
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, a, b), $"'{a}' should border '{b}'.");
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, b, a), "The relation must be symmetric.");
    }

    /// <summary>
    /// Rework round 1, N2: pins the derivation's rows against the DAT's own 24 pairs exactly -- for
    /// every one of the 120 unordered pairs among the sixteen classical nations, the derivation must
    /// agree with the DAT except on the six known false positives above. Deleting the core-region step
    /// (<see cref="NeighbourGeography"/>'s remarks, search step 5's "dropping small colonial enclaves")
    /// adds carthage ↔ greece, a seventh pair in neither list, and this test is what catches it -- the
    /// row-scoped checks above and below cannot, since none of them touches that particular pair.
    /// </summary>
    [Fact]
    public void TheDerivationMatchesTheDatMaskExactlyExceptTheSixKnownFalsePositives()
    {
        var expectedTrue = new HashSet<(string A, string B)>(DatConfirmedPairs.Concat(KnownFalsePositives));
        var ids = ClassicalWorld.Nations.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

        var mismatches = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
            {
                var (a, b) = (ids[i], ids[j]);
                var expected = expectedTrue.Contains((a, b)) || expectedTrue.Contains((b, a));
                var actual = NeighbourGeography.AreNeighbours(ClassicalWorld, a, b);
                if (actual != expected)
                {
                    mismatches.Add($"{a} <-> {b}: expected {expected}, got {actual}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, "Unexpected mismatch(es):\n" + string.Join('\n', mismatches));
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

    /// <summary>
    /// Rework round 1, N8: <see cref="NeighbourGeography.NeighboursOf"/> used to hand out its internal
    /// <see cref="HashSet{T}"/> directly, "in no particular order". It now returns an ordered list, in
    /// <see cref="World.Nations"/>' own stable order -- checked directly against that same order, not
    /// merely that the right set of ids comes back.
    /// </summary>
    [Fact]
    public void NeighboursOfReturnsThemInWorldNationsOwnOrder()
    {
        var romeNeighbours = NeighbourGeography.NeighboursOf(ClassicalWorld, "rome");
        var nationOrder = ClassicalWorld.Nations.Select(n => n.Id).ToList();
        var expectedOrder = nationOrder.Where(id => romeNeighbours.Contains(id)).ToList();

        Assert.Equal(expectedOrder, romeNeighbours);
    }

    // ---- The six known, unresolved spurious pairs (documented, not asserted away) ----

    /// <summary>
    /// Rework round 1 (N1): <c>dat-neighbour-mask.md</c> §6 confirms this derivation adds six pairs
    /// against the DAT's full 24-pair mask, not the two or three visible from the three-row sample --
    /// Rome ↔ Greece and Thracia ↔ {Bithynia, Seleucid} were already known; Seleucid ↔ {Macedonia,
    /// Greece} and Ptolemaic ↔ Greece only the full DAT mask revealed. This derivation cannot rule any
    /// of the six out (see <see cref="NeighbourGeography"/>'s remarks, search step 5). Pinned
    /// individually, by name, so a future tightening of the method is a visible, deliberate change here,
    /// not a silent one; <see cref="TheDerivationMatchesTheDatMaskExactlyExceptTheSixKnownFalsePositives"/>
    /// is the exhaustive version of the same claim, over every pair rather than just these six.
    /// </summary>
    [Fact]
    public void TheSixKnownSpuriousPairsAreStillPresent()
    {
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "rome", "greece"));
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "thracia", "bithynia"));
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "thracia", "seleucid"));
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "seleucid", "macedonia"));
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "seleucid", "greece"));
        Assert.True(NeighbourGeography.AreNeighbours(ClassicalWorld, "ptolemaic", "greece"));
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
