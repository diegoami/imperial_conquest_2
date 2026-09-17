using IC2.Engine.Model;
using IC2.Engine.Victory;
using Xunit;

namespace IC2.Engine.Tests.Victory;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T12 Victory conditions": the original's all-cities condition, checked
/// against the shipped toy world's own city count — never a hardcoded 334 — and its 250 BC year limit,
/// checked to fire at 250 BC and not at 251 BC.
/// </summary>
public sealed class TotalConquestVictoryTests
{
    [Fact]
    public void Wins_WhenNationOwnsEveryCityOnTheMap()
    {
        var state = VictoryTestbed.InitialState();

        // The world's own city count, whatever it happens to be for this world -- not a literal 334.
        var totalCities = state.Cities.Count;
        Assert.True(totalCities > 0, "The toy world must define at least one city for this test to mean anything.");

        var conquered = VictoryTestbed.WithAllCitiesOwnedBy(state, "north");
        Assert.Equal(totalCities, conquered.Cities.Count(c => c.Owner == "north"));

        var outcome = VictoryEvaluator.EvaluateTotalConquest(conquered, VictoryTestbed.Ruleset.Victory);

        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
        Assert.Equal(VictoryConditionType.TotalConquest, outcome.ConditionType);
    }

    [Fact]
    public void DoesNotWin_WhileAnyCityRemainsUnowned()
    {
        // The toy scenario's own starting split: North holds 2 of 3 cities, South the third.
        var state = VictoryTestbed.InitialState();
        Assert.True(state.Cities.Any(c => c.Owner != "north"), "The toy world's starting split must leave North short of every city.");

        var outcome = VictoryEvaluator.EvaluateTotalConquest(state, VictoryTestbed.Ruleset.Victory);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
        Assert.Null(outcome.WinningNationId);
    }

    /// <summary>
    /// "the year-limit test fires at 250 BC and not at 251 BC" — <c>tests/fixtures/corpus.json</c>'s
    /// <c>victory.yearLimitBC</c>, read off the loaded ruleset rather than a literal 250.
    /// </summary>
    [Fact]
    public void Expires_AtTheHardEndYear_WithNobodyHavingConquered()
    {
        var state = VictoryTestbed.InitialState();
        var hardEndYear = VictoryTestbed.Ruleset.Victory.HardEndYearBc;

        var atTheLimit = VictoryTestbed.WithYear(state, hardEndYear);

        var outcome = VictoryEvaluator.EvaluateTotalConquest(atTheLimit, VictoryTestbed.Ruleset.Victory);

        Assert.Equal(VictoryStatus.Expired, outcome.Status);
        Assert.Null(outcome.WinningNationId);
    }

    [Fact]
    public void DoesNotExpire_OneYearBeforeTheHardEndYear()
    {
        var state = VictoryTestbed.InitialState();
        var oneYearBefore = VictoryTestbed.Ruleset.Victory.HardEndYearBc + 1;

        var notYetAtTheLimit = VictoryTestbed.WithYear(state, oneYearBefore);

        var outcome = VictoryEvaluator.EvaluateTotalConquest(notYetAtTheLimit, VictoryTestbed.Ruleset.Victory);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    /// <summary>
    /// A nation the game has already eliminated cannot be the one credited with total conquest, even if
    /// the city bookkeeping were ever to (wrongly) show it owning everything.
    /// </summary>
    [Fact]
    public void EliminatedNation_CannotWin_EvenIfItOwnsEveryCity()
    {
        var state = VictoryTestbed.InitialState();
        var conquered = VictoryTestbed.WithAllCitiesOwnedBy(state, "north");
        var eliminated = VictoryTestbed.WithEliminated(conquered, "north");

        var outcome = VictoryEvaluator.EvaluateTotalConquest(eliminated, VictoryTestbed.Ruleset.Victory);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    /// <summary>
    /// No report describes a total-conquest rule for anything other than every city, and
    /// <c>toy-ruleset.json</c>'s own provenance for the field says so explicitly — see
    /// <see cref="VictoryEvaluator.EvaluateTotalConquest"/>'s remarks. This is left unimplemented rather
    /// than guessed at.
    /// </summary>
    [Fact]
    public void RequiresEveryCityFalse_IsNotImplemented()
    {
        var state = VictoryTestbed.InitialState();
        var rules = VictoryTestbed.Ruleset.Victory with { TotalConquestRequiresEveryCity = false };

        Assert.Throws<NotSupportedException>(() => VictoryEvaluator.EvaluateTotalConquest(state, rules));
    }
}
