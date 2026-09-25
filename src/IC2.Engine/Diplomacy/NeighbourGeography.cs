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
/// <strong>The hazard, read in full.</strong> The report states plainly that "where the mask is built
/// (the DAT or a map pass) was not traced", and it was checked against only three of the sixteen
/// nations' own rows, each read whole from a save: Rome ↔ {Carthage, Gaul, Illyria}, Carthage ↔ {Rome,
/// Ptolemaic, Numidia, Celtiberia}, Thracia ↔ {Macedonia, Dacia}. This type derives a border relation
/// from geometry instead — the only thing <see cref="World"/> actually carries — and <c>[designed]</c>
/// is the honest tag for it, not <c>[confirmed]</c> or <c>[derived]</c>: nothing here is read from the
/// DAT, and the method was chosen by trial against the three known rows, not deduced from them.
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
/// <strong>The result: full recall, imperfect precision.</strong> Every one of the nine pairs the report
/// confirms (Rome's three, Carthage's four, Thracia's two) is reproduced — nothing here has ever been
/// found <em>missing</em> a confirmed relationship. Two spurious additions remain and are not resolved
/// by this method: Rome ↔ Greece (a 32-tile border, from a Greek colonial cluster along the Ligurian
/// coast) and Thracia ↔ {Bithynia, Seleucid} (66 and 27 tiles, across the Bosphorus at Byzantium — real
/// geography the original's own hand-authored or generated mask apparently excludes for a reason this
/// derivation cannot see). Both are reported rather than hidden: see the PR body's hazard section.
/// </para>
/// <para>
/// <strong>Fixed at scenario start, like the original's own field.</strong> Every input is
/// <see cref="World.Cities"/> — the starting owner, never <see cref="GameState.Cities"/> — so a border
/// never shifts as cities change hands mid-game, matching a field the original never rewrites after
/// scenario setup.
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
    /// that produced it. Every one of the report's nine confirmed pairs clears it by a wide margin (18
    /// tiles or more); the spurious pairs this derivation cannot resolve (Rome ↔ Greece, Thracia ↔
    /// Bithynia/Seleucid) clear it too, so this threshold trims sliver artefacts, not the genuine
    /// disagreement.
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

        var coreComponentIds = new HashSet<int>(coreComponentByNation.Values);

        // 3. Border-edge tally between different nations' core components only.
        var borderTiles = new Dictionary<(string A, string B), int>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var cell = (y * width) + x;
                var componentHere = componentId[cell];
                if (!coreComponentIds.Contains(componentHere))
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
                    if (!coreComponentIds.Contains(componentThere))
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

        // 4. The threshold, and the symmetric result map.
        foreach (var ((a, b), tiles) in borderTiles)
        {
            if (tiles < MinimumBorderTiles)
            {
                continue;
            }

            AddNeighbour(result, a, b);
            AddNeighbour(result, b, a);
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
