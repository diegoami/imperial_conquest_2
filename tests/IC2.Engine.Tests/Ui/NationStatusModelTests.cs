using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T110 (docs/tasks/T110.md, Done-when 2): the context panel's nation status panel against the live
/// state. The own nation (Rome) carries the confirmed full list; a foreign nation (Carthage, with Rome
/// as the viewer) carries public facts only.
/// </summary>
/// <remarks>
/// Every expected value is read from the same <see cref="GameSession"/> the panel would draw, so the test
/// pins the model's own choice of fields rather than a hand-copied number. The absence assertions are the
/// load-bearing half: they are what fail if a later change makes the foreign panel leak the own nation's
/// facts.
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

    /// <summary>Rome's own panel: the report §1 list, each value read from the state.</summary>
    [Fact]
    public void The_own_nations_panel_lists_the_confirmed_fields()
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
        Assert.Equal($"Population: {rome.Population}", byKey[NationStatusModel.PopulationKey]);
        Assert.Equal($"Unity: {rome.Unity}", byKey[NationStatusModel.UnityKey]);
        Assert.Equal($"Tax rate: {rome.TaxRatePercent}%", byKey[NationStatusModel.TaxRateKey]);
        Assert.Equal($"Mobilized: {rome.MobilizedPercent}%", byKey[NationStatusModel.MobilizedKey]);
        Assert.Equal($"Treasury: {rome.Treasury}", byKey[NationStatusModel.TreasuryKey]);

        // Relations: one line per other nation (15 of the world's 16), and Carthage is among them.
        Assert.Equal(state.Nations.Count - 1, lines.Count(line =>
            line.Key.StartsWith(NationStatusModel.RelationKeyPrefix, StringComparison.Ordinal)));
        Assert.Contains(
            lines,
            line => string.Equals(line.Key, NationStatusModel.RelationKeyPrefix + CarthageId, StringComparison.Ordinal)
                && line.Text.StartsWith("Carthage:", StringComparison.Ordinal));

        // Bug #513's fix stays: the own panel carries the training section.
        Assert.Contains(lines, line => string.Equals(line.Key, NationStatusModel.TrainingHeaderKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// A foreign nation's panel (Carthage, viewed by Rome) holds its public facts and withholds every
    /// one of the own-nation-only fields.
    /// </summary>
    [Fact]
    public void A_foreign_nations_panel_shows_public_facts_only()
    {
        var session = RomeSession();
        var state = session.State;
        var carthage = state.NationById(CarthageId)!;
        var capital = state.CityById(carthage.CapitalCityId!)!;

        var lines = NationStatusModel.Build(state, session.Ruleset, CarthageId, viewerNationId: RomeId);
        var byKey = ByKey(lines);

        // 1. Public facts: leader, capital, cities and the relation with the viewer's nation.
        Assert.Equal($"Leader: {carthage.LeaderName}", byKey[NationStatusModel.LeaderKey]);
        Assert.Equal($"Capital: {capital.Name}", byKey[NationStatusModel.CapitalKey]);
        Assert.Equal($"Cities: {state.CountCitiesOwnedBy(CarthageId)}", byKey[NationStatusModel.CitiesKey]);
        Assert.Contains(
            lines,
            line => string.Equals(line.Key, NationStatusModel.RelationKeyPrefix + RomeId, StringComparison.Ordinal)
                && line.Text.StartsWith("Rome:", StringComparison.Ordinal));

        // 2. Only the one viewer relation, not the whole per-nation list.
        Assert.Equal(1, lines.Count(line =>
            line.Key.StartsWith(NationStatusModel.RelationKeyPrefix, StringComparison.Ordinal)));

        // 3. Withheld: no treasury, tax rate, population, unity, mobilization or units in training.
        foreach (var withheld in new[]
        {
            NationStatusModel.TreasuryKey,
            NationStatusModel.TaxRateKey,
            NationStatusModel.PopulationKey,
            NationStatusModel.UnityKey,
            NationStatusModel.MobilizedKey,
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
    /// The non-vacuous half of the foreign rule: Carthage's own panel (Carthage active) grows the full
    /// list back, so the absence assertions above are about being foreign, not about the field ever being
    /// absent.
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
}
