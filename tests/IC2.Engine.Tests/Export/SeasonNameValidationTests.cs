using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// DoD 10 (folded follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/99">#99</see>):
/// a ruleset whose <c>newsLog.seasonNames</c> list does not have exactly one name per
/// <c>calendar.seasonsPerYear</c> season must be rejected, naming the counts, rather than crashing
/// the first time a round-tick header indexes it.
/// </summary>
/// <remarks>
/// <see cref="Ruleset.ValidateSeasonNames"/>'s own doc comment explains the check itself.
/// <see cref="GameDataValidation.ValidateRuleset"/> (added by this task's widened Owns grant,
/// <c>docs/task-catalogue.md</c> T29 DoD 10) calls it from <c>GameDataLoader.Load</c>'s own
/// validation step, so a bad file now fails **at load** — before any document deserialized from it
/// is handed back to a caller, let alone reaches a round tick. Most of the tests below exercise
/// <see cref="Ruleset.ValidateSeasonNames"/> directly (a focused unit test of the check's own
/// logic); <see cref="A_short_season_name_list_fails_at_GameDataLoader_Load_not_later"/> is the one
/// that proves the wiring itself, by going through the public loader the same way every other
/// document in this engine is read.
/// </remarks>
public class SeasonNameValidationTests
{
    /// <summary>The shipped classical-faithful ruleset's own season-name list already agrees with its calendar.</summary>
    [Fact]
    public void Shipped_classical_faithful_ruleset_has_a_matching_season_name_count()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.RulesetFile);

        ruleset.ValidateSeasonNames(); // must not throw
        Assert.Equal(ruleset.Calendar.SeasonsPerYear, ruleset.NewsLog.SeasonNames.Count);
    }

    /// <summary>The toy ruleset's season-name list also already agrees with its calendar.</summary>
    [Fact]
    public void Toy_ruleset_has_a_matching_season_name_count()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);

        ruleset.ValidateSeasonNames(); // must not throw
    }

    /// <summary>
    /// Mutation proof: a season-name list one entry SHORT of the calendar's season count is
    /// rejected, naming both counts.
    /// </summary>
    [Fact]
    public void A_short_season_name_list_is_rejected_naming_both_counts()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        Assert.Equal(4, ruleset.Calendar.SeasonsPerYear);

        var mutated = ruleset with
        {
            NewsLog = ruleset.NewsLog with { SeasonNames = ValueList.Of("Spring", "Summer", "Autumn") },
        };

        var ex = Assert.Throws<InvalidOperationException>(mutated.ValidateSeasonNames);
        Assert.Contains("3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("4", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Mutation proof: a season-name list one entry LONG of the calendar's season count is also rejected.</summary>
    [Fact]
    public void A_long_season_name_list_is_rejected()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);

        var mutated = ruleset with
        {
            NewsLog = ruleset.NewsLog with { SeasonNames = ValueList.Of("Spring", "Summer", "Autumn", "Winter", "Extra") },
        };

        Assert.Throws<InvalidOperationException>(mutated.ValidateSeasonNames);
    }

    /// <summary>An empty season-name list is rejected exactly like any other mismatched count.</summary>
    [Fact]
    public void An_empty_season_name_list_is_rejected()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);

        var mutated = ruleset with
        {
            NewsLog = ruleset.NewsLog with { SeasonNames = ValueList<string>.Empty },
        };

        Assert.Throws<InvalidOperationException>(mutated.ValidateSeasonNames);
    }

    /// <summary>
    /// DoD 10's actual requirement: a ruleset file whose <c>newsLog.seasonNames</c> list disagrees
    /// with its <c>calendar.seasonsPerYear</c> fails <strong>at <c>GameDataLoader.Load</c></strong> --
    /// the same call every other document in this engine goes through to reach a caller -- naming
    /// the document path and the mismatch, rather than deserializing successfully and only failing
    /// later (e.g. the first time a round tick indexes the list by season). This is the test that
    /// depends on <see cref="GameDataValidation.ValidateRuleset"/>'s call to
    /// <see cref="Ruleset.ValidateSeasonNames"/>; every other test in this file exercises the check
    /// directly and would still pass even if that wiring were removed.
    /// </summary>
    [Fact]
    public void A_short_season_name_list_fails_at_GameDataLoader_Load_not_later()
    {
        var goodJson = File.ReadAllText(TestPaths.ToyRulesetFile);
        var badJson = goodJson.Replace(
            "\"seasonNames\": [\"Spring\", \"Summer\", \"Autumn\", \"Winter\"],",
            "\"seasonNames\": [\"Spring\", \"Summer\", \"Autumn\"],");
        Assert.NotEqual(goodJson, badJson); // the replacement actually matched something

        var ex = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<Ruleset>("mutated-toy-ruleset.json", badJson));

        Assert.Contains("mutated-toy-ruleset.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("4", ex.Message, StringComparison.Ordinal);

        // The good file, unmodified, still loads cleanly through the very same call.
        var loaded = GameDataLoader.Load<Ruleset>("toy-ruleset.json", goodJson);
        Assert.Equal(4, loaded.NewsLog.SeasonNames.Count);
    }
}
