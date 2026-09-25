using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// Rework round 1, B1: pins the exact shipped-data values on which <c>score(n) = (Wealth/100) × Unity</c>
/// and the dropped-divisor form <c>Wealth × Unity</c> disagree, so the divisor can never be silently
/// dropped again without a named test failing. See <see cref="HonourablePeaceGate"/>'s own remarks for
/// why the two forms are not equivalent in general.
/// </summary>
/// <remarks>
/// The values are the shipped <c>data/worlds/toy-3city.json</c>'s own nation wealth figures (400, 360, of
/// which only 360 is a non-multiple of 100 — confirmed by construction: <see cref="Model.GameStateFactory.CreateInitial"/>
/// seeds <see cref="NationState.Wealth"/> verbatim from scenario data, never derived from population),
/// paired with unity values chosen so the two forms land on opposite sides of the gate.
/// </remarks>
public sealed class HonourablePeaceGateDivergenceTests
{
    private const string Winner = "winner-nation";
    private const string Loser = "loser-nation";

    /// <summary>
    /// Confirmed form: <c>(400/100)×80 = 320</c>, <c>(360/100)×100 = 300</c>. <c>320 &lt; 300</c> is
    /// false, so the honourable branch does <em>not</em> fire on the score term alone.
    /// </summary>
    /// <remarks>
    /// Mutation proof (quoted verbatim in the PR): reverting <see cref="HonourablePeaceGate.Fires"/> to
    /// the dropped-divisor form makes this test fail with <c>Assert.False() Failure</c>. Not re-run here
    /// (that would require the reverted code to be present); the PR body records the live run and revert.
    /// </remarks>
    [Fact]
    public void B1_ConfirmedForm_DoesNotFireOnTheseShippedValues()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: 80, wealth: 400),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: 100, wealth: 360));

        // Neither nation has an army, so the army-power term is 0 == 0 (not <) and cannot fire the
        // branch on its own -- isolating the assertion to the score term alone.
        Assert.False(HonourablePeaceGate.Fires(state, ruleset, Winner, Loser));
    }

    /// <summary>
    /// The dropped-divisor form gives the opposite answer on the very same values:
    /// <c>400×80 = 32,000</c>, <c>360×100 = 36,000</c>, and <c>32,000 &lt; 36,000</c> is true. Recorded
    /// as an explicit arithmetic fact (not a call into production code) so the divergence itself is
    /// pinned, independent of whichever form <see cref="HonourablePeaceGate"/> happens to implement.
    /// </summary>
    [Fact]
    public void B1_TheDroppedDivisorForm_WouldHaveDisagreed()
    {
        Assert.True(400L * 80 < 360L * 100);
        Assert.False((400L / 100) * 80 < (360L / 100) * 100);
    }

    /// <summary>
    /// A pair where both forms would agree the branch fires, so this is a genuine "does the gate still
    /// work at all" check alongside the divergence pin above — not just "always false".
    /// </summary>
    [Fact]
    public void B1_ConfirmedForm_FiresWhenTheDividedScoreActuallyIsLower()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: 50, wealth: 200),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: 900, wealth: 900));

        Assert.True(HonourablePeaceGate.Fires(state, ruleset, Winner, Loser));
    }
}
