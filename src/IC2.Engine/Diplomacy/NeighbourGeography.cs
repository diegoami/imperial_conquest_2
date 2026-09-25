using System.Runtime.CompilerServices;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// A derived, symmetric "who borders whom" relation between nations, standing in for the original's
/// nation record <c>+0x46</c>, a 16-bit neighbour mask —
/// <c>decompiled-ai-offers-to-human-seats.md</c> §1a <strong>[confirmed: the field exists and is
/// symmetric and geographic]</strong>, needed by <see cref="PendingOfferSystem"/>'s alliance-offer
/// condition (§1b, "r in neighbours(h)") and by the AI's own war-target search (§1a, "me in
/// neighbours(k)").
/// </summary>
/// <remarks>
/// <para>
/// <strong>The hazard, read in full — now resolved against the DAT itself.</strong> A follow-up report,
/// <c>dat-neighbour-mask.md</c> (rework round 1, N1 addition), traced the field
/// <c>decompiled-ai-offers-to-human-seats.md</c> §1a could only say "was not traced": the mask lives at
/// DAT nation-record <c>+0x2B</c>, loaded once by <c>FUN_004481A0</c>, and is <strong>[confirmed: bytes,
/// all 101 local saves]</strong> — not just the three rows (Rome, Carthage, Thracia) the offers report
/// checked, but all sixteen, matching every save in the set exactly. This type still derives a border
/// relation from geometry instead of loading that DAT field directly — the only thing <see cref="World"/>
/// actually carries — and <c>[designed]</c> is the honest tag for it, not <c>[confirmed]</c> or
/// <c>[derived]</c>: nothing here is read from the DAT, and the method was chosen by trial against the
/// confirmed rows, not deduced from them. Shipping the derivation, knowing exactly how it disagrees with
/// the DAT (below), is this task's own call — loading the DAT mask directly is <c>dat-neighbour-mask.md</c>
/// §7's own recommendation, for a later task to carry out, not this one's to fold in.
/// </para>
/// <para>
/// <strong>What was searched, and what came up empty, before settling on this method</strong> (build-
/// process.md §4.2 gate 2's own requirement for a <c>[designed]</c> value):
/// </para>
/// <list type="number">
/// <item>
/// <strong>Nearest-city-pair distance</strong> (the closest two cities, one per nation, by tile
/// distance) disagrees outright: Rome's closest city to a Greek one is 2 tiles away — closer than every
/// one of Rome's three real neighbours — yet Rome ↔ Greece is not in the observed mask. Greece owns
/// scattered colonial cities (Massilia, Nicaea, Genua — real Greek colonies on the Gaulish and Italian
/// coasts) that sit inside or beside Roman and Gaulish territory, so "nearest city" mostly measures
/// where Greece happened to plant a colony, not who shares a frontier with whom.
/// </item>
/// <item>
/// <strong>Capital-to-capital distance</strong>, ranked or thresholded, reproduces Rome's own row
/// exactly (Illyria 27.3, Gaul 35.4, Carthage 35.9 tiles are its three nearest capitals, in order) but
/// not Carthage's: Numidia (26.4) and Rome (35.9) rank correctly, but Ptolemaic — in the observed row —
/// sits at rank 10 of 15 (98.3 tiles), far behind Gaul and Illyria, which the observed row omits.
/// </item>
/// <item>
/// <strong>Whole-map Voronoi</strong> (every tile assigned to its nearest city, any nation) reproduces
/// every relationship the report confirms — nothing is missing for Rome, Carthage or Thracia — but adds
/// false positives: Rome ↔ Greece and Rome ↔ Ptolemaic (again, Greece's scattered colonies), and,
/// for Thracia, Bithynia and Seleucid across the narrow strait at Byzantium.
/// </item>
/// <item>
/// <strong>This method</strong>: the same whole-map Voronoi, restricted to each nation's own largest
/// connected region (dropping small colonial enclaves before any border is measured — this alone
/// removes Carthage ↔ Greece outright, since Carthage's real territory never touches one of Greece's
/// enclaves) and then requiring the shared border to be at least <see cref="MinimumBorderTiles"/> tiles
/// long (dropping thin slivers — Rome ↔ Ptolemaic falls from 54/46/32/18/3 tiles per neighbour down to
/// a 3-tile sliver once enclaves are dropped, well under the threshold, while every one of the report's
/// nine confirmed pairs keeps a border of 18 tiles or more).
/// </item>
/// </list>
/// <para>
/// <strong>The result against the full DAT mask (rework round 1, N1): full recall, six false
/// positives.</strong> <c>dat-neighbour-mask.md</c> §2 gives all 24 DAT pairs across all sixteen rows
/// (not just the nine the offers report's own three-row sample could confirm), and §6 runs this
/// derivation against every one of them <strong>[confirmed: engine run]</strong>: every DAT pair is
/// reproduced — nothing here is missing a confirmed relationship — and six spurious pairs remain, not
/// two or three:
/// </para>
/// <list type="bullet">
/// <item>
/// <strong>Rome ↔ Greece</strong> (a 32-tile border, from a Greek colonial cluster along the Ligurian
/// coast) — already known from the three-row sample.
/// </item>
/// <item>
/// <strong>Thracia ↔ Bithynia</strong> and <strong>Thracia ↔ Seleucid</strong> (66 and 27 tiles, across
/// the Bosphorus at Byzantium) — also already known.
/// </item>
/// <item>
/// <strong>Seleucid ↔ Macedonia</strong>, <strong>Seleucid ↔ Greece</strong> and
/// <strong>Ptolemaic ↔ Greece</strong> — newly found by checking the full DAT mask instead of only the
/// three sampled rows; two of the three involve Greece's own scattered colonies again, the same cause
/// already named for Rome ↔ Greece, and the Seleucid ↔ Macedonia pair's own cause was not examined by
/// <c>dat-neighbour-mask.md</c> itself <c>[hypothesis]</c>.
/// </item>
/// </list>
/// <para>
/// This is real geography the original's own hand-authored frontier graph apparently excludes for a
/// reason this derivation cannot see (<c>dat-neighbour-mask.md</c> §2: "it looks like a hand-authored
/// frontier graph, not a distance rule"). All six are reported rather than hidden: see the PR body's
/// hazard section, and <see cref="NeighbourGeographyTests"/>'s own row-exact pin (N2) against the DAT's
/// 24 pairs.
/// </para>
/// <para>
/// <strong>Fixed at scenario start — unlike the original's own field.</strong> Every input is
/// <see cref="World.Cities"/> — the starting owner, never <see cref="GameState.Cities"/> — so a border
/// never shifts as cities change hands mid-game. <c>dat-neighbour-mask.md</c> §4 corrects an earlier
/// reading here: the original's own mask is <em>not</em> fixed after scenario setup. Its conquest
/// routine, <c>FUN_0044C528</c>, merges a defeated nation's neighbours into its conqueror's mask (and
/// sets the conqueror's own bit in each of those neighbours' masks) every time a nation is eliminated by
/// conquest — a rewrite this derivation does not model, since it never reads anything but the starting
/// city owners. <c>dat-neighbour-mask.md</c> §7 recommends loading the mask from the DAT and applying
/// that merge in a reimplementation; both are out of this task's own scope.
/// </para>
/// <para>
/// <strong>Cached per <see cref="World"/> instance.</strong> The scan is <c>O(width × height ×
/// cityCount)</c> — a few million operations for the shipped classical world, run once and reused for
/// the rest of that world's lifetime; a hand-built test world with a handful of cities and tiles is
/// effectively free.
/// </para>
/// </remarks>
public static class NeighbourGeography
{
    /// <summary>
    /// <c>[designed]</c>: the minimum shared border, in 4-connected boundary tile-pairs, for two
    /// nations' core territories to count as neighbours — see this type's own remarks for the search
    /// that produced it. Every one of the DAT's 24 confirmed pairs (<c>dat-neighbour-mask.md</c> §2)
    /// clears it by a wide margin (18 tiles or more); the six spurious pairs this derivation cannot
    /// resolve (Rome ↔ Greece, Thracia ↔ Bithynia, Thracia ↔ Seleucid, Seleucid ↔ Macedonia, Seleucid ↔
    /// Greece, Ptolemaic ↔ Greece) clear it too, so this threshold trims sliver artefacts, not the
    /// genuine disagreement.
    /// </summary>
    private const int MinimumBorderTiles = 10;

    private static readonly ConditionalWeakTable<World, Dictionary<string, HashSet<string>>> Cache = new();

    /// <summary>Whether <paramref name="nationAId"/> and <paramref name="nationBId"/> border each other in <paramref name="world"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="world"/>, <paramref name="nationAId"/> or <paramref name="nationBId"/> is null.</exception>
    public static bool AreNeighbours(World world, string nationAId, string nationBId)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(nationAId);
        ArgumentNullException.ThrowIfNull(nationBId);

        if (string.Equals(nationAId, nationBId, StringComparison.Ordinal))
        {
            return false;
        }

        var map = Cache.GetValue(world, BuildNeighbourMap);
        return map.TryGetValue(nationAId, out var neighbours) && neighbours.Contains(nationBId);
    }

    /// <summary>Every nation <paramref name="nationId"/> borders in <paramref name="world"/>, in no particular order.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> or <paramref name="nationId"/> is null.</exception>
    public static IReadOnlySet<string> NeighboursOf(World world, string nationId)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(nationId);

        var map = Cache.GetValue(world, BuildNeighbourMap);
        return map.TryGetValue(nationId, out var neighbours) ? neighbours : EmptySet;
    }

    private static readonly HashSet<string> EmptySet = new(StringComparer.Ordinal);

    private static Dictionary<string, HashSet<string>> BuildNeighbourMap(World world)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var width = world.Width;
        var height = world.Height;
        if (width <= 0 || height <= 0 || world.Cities.Count == 0)
        {
            return result;
        }

        // 1. Nearest-owned-city assignment (whole-map Voronoi), by squared distance -- avoids a
        // sqrt per cell over up to ~45,000 cells. Ties keep the first city in World.Cities' own
        // (stable, file) order, never iteration order of anything else.
        var ownerGrid = new string?[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                string? bestOwner = null;
                var bestDistance = long.MaxValue;
                foreach (var city in world.Cities)
                {
                    var dx = (long)(city.X - x);
                    var dy = (long)(city.Y - y);
                    var distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestOwner = city.Owner;
                    }
                }

                ownerGrid[(y * width) + x] = bestOwner;
            }
        }

        // 2. Connected components (4-connected flood fill) over the Voronoi grid, then each nation's
        // own largest component is its "core" territory -- a small colonial enclave, surrounded by
        // another nation's cells, forms its own tiny component and is dropped before any border is
        // measured (this type's own remarks, search step 4).
        var componentId = new int[width * height];
        Array.Fill(componentId, -1);
        var componentOwner = new List<string>();
        var componentSize = new List<int>();
        var queue = new Queue<int>();

        for (var start = 0; start < ownerGrid.Length; start++)
        {
            if (componentId[start] >= 0)
            {
                continue;
            }

            var owner = ownerGrid[start];
            var id = componentOwner.Count;
            componentOwner.Add(owner!);
            var size = 0;

            componentId[start] = id;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                size++;
                var cx = cell % width;
                var cy = cell / width;

                Consider(cx - 1, cy);
                Consider(cx + 1, cy);
                Consider(cx, cy - 1);
                Consider(cx, cy + 1);

                void Consider(int nx, int ny)
                {
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    {
                        return;
                    }

                    var neighbourCell = (ny * width) + nx;
                    if (componentId[neighbourCell] >= 0 || ownerGrid[neighbourCell] != owner)
                    {
                        return;
                    }

                    componentId[neighbourCell] = id;
                    queue.Enqueue(neighbourCell);
                }
            }

            componentSize.Add(size);
        }

        var coreComponentByNation = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var id = 0; id < componentOwner.Count; id++)
        {
            var owner = componentOwner[id];
            if (!coreComponentByNation.TryGetValue(owner, out var currentBest)
                || componentSize[id] > componentSize[currentBest])
            {
                coreComponentByNation[owner] = id;
            }
        }

        // A component id is "core" iff it is the best one recorded for the nation that owns it --
        // checked by lookup, not by enumerating coreComponentByNation.Values (whose order the engine's
        // own determinism guard forbids relying on).
        var isCoreComponent = new bool[componentOwner.Count];
        for (var id = 0; id < componentOwner.Count; id++)
        {
            if (coreComponentByNation[componentOwner[id]] == id)
            {
                isCoreComponent[id] = true;
            }
        }

        // 3. Border-edge tally between different nations' core components only.
        var borderTiles = new Dictionary<(string A, string B), int>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var cell = (y * width) + x;
                var componentHere = componentId[cell];
                if (!isCoreComponent[componentHere])
                {
                    continue;
                }

                var ownerHere = ownerGrid[cell]!;
                TallyIfBorder(x + 1, y);
                TallyIfBorder(x, y + 1);

                void TallyIfBorder(int nx, int ny)
                {
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    {
                        return;
                    }

                    var neighbourCell = (ny * width) + nx;
                    var componentThere = componentId[neighbourCell];
                    if (!isCoreComponent[componentThere])
                    {
                        return;
                    }

                    var ownerThere = ownerGrid[neighbourCell]!;
                    if (string.Equals(ownerHere, ownerThere, StringComparison.Ordinal))
                    {
                        return;
                    }

                    var key = string.CompareOrdinal(ownerHere, ownerThere) <= 0
                        ? (ownerHere, ownerThere)
                        : (ownerThere, ownerHere);
                    borderTiles[key] = borderTiles.GetValueOrDefault(key) + 1;
                }
            }
        }

        // 4. The threshold, and the symmetric result map -- walked over World.Nations' own stable order
        // (never borderTiles directly: the engine's own determinism guard forbids relying on a
        // Dictionary's enumeration order).
        for (var i = 0; i < world.Nations.Count; i++)
        {
            for (var j = i + 1; j < world.Nations.Count; j++)
            {
                var a = world.Nations[i].Id;
                var b = world.Nations[j].Id;
                var key = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
                if (!borderTiles.TryGetValue(key, out var tiles) || tiles < MinimumBorderTiles)
                {
                    continue;
                }

                AddNeighbour(result, a, b);
                AddNeighbour(result, b, a);
            }
        }

        return result;
    }

    private static void AddNeighbour(Dictionary<string, HashSet<string>> map, string nationId, string neighbourId)
    {
        if (!map.TryGetValue(nationId, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            map[nationId] = set;
        }

        set.Add(neighbourId);
    }
}
