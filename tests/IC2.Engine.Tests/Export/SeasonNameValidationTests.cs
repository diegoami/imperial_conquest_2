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
/// <see cref="Ruleset.ValidateSeasonNames"/>'s own doc comment explains why this check is proved
/// here, directly against the method, rather than through <c>GameDataLoader.LoadFile</c> failing at
/// load: wiring it into the loader needs a one-line addition to
/// <c>IC2.Engine.Serialization.GameDataValidation.ValidateRuleset</c>, a file outside this task's
/// Owns list. This test proves the check itself is correct and ready for that wiring.
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
}
