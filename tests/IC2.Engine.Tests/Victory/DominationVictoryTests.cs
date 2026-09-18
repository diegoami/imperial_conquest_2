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

    /// <summary>
    /// Review round 1, non-blocking finding 3: an eliminated nation is never counted as a "live"
    /// hostile, matching the outer loop's own elimination check, even when a hand-built state (this
    /// evaluator's whole point per the task's Scope) violates <c>GameStateFactory</c>'s usual
    /// "Eliminated implies owns 0 cities" invariant by leaving it still holding a city. North is at war
    /// with South, South is marked eliminated but (deliberately, to exercise this) still owns
    /// <c>meridia</c> -- North does not dominate, because an eliminated South is skipped entirely
    /// rather than counted as an undominated hostile.
    /// </summary>
    [Fact]
    public void EliminatedHostile_IsNeverCountedAsALiveHostile_EvenIfItStillOwnsACity()
    {
        var state = VictoryTestbed.InitialState();
        var warCode = VictoryTestbed.Ruleset.Diplomacy.StateCodes.War;

        var atWar = VictoryTestbed.WithRelation(state, "north", "south", warCode);
        var invariantViolatingState = VictoryTestbed.WithEliminated(atWar, "south");

        var outcome = VictoryEvaluator.EvaluateDomination(invariantViolatingState, VictoryTestbed.Ruleset);

        // Not a win: North owns only 2 of 3 cities (not literal total conquest), and South -- the only
        // other nation -- is skipped as eliminated rather than counted as a live, undominated hostile.
        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }
}
