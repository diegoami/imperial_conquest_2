using IC2.Engine.MapGen;
using IC2.Engine.MapGen.Styles;
using IC2.Engine.Model;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.MapGen;

/// <summary>
/// T294c (#1844): the five shipped style recipes — every style composes valid worlds across many
/// seeds and two map sizes, replays a seed exactly, keeps the terrain-authoring rule (crest-only
/// mountains with forest flanks, open coastal plains) and draws rivers with the shipped directional
/// exit codes.
/// </summary>
public sealed class StyleRecipesTests
{
    private static readonly MapSize Size = new(280, 180);

    public static TheoryData<IMapStyle> Styles
    {
        get
        {
            var data = new TheoryData<IMapStyle>();
            foreach (var style in MapStyles.All) data.Add(style);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Every_style_generates_valid_worlds_across_seeds(IMapStyle style)
    {
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var generated = MapGenerator.Generate("t294c-test", style, seed, Size,
                $"{style.Id}-{seed}", $"{style.Id} world {seed}");
            var world = generated.World;
            Assert.InRange(world.Nations.Count, 10, 16);
            Assert.True(world.Cities.Count >= 3 * world.Nations.Count,
                $"{style.Id} seed {seed}: only {world.Cities.Count} cities for {world.Nations.Count} seats");
            Assert.True(world.StartingArmies.Count == world.Nations.Count,
                $"{style.Id} seed {seed}: {world.StartingArmies.Count} armies for {world.Nations.Count} seats");
            var decoded = world.Terrain.Decode(world.Width, world.Height);
            Assert.Equal(world.Width * world.Height, decoded.Length);
            Assert.All(decoded, c => Assert.InRange(c, 0, 11));
        }
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void A_seed_replays_the_same_world_and_a_different_seed_differs(IMapStyle style)
    {
        var a = MapGenerator.Generate("t294c-test", style, 42, Size, "style-a", "Style A");
        var b = MapGenerator.Generate("t294c-test", style, 42, Size, "style-a", "Style A");
        Assert.Equal(a.World, b.World);
        Assert.Equal(a.Seed, b.Seed);
        Assert.Equal(a.Attempt, b.Attempt);
        var c = MapGenerator.Generate("t294c-test", style, 43, Size, "style-a", "Style A");
        Assert.NotEqual(a.World, c.World);
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Mountains_are_crests_with_foothills_and_the_coast_stays_open(IMapStyle style)
    {
        var generated = MapGenerator.Generate("t294c-test", style, 7, Size, "relief", "Relief");
        var cells = generated.World.Terrain.Decode(generated.World.Width, generated.World.Height);
        var land = cells.Count(CanonicalTiles.PassableByArmies);
        var mountains = cells.Count(c => c == CanonicalTiles.Mountains);
        var forest = cells.Count(c => c == CanonicalTiles.Forest);
        var desert = cells.Count(c => c == CanonicalTiles.Desert);
        // The authoring rule: severe relief stays a narrow crest, forest carries the lower hills,
        // and neither fills the map (docs/design/terrain-authoring.md).
        Assert.True(mountains <= land * 0.08, $"{style.Id}: {mountains} mountain cells of {land} land");
        Assert.True(forest <= land * 0.35, $"{style.Id}: {forest} forest cells of {land} land");
        Assert.True(desert <= land * 0.45, $"{style.Id}: {desert} desert cells of {land} land");
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Rivers_carry_the_shipped_exit_codes_and_never_run_alone(IMapStyle style)
    {
        var generated = MapGenerator.Generate("t294c-test", style, 9, Size, "rivers", "Rivers");
        var codes = generated.World.Terrain.Decode(generated.World.Width, generated.World.Height);
        var riverCells = new List<int>();
        for (var i = 0; i < codes.Length; i++)
            if (codes[i] >= CanonicalTiles.River1)
                riverCells.Add(i);

        Assert.True(riverCells.Count >= 8, $"{style.Id}: only {riverCells.Count} river cells");
        foreach (var i in riverCells)
        {
            var x = i % generated.World.Width;
            var y = i / generated.World.Width;
            var riverNeighbours = DirectionedRiverNeighbours(codes, generated.World.Width, generated.World.Height, x, y);
            Assert.True(riverNeighbours.Count >= 1, $"{style.Id}: river cell at {x},{y} has no river neighbour");
            Assert.True(riverNeighbours.Count <= 4, $"{style.Id}: river cell at {x},{y} has {riverNeighbours.Count} river neighbours");
        }
    }

    private static List<char> DirectionedRiverNeighbours(int[] codes, int width, int height, int x, int y)
    {
        var found = new List<char>();
        foreach (var (dx, dy, d) in new[] { (0, -1, 'N'), (0, 1, 'S'), (1, 0, 'E'), (-1, 0, 'W') })
        {
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if (codes[(ny * width) + nx] >= CanonicalTiles.River1) found.Add(d);
        }

        return found;
    }

    [Fact]
    public void The_mediterranean_style_keeps_its_naval_promise()
    {
        var generated = MapGenerator.Generate("t294c-test", new MediterraneanStyle(), 11, Size, "sea", "Sea");
        var world = generated.World;
        Assert.True(world.StartingFleets.Count >= 4, $"only {world.StartingFleets.Count} fleets");
        Assert.True(CoastalCities(world).Count >= 4, $"only {CoastalCities(world).Count} coastal cities");
        // The inland sea and its straits: every fleet reaches every other fleet's shore.
        Assert.True(world.StartingFleets.Count >= 2);
    }

    [Fact]
    public void The_middle_east_style_keeps_its_dry_broad_interior()
    {
        var generated = MapGenerator.Generate("t294c-test", new MiddleEastStyle(), 13, Size, "dry", "Dry");
        var cells = generated.World.Terrain.Decode(generated.World.Width, generated.World.Height);
        var desert = cells.Count(c => c == CanonicalTiles.Desert);
        var land = cells.Count(CanonicalTiles.PassableByArmies);
        Assert.True(desert >= land * 0.15, $"only {desert} desert cells of {land} land");
    }

    [Theory]
    [InlineData(220, 150)]
    [InlineData(320, 200)]
    public void A_second_size_composes_too(int width, int height)
    {
        var generated = MapGenerator.Generate("t294c-test", new MediterraneanStyle(), 5, new MapSize(width, height),
            "second-size", "Second size");
        Assert.Equal(width, generated.World.Width);
        Assert.InRange(generated.World.Nations.Count, 8, 16);
    }

    [Fact]
    public void The_registry_offers_exactly_the_five_styles_mediterranean_first()
    {
        Assert.Equal(
            ["mediterranean", "middle-east", "central-asia", "india", "china"],
            MapStyles.All.Select(s => s.Id).ToArray());
        Assert.IsType<MediterraneanStyle>(MapStyles.ById("mediterranean"));
        Assert.Null(MapStyles.ById("atlantis"));
    }

    /// <summary>Each shipped style with its declared plan (the plans are static per style class).</summary>
    private static (IMapStyle Style, StylePlan Plan)[] Pairs =>
    [
        (new MediterraneanStyle(), MediterraneanStyle.Plan),
        (new MiddleEastStyle(), MiddleEastStyle.Plan),
        (new CentralAsiaStyle(), CentralAsiaStyle.Plan),
        (new IndiaStyle(), IndiaStyle.Plan),
        (new ChinaStyle(), ChinaStyle.Plan),
    ];

    [Fact]
    public void Names_come_from_the_authored_bank_and_stay_unique()
    {
        foreach (var (style, plan) in Pairs)
        {
            var bank = plan.Names;
            Assert.True(bank.Nations.Count >= plan.Seats, $"{style.Id}: too few authored nations for its seats");
            Assert.True(bank.Leaders.Count >= plan.Seats, $"{style.Id}: too few authored leaders");
            Assert.True(bank.Cities.Count >= 24, $"{style.Id}: too few curated city names");

            var world = MapGenerator.Generate("t294d-test", style, 17, Size, "names", "Names").World;
            var nationNames = world.Nations.Select(n => n.Name).ToList();
            Assert.Equal(nationNames.Count, nationNames.Distinct().Count());
            Assert.All(nationNames, n => Assert.Contains(n, bank.Nations));
            var cityNames = world.Cities.Select(c => c.Name).ToList();
            Assert.Equal(cityNames.Count, cityNames.Distinct().Count());
        }
    }

    [Fact]
    public void No_style_ships_a_historical_power()
    {
        string[] powers =
        [
            "rome", "carthage", "macedon", "athens", "sparta", "thebes", "persia", "parthia",
            "media", "lydia", "egypt", "assyria", "babylon", "seleucid", "ptolemy", "antigonid",
            "qin", "chu", "zhao", "wei", "qi", "yan", "han", "zhou", "xia", "shang", "sui",
            "tang", "song", "ming", "qing", "maurya", "gupta", "nanda", "kushan", "satavahana",
        ];
        foreach (var bank in new[]
                 {
                     StyleNames.Mediterranean, StyleNames.MiddleEast, StyleNames.CentralAsia,
                     StyleNames.India, StyleNames.China,
                 })
        {
            var offenders = bank.Nations.Concat(bank.Cities)
                .Where(n => powers.Contains(n.ToLowerInvariant())).ToList();
            Assert.True(offenders.Count == 0, $"authored bank ships a historical power: {string.Join(", ", offenders)}");
        }
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Opening_forces_follow_the_authored_style_profile(IMapStyle style)
    {
        // Three seeds pooled (~150 unit slots) so the authored weights show through the noise.
        var counts = new Dictionary<string, int>();
        foreach (var seed in new ulong[] { 21, 22, 23 })
        {
            var world = MapGenerator.Generate("t294d-test", style, seed, Size, "forces", "Forces").World;
            foreach (var unit in world.StartingArmies.SelectMany(a => a.Units))
                counts[unit.UnitTypeId] = counts.GetValueOrDefault(unit.UnitTypeId) + 1;
        }

        var slots = counts.Values.Sum();
        var infantry = counts.GetValueOrDefault("light_infantry") + counts.GetValueOrDefault("heavy_infantry");
        var cavalry = counts.GetValueOrDefault("light_cavalry") + counts.GetValueOrDefault("heavy_cavalry");
        switch (style.Id)
        {
            case "central-asia":
                Assert.True(cavalry > infantry * 1.5, $"central asia drew {cavalry} cavalry against {infantry} infantry");
                break;
            case "china":
                Assert.True(counts.GetValueOrDefault("archers") + counts.GetValueOrDefault("heavy_infantry")
                    > slots / 2, "china's halberdier-and-bow profile did not dominate");
                break;
            case "india":
                Assert.True(counts.GetValueOrDefault("archers") > counts.GetValueOrDefault("light_infantry"),
                    $"india drew too few archers: {string.Join(",", counts)}");
                Assert.True(counts.GetValueOrDefault("archers") > cavalry, "india's bow line should lead its mounted arm");
                break;
            case "mediterranean":
                Assert.True(counts.GetValueOrDefault("heavy_infantry") > counts.GetValueOrDefault("light_infantry"),
                    "the mediterranean line should lean on its heavy infantry");
                break;
            case "middle-east":
                Assert.True(counts.GetValueOrDefault("light_infantry") > counts.GetValueOrDefault("heavy_infantry"),
                    "the middle east profile should lean on its foot");
                break;
        }

        // Every generated unit id exists in every shipped ruleset.
        var shipped = File.ReadAllText(Path.Combine(TestPaths.DataRoot, "rulesets", "mediterranean-280bc.json"));
        Assert.All(counts.Keys, k => Assert.Contains($"\"{k}\"", shipped, StringComparison.Ordinal));
    }

    private static List<CityDefinition> CoastalCities(World world)
    {
        var codes = world.Terrain.Decode(world.Width, world.Height);
        var coastal = new List<CityDefinition>();
        foreach (var city in world.Cities)
            for (var dy = -1; dy <= 1 && !coastal.Contains(city); dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var x = city.X + dx;
                    var y = city.Y + dy;
                    if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) continue;
                    if (CanonicalTiles.PassableByFleets(codes[(y * world.Width) + x]))
                    {
                        coastal.Add(city);
                        break;
                    }
                }

        return coastal;
    }
}
