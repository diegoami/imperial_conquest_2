using System.Runtime.CompilerServices;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// A symmetric "who borders whom" relation between nations, standing in for the original's nation
/// record <c>+0x46</c>, a 16-bit neighbour mask — <c>decompiled-ai-offers-to-human-seats.md</c> §1a
/// <strong>[confirmed: the field exists and is symmetric and geographic]</strong>, needed by
/// <see cref="PendingOfferSystem"/>'s alliance-offer condition (§1b, "r in neighbours(h)") and by the
/// AI's own war-target search (§1a, "me in neighbours(k)"). T85 (correction for #385): when
/// <see cref="World.StartingNeighbours"/> is present, this reads the DAT's own mask through it
/// directly — every classical-mediterranean query now answers the DAT's 24 pairs exactly, not the
/// derivation below. The derivation is the <strong>fallback</strong>, for a world (the toy fixture,
/// and any other world without the field) that carries none.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The hazard, read in full — now resolved against the DAT itself, and loaded (T85).</strong>
/// A follow-up report, <c>dat-neighbour-mask.md</c> (rework round 1, N1 addition), traced the field
/// <c>decompiled-ai-offers-to-human-seats.md</c> §1a could only say "was not traced": the mask lives at
/// DAT nation-record <c>+0x2B</c>, loaded once by <c>FUN_004481A0</c>, and is <strong>[confirmed: bytes,
/// all 101 local saves]</strong> — not just the three rows (Rome, Carthage, Thracia) the offers report
/// checked, but all sixteen, matching every save in the set exactly. <c>dat-neighbour-mask.md</c> §7
/// recommended loading that DAT field directly, as <c>World.StartingNeighbours</c> now does (T85); the
/// geometric derivation below remains, unchanged, as the fallback this method still runs for a world
/// (like the toy fixture) that carries no such field — <c>[designed]</c> is still the honest tag for
/// that derivation, not <c>[confirmed]</c> or <c>[derived]</c>: nothing in it is read from the DAT, and
/// the method was chosen by trial against the confirmed rows, not deduced from them. The rest of this
/// remark documents that fallback's own search and its known disagreement with the DAT — relevant only
/// to a world without <see cref="World.StartingNeighbours"/>.
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
/// false positives: Rome ↔ Greece, Carthage ↔ Greece and Rome ↔ Ptolemaic (the first two both from
/// Greece's scattered colonies), and, for Thracia, Bithynia and Seleucid across the narrow strait at
/// Byzantium.
/// </item>
/// <item>
/// <strong>This method</strong>: the same whole-map Voronoi, restricted to each nation's own largest
/// connected region (dropping small colonial enclaves before any border is measured — this alone
/// removes Carthage ↔ Greece outright, since Carthage's real territory never touches one of Greece's
/// enclaves) and then requiring the shared border to be at least <see cref="MinimumBorderTiles"/> tiles
/// long (dropping thin slivers — Rome ↔ Ptolemaic falls to a 3-tile sliver once enclaves are dropped,
/// well under the threshold, while every one of the report's nine confirmed pairs keeps a border of 18
/// tiles or more). Rework round 1, N7 correction: an earlier revision of this item listed Rome ↔ Greece
/// and Rome ↔ Ptolemaic as step 3's only false positives while separately crediting this step with
/// removing Carthage ↔ Greece — a pair step 3's own list never mentioned having added in the first
/// place. It is added above so both items agree.
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
/// frontier graph, not a distance rule"). <strong>T85: the classical-mediterranean world no longer uses
/// this derivation at all</strong> — it ships <see cref="World.StartingNeighbours"/>, so these six pairs
/// (and the DAT's real 24) are read from that field instead; <see cref="NeighbourGeographyTests"/>'s
/// DAT-exact assertions now run against the loaded mask, not against this fallback. A future world that
/// ships without <see cref="World.StartingNeighbours"/> would still see the same six false positives this
/// paragraph documents.
/// </para>
/// <para>
/// <strong>Fixed at scenario start — unlike the original's own field.</strong> Every input is
/// <see cref="World.Cities"/> — the starting owner, never <see cref="GameState.Cities"/> — so a border
/// never shifts as cities change hands mid-game. <c>dat-neighbour-mask.md</c> §4 corrects an earlier
/// reading here: the original's own mask is <em>not</em> fixed after scenario setup. Its conquest
/// routine, <c>FUN_0044C528</c>, merges a defeated nation's neighbours into its conqueror's mask (and
/// sets the conqueror's own bit in each of those neighbours' masks) every time a nation is eliminated by
/// conquest — a rewrite neither this derivation nor T85's own <see cref="World.StartingNeighbours"/>
/// models: the loaded field is <em>also</em> fixed for the run, deliberately (<see cref="World"/>'s own
/// remarks on that field) — a later task (T86) moves the mask into <see cref="GameState"/> and applies
/// the merge there. <c>dat-neighbour-mask.md</c> §7 recommended both loading the mask from the DAT and
/// applying that merge; T85 does the first, not the second.
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
    /// that produced it.
    /// </summary>
    /// <remarks>
    /// <strong>Rework round 2, N-e correction: the margin is not wide, and this value is not being
    /// re-tuned to fix it.</strong> An earlier revision of this remark claimed every DAT pair clears the
    /// threshold "by a wide margin (18 tiles or more)"; that is wrong, measured directly by raising the
    /// threshold and recording which pair drops out first at each step:
    /// <list type="bullet">
    /// <item>The smallest-margin <em>DAT</em> pair is Greece ↔ Illyria, at 13 tiles -- only 3 tiles above
    /// this threshold's own 10, the first DAT pair lost once the threshold reaches 14.</item>
    /// <item>The closest-margin <em>false</em> pair is Seleucid ↔ Macedonia, at 12 tiles -- closer to the
    /// threshold than any DAT pair, the first pair lost once the threshold reaches 13.</item>
    /// <item>Two more DAT pairs sit between those and clearly safe: Dacia ↔ Illyria (lost at threshold
    /// 17) and Rome ↔ Gaul (lost at threshold 19).</item>
    /// </list>
    /// T85 correction (#392, NB2): the range above is narrower than an earlier revision of this remark
    /// claimed. The four measurements bracket it exactly: Seleucid ↔ Macedonia is lost at 13, and the
    /// very next pair lost going up is Greece ↔ Illyria at 14 — a real DAT pair. So <strong>only 13</strong>
    /// drops the Seleucid ↔ Macedonia false pair while keeping all 24 DAT pairs; 14 through 16 would
    /// also drop Greece ↔ Illyria, and Dacia ↔ Illyria's 17 and Rome ↔ Gaul's 19 only mark where two
    /// more real pairs would go if the threshold were pushed further still — not a usable upper bound
    /// for "keeps all 24, drops the false one". Choosing 13 over today's 10 is tuning this
    /// <c>[designed]</c> value directly against the answer key it is being checked against, which needs
    /// the user's own call, not a change made unasked in a rework round. T85 makes retuning this moot
    /// for the classical world regardless — it now ships <see cref="World.StartingNeighbours"/>, so this
    /// derivation (and this constant) runs only as the fallback for a world without that field, where no
    /// DAT mask exists to tune against. Left at 10.
    /// </remarks>
    private const int MinimumBorderTiles = 10;

    private static readonly ConditionalWeakTable<World, Dictionary<string, HashSet<string>>> Cache = new();

    /// <summary>
    /// Whether <paramref name="nationAId"/> and <paramref name="nationBId"/> border each other, reading
    /// <see cref="GameState.Neighbours"/> when it carries one (T86: every game the engine itself starts
    /// or merges a conquest into) and falling back to <paramref name="world"/>'s own data — its
    /// <see cref="World.StartingNeighbours"/> field or T85's geometric derivation — only for a
    /// <paramref name="state"/> loaded from a save written before this field existed
    /// (<see cref="GameState.Neighbours"/>'s own remarks).
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="state"/>, <paramref name="world"/>, <paramref name="nationAId"/> or
    /// <paramref name="nationBId"/> is null.
    /// </exception>
    public static bool AreNeighbours(GameState state, World world, string nationAId, string nationBId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(nationAId);
        ArgumentNullException.ThrowIfNull(nationBId);

        if (string.Equals(nationAId, nationBId, StringComparison.Ordinal))
        {
            return false;
        }

        var entry = FindEntry(state.Neighbours, nationAId);
        if (entry is not null)
        {
            return entry.NeighbourIds.Contains(nationBId);
        }

        if (state.Neighbours is not null)
        {
            // The state carries a real (possibly conquest-merged) adjacency list, and nationAId simply
            // has no entry in it -- "no neighbours", not "fall back to world geometry".
            return false;
        }

        var map = Cache.GetValue(world, BuildNeighbourMap);
        return map.TryGetValue(nationAId, out var neighbours) && neighbours.Contains(nationBId);
    }

    /// <summary>
    /// Every nation <paramref name="nationId"/> borders, in <paramref name="world"/>'s own
    /// <see cref="World.Nations"/> stable order — see <see cref="AreNeighbours(GameState, World, string, string)"/>
    /// for which of <paramref name="state"/> or <paramref name="world"/> actually answers.
    /// </summary>
    /// <remarks>
    /// Rework round 1, N8: this used to hand out the internal lookup <see cref="HashSet{T}"/> directly,
    /// "in no particular order" — a trap for a future caller that iterates the result, since the engine's
    /// own determinism guard forbids relying on a <see cref="HashSet{T}"/>'s enumeration order elsewhere
    /// in this codebase. This method's own result is always ordered, at the boundary.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="state"/>, <paramref name="world"/> or <paramref name="nationId"/> is null.
    /// </exception>
    public static IReadOnlyList<string> NeighboursOf(GameState state, World world, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(nationId);

        var entry = FindEntry(state.Neighbours, nationId);
        if (entry is not null)
        {
            var fromState = new List<string>(entry.NeighbourIds.Count);
            foreach (var id in entry.NeighbourIds)
            {
                fromState.Add(id);
            }

            return fromState;
        }

        if (state.Neighbours is not null)
        {
            // The state carries a real (possibly conquest-merged) adjacency list with no entry for
            // nationId -- "no neighbours", not "fall back to world geometry".
            return Array.Empty<string>();
        }

        var map = Cache.GetValue(world, BuildNeighbourMap);
        if (!map.TryGetValue(nationId, out var neighbours) || neighbours.Count == 0)
        {
            return Array.Empty<string>();
        }

        var ordered = new List<string>(neighbours.Count);
        foreach (var nation in world.Nations)
        {
            if (neighbours.Contains(nation.Id))
            {
                ordered.Add(nation.Id);
            }
        }

        return ordered;
    }

    /// <summary>Finds <paramref name="nationId"/>'s own entry in <paramref name="neighbours"/>, or <see langword="null"/>.</summary>
    private static NationNeighbours? FindEntry(ValueList<NationNeighbours>? neighbours, string nationId)
    {
        if (neighbours is not { } list)
        {
            return null;
        }

        foreach (var entry in list)
        {
            if (string.Equals(entry.NationId, nationId, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// T86: the adjacency <see cref="GameStateFactory.CreateInitial"/> seeds <see cref="GameState.Neighbours"/>
    /// with at New Game — <paramref name="world"/>'s own <see cref="World.StartingNeighbours"/> when it
    /// carries one, otherwise the same geometric derivation <see cref="BuildNeighbourMap"/> has always
    /// run as its fallback, materialised here into the same shape the game state persists.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public static ValueList<NationNeighbours> InitialAdjacency(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var map = Cache.GetValue(world, BuildNeighbourMap);
        var entries = new List<NationNeighbours>(world.Nations.Count);
        foreach (var nation in world.Nations)
        {
            if (!map.TryGetValue(nation.Id, out var neighbours) || neighbours.Count == 0)
            {
                entries.Add(new NationNeighbours(nation.Id, ValueList<string>.Empty));
                continue;
            }

            var ordered = new List<string>(neighbours.Count);
            foreach (var other in world.Nations)
            {
                if (neighbours.Contains(other.Id))
                {
                    ordered.Add(other.Id);
                }
            }

            entries.Add(new NationNeighbours(nation.Id, ValueList.From(ordered)));
        }

        return ValueList.From(entries);
    }

    private static readonly HashSet<string> EmptySet = new(StringComparer.Ordinal);

    private static Dictionary<string, HashSet<string>> BuildNeighbourMap(World world)
    {
        // T85: the DAT's own mask, when the world carries one, wins outright -- no geometric
        // derivation runs at all. The classical-mediterranean world ships this field; the toy world
        // (and any other world without it) keeps the derivation below as its fallback.
        if (world.StartingNeighbours is { } startingNeighbours)
        {
            return BuildFromStartingNeighbours(startingNeighbours);
        }

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

    /// <summary>
    /// T85: builds the lookup straight from <see cref="World.StartingNeighbours"/> -- no geometry, no
    /// Voronoi, no threshold. <see cref="World.ValidateStartingNeighboursShape"/> already guarantees
    /// the field is symmetric by the time a <see cref="World"/> reaches here (through
    /// <see cref="IC2.Engine.Serialization.GameDataLoader"/>), but this adds both directions from every
    /// entry regardless -- cheap, and it means a hand-built <see cref="World"/> in a test that skips
    /// that load-time validation still gets a symmetric result rather than a silently one-sided one.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildFromStartingNeighbours(
        ValueList<NationNeighbours> startingNeighbours)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in startingNeighbours)
        {
            foreach (var neighbourId in entry.NeighbourIds)
            {
                AddNeighbour(result, entry.NationId, neighbourId);
                AddNeighbour(result, neighbourId, entry.NationId);
            }
        }

        return result;
    }
}
