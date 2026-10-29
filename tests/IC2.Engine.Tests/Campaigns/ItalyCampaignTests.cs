using System.Text.Json.Nodes;
using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using IC2.Tools.TerrainImport;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Campaigns;

/// <summary>
/// T202: the Italy 500 BC campaign resolves, is offered on New Game, plays 200 all-AI seat-turns cleanly, rebuilds
/// its terrain from its source PNG, and gives every fact a provenance tag and every historical fact a source.
/// </summary>
public sealed class ItalyCampaignTests
{
    private const string Id = "italy-500bc";

    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    private static readonly string[] Tags = { "confirmed:", "derived:", "designed", "historical:", "open:" };

    [Fact]
    public void The_campaign_resolves_and_is_offered_on_New_Game()
    {
        var resolved = Repository.Resolve(Id);

        Assert.True(resolved.Scenario.Listed);
        Assert.Equal(12, resolved.World.Nations.Count);
        Assert.Equal((209, 233), (resolved.World.Width, resolved.World.Height));
        Assert.Equal(500, resolved.Ruleset.Calendar.StartYearBc);
        Assert.Equal(460, resolved.Ruleset.Victory.HardEndYearBc);
        Assert.Contains(RulesetPresets.CardsFor(Repository), card => card.ScenarioId == Id && card.DisplayName == "Italy, 500 BC");
    }

    [Fact]
    public void The_ruleset_is_the_classical_one_but_for_its_calendar_and_army_table()
    {
        var italy = Repository.RulesetById(Id)!;
        var classical = Repository.RulesetById("classical-faithful")!;

        var expected = classical with
        {
            Id = italy.Id,
            Name = italy.Name,
            Description = italy.Description,
            Calendar = classical.Calendar with { StartYearBc = 500, Provenance = italy.Calendar.Provenance },
            Victory = classical.Victory with { HardEndYearBc = 460, Provenance = italy.Victory.Provenance },
            MapMarkers = classical.MapMarkers with
            {
                CityPopulationTierThresholds = italy.MapMarkers.CityPopulationTierThresholds,
                Provenance = italy.MapMarkers.Provenance,
            },
            // T240 (#1601): the owner's decision keeps the Italy ruleset's army table in order.
            Flags = classical.Flags with
            {
                ArmyTableCompaction = ArmyTableCompactionPolicy.InOrder,
                Provenance = italy.Flags.Provenance,
            },
            Provenance = italy.Provenance,
        };
        Assert.Equal(expected, italy);
        Assert.Equal(new[] { 6, 10, 18 }, italy.MapMarkers.CityPopulationTierThresholds);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void An_all_AI_game_plays_200_seat_turns_with_no_rejection_mismatch_or_full_round_stall(ulong seed)
    {
        var resolved = Repository.Resolve(Id);

        var result = AiGameRunner.Run(resolved.World, resolved.Ruleset, resolved.Scenario, seed, turnCap: 200);

        Assert.Equal(0, result.CommandsRejected);
        Assert.Equal(0, result.ProjectionMismatches);
        Assert.Equal(0, result.FullRoundStalls);
        Assert.True(result.TurnsPlayed > 0);
    }

    [Fact]
    public void The_terrain_sidecar_is_the_source_PNG_imported()
    {
        var world = Repository.WorldById(Id)!;
        var png = File.ReadAllBytes(Path.Combine(ModelTestPaths.RepositoryRoot, "tools", "Italy", "italy-500bc.png"));

        var codes = TerrainImporter.CodesFrom(PngCodec.Read(png), world.Width, world.Height);

        var committed = File.ReadAllText(Path.Combine(ModelTestPaths.DataRoot, "worlds", Id + ".terrain.b64"));
        Assert.Equal(committed, TerrainImporter.Sidecar(codes));
    }

    /// <summary>
    /// The world and scenario are this campaign's own, so every entry is checked. The ruleset is a copy of the
    /// classical one, whose inherited entries predate the tag convention; only the entries this campaign wrote are.
    /// </summary>
    [Theory]
    [InlineData("worlds")]
    [InlineData("scenarios")]
    public void Every_provenance_entry_carries_a_tag(string folder)
    {
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(ModelTestPaths.DataRoot, folder, Id + ".json")))!;

        foreach (var (path, text) in ProvenanceEntries(document, "$"))
        {
            Assert.True(Tags.Any(t => text.StartsWith(t, StringComparison.Ordinal)), $"{path} has no provenance tag: '{text}'");
        }
    }

    [Fact]
    public void The_rulesets_own_changes_are_marked_designed()
    {
        var ruleset = JsonNode.Parse(File.ReadAllText(Path.Combine(ModelTestPaths.DataRoot, "rulesets", Id + ".json")))!;

        foreach (var text in new[]
        {
            (string?)ruleset["_provenance"]!["_group"],
            (string?)ruleset["calendar"]!["_provenance"]!["startYearBc"],
            (string?)ruleset["victory"]!["_provenance"]!["hardEndYearBc"],
        })
        {
            Assert.StartsWith("designed", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_city_and_nation_cites_its_historical_source()
    {
        var world = JsonNode.Parse(File.ReadAllText(Path.Combine(ModelTestPaths.DataRoot, "worlds", Id + ".json")))!;

        foreach (var key in new[] { "cities", "nations" })
        {
            foreach (var entry in world[key]!.AsArray())
            {
                var group = (string?)entry!["_provenance"]?["_group"] ?? string.Empty;
                Assert.True(group.StartsWith("historical:", StringComparison.Ordinal), $"{key} '{entry["id"]}' has no historical source");
                Assert.Matches(@"\(([A-Z][A-Za-z ,.]+ )?[0-9]|Herodotus|Livy|Strabo|Thucydides|Polybius|Pliny|Barrington", group);
            }
        }
    }

    private static IEnumerable<(string Path, string Text)> ProvenanceEntries(JsonNode? node, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key == "_provenance" && value is JsonObject provenance)
                    {
                        foreach (var (field, text) in provenance)
                        {
                            yield return ($"{path}._provenance.{field}", (string?)text ?? string.Empty);
                        }
                    }
                    else
                    {
                        foreach (var inner in ProvenanceEntries(value, $"{path}.{key}"))
                        {
                            yield return inner;
                        }
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    foreach (var inner in ProvenanceEntries(array[i], $"{path}[{i}]"))
                    {
                        yield return inner;
                    }
                }

                break;
        }
    }
}
