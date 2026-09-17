using IC2.Engine.Victory;
using Xunit;

namespace IC2.Engine.Tests.Victory;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T12 Victory conditions" — <c>docs/game-design.md</c>
/// §"Victory conditions" <strong>[designed]</strong>: "control every city, or every city belonging to
/// nations still at war with you." South owns exactly one city (<c>meridia</c>) in the shipped toy
/// world, which makes it a clean single hostile to dominate.
/// </summary>
public sealed class DominationVictoryTests
{
    [Fact]
    public void Wins_WhenEveryCityOfALiveHostileHasBeenCaptured()
    {
        var state = VictoryTestbed.InitialState();
        var warCode = VictoryTestbed.Ruleset.Diplomacy.StateCodes.War;

        var atWar = VictoryTestbed.WithRelation(state, "north", "south", warCode);
        var stripped = VictoryTestbed.WithCityOwner(atWar, "meridia", "north");

        var outcome = VictoryEvaluator.EvaluateDomination(stripped, VictoryTestbed.Ruleset);

        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }

    [Fact]
    public void DoesNotWin_WhileTheHostileStillHoldsACity()
    {
        var state = VictoryTestbed.InitialState();
        var warCode = VictoryTestbed.Ruleset.Diplomacy.StateCodes.War;

        // South is at war with North but still owns meridia -- not dominated yet.
        var atWar = VictoryTestbed.WithRelation(state, "north", "south", warCode);

        var outcome = VictoryEvaluator.EvaluateDomination(atWar, VictoryTestbed.Ruleset);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    /// <summary>
    /// Every shipped scenario starts every relation at peace
    /// (<see cref="Model.GameStateFactory.CreateInitial"/>), so this pins that domination cannot fire as
    /// a trivial win at scenario start merely because nobody has fought yet.
    /// </summary>
    [Fact]
    public void DoesNotWin_AtPeace_EvenThoughNoHostileCityRemainsUncaptured()
    {
        var state = VictoryTestbed.InitialState();

        var outcome = VictoryEvaluator.EvaluateDomination(state, VictoryTestbed.Ruleset);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    [Fact]
    public void Wins_ViaLiteralTotalConquest_EvenWithNoDeclaredWar()
    {
        var state = VictoryTestbed.InitialState();

        var conquered = VictoryTestbed.WithAllCitiesOwnedBy(state, "north");

        var outcome = VictoryEvaluator.EvaluateDomination(conquered, VictoryTestbed.Ruleset);

        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }
}
