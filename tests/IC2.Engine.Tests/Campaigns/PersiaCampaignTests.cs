using System.Text.Json;
using IC2.Engine.Ai;
using IC2.Engine.Barbarians;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using IC2.Slice.UI;
using IC2.Tools.TerrainImport;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Campaigns;

/// <summary>
/// T293.2 (#1838): the Persia campaigns, c.550 BC and 480 BC, on the shared Near East revision-2
/// terrain. Owner decisions 2026-11-15 (docs/design/persia-campaigns.md): the two seat lists,
/// Persis the default human seat at 550 and Athens at 480, a twenty-year total-conquest horizon,
/// and wars in progress at both openings.
/// </summary>
public sealed class PersiaCampaignTests(ITestOutputHelper output)
{
    private static ResolvedScenario Resolve(int year) => GameDataRepository.Load(TestPaths.DataRoot).Resolve($"persia-{year}bc");

    [Theory]
    [InlineData(550, 15)]
    [InlineData(480, 15)]
    public void Historical_campaign_resolves_with_dated_centres_valid_seats_and_native_roundtrip(int year, int playable)
    {
        var r = Resolve(year);
        Assert.True(r.Scenario.Listed);
        Assert.Equal(playable + 1, r.World.Nations.Count);
        Assert.Equal(playable, GameSessionFactory.SeatOptionsFor(r).Count);
        Assert.InRange(r.World.Cities.Count, 80, 180);
        Assert.Equal(year, r.Ruleset.Calendar.StartYearBc);
        Assert.Equal(year - 20, r.Ruleset.Victory.HardEndYearBc);
        Assert.NotNull(r.Scenario.Barbarians);
        var state = GameStateFactory.CreateInitial(r.World, r.Ruleset, r.Scenario);
        GameDataValidation.Validate("campaign", state);
        Assert.False(state.NationById("barbarians")!.Eliminated);
        var occupied = state.Cities.Select(c => (c.X, c.Y)).ToHashSet();
        Assert.Equal(state.Cities.Count, occupied.Count);
        var cells = r.World.Terrain.Decode(r.World.Width, r.World.Height);
        foreach (var city in state.Cities)
            Assert.True(r.World.TileTypeByCode(cells[city.Y * r.World.Width + city.X])!.PassableByArmies, city.Name);
        foreach (var nation in state.Nations.Where(n => !n.IsBarbarian))
        {
            Assert.False(nation.Eliminated);
            Assert.Equal(nation.Id, state.CityById(nation.CapitalCityId!)!.Owner);
        }
        foreach (var army in state.Armies)
        {
            Assert.DoesNotContain((army.X, army.Y), occupied);
            Assert.True(r.World.TileTypeByCode(cells[army.Y * r.World.Width + army.X])!.PassableByArmies);
        }
        foreach (var fleet in state.Fleets)
        {
            Assert.DoesNotContain((fleet.X, fleet.Y), occupied);
            Assert.True(r.World.TileTypeByCode(cells[fleet.Y * r.World.Width + fleet.X])!.PassableByFleets);
        }
        var save = new SaveGame(GameDataSchema.CurrentVersion, "test", "test", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var restored = JsonSerializer.Deserialize<SaveGame>(GameJson.Serialize(save), GameJson.Options)!;
        GameDataValidation.Validate("campaign", restored);
        Assert.Equal(state, new GameSession(r.World, r.Ruleset, r.Scenario, restored).State);
    }

    [Theory]
    [InlineData(550)]
    [InlineData(480)]
    public void Numeric_rules_are_exactly_the_existing_Italy_rules_except_the_calendar(int year)
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var r = repository.Resolve($"persia-{year}bc").Ruleset;
        var baseline = repository.Resolve("italy-500bc").Ruleset;
        foreach (var unit in r.UnitTypes)
        {
            var old = baseline.UnitTypeById(unit.Id)!;
            Assert.Equal(old, unit with { Name = old.Name, Abbreviation = old.Abbreviation, Provenance = old.Provenance, BattalionLabel = null, VisualTypeId = null });
        }
        Assert.Equal(baseline, r with
        {
            Id = baseline.Id, Name = baseline.Name, Description = baseline.Description, Provenance = baseline.Provenance,
            UnitTypes = baseline.UnitTypes,
            Calendar = r.Calendar with { StartYearBc = baseline.Calendar.StartYearBc, Provenance = baseline.Calendar.Provenance },
            Victory = r.Victory with { HardEndYearBc = baseline.Victory.HardEndYearBc, Provenance = baseline.Victory.Provenance },
            MapMarkers = r.MapMarkers with { Provenance = baseline.MapMarkers.Provenance },
        });
    }

    [Theory]
    [InlineData(550, 1UL)]
    [InlineData(480, 1UL)]
    [InlineData(550, 2UL)]
    [InlineData(480, 2UL)]
    public void Two_hundred_real_AI_seat_turns_have_no_rejections_mismatches_or_full_round_stalls(int year, ulong seed)
    {
        var r = Resolve(year);
        var result = AiGameRunner.Run(r.World, r.Ruleset, r.Scenario, seed, turnCap: 200);
        output.WriteLine(result.Summary());
        Assert.Equal(0, result.CommandsRejected);
        Assert.Equal(0, result.ProjectionMismatches);
        Assert.Equal(0, result.FullRoundStalls);
        Assert.NotEqual("barbarians", result.WinningNationId);
        Assert.NotNull(result.FinalState.NationById("barbarians")!.LastBarbarianArrivalTurn);
        GameDataValidation.Validate("campaign checkpoint", result.FinalState);
        if (seed == 1UL)
        {
            var repeated = AiGameRunner.Run(r.World, r.Ruleset, r.Scenario, seed, turnCap: 200);
            Assert.Equal(result.FinalState, repeated.FinalState);
            Assert.Equal(result.Transcript, repeated.Transcript);
        }
    }

    [Theory]
    [InlineData(550)]
    [InlineData(480)]
    public void Every_owned_coastal_nation_can_order_a_fleet_through_the_existing_command(int year)
    {
        var r = Resolve(year);
        var state = GameStateFactory.CreateInitial(r.World, r.Ruleset, r.Scenario);
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), r.Ruleset, r.World, new RecordingEventSink());
        foreach (var nation in state.Nations.Where(n => !n.IsBarbarian))
        {
            var port = state.Cities.FirstOrDefault(c => c.Owner == nation.Id && CoastalCity.FirstAdjacentSeaTile(c, r.World) is not null);
            if (port is null) continue;
            var seat = state with { ActiveSeatIndex = state.TurnOrder.ToList().IndexOf(nation.Id) };
            var result = dispatcher.Dispatch(seat, new OrderFleetCommand(nation.Id, port.Id, r.Ruleset.Naval.OrderMinShips, "acceptance-fleet"));
            Assert.True(result.IsAccepted, $"{nation.Name}/{port.Name}: {result.Rejection?.Message}");
        }
    }

    [Theory]
    [InlineData(550)]
    [InlineData(480)]
    public void Guaranteed_boundary_arrival_uses_existing_forces_and_survives_native_reload(int year)
    {
        var r = Resolve(year);
        var scenario = r.Scenario with { Barbarians = r.Scenario.Barbarians! with { ChancePercent = 100 } };
        var state = GameStateFactory.CreateInitial(r.World, r.Ruleset, scenario);
        var after = BarbarianArrivalSystem.Apply(state, r.World, r.Ruleset, scenario,
            SplitMix64Rng.ForStream(42, "arrival"), new RecordingEventSink());
        var army = Assert.Single(after.Armies, a => a.Nation == "barbarians");
        Assert.True(army.X == 0 || army.X == r.World.Width - 1 || army.Y == 0 || army.Y == r.World.Height - 1);
        Assert.Equal(18000, army.TotalTroops);
        if (army.AboardFleetId is { } fleetId)
            Assert.Equal(army.Id, after.FleetById(fleetId)!.CarriedArmyId);
        GameDataValidation.Validate("boundary arrival", after);
        var save = new SaveGame(after.SchemaVersion, "test", "test", after.ScenarioId, after.WorldId, after.RulesetId, after);
        var reloaded = JsonSerializer.Deserialize<SaveGame>(GameJson.Serialize(save), GameJson.Options)!;
        Assert.Equal(after, new GameSession(r.World, r.Ruleset, scenario, reloaded).State);
    }

    [Theory]
    [InlineData(550)]
    [InlineData(480)]
    public void Existing_embark_and_landing_commands_conserve_campaign_troops_and_resources(int year)
    {
        var r = Resolve(year);
        var state = GameStateFactory.CreateInitial(r.World, r.Ruleset, r.Scenario);
        var grid = r.World.Terrain.Decode(r.World.Width, r.World.Height);
        var fleet = state.Fleets[0];
        var army = state.Armies.First(a => a.Nation == fleet.Nation);
        var land = (from dy in Enumerable.Range(-1, 3) from dx in Enumerable.Range(-1, 3)
            let x = fleet.X + dx let y = fleet.Y + dy
            where x >= 0 && x < r.World.Width && y >= 0 && y < r.World.Height
                && r.World.TileTypeByCode(grid[y * r.World.Width + x])!.PassableByArmies
                && !state.Cities.Any(c => c.X == x && c.Y == y)
                && !state.Armies.Any(a => a.Id != army.Id && a.X == x && a.Y == y)
            select (X: x, Y: y)).First();
        army = army with { X = land.X, Y = land.Y, CoveredTileCode = grid[land.Y * r.World.Width + land.X] };
        state = state with { Armies = new(state.Armies.Select(a => a.Id == army.Id ? army : a)),
            ActiveSeatIndex = state.TurnOrder.ToList().IndexOf(army.Nation) };
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), r.Ruleset, r.World, new RecordingEventSink());
        var embarked = dispatcher.Dispatch(state, new EmbarkArmyCommand(army.Nation, army.Id, fleet.Id));
        Assert.True(embarked.IsAccepted, embarked.Rejection?.Message);
        Assert.Equal(fleet.Id, embarked.State.ArmyById(army.Id)!.AboardFleetId);
        Assert.Equal(army.Id, embarked.State.FleetById(fleet.Id)!.CarriedArmyId);
        var landed = dispatcher.Dispatch(embarked.State, new DisembarkArmyCommand(army.Nation, army.Id, land.X, land.Y));
        Assert.True(landed.IsAccepted, landed.Rejection?.Message);
        var after = landed.State.ArmyById(army.Id)!;
        Assert.Equal(army.TotalTroops, after.TotalTroops);
        Assert.Equal(army.Money, after.Money);
        Assert.Equal(army.SupplyTons, after.SupplyTons);
        Assert.Null(after.AboardFleetId);
        Assert.Null(landed.State.FleetById(fleet.Id)!.CarriedArmyId);
        Assert.Equal((army.X, army.Y), (after.X, after.Y));
    }

    [Theory]
    [InlineData(550)]
    [InlineData(480)]
    public void Centres_have_free_land_approaches_and_the_source_snapshot_dated_context(int year)
    {
        var r = Resolve(year);
        var state = GameStateFactory.CreateInitial(r.World, r.Ruleset, r.Scenario);
        var grid = r.World.Terrain.Decode(r.World.Width, r.World.Height);
        var occupied = state.Cities.Select(c => (c.X, c.Y)).Concat(state.Armies.Select(a => (a.X, a.Y))).ToHashSet();
        foreach (var city in state.Cities)
        {
            var approaches = from dy in Enumerable.Range(-1, 3) from dx in Enumerable.Range(-1, 3)
                let x = city.X + dx let y = city.Y + dy
                where x >= 0 && x < r.World.Width && y >= 0 && y < r.World.Height && !occupied.Contains((x, y))
                    && r.World.TileTypeByCode(grid[y * r.World.Width + x])!.PassableByArmies
                select (x, y);
            Assert.True(approaches.Count() >= 2, $"{city.Name} needs more than one initially free approach.");
        }
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataRoot, "..", "tools", "Persia", "sources", "sites.json")));
        var sites = source.RootElement.GetProperty("sites").EnumerateArray().ToDictionary(s => s.GetProperty("id").GetString()!);
        foreach (var city in state.Cities)
        {
            var site = sites[city.Id];
            var uri = site.GetProperty("pleiades").GetString()!;
            if (uri.Length > 0) Assert.StartsWith("https://pleiades.stoa.org/places/", uri);
            var era = site.GetProperty("eras").GetProperty(year.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(city.Owner, era.GetProperty("owner").GetString());
            Assert.Contains(era.GetProperty("evidence").EnumerateArray(), e => e.GetProperty("from").GetInt32() <= -year && e.GetProperty("to").GetInt32() >= -year);
        }
        // Founded after the opening (Persepolis, begun about 515 BC) is never silently included.
        Assert.DoesNotContain(state.Cities, c => c.Id == "alexandria" || c.Id == "antioch" || c.Id == "messene");
        if (year == 550) Assert.DoesNotContain(state.Cities, c => c.Id is "persepolis" or "piraeus" or "amisus" or "thebes_greece" or "argos");
        else Assert.Contains(state.Cities, c => c.Id is "persepolis" or "piraeus" or "amisus");
    }

    /// <summary>The approved opening diplomacy: wars in progress (owner decision 2026-11-15).</summary>
    [Theory]
    [InlineData(550)]
    [InlineData(480)]
    public void The_opening_relations_carry_the_approved_wars(int year)
    {
        var r = Resolve(year);
        var war = r.Ruleset.Diplomacy.StateCodes.War;
        var peace = r.Ruleset.Diplomacy.StateCodes.Peace;
        var index = r.World.StartingRelations!.NationIds.ToList();
        int StateOf(string a, string b) => r.World.StartingRelations.Matrix![index.IndexOf(a)][index.IndexOf(b)];
        var wars = year == 550
            ? new (string a, string b)[] { ("persis", "media") }
            : new (string a, string b)[] { ("persia", "sparta"), ("persia", "athens"), ("persia", "corinth") };
        foreach (var (a, b) in wars) Assert.Equal(war, StateOf(a, b));
        foreach (var pair in new (string a, string b)[] { ("persis", "lydia"), ("persis", "babylon"), ("lydia", "media"), ("babylon", "egypt"), ("sparta", "athens"), ("sparta", "persis") })
            if (index.Contains(pair.a) && index.Contains(pair.b)) Assert.Equal(peace, StateOf(pair.a, pair.b));
        if (year == 480) Assert.Equal(peace, StateOf("persia", "boeotia"));
    }

    [Fact]
    public void Derived_sidecar_differs_from_the_shared_map_only_by_city_tiles_and_landing_aprons()
    {
        var shared = GameDataRepository.Load(TestPaths.DataRoot).Resolve("near-east-750bc").World;
        var baseCells = shared.Terrain.Decode(shared.Width, shared.Height);
        foreach (var year in new[] { 550, 480 })
        {
            var r = Resolve(year);
            Assert.Equal((shared.Width, shared.Height), (r.World.Width, r.World.Height));
            var cells = r.World.Terrain.Decode(r.World.Width, r.World.Height);
            var changed = Enumerable.Range(0, cells.Length).Count(i => cells[i] != baseCells[i]);
            Assert.InRange(changed, 0, 60);
        }
    }

    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>> OpeningCityCounts = new Dictionary<int, IReadOnlyDictionary<string, int>>
    {
        [550] = new Dictionary<string, int>
        {
            ["armenia"] = 3, ["athens"] = 4, ["babylon"] = 9, ["cappadocia"] = 8, ["cilicia"] = 4, ["cyprus"] = 6,
            ["egypt"] = 8, ["ionia"] = 9, ["judah"] = 5, ["lydia"] = 12, ["media"] = 7, ["persis"] = 3,
            ["phoenicia"] = 5, ["sparta"] = 8, ["susiana"] = 3,
        },
        [480] = new Dictionary<string, int>
        {
            ["argos"] = 3, ["athens"] = 7, ["boeotia"] = 3, ["caria"] = 5, ["cilicia"] = 4, ["corinth"] = 4,
            ["egypt"] = 8, ["ionia"] = 12, ["lycia"] = 6, ["macedon"] = 3, ["persia"] = 51, ["phoenicia_cyprus"] = 11,
            ["sparta"] = 4, ["thessaly"] = 4, ["thrace"] = 7,
        },
    };

    /// <summary>The v0.11.0 reviewer rule (F2): a data edit that thins a seat must not ship silently.</summary>
    [Theory]
    [InlineData(550)]
    [InlineData(480)]
    public void Every_seat_opens_with_its_pinned_number_of_cities(int year)
    {
        var actual = Resolve(year).World.Cities.GroupBy(c => c.Owner).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(OpeningCityCounts[year].OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
    }

    /// <summary>The default human seats (owner decision 2026-11-15) and the guides' "default opening" advice agree.</summary>
    [Theory]
    [InlineData(550, "persis")]
    [InlineData(480, "athens")]
    public void The_default_human_seat_matches_the_guide(int year, string defaultSeat)
    {
        var r = Resolve(year);
        Assert.Equal([defaultSeat], r.Scenario.Seats.Where(s => s.Control == SeatControl.Human).Select(s => s.Nation).ToArray());

        var guidePath = Path.Combine(TestPaths.DataRoot, "campaign-guides", $"persia-{year}bc.json");
        using var guide = JsonDocument.Parse(File.ReadAllText(guidePath));
        var named = guide.RootElement.GetProperty("powers").EnumerateArray()
            .Where(p => p.GetProperty("advice").GetString()!.Contains("default opening", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.GetProperty("nationId").GetString()!).ToArray();
        Assert.Equal([defaultSeat], named);
    }
}
