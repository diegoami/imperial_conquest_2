using IC2.Engine.Core;
using IC2.Engine.MapGen;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.MapGen;

/// <summary>
/// T294b (#1843): the generator core's property tests — determinism, retry with a bounded attempt
/// count, failures naming their check, many-seed generation, and the canonical tile table staying
/// pinned to the shipped data. The styles here are test fixtures, not shipped recipes (the five
/// shipped styles are T294c).
/// </summary>
public sealed class MapGeneratorTests
{
    private static readonly MapSize Size = new(200, 72);

    /// <summary>Deterministic row-of-islands archipelago: every seat on its own bridged island, fleets on the shore.</summary>
    private sealed class ArchipelagoStyle(int seats, bool broken) : IMapStyle
    {
        public string Id => broken ? "test-broken-archipelago" : "test-archipelago";

        public GeneratedMap Compose(MapSeedStream rng, MapSize size, IReadOnlyDictionary<string, string> parameters)
        {
            var codes = new int[size.Cells]; // all sea
            var islands = rng.Stream("islands");
            var margin = 8;
            var spacing = (size.Width - (2 * margin)) / seats;
            var cy = size.Height / 2;
            var centres = new List<(int X, int Y)>();
            for (var i = 0; i < seats; i++)
            {
                var cx = margin + (spacing * i) + (spacing / 2);
                var dy = islands.NextInt(-4, 5);
                PaintIsland(codes, size, cx, cy + dy, 5, islands);
                centres.Add((cx, cy + dy));
            }

            if (!broken)
                for (var i = 0; i + 1 < centres.Count; i++)
                {
                    // A sloped bridge: it must land inside both islands whatever the y-jitter did,
                    // so the two banks stay one land component.
                    var (ax, ay) = centres[i];
                    var (bx, by) = centres[i + 1];
                    for (var x = ax; x <= bx; x++)
                    {
                        var f = (double)(x - ax) / (bx - ax);
                        var y = (int)Math.Round(ay + ((by - ay) * f));
                        codes[(y * size.Width) + x] = CanonicalTiles.Plain;
                    }
                }

            var nations = new List<NationDefinition>();
            var cities = new List<CityDefinition>();
            var armies = new List<StartingArmy>();
            var fleets = new List<StartingFleet>();
            for (var i = 0; i < seats; i++)
            {
                var (ccx, ccy) = centres[i];
                codes[(ccy * size.Width) + ccx] = CanonicalTiles.Plain;
                codes[(ccy * size.Width) + ccx + 1] = CanonicalTiles.Plain;
                nations.Add(new NationDefinition($"seat-{i}", $"Seat {i}", Palette[i % Palette.Length],
                    "the test king", $"city-{i}", 3000, 760, 30000, 28, 10, 20, 10));
                cities.Add(new CityDefinition($"city-{i}", $"City {i}", ccx, ccy, $"seat-{i}", $"seat-{i}", 80,
                    280, 80, 10, 15, 7, new ValueList<UnitSlot>(Array.Empty<UnitSlot>())));
                armies.Add(new StartingArmy($"seat-{i}-army-1", $"seat-{i}", ccx + 1, ccy, 70, 100, 200, 8,
                    new ValueList<UnitSlot>(new List<UnitSlot>
                    {
                        new(0, "heavy_infantry", 6000, 6, "1st Halberdier Battalion"),
                        new(0, "archers", 4000, 6, "1st Crossbow Battalion"),
                    })));
                var fy = ccy - 6;
                if (fy >= 0 && CanonicalTiles.PassableByFleets(codes[(fy * size.Width) + ccx]))
                    fleets.Add(new StartingFleet($"seat-{i}-fleet-1", $"seat-{i}", ccx, fy, 60, 100, 200, 600, 25));
            }

            return new GeneratedMap(size.Width, size.Height, codes,
                new ValueList<NationDefinition>(nations), new ValueList<CityDefinition>(cities),
                new ValueList<StartingArmy>(armies), new ValueList<StartingFleet>(fleets),
                new ValueList<string>(nations.Select(n => n.Id).ToList()));
        }

        private static void PaintIsland(int[] codes, MapSize size, int cx, int cy, int radius, SplitMix64Rng rng)
        {
            for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if ((dx * dx) + (dy * dy) > radius * radius) continue;
                    var x = cx + dx;
                    var y = cy + dy;
                    if (x < 0 || y < 0 || x >= size.Width || y >= size.Height) continue;
                    var roll = rng.NextInt(100);
                    codes[(y * size.Width) + x] = roll < 12 ? CanonicalTiles.Forest
                        : roll < 18 ? CanonicalTiles.Mountains : CanonicalTiles.Plain;
                }
        }
    }

    /// <summary>All water on attempts 1-3, valid afterwards: exercises the bounded retry.</summary>
    private sealed class FlakyStyle : IMapStyle
    {
        private readonly ArchipelagoStyle _inner = new(3, broken: false);

        public string Id => "test-flaky";

        public GeneratedMap Compose(MapSeedStream rng, MapSize size, IReadOnlyDictionary<string, string> parameters) =>
            rng.Attempt <= 3
                ? new GeneratedMap(size.Width, size.Height, new int[size.Cells],
                    new ValueList<NationDefinition>(Array.Empty<NationDefinition>()),
                    new ValueList<CityDefinition>(Array.Empty<CityDefinition>()),
                    new ValueList<StartingArmy>(Array.Empty<StartingArmy>()),
                    new ValueList<StartingFleet>(Array.Empty<StartingFleet>()),
                    new ValueList<string>(Array.Empty<string>()))
                : _inner.Compose(rng, size, parameters);
    }

    private static readonly string[] Palette = ["#B23A2A", "#395A93", "#4E8A6B", "#2E7A76", "#6A3C88"];

    [Fact]
    public void Many_seeds_generate_valid_worlds_that_differ_across_seeds()
    {
        var worlds = new List<World>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var generated = MapGenerator.Generate("t294b-test", new ArchipelagoStyle(5, broken: false), seed, Size,
                $"test-{seed}", $"Test world {seed}", new Dictionary<string, string> { ["seats"] = "5" });
            Assert.Equal(seed, generated.Seed);
            Assert.Equal(5, generated.World.Cities.Count);
            Assert.Equal(5, generated.World.Nations.Count); // the style ships exactly the seats; no reserved role in the core
            var decoded = generated.World.Terrain.Decode(generated.World.Width, generated.World.Height);
            Assert.Equal(generated.World.Width * generated.World.Height, decoded.Length);
            worlds.Add(generated.World);
        }

        Assert.Equal(20, worlds.Count);
        Assert.True(worlds.Distinct().Count() >= 18, "different seeds should compose different worlds");
    }

    [Fact]
    public void The_same_seed_composes_the_same_world_twice()
    {
        var a = MapGenerator.Generate("t294b-test", new ArchipelagoStyle(5, broken: false), 7, Size,
            "test-a", "Test A", new Dictionary<string, string> { ["seats"] = "5" });
        var b = MapGenerator.Generate("t294b-test", new ArchipelagoStyle(5, broken: false), 7, Size,
            "test-a", "Test A", new Dictionary<string, string> { ["seats"] = "5" });
        Assert.Equal(a.World, b.World);
        Assert.Equal(a.Seed, b.Seed);
        Assert.Equal(a.Attempt, b.Attempt);
    }

    [Fact]
    public void A_broken_style_fails_naming_its_check_after_the_bounded_attempts()
    {
        var failure = Assert.Throws<MapGenerationFailed>(() =>
            MapGenerator.Generate("t294b-test", new ArchipelagoStyle(5, broken: true), 3, Size,
                "test-broken", "Test broken", new Dictionary<string, string> { ["seats"] = "5" }));
        Assert.Contains("connectivity", failure.Message);
        Assert.Contains("20 attempts", failure.Message);
        Assert.NotEmpty(failure.Problems);
    }

    [Fact]
    public void An_invalid_candidate_is_retried_until_one_validates()
    {
        var generated = MapGenerator.Generate("t294b-test", new FlakyStyle(), 5, Size,
            "test-flaky", "Test flaky", new Dictionary<string, string> { ["seats"] = "3" });
        Assert.Equal(4, generated.Attempt);
        Assert.Equal(3, generated.World.Cities.Count);
    }

    [Fact]
    public void The_validator_names_problems_on_a_hand_built_map()
    {
        // All water: no land at all, plus a nation with no centres.
        var map = new GeneratedMap(8, 8, new int[64],
            new ValueList<NationDefinition>(new List<NationDefinition>
            {
                new("lonely", "Lonely", "#B23A2A", "the test king", "city-0", 100, 760, 100, 4, 10, 20, 5),
            }),
            new ValueList<CityDefinition>(new List<CityDefinition>
            {
                new("city-0", "City 0", 3, 3, "lonely", "lonely", 80, 100, 80, 5, 8, 3,
                    new ValueList<UnitSlot>(Array.Empty<UnitSlot>())),
            }),
            new ValueList<StartingArmy>(Array.Empty<StartingArmy>()),
            new ValueList<StartingFleet>(Array.Empty<StartingFleet>()),
            new ValueList<string>(new List<string> { "lonely" }));
        var problems = MapValidator.Validate(map);
        Assert.Contains(problems, p => p.StartsWith("terrain:", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("settlements:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_canonical_tile_table_stays_pinned_to_the_shipped_italy_table()
    {
        var shipped = GameDataRepository.Load(TestPaths.DataRoot).Resolve("italy-500bc").World.TileTypes;
        var mine = CanonicalTiles.Values;
        Assert.Equal(shipped.Select(t => (t.Id, t.Code, t.Name, t.PassableByArmies, t.PassableByFleets)),
            mine.Select(t => (t.Id, t.Code, t.Name, t.PassableByArmies, t.PassableByFleets)));
    }

    [Fact]
    public void Generated_terrain_round_trips_through_the_sidecar_encoding()
    {
        var generated = MapGenerator.Generate("t294b-test", new ArchipelagoStyle(3, broken: false), 11, Size,
            "test-roundtrip", "Test roundtrip", new Dictionary<string, string> { ["seats"] = "3" });
        var decoded = generated.World.Terrain.Decode(generated.World.Width, generated.World.Height);
        var reencoded = CanonicalTiles.Encode(decoded);
        Assert.Equal(generated.World.Terrain.Data, reencoded);
    }
}
