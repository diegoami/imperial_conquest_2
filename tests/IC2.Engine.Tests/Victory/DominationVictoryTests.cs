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
    /// T43 (closing #108): updated from round 1's "an eliminated hostile is never counted as live" --
    /// see <see cref="VictoryEvaluator.EvaluateDomination"/>'s remarks for why that reading is exactly
    /// what made domination unreachable. <c>other.Eliminated</c> is no longer read at all; a hostile's
    /// own city count decides whether it still blocks the win. This state deliberately violates
    /// <c>GameStateFactory</c>'s usual "Eliminated implies owns 0 cities" invariant (South is marked
    /// eliminated but still hand-set to own <c>meridia</c>) specifically to prove the count, not the
    /// flag, is what is read: South still owns a city, so North still does not dominate, regardless of
    /// South's elimination bookkeeping.
    /// </summary>
    [Fact]
    public void EliminatedHostile_StillBlocksTheWin_IfItSomehowStillOwnsACity()
    {
        var state = VictoryTestbed.InitialState();
        var warCode = VictoryTestbed.Ruleset.Diplomacy.StateCodes.War;

        var atWar = VictoryTestbed.WithRelation(state, "north", "south", warCode);
        var invariantViolatingState = VictoryTestbed.WithEliminated(atWar, "south");

        var outcome = VictoryEvaluator.EvaluateDomination(invariantViolatingState, VictoryTestbed.Ruleset);

        // Not a win: North owns only 2 of 3 cities (not literal total conquest), and South still owns
        // meridia -- one city is enough to block domination, whether or not South also happens to be
        // marked eliminated.
        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }

    /// <summary>
    /// <strong>T43 DoD 1 (closing #108).</strong> North is at war only with South; South has been reduced
    /// to zero cities (its last city, <c>meridia</c>, fell to a third party rather than to North itself,
    /// so North does <em>not</em> own every city on the map either); South carries the
    /// <see cref="Model.NationState.Eliminated"/> flag <see cref="Model.GameStateFactory"/> always sets
    /// once a nation's city count hits zero. On the merged T12 code this returns <c>Undecided</c> forever
    /// -- the inner hostile scan skipped South for being eliminated, so <c>hasLiveHostile</c> could never
    /// become true, and domination silently degraded to a demand for literal total conquest. This is the
    /// exact scenario #108 describes as "unreachable unless it owns every city on the map."
    /// </summary>
    [Fact]
    public void Wins_WhenTheOnlyHostileHasBeenReducedToZeroCities_EvenThoughNorthOwnsLessThanTheWholeMap()
    {
        var state = VictoryTestbed.InitialState();
        var warCode = VictoryTestbed.Ruleset.Diplomacy.StateCodes.War;

        var atWar = VictoryTestbed.WithRelation(state, "north", "south", warCode);
        // meridia falls to neither belligerent -- so North (2 of 3 cities) does not own every city on
        // the map, and South (0 of 3) is reduced to nothing without North having conquered it directly.
        var southHoldsNothing = VictoryTestbed.WithCityOwner(atWar, "meridia", "a-third-party");
        var southEliminated = VictoryTestbed.WithEliminated(southHoldsNothing, "south");

        Assert.True(
            southEliminated.Cities.Count(c => c.Owner == "north") < southEliminated.Cities.Count,
            "North must not own every city on the map for this test to mean anything.");

        var outcome = VictoryEvaluator.EvaluateDomination(southEliminated, VictoryTestbed.Ruleset);

        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }

    /// <summary>
    /// <strong>T43 DoD 2 (closing #108).</strong> Same scripted state as
    /// <see cref="Wins_WhenTheOnlyHostileHasBeenReducedToZeroCities_EvenThoughNorthOwnsLessThanTheWholeMap"/>
    /// -- restated on its own so the decision it pins is explicit: marking the defeated hostile
    /// <see cref="Model.NationState.Eliminated"/> must not erase the win. <c>_provenance</c>: what was
    /// searched for this decision is exactly what <see cref="VictoryEvaluator.EvaluateDomination"/>'s
    /// remarks record -- <c>game-design.md</c>'s one sentence on this condition and
    /// <c>design-audit.md</c> Q5, neither of which mentions elimination at all. Decided from the model's
    /// own construction instead: <see cref="Model.GameStateFactory.CreateInitial"/> sets
    /// <c>Eliminated: cityCount == 0</c>, so "eliminated" is not a fact independent of "holds nothing" --
    /// it is a restatement of it. Reading the flag as an <em>additional</em> requirement (as round 1's
    /// rework did) can only ever narrow the set of hostiles that count, never widen it, and here it
    /// narrows the set to nothing: the sole hostile a game will realistically ever fully defeat is
    /// exactly the one whose city count reached zero and so was marked eliminated. Treating elimination
    /// as disqualifying rather than confirming turns "defeat your only enemy" into "defeat your only
    /// enemy without the bookkeeping noticing," which is not a coherent rule for anything a player would
    /// call domination.
    /// </summary>
    [Fact]
    public void Wins_EvenThoughTheDefeatedHostileIsMarkedEliminated()
    {
        var state = VictoryTestbed.InitialState();
        var warCode = VictoryTestbed.Ruleset.Diplomacy.StateCodes.War;

        var atWar = VictoryTestbed.WithRelation(state, "north", "south", warCode);
        var southHoldsNothing = VictoryTestbed.WithCityOwner(atWar, "meridia", "a-third-party");
        var southEliminated = VictoryTestbed.WithEliminated(southHoldsNothing, "south");

        var outcome = VictoryEvaluator.EvaluateDomination(southEliminated, VictoryTestbed.Ruleset);

        Assert.Equal(VictoryStatus.Won, outcome.Status);
        Assert.Equal("north", outcome.WinningNationId);
    }

    /// <summary>
    /// <strong>T43 DoD 3 (closing #108's "second, related" finding).</strong> The hostile-domination
    /// branch had no <c>ownedByNation &gt; 0</c> guard, unlike the total-conquest branch's own
    /// <c>totalCities &gt; 0</c> guard -- so a nation holding no city of its own could be crowned merely
    /// because every declared hostile also happened to hold none. North is not eliminated but (by an
    /// adversarial, invariant-violating construction -- this evaluator's whole point per the task's
    /// Scope) owns no city itself; South is its only declared hostile and also owns nothing. North must
    /// not win: it holds nothing to have dominated with.
    /// </summary>
    [Fact]
    public void DoesNotWin_WhenTheCandidateNationOwnsNoCityItself()
    {
        var state = VictoryTestbed.InitialState();
        var warCode = VictoryTestbed.Ruleset.Diplomacy.StateCodes.War;

        var atWar = VictoryTestbed.WithRelation(state, "north", "south", warCode);
        var nobodyOwnsAnything = VictoryTestbed.WithAllCitiesOwnedBy(atWar, "a-third-party");

        var outcome = VictoryEvaluator.EvaluateDomination(nobodyOwnsAnything, VictoryTestbed.Ruleset);

        Assert.Equal(VictoryStatus.Undecided, outcome.Status);
    }
}
