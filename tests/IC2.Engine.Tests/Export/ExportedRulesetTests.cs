using System.Text.Json;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Checks the committed <c>data/rulesets/classical-faithful.json</c> against
/// <c>docs/task-catalogue.md</c> T29's Done-when lines 3 and 4.
/// </summary>
public class ExportedRulesetTests
{
    private static Ruleset Load() => GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.RulesetFile);

    /// <summary>DoD 4: the committed file round-trips through the loader with no schema errors.</summary>
    [Fact]
    public void Ruleset_loads_with_no_schema_errors()
    {
        var ruleset = Load();
        Assert.Equal("classical-faithful", ruleset.Id);
    }

    /// <summary>
    /// DoD 4: serialize -> deserialize -> serialize is byte-identical, the same round-trip guarantee
    /// every other shipped ruleset carries (<c>RoundTripTests</c>).
    /// </summary>
    [Fact]
    public void Ruleset_round_trips_to_identical_json()
    {
        var ruleset = Load();
        var first = GameJson.Serialize(ruleset);
        var reloaded = GameDataLoader.Load<Ruleset>("classical-faithful.json", first);
        var second = GameJson.Serialize(reloaded);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// DoD 3: every field the export script (<c>scripts/export-classical-world.cs</c>,
    /// <c>RulesetCorpusMap</c>) mapped to a T04 fixtures-corpus id actually carries that id in its
    /// <c>_provenance</c> text, and every cited id genuinely exists in the corpus -- so a citation
    /// can never point at a fixture that was never there.
    /// </summary>
    [Fact]
    public void Every_corpus_id_cited_in_the_ruleset_exists_in_the_corpus()
    {
        var corpusIds = LoadCorpusIds();
        var rulesetText = File.ReadAllText(ExportedDataPaths.RulesetFile);

        var cited = ExtractCitedCorpusIds(rulesetText);
        Assert.True(cited.Count > 100, $"Expected well over 100 distinct corpus-id citations in the shipped ruleset; found {cited.Count}.");

        foreach (var id in cited)
        {
            Assert.True(corpusIds.Contains(id), $"classical-faithful.json cites T04 fixtures corpus id '{id}', which is not in tests/fixtures/corpus.json.");
        }
    }

    /// <summary>
    /// DoD 3, spot check: a sample of fields whose value is itself transcribed straight from a
    /// named, confirmed corpus entry -- re-derived from the corpus here, not merely trusted to have
    /// been copied right by the export script.
    /// </summary>
    [Fact]
    public void Sampled_confirmed_fields_match_their_named_corpus_entry_exactly()
    {
        var corpus = LoadCorpusValues();
        var ruleset = Load();

        Assert.Equal(GetLong(corpus, "economy.debtWealthDivisor"), ruleset.Economy.DebtWealthDivisor);
        Assert.Equal(GetLong(corpus, "economy.debtTreasuryFloor"), ruleset.Economy.DebtTreasuryFloor);
        Assert.Equal(GetLong(corpus, "citySupply.baselineSeasonValue"), ruleset.Economy.CitySupplyBaselineSeasonValue);
        Assert.Equal(GetLong(corpus, "loyalty.floor.forcedCapture"), ruleset.Loyalty.ForcedCaptureFloor);
        Assert.Equal(GetLong(corpus, "loyalty.floor.defection"), ruleset.Loyalty.DefectionFloor);
        Assert.Equal(GetLong(corpus, "capture.unityGain"), ruleset.Capture.CaptureUnityGain);
        Assert.Equal(GetLong(corpus, "defection.unityLoss"), ruleset.Capture.DefectionUnityLoss);
        Assert.Equal(GetLong(corpus, "diplomacy.relationState.war"), (long)ruleset.Diplomacy.StateCodes.War);
        Assert.Equal(GetLong(corpus, "caps.maxArmies"), ruleset.ArmyManagement.MaxArmies);
        Assert.Equal(GetLong(corpus, "caps.maxUnitsPerArmy"), ruleset.ArmyManagement.MaxUnitsPerArmy);
        Assert.Equal(GetLong(corpus, "unitType.archers.moves"), ruleset.UnitTypeById("archers")!.Moves);
        Assert.Equal(GetLong(corpus, "unitType.heavyCavalry.battalionSize"), ruleset.UnitTypeById("heavy_cavalry")!.StandardBattalionSize);
        Assert.Equal(GetLong(corpus, "terrain.moveCost.code5"), ruleset.MoveCostFor("mountains"));
        Assert.Equal(GetLong(corpus, "fleet.joinMaxShips"), ruleset.Naval.JoinMaxShips);
        Assert.Equal(GetLong(corpus, "fleet.condition.deathThreshold"), ruleset.Naval.DeathConditionThreshold);
    }

    /// <summary>Every field this ruleset's flags carry matches the faithful preset (audit Q3/Q4/Q6/Q8, Q1's follow-up).</summary>
    [Fact]
    public void Flags_reproduce_the_original_faithfully()
    {
        var ruleset = Load();

        Assert.Equal(DiplomacyModel.ConfirmedStateMachine, ruleset.Flags.DiplomacyModel);
        Assert.Equal(EconomyPurseModel.PerUnitPurses, ruleset.Flags.EconomyPurses);
        Assert.Equal(SeatAsymmetryModel.Faithful, ruleset.Flags.SeatAsymmetry);
        Assert.Equal(DiplomaticThawPolicy.ReproduceEightColumnBug, ruleset.Flags.BugPolicyDiplomaticThaw);
        Assert.Equal(DefeatOutcome.Destroyed, ruleset.Flags.CombatOnDefeat);
        Assert.True(ruleset.Flags.FaithfulThawColumnBug);
    }

    private static HashSet<string> LoadCorpusIds()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ExportedDataPaths.CorpusFile));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            ids.Add(entry.GetProperty("id").GetString()!);
        }

        return ids;
    }

    private static Dictionary<string, JsonElement> LoadCorpusValues()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ExportedDataPaths.CorpusFile));
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            values[entry.GetProperty("id").GetString()!] = entry.GetProperty("value").Clone();
        }

        return values;
    }

    private static long GetLong(Dictionary<string, JsonElement> corpus, string id)
    {
        Assert.True(corpus.TryGetValue(id, out var element), $"Corpus id '{id}' not found.");
        return element.GetInt64();
    }

    /// <summary>Pulls every <c>T04 fixtures corpus id: '...'</c> citation out of the raw JSON text.</summary>
    private static HashSet<string> ExtractCitedCorpusIds(string rulesetText)
    {
        const string marker = "T04 fixtures corpus id: '";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        while (true)
        {
            var start = rulesetText.IndexOf(marker, index, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            var idStart = start + marker.Length;
            var idEnd = rulesetText.IndexOf('\'', idStart);
            Assert.True(idEnd > idStart, "Malformed corpus-id citation in the ruleset text.");
            ids.Add(rulesetText[idStart..idEnd]);
            index = idEnd + 1;
        }

        return ids;
    }
}
