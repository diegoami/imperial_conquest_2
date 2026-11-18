using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the composing pipeline every shipped recipe runs — land, islands and sea arms, texture,
/// ridges with passes, rivers, then settlements, forces and names. The five styles declare a
/// <see cref="StylePlan"/> and their own name banks; the deterministic order of every draw lives
/// here so a seed replays exactly.
/// </summary>
public static class StyleComposer
{
    /// <summary>The shipped authored palette (16 distinct muted colours, india-325bc's table).</summary>
    public static readonly string[] Palette =
    [
        "#2E7A76", "#395A93", "#39718B", "#41708E", "#4A5FA5", "#4E8A6B", "#522727", "#6A3C88",
        "#6A6530", "#7B446E", "#8A4B29", "#8A683C", "#8F5A2E", "#984A63", "#B23A2A", "#B87924",
    ];

    public static GeneratedMap Compose(MapSeedStream rng, MapSize size, StylePlan plan)
    {
        var canvas = new StyleCanvas(size.Width, size.Height);
        var land = rng.Stream("landmass");
        foreach (var shape in plan.Land)
            canvas.PaintDisc(Fx(size, shape.Fx), Fy(size, shape.Fy), Fr(size, shape.Fr), StyleCanvas.Wobble(land),
                (_, _) => CanonicalTiles.Plain);
        foreach (var shape in plan.Water)
            canvas.PaintDisc(Fx(size, shape.Fx), Fy(size, shape.Fy), Fr(size, shape.Fr), StyleCanvas.Wobble(land),
                (_, _) => CanonicalTiles.SeaCalm);
        foreach (var shape in plan.Islands)
            canvas.PaintDisc(Fx(size, shape.Fx), Fy(size, shape.Fy), Fr(size, shape.Fr), StyleCanvas.Wobble(land),
                (_, _) => CanonicalTiles.Plain);

        // Texture: dry interiors, wooded ground, plain coastal band. Painted before the ridges so
        // the passes and their corridors stay clean plain routes.
        var dryness = StyleCanvas.NoiseField(rng.Stream("dryness"), size.Width, size.Height, 28, 3);
        var wood = StyleCanvas.NoiseField(rng.Stream("wood"), size.Width, size.Height, 16, 3);
        var coast = canvas.DistanceToWater(6);
        for (var y = 0; y < size.Height; y++)
            for (var x = 0; x < size.Width; x++)
            {
                if (!canvas.IsLand(x, y)) continue;
                var i = (y * size.Width) + x;
                if (coast[i] <= 2) continue; // coastal plains stay open
                if (dryness[i] > 1 - plan.Dryness) canvas.Set(x, y, CanonicalTiles.Desert);
                else if (wood[i] > 1 - plan.Woodiness) canvas.Set(x, y, CanonicalTiles.Forest);
            }

        foreach (var ridge in plan.Ridges)
            canvas.PaintRidge(rng.Stream("ridges"), From(size, ridge), To(size, ridge), ridge.Passes);

        // Rivers: springs on open land away from each other, walked to the water. One shared stream
        // for every walk — a fresh Stream(name) per walk would restart the same sequence.
        var relief = StyleCanvas.NoiseField(rng.Stream("relief"), size.Width, size.Height, 22, 4);
        var springs = PickSites(rng.Stream("springs"), canvas, plan.Rivers,
            minDistance: Math.Max(10, Math.Min(size.Width, size.Height) / 12),
            allowed: (x, y) => SpringSite(canvas, x, y));
        var walks = rng.Stream("river");
        foreach (var spring in springs)
        {
            var path = canvas.WalkRiver(walks, spring.X, spring.Y, relief);
            if (path.Count >= 3) canvas.WriteRiverCodes(path);
        }

        canvas.CarveSeaBorder();

        // Settlements: capitals first, spread; then each seat's towns. Sites must sit on the
        // mainland component — an island town would strand its seat off the connected land — and
        // fleets berth on the main sea, so every port reaches every shore.
        var mainland = canvas.LargestComponent(land: true);
        var mainSea = canvas.LargestComponent(land: false);
        bool OnMainland(int x, int y) => mainland.Contains((y * canvas.Width) + x) && TownSite(canvas, x, y);
        bool ByTheMainSea(int x, int y)
        {
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    if (canvas.InBounds(x + dx, y + dy) && mainSea.Contains(((y + dy) * canvas.Width) + x + dx))
                        return true;
            return false;
        }

        var capitals = PickSites(rng.Stream("capitals"), canvas, plan.Seats,
            minDistance: Math.Max(16, Math.Min(size.Width, size.Height) / 6), allowed: OnMainland,
            firstMustSatisfy: plan.CoastalSeats, byTheSea: true, onWater: ByTheMainSea);
        var towns = PickSites(rng.Stream("towns"), canvas, plan.Seats * plan.TownsMax,
            minDistance: Math.Max(8, Math.Min(size.Width, size.Height) / 11), allowed: OnMainland,
            keepAway: capitals, keepAwayDistance: Math.Max(6, Math.Min(size.Width, size.Height) / 16));

        return Populate(rng, canvas, plan, capitals, AssignTowns(capitals, towns, plan.TownsMin, plan.TownsMax), mainSea);
    }

    private static int Fx(MapSize size, double f) => Math.Clamp((int)(f * size.Width), 1, size.Width - 2);

    private static int Fy(MapSize size, double f) => Math.Clamp((int)(f * size.Height), 1, size.Height - 2);

    private static int Fr(MapSize size, double f) => Math.Max(2, (int)(f * Math.Min(size.Width, size.Height)));

    private static (int X, int Y) From(MapSize size, Ridge r) => (Fx(size, r.FromFx), Fy(size, r.FromFy));

    private static (int X, int Y) To(MapSize size, Ridge r) => (Fx(size, r.ToFx), Fy(size, r.ToFy));

    private static bool TownSite(StyleCanvas canvas, int x, int y)
    {
        if (!canvas.IsLand(x, y)) return false;
        var code = canvas.Code(x, y);
        if (code is CanonicalTiles.Mountains or >= CanonicalTiles.River1) return false;
        var open = 0;
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (canvas.IsLand(x + dx, y + dy) && !canvas.IsMountain(x + dx, y + dy)) open++;
            }

        return open >= 6;
    }

    private static bool SpringSite(StyleCanvas canvas, int x, int y) =>
        canvas.IsLand(x, y) && !canvas.IsMountain(x, y) && !canvas.IsRiver(x, y);

    /// <summary>
    /// Site picking: one deterministic scan collects every allowed cell, the stream shuffles them,
    /// and a greedy pass takes sites at least <paramref name="minDistance"/> apart. Packing by
    /// greedy take instead of rejection sampling spreads the seats reliably even on broken-up
    /// geography. <paramref name="firstMustSatisfy"/> takes that many coastal sites first (the
    /// ports' promise), with <paramref name="onWater"/> deciding "coastal".
    /// </summary>
    private static List<(int X, int Y)> PickSites(SplitMix64Rng rng, StyleCanvas canvas, int count, int minDistance,
        Func<int, int, bool> allowed, List<(int X, int Y)>? keepAway = null, int keepAwayDistance = 0,
        int firstMustSatisfy = 0, bool byTheSea = false, Func<int, int, bool>? onWater = null)
    {
        var coastal = new List<(int X, int Y)>();
        var inland = new List<(int X, int Y)>();
        for (var y = 1; y < canvas.Height - 1; y++)
            for (var x = 1; x < canvas.Width - 1; x++)
            {
                if (!allowed(x, y)) continue;
                if (byTheSea && (onWater?.Invoke(x, y) ?? TouchesWater(canvas, x, y))) coastal.Add((x, y));
                else inland.Add((x, y));
            }

        Shuffle(rng, coastal);
        Shuffle(rng, inland);
        var sites = new List<(int X, int Y)>();
        void Take(List<(int X, int Y)> pool)
        {
            foreach (var site in pool)
            {
                if (sites.Count >= count) return;
                if (sites.Any(s => Chebyshev(s, site) < minDistance)) continue;
                if (keepAway is not null && keepAway.Any(s => Chebyshev(s, site) < keepAwayDistance)) continue;
                sites.Add(site);
            }
        }

        if (byTheSea && coastal.Count >= firstMustSatisfy)
        {
            Take(coastal); // the coastal quota comes off its own pool so ports never miss out
        }

        Take(inland);
        if (sites.Count < firstMustSatisfy) Take(coastal); // a short coastal pool falls back to any site
        return sites;
    }

    private static void Shuffle<T>(SplitMix64Rng rng, IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static bool TouchesWater(StyleCanvas canvas, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (canvas.IsWater(x + dx, y + dy))
                    return true;
        return false;
    }

    private static int Chebyshev((int X, int Y) a, (int X, int Y) b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static List<(int X, int Y)[]> AssignTowns(
        List<(int X, int Y)> capitals, List<(int X, int Y)> towns, int townsMin, int townsMax)
    {
        var perSeat = new List<(int X, int Y)[]>();
        var pool = new List<(int X, int Y)>(towns);
        for (var seat = 0; seat < capitals.Count; seat++)
        {
            var want = Math.Min(townsMax, townsMin + (seat % Math.Max(1, townsMax - townsMin + 1)));
            var mine = pool.OrderBy(t => Chebyshev(t, capitals[seat])).Take(want).ToList();
            foreach (var t in mine) pool.Remove(t);
            perSeat.Add(mine.ToArray());
        }

        return perSeat;
    }

    private static GeneratedMap Populate(
        MapSeedStream rng, StyleCanvas canvas, StylePlan plan,
        List<(int X, int Y)> capitals, List<(int X, int Y)[]> townsPerSeat, HashSet<int> mainSea)
    {
        var nations = new List<NationDefinition>();
        var cities = new List<CityDefinition>();
        var armies = new List<StartingArmy>();
        var fleets = new List<StartingFleet>();
        var names = rng.Stream("names");
        var nationNames = plan.Names.Nations.ToArray();
        var leaders = plan.Names.Leaders.ToArray();
        Shuffle(names, nationNames);
        Shuffle(names, leaders);
        var curatedCities = plan.Names.Cities.ToArray();
        Shuffle(names, curatedCities);
        var curatedCursor = 0;
        var usedCityNames = new HashSet<string>();
        var occupied = capitals.ToHashSet();
        var fleetsPlaced = 0;
        var armyRolls = rng.Stream("armies");
        var berthRolls = rng.Stream("fleets");

        for (var seat = 0; seat < capitals.Count; seat++)
        {
            var (cx, cy) = capitals[seat];
            var id = NationId(nationNames[seat]);
            var towns = townsPerSeat[seat];
            var capitalId = $"{id}-capital";
            nations.Add(new NationDefinition(
                id, nationNames[seat], Palette[seat % Palette.Length],
                $"{leaders[seat]} of {nationNames[seat]}", capitalId,
                Treasury(names, towns.Length), 760,
                Wealth(names, towns.Length), TaxBase(names, towns.Length),
                10, 20, Population(names, towns.Length)));
            cities.Add(City(NextCityName(names, plan.Names, usedCityNames, curatedCities, ref curatedCursor),
                capitalId, cx, cy, id, isCapital: true, names));
            occupied.Add((cx, cy));
            if (TryPlaceArmy(armyRolls, canvas, occupied, cx, cy) is { } spot)
            {
                armies.Add(Army($"{id}-army-1", id, spot.X, spot.Y, names, plan.ForceWeights));
                occupied.Add(spot);
            }

            if (fleetsPlaced < plan.Fleets && ByTheMainSea(canvas, mainSea, cx, cy)
                && TryPlaceFleet(berthRolls, canvas, mainSea, occupied, cx, cy) is { } berth)
            {
                fleets.Add(new StartingFleet($"{id}-fleet-1", id, berth.X, berth.Y,
                    40 + names.NextInt(40), 90 + names.NextInt(11), 150 + names.NextInt(100),
                    500 + names.NextInt(200), 25));
                occupied.Add(berth);
                fleetsPlaced++;
            }

            for (var t = 0; t < towns.Length; t++)
            {
                var (tx, ty) = towns[t];
                cities.Add(City(NextCityName(names, plan.Names, usedCityNames, curatedCities, ref curatedCursor),
                    $"{id}-town-{t + 1}", tx, ty, id, isCapital: false, names));
                occupied.Add((tx, ty));
            }
        }

        return new GeneratedMap(canvas.Width, canvas.Height, canvas.Codes,
            new ValueList<NationDefinition>(nations), new ValueList<CityDefinition>(cities),
            new ValueList<StartingArmy>(armies), new ValueList<StartingFleet>(fleets),
            new ValueList<string>(nations.Select(n => n.Id).ToList()));
    }

    private static int Treasury(SplitMix64Rng rng, int towns) => 2500 + (towns * 1200) + rng.NextInt(2500);

    private static int Wealth(SplitMix64Rng rng, int towns) => 45000 + (towns * 52000) + (rng.NextInt(40) * 1000);

    private static int TaxBase(SplitMix64Rng rng, int towns) => 40 + (towns * 46) + rng.NextInt(30);

    private static int Population(SplitMix64Rng rng, int towns) => 15 + (towns * 24) + rng.NextInt(20);

    private static CityDefinition City(
        string name, string id, int x, int y, string owner, bool isCapital, SplitMix64Rng rng)
    {
        var population = isCapital ? 8 + rng.NextInt(7) : 4 + rng.NextInt(6);
        return new CityDefinition(
            id, name, x, y, owner, owner,
            isCapital ? 80 + rng.NextInt(11) : 75 + rng.NextInt(11),
            isCapital ? 220 + rng.NextInt(100) : 150 + rng.NextInt(100),
            isCapital ? 100 : 60 + rng.NextInt(30),
            population, population + 3 + rng.NextInt(4), 2 + rng.NextInt(6),
            isCapital
                ? new ValueList<UnitSlot>(new List<UnitSlot>
                {
                    new(0, "light_infantry", 3000 + rng.NextInt(2000), 6, "1st Garrison Battalion"),
                })
                : new ValueList<UnitSlot>(Array.Empty<UnitSlot>()));
    }

    /// <summary>
    /// The next city name: the style's curated list first (shuffled once per world, no repeats),
    /// then authored prefix/suffix parts, then a numbered last resort.
    /// </summary>
    private static string NextCityName(
        SplitMix64Rng rng, NameBank bank, HashSet<string> used, string[] curated, ref int curatedCursor)
    {
        while (curatedCursor < curated.Length)
        {
            var name = curated[curatedCursor++];
            if (used.Add(name)) return name;
        }

        for (var tries = 0; tries < 400; tries++)
        {
            var name = bank.CityPrefixes[rng.NextInt(bank.CityPrefixes.Count)]
                + bank.CitySuffixes[rng.NextInt(bank.CitySuffixes.Count)];
            if (used.Add(name)) return name;
        }

        var fallback = bank.CityPrefixes[rng.NextInt(bank.CityPrefixes.Count)] + rng.NextInt(900) + 100;
        used.Add(fallback);
        return fallback;
    }

    /// <summary>The canonical unit kinds, in <see cref="StylePlan.ForceWeights"/>' order.</summary>
    public static readonly string[] UnitKinds =
        ["light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry"];

    private static StartingArmy Army(
        string id, string nation, int x, int y, SplitMix64Rng rng, IReadOnlyList<int> forceWeights)
    {
        var units = new List<UnitSlot>();
        var count = 3 + rng.NextInt(2);
        for (var u = 0; u < count; u++)
        {
            var kind = WeightedKind(rng, forceWeights);
            units.Add(new UnitSlot(0, kind, 3000 + rng.NextInt(3000), 5 + rng.NextInt(3),
                $"{u + 1}{Ordinal(u + 1)} {KindLabel(kind)} Battalion"));
        }

        return new StartingArmy(id, nation, x, y, 60 + rng.NextInt(16), 100 + rng.NextInt(100),
            150 + rng.NextInt(100), 8, new ValueList<UnitSlot>(units));
    }

    private static string WeightedKind(SplitMix64Rng rng, IReadOnlyList<int> weights)
    {
        var roll = rng.NextInt(weights.Sum());
        for (var i = 0; i < weights.Count; i++)
        {
            if (roll < weights[i]) return UnitKinds[i];
            roll -= weights[i];
        }

        return UnitKinds[^1];
    }

    private static string KindLabel(string kind) => kind switch
    {
        "heavy_infantry" => "Halberdier",
        "archers" => "Bowmen",
        "light_cavalry" => "Lancer",
        _ => "Foot",
    };

    private static string Ordinal(int n) => n switch
    {
        1 => "st",
        2 => "nd",
        3 => "rd",
        _ => "th",
    };

    /// <summary>The seat's opening army: a free, open land cell within three rings of the capital.</summary>
    private static (int X, int Y)? TryPlaceArmy(
        SplitMix64Rng rng, StyleCanvas canvas, HashSet<(int X, int Y)> occupied, int cx, int cy)
    {
        for (var ring = 1; ring <= 3; ring++)
        {
            var options = new List<(int X, int Y)>();
            for (var dy = -ring; dy <= ring; dy++)
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                    var x = cx + dx;
                    var y = cy + dy;
                    if (!canvas.IsLand(x, y) || canvas.IsMountain(x, y) || canvas.IsRiver(x, y)) continue;
                    if (occupied.Contains((x, y))) continue;
                    options.Add((x, y));
                }

            if (options.Count > 0)
            {
                var pick = options[rng.NextInt(options.Count)];
                occupied.Add(pick);
                return pick;
            }
        }

        return null;
    }

    /// <summary>A fleet berth: a free main-sea cell near the capital that still has land beside it.</summary>
    private static (int X, int Y)? TryPlaceFleet(
        SplitMix64Rng rng, StyleCanvas canvas, HashSet<int> mainSea, HashSet<(int X, int Y)> occupied, int cx, int cy)
    {
        for (var ring = 1; ring <= 3; ring++)
        {
            var options = new List<(int X, int Y)>();
            for (var dy = -ring; dy <= ring; dy++)
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                    var x = cx + dx;
                    var y = cy + dy;
                    if (!canvas.IsWater(x, y) || occupied.Contains((x, y))) continue;
                    if (!mainSea.Contains((y * canvas.Width) + x)) continue;
                    if (!HasLandBeside(canvas, x, y)) continue;
                    options.Add((x, y));
                }

            if (options.Count > 0)
            {
                var pick = options[rng.NextInt(options.Count)];
                occupied.Add(pick);
                return pick;
            }
        }

        return null;
    }

    private static bool HasLandBeside(StyleCanvas canvas, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (canvas.IsLand(x + dx, y + dy))
                    return true;
        return false;
    }

    private static bool ByTheMainSea(StyleCanvas canvas, HashSet<int> mainSea, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (canvas.InBounds(x + dx, y + dy) && mainSea.Contains(((y + dy) * canvas.Width) + x + dx))
                    return true;
        return false;
    }

    private static string NationId(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        return slug.Trim('-');
    }
}
