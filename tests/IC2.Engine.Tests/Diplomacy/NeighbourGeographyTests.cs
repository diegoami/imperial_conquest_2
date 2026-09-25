using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Export;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// T82 (#359, bug #357), rework round 1 (N1/N2); T85 (#387, Done-when 4) repoints the classical-world
/// checks below at the DAT's own loaded <see cref="World.StartingNeighbours"/> mask, transcribed as data
/// from <c>dat-neighbour-mask.md</c> §2 -- not just the three rows (Rome, Carthage, Thracia) the earlier
/// <c>decompiled-ai-offers-to-human-seats.md</c> §1a sample could confirm, but all sixteen.
/// <see cref="NeighbourGeography"/>'s query answers this loaded mask exactly for
/// <see cref="ClassicalWorld"/> now (T85), never the geometric derivation; the derivation itself is
/// exercised only by this file's own hand-built tiny worlds further down, which carry no
/// <see cref="World.StartingNeighbours"/> field and so still fall back to it. See
/// <see cref="NeighbourGeography"/>'s own remarks for the search that produced the derivation method and
/// the full account of the six pairs it gets wrong -- still true of that fallback, just no longer
/// observable through <see cref="ClassicalWorld"/>.
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
    /// T85: the six pairs the geometric derivation used to add on <see cref="ClassicalWorld"/>, before
    /// that world shipped <see cref="World.StartingNeighbours"/> -- the DAT's own mask does not have any
    /// of them (<c>dat-neighbour-mask.md</c> §6, engine run against the DAT). Three were already known
    /// from the three-row sample (Rome ↔ Greece; Thracia ↔ Bithynia/Seleucid), three more only the full
    /// DAT mask revealed (Seleucid ↔ Macedonia/Greece; Ptolemaic ↔ Greece). Kept as a named, documented
    /// negative rather than deleted outright: <see cref="TheSixDerivationOnlyPairsAreNotPresentOnTheClassicalWorld"/>
    /// is Done-when 4's own "Rome–Greece is not a neighbour pair" check, generalised to all six.
    /// </summary>
    private static readonly (string A, string B)[] DerivationOnlyPairs =
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
    /// T85 Done-when 4: "on the classical world, the query answers the DAT's 24 pairs exactly." Rework
    /// round 1, N2 pinned the pre-T85 derivation's rows against the DAT with six known exceptions; now
    /// that <see cref="ClassicalWorld"/> ships <see cref="World.StartingNeighbours"/>, the query goes
    /// through <see cref="NeighbourGeography.BuildFromStartingNeighbours"/> instead of the derivation, and
    /// the match is exact -- for every one of the 120 unordered pairs among the sixteen classical
    /// nations, <see cref="NeighbourGeography.AreNeighbours"/> must agree with the DAT with <em>no</em>
    /// exceptions. Deleting the DAT-mask lookup (falling back to the derivation for this world too) would
    /// reintroduce the six pairs <see cref="DerivationOnlyPairs"/> names and this test would catch every
    /// one of them, plus any other pair the derivation gets wrong that no row-scoped check above happens
    /// to touch (rework round 1, N2's own reasoning, still valid against the new source).
    /// </summary>
    [Fact]
    public void TheClassicalWorldsQueryMatchesTheDatMaskExactly()
    {
        var expectedTrue = new HashSet<(string A, string B)>(DatConfirmedPairs);
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

    // ---- T85: the six pairs the derivation used to add are gone now that the DAT mask is loaded ----

    /// <summary>
    /// T85 Done-when 4: "Rome–Greece is not a neighbour pair" (and, generalised, none of the other five
    /// derivation-only pairs either). Before T85, <c>TheSixKnownSpuriousPairsAreStillPresent</c> asserted
    /// these six <em>present</em> on <see cref="ClassicalWorld"/>, documenting a known defect in the
    /// geometric derivation that world used to run through
    /// (<c>dat-neighbour-mask.md</c> §6: three known from the three-row sample, three more only the full
    /// DAT mask revealed). Now that <see cref="ClassicalWorld"/> ships
    /// <see cref="World.StartingNeighbours"/>, the query no longer runs that derivation for this world at
    /// all, so all six flip to absent -- exactly <see cref="TheClassicalWorldsQueryMatchesTheDatMaskExactly"/>'s
    /// own claim, pinned individually here by name so a regression that reintroduced the derivation path
    /// for this world is a visible, specific failure rather than only the exhaustive sweep's.
    /// </summary>
    [Fact]
    public void TheSixDerivationOnlyPairsAreNotPresentOnTheClassicalWorld()
    {
        foreach (var (a, b) in DerivationOnlyPairs)
        {
            Assert.False(NeighbourGeography.AreNeighbours(ClassicalWorld, a, b), $"'{a}' should not border '{b}'.");
            Assert.False(NeighbourGeography.AreNeighbours(ClassicalWorld, b, a), $"'{b}' should not border '{a}'.");
        }
    }

    // ---- Rome does not border Ptolemaic, in the DAT mask or the derivation alike ----

    [Fact]
    public void RomeDoesNotBorderPtolemaic()
    {
        // T85: on ClassicalWorld this is now the loaded DAT mask's own answer -- Ptolemaic is not in
        // Rome's confirmed row (dat-neighbour-mask.md §2). Before T85 this held for a different reason,
        // a 3-tile Voronoi sliver in the derivation this world no longer runs (this type's remarks,
        // search step 3 vs step 4) -- still true of the derivation itself, exercised by the hand-built
        // tiny worlds below, just no longer why this particular assertion holds.
        Assert.False(NeighbourGeography.AreNeighbours(ClassicalWorld, "rome", "ptolemaic"));
    }

    // ---- Review round 1, B1: BuildFromStartingNeighbours adds both directions even for a ----
    // ---- one-sided entry, for a World built directly, bypassing GameDataValidation.Validate ----

    /// <summary>
    /// Rework round 1, B1: <c>NeighbourGeography.BuildFromStartingNeighbours</c>'s own remarks claim it
    /// "adds both directions from every entry regardless... so a hand-built <see cref="World"/> in a
    /// test that skips that load-time validation still gets a symmetric result rather than a silently
    /// one-sided one" -- a claim nothing visited before this test: deleting the reverse
    /// <c>AddNeighbour</c> call left every test green. This builds a one-sided <see cref="World"/>
    /// directly (<c>a</c> lists <c>b</c> as a neighbour; <c>b</c> has no entry at all), <em>never</em>
    /// passing it through <see cref="GameDataValidation.Validate"/> -- which would reject it as
    /// asymmetric (<see cref="World.ValidateStartingNeighboursShape"/>) -- so this is exactly the
    /// "skips that load-time validation" case the comment describes. Both directions must still answer
    /// true.
    /// </summary>
    [Fact]
    public void BuildFromStartingNeighboursAddsBothDirectionsForAOneSidedEntryOnAWorldThatSkipsValidation()
    {
        var world = TinyWorld(("a", 0, 0), ("b", 1, 0)) with
        {
            StartingNeighbours = ValueList.Of(new NationNeighbours("a", ValueList.Of("b"))),
            // "b" gets no entry of its own -- genuinely one-sided, unlike the DAT's own mask or the
            // exported world, both of which are symmetric by construction. Never passed through
            // GameDataValidation.Validate, which would reject this as an asymmetric pair.
        };

        Assert.True(NeighbourGeography.AreNeighbours(world, "a", "b"));
        Assert.True(NeighbourGeography.AreNeighbours(world, "b", "a"));
    }

    // ---- Review round 2, B3: a present StartingNeighbours field never falls back to the ----
    // ---- derivation, even for an omitted nation or an empty row ----

    /// <summary>
    /// Review round 2, B3: <see cref="NationNeighbours"/>'s own doc comment on <see cref="World"/> claims
    /// <see cref="NeighbourGeography"/> "never falls back to its own geometric derivation for a
    /// <see cref="World"/> that carries <see cref="World.StartingNeighbours"/> at all -- not even for a
    /// nation this list omits, and not even if a present entry's own
    /// <see cref="NationNeighbours.NeighbourIds"/> is empty" -- a claim nothing visited before this test:
    /// the reviewer's mutation E3 (falling back unless the loaded map names every nation) left all 2902
    /// tests green. <c>a</c> and <c>b</c> are the same adjacent single-city pair
    /// <see cref="TwoAdjacentSingleCityNationsBorderOnATinyWorld"/> proves the derivation calls
    /// neighbours below -- so if the query ever fell back for this field, it would answer <em>true</em>.
    /// Here the field says otherwise: <c>a</c> gets an entry with an empty row, and <c>b</c> is omitted
    /// entirely (both edges B3 names, in one scenario) -- <see cref="NeighbourGeography.AreNeighbours"/>
    /// must answer <em>false</em> in both directions regardless.
    /// </summary>
    [Fact]
    public void APresentFieldNeverFallsBackToTheDerivationForAnOmittedNationOrAnEmptyRow()
    {
        var world = TinyWorld(("a", 0, 0), ("b", 1, 0)) with
        {
            StartingNeighbours = ValueList.Of(new NationNeighbours("a", ValueList<string>.Empty)),
            // "a" has an explicit, empty neighbour list; "b" has no entry at all -- both of B3's edges.
        };

        // The derivation alone (no field) would call these two neighbours -- pinned directly so a
        // change to TinyWorld's geometry can't silently make this test meaningless.
        var derivedOnly = TinyWorld(("a", 0, 0), ("b", 1, 0));
        Assert.True(NeighbourGeography.AreNeighbours(derivedOnly, "a", "b"));

        Assert.False(NeighbourGeography.AreNeighbours(world, "a", "b"));
        Assert.False(NeighbourGeography.AreNeighbours(world, "b", "a"));
    }

    // ---- T85 Done-when 4: the shipped toy world has no field, so it keeps the derivation ----

    /// <summary>
    /// T85 Done-when 4: "the toy world, which has no field, keeps the derivation." The shipped
    /// <c>toy-3city</c> fixture never gained <see cref="World.StartingNeighbours"/> (T85's Owns list
    /// never touches <c>data/worlds/toy-3city.json</c>), so <see cref="NeighbourGeography.AreNeighbours"/>
    /// still runs the geometric derivation for it, unchanged -- checked directly, not only inferred from
    /// the goldens and the 50-seed soak staying green (this test file's own companion evidence for that
    /// half of Done-when 4 is the unmodified <c>demo.golden.txt</c>/<c>AiSoakTests</c> runs, both outside
    /// this file's Owns).
    /// </summary>
    [Fact]
    public void TheToyWorldHasNoStartingNeighboursFieldAndStillUsesTheDerivation()
    {
        var toyWorld = CoreTestbed.Toy.World;
        Assert.Null(toyWorld.StartingNeighbours);

        // toy-3city has exactly two nations (north, south) sharing the whole tiny map -- the same
        // "two single-city[-ish] nations always split the map and so always border" shape
        // TwoAdjacentSingleCityNationsBorderOnATinyWorld below proves for the derivation directly.
        Assert.True(NeighbourGeography.AreNeighbours(toyWorld, "north", "south"));
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
