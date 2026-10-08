using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T140 (docs/tasks/T140.md, Done-when 3): the context panel's nation status panel against the live
/// state. The own nation (Rome) carries the full list — Population read from <see cref="NationState.Wealth"/>
/// (bug #742), Unity as a word, every relation in the matrix including the foreign-nation ones, and
/// the training section. A foreign nation (Carthage, viewed by Rome) shows Population, Unity, Tax
/// rate, every relation, with the conquered-nation row flagged red and the rest plain.
/// </summary>
/// <remarks>
/// Every expected value is read from the same <see cref="GameSession"/> the panel would draw, so the
/// test pins the model's own choice of fields rather than a hand-copied number. The foreign panel's
/// "every relation" assertions are the load-bearing half: a future regression that drops the per-nation
/// list to a single viewer cell breaks the foreign panel's row count.
/// </remarks>
public sealed class NationStatusModelTests
{
    private const string RomeId = "rome";
    private const string CarthageId = "carthage";

    private static GameSession RomeSession(ulong seed = 1)
    {
        var classical = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");
        return new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: seed, humanSeatNationId: RomeId);
    }

    private static IReadOnlyDictionary<string, string> ByKey(IReadOnlyList<NationStatusLine> lines) =>
        lines.ToDictionary(line => line.Key, line => line.Text, StringComparer.Ordinal);

    private static string ExpectedRelationWord(int value)
    {
        // The original's words (N11): 1 trade, 2 ally, 3 war; below or equal 0 and above 3 print nothing.
        if (value <= 0 || value > 3)
        {
            return string.Empty;
        }

        return value switch
        {
            1 => "trade",
            2 => "ally",
            3 => "war",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Rome's own panel: every key the new model emits, with each value read back from the live state
    /// — the report §1 list, the research-read panels, and bug #742's Population = Wealth fix.
    /// </summary>
    [Fact]
    public void The_own_nations_panel_lists_the_research_read_full_list()
    {
        var session = RomeSession();
        var state = session.State;
        var rome = state.NationById(RomeId)!;
        var capital = state.CityById(rome.CapitalCityId!)!;

        var lines = NationStatusModel.Build(state, session.Ruleset, RomeId, viewerNationId: RomeId);
        var byKey = ByKey(lines);

        Assert.Equal($"Leader: {rome.LeaderName}", byKey[NationStatusModel.LeaderKey]);
        Assert.Equal($"Capital: {capital.Name}", byKey[NationStatusModel.CapitalKey]);
        Assert.Equal($"Cities: {state.CountCitiesOwnedBy(RomeId)}", byKey[NationStatusModel.CitiesKey]);

        // N05: Population = the nation's stored wealth field (3000 × Σ city populations in
        // thousands), per the research read; bug #742's fix.
        Assert.Equal($"Population: {rome.Wealth}", byKey[NationStatusModel.PopulationKey]);
        Assert.Equal(2_577_000, rome.Wealth);

        // N06: Unity is the band word, not the number.
        Assert.Equal($"Unity: {InformationWords.Unity(rome.Unity)}", byKey[NationStatusModel.UnityKey]);

        Assert.Equal($"Tax rate: {rome.TaxRatePercent}%", byKey[NationStatusModel.TaxRateKey]);
        Assert.Equal($"Mobilized: {rome.MobilizedPercent}%", byKey[NationStatusModel.MobilizedKey]);
        Assert.Equal($"Treasury: {rome.Treasury}", byKey[NationStatusModel.TreasuryKey]);

        // N11: every other nation has a relation row. The text uses the original's words
        // (trade/ally/war) on a "Name: word" line, or just the name when the value is <= 0
        // (peace, or a cooldown counter).
        var relationLines = lines.Where(line =>
            line.Key.StartsWith(NationStatusModel.RelationKeyPrefix, StringComparison.Ordinal)).ToList();
        Assert.Equal(state.Nations.Count - 1, relationLines.Count);
        foreach (var other in state.Nations.Where(nation => !string.Equals(nation.Id, RomeId, StringComparison.Ordinal)))
        {
            var value = state.Relations.Get(RomeId, other.Id);
            if (other.Eliminated)
            {
                // N12: the conquered-nation row is the line in red. The relation lookup is still
                // there underneath, but the row's text is the conquerred line, not the relation.
                var conqueror = state.NationById(other.ConqueredBy!)?.Name ?? string.Empty;
                var expected = $"   ( {other.Name} conquerred by {conqueror} )";
                Assert.Equal(expected, byKey[NationStatusModel.RelationKeyPrefix + other.Id]);
            }
            else
            {
                var word = ExpectedRelationWord(value);
                var expectedText = word.Length == 0 ? other.Name : $"{other.Name}: {word}";
                Assert.Equal(expectedText, byKey[NationStatusModel.RelationKeyPrefix + other.Id]);
            }
        }

        // Bug #513's fix stays: the own panel carries the training section.
        Assert.Contains(lines, line => string.Equals(line.Key, NationStatusModel.TrainingHeaderKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// Carthage viewed by Rome — the foreign panel adds Population (Wealth), Unity (word), Tax rate
    /// and the whole per-nation relations list, in place of the viewer's cell alone. Mobilized and
    /// Treasury are blank; the training section is absent.
    /// </summary>
    [Fact]
    public void The_foreign_panel_lists_population_unity_tax_rate_and_every_relation()
    {
        var session = RomeSession();
        var state = session.State;
        var carthage = state.NationById(CarthageId)!;
        var capital = state.CityById(carthage.CapitalCityId!)!;

        var lines = NationStatusModel.Build(state, session.Ruleset, CarthageId, viewerNationId: RomeId);
        var byKey = ByKey(lines);

        // Public facts the panel still carries.
        Assert.Equal($"Leader: {carthage.LeaderName}", byKey[NationStatusModel.LeaderKey]);
        Assert.Equal($"Capital: {capital.Name}", byKey[NationStatusModel.CapitalKey]);
        Assert.Equal($"Cities: {state.CountCitiesOwnedBy(CarthageId)}", byKey[NationStatusModel.CitiesKey]);

        // 2026-10-05 decision: Population, Unity and Tax rate join the foreign panel.
        Assert.Equal($"Population: {carthage.Wealth}", byKey[NationStatusModel.PopulationKey]);
        Assert.Equal($"Unity: {InformationWords.Unity(carthage.Unity)}", byKey[NationStatusModel.UnityKey]);
        Assert.Equal($"Tax rate: {carthage.TaxRatePercent}%", byKey[NationStatusModel.TaxRateKey]);

        // Every relation line is rendered (N11), not just the viewer cell (the 2026-10-01 default).
        var relationLines = lines.Where(line =>
            line.Key.StartsWith(NationStatusModel.RelationKeyPrefix, StringComparison.Ordinal)).ToList();
        Assert.Equal(state.Nations.Count - 1, relationLines.Count);
        foreach (var other in state.Nations.Where(nation => !string.Equals(nation.Id, CarthageId, StringComparison.Ordinal)))
        {
            var value = state.Relations.Get(CarthageId, other.Id);
            if (other.Eliminated)
            {
                var conqueror = state.NationById(other.ConqueredBy!)?.Name ?? string.Empty;
                Assert.Equal(
                    $"   ( {other.Name} conquerred by {conqueror} )",
                    byKey[NationStatusModel.RelationKeyPrefix + other.Id]);
            }
            else
            {
                var word = ExpectedRelationWord(value);
                var expected = word.Length == 0 ? other.Name : $"{other.Name}: {word}";
                Assert.Equal(expected, byKey[NationStatusModel.RelationKeyPrefix + other.Id]);
            }
        }

        // Withheld: Mobilized, Treasury and the training section are absent from the foreign panel.
        foreach (var withheld in new[]
        {
            NationStatusModel.MobilizedKey,
            NationStatusModel.TreasuryKey,
            NationStatusModel.TrainingHeaderKey,
            NationStatusModel.TrainingNoneKey,
        })
        {
            Assert.False(byKey.ContainsKey(withheld), $"the foreign panel must not carry '{withheld}'");
        }

        Assert.DoesNotContain(lines, line =>
            line.Key.StartsWith(NationStatusModel.TrainingKeyPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// A nation's own panel always carries the full list — even a foreign nation, when chosen as the
    /// viewer. The Carthage-as-own assertion pins the same code path regardless of who selects the
    /// panel.
    /// </summary>
    [Fact]
    public void The_same_nation_as_the_own_nation_carries_the_full_list()
    {
        var session = RomeSession();
        var state = session.State;

        var asForeign = NationStatusModel.Build(state, session.Ruleset, CarthageId, viewerNationId: RomeId);
        var asOwn = NationStatusModel.Build(state, session.Ruleset, CarthageId, viewerNationId: CarthageId);

        Assert.DoesNotContain(asForeign, line => string.Equals(line.Key, NationStatusModel.TreasuryKey, StringComparison.Ordinal));
        Assert.Contains(asOwn, line => string.Equals(line.Key, NationStatusModel.TreasuryKey, StringComparison.Ordinal));
        Assert.Contains(asOwn, line => string.Equals(line.Key, NationStatusModel.TrainingHeaderKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// The eliminated row is flagged red (N12), every other relation row is plain. Mark a nation's
    /// <see cref="NationState.Eliminated"/> and <see cref="NationState.ConqueredBy"/> on the live
    /// state and assert the lines model emits.
    /// </summary>
    [Fact]
    public void A_conquered_nation_row_is_flagged_red_and_others_stay_plain()
    {
        var session = RomeSession();
        var state = session.State;
        var carthage = state.NationById(CarthageId)!;
        var conqueror = state.NationById("rome")!;

        var arranged = state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, CarthageId, StringComparison.Ordinal)
                    ? n with { Eliminated = true, ConqueredBy = conqueror.Id }
                    : n)),
        };

        var lines = NationStatusModel.Build(arranged, session.Ruleset, RomeId, viewerNationId: RomeId);
        var conquered = lines.Single(line =>
            string.Equals(line.Key, NationStatusModel.RelationKeyPrefix + CarthageId, StringComparison.Ordinal));
        var other = lines.Where(line =>
            line.Key.StartsWith(NationStatusModel.RelationKeyPrefix, StringComparison.Ordinal)
            && !string.Equals(line.Key, NationStatusModel.RelationKeyPrefix + CarthageId, StringComparison.Ordinal))
            .ToList();

        Assert.True(conquered.IsRed, "the conquered nation's relation row is flagged red (N12)");
        Assert.Contains("conquerred", conquered.Text, StringComparison.Ordinal);
        Assert.All(other, line => Assert.False(line.IsRed, $"non-conquered row '{line.Key}' is not red"));
    }

    /// <summary>
    /// The model's separate relation-word rule on its own, restated from the research read — duplicated
    /// here (the panel itself asserts on the panel's output) so a regression that swaps the helper for
    /// the clone-ship Trade/Alliance/War words breaks one place, not every test.
    /// </summary>
    [Theory]
    [InlineData(0, "")]
    [InlineData(-1, "")]
    [InlineData(1, "trade")]
    [InlineData(2, "ally")]
    [InlineData(3, "war")]
    [InlineData(4, "")]
    public void Relation_word_matches_the_original_band(int value, string word)
    {
        Assert.Equal(word, InformationWords.Relation(value));
    }
}
