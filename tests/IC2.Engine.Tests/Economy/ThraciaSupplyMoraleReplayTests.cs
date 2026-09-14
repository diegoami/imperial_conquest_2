using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses", Done-when 11: replays the
/// thirteen recorded turns of <c>1_thracia_271_*</c> (<c>docs/investigations/thracia-supply-morale.md</c>)
/// as a fixture, through the real <see cref="ArmySupplyAndMoraleSystem"/> registered in
/// <see cref="TurnPhase.ArmyTick"/> and run via the real <see cref="TurnCoordinator"/> pipeline (T06's
/// <c>calendar.advance-week</c> alongside it), not by calling <see cref="SupplyMoraleRule"/> directly —
/// proving the phase ordering and the pre-advance season reading end to end, not just the formula.
/// </summary>
public sealed class ThraciaSupplyMoraleReplayTests
{
    private const string ArmyId = "thracia-replay-army";
    private const int Troops = 22_000;

    /// <summary>
    /// The 13 recorded saves' morale, <c>spring_1</c> (index 0, the starting state, no tick yet) through
    /// <c>autumn_1</c> (index 12) — 12 transitions, all confirmed in
    /// <c>docs/investigations/thracia-supply-morale.md</c>'s save table.
    /// </summary>
    private static readonly int[] ExpectedMoraleSequence =
        { 65, 66, 67, 65, 63, 61, 59, 57, 55, 53, 51, 51, 51 };

    /// <summary>
    /// Moves for the 12 post-tick saves (<c>spring_3</c> through <c>autumn_1</c>) — 9 while supplied,
    /// 8 from the first <c>pct &lt; 10</c> turn (<c>spring_7</c>) on. <c>spring_1</c> (the pre-tick start)
    /// is excluded, exactly as the investigation notes it is not a post-tick reading.
    /// </summary>
    private static readonly int[] ExpectedMovesSequence =
        { 9, 9, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8 };

    private static GameState BuildStartingState()
    {
        var state = EconomyTestbed.InitialState();
        var nationId = state.Nations[0].Id;

        var army = new ArmyState(
            Id: ArmyId,
            Nation: nationId,
            X: 3,
            Y: 2,
            Moves: 9,
            Morale: 65,
            Money: 0,
            SupplyTons: 142,
            CoveredTileCode: null,
            AboardFleetId: null,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", Troops, 6, "Thracian Replay Battalion")));

        return state with { Armies = ValueList.Of(army) };
    }

    /// <summary>
    /// Done-when 11's main assertion: the morale sequence, turn for turn, over six Spring turns and into
    /// Summer, through the real pipeline -- and the floor holding on the last two turns
    /// (<c>max(51, 49)</c>), not merely trending downwards.
    /// </summary>
    [Fact]
    public void Replay_ThirteenThracianTurns_ReproducesTheRecordedMoraleSequenceExactly()
    {
        // Scoped to exactly CalendarAdvance + ArmyTick -- the two real, production systems Done-when 11
        // exercises. Quarterly billing and weather are deliberately excluded here: they are real
        // mechanisms with their own dedicated tests below/elsewhere, but the toy world's treasury and
        // fleet numbers were never chosen to be neutral against a hand-built 22,000-troop test army, and
        // this test's job is to pin the supply->morale sequence exactly, not to also incidentally assert
        // nothing else in the pipeline perturbs it.
        var coordinator = EconomyTestbed.CoordinatorOnly(
            sink: null, typeof(CalendarSystem), typeof(ArmySupplyAndMoraleSystem));
        var state = BuildStartingState();

        Assert.Equal(ExpectedMoraleSequence[0], state.ArmyById(ArmyId)!.Morale);

        for (var turn = 1; turn <= 12; turn++)
        {
            var result = coordinator.RunRoundTick(state);
            state = result.State;

            var army = state.ArmyById(ArmyId)!;
            Assert.Equal(ExpectedMoraleSequence[turn], army.Morale);
            Assert.Equal(ExpectedMovesSequence[turn - 1], army.Moves);
        }

        // The floor explicitly, not just "the sequence happened to end low": the last two entries are
        // both exactly 51, i.e. max(51, 49) held rather than a monotonic decay continuing past it.
        Assert.Equal(51, ExpectedMoraleSequence[^1]);
        Assert.Equal(51, ExpectedMoraleSequence[^2]);
    }

    /// <summary>
    /// Done-when 11's dead-band case: "an army held at 12% for three turns does not move at all" -- held
    /// here at the formula level (<see cref="SupplyMoraleRule.ApplyToMorale"/>) rather than by hand-tuning
    /// a troop/supply pair that naturally lands on exactly 12% for three consecutive real consumption
    /// steps, which the confirmed consumption formula does not generally produce. 12 sits inside the
    /// confirmed dead band (<c>10 ≤ pct ≤ 15</c>): three consecutive turns at that percentage move neither
    /// morale nor moves.
    /// </summary>
    [Fact]
    public void DeadBand_HeldAt12PercentForThreeTurns_MovesNeitherMoraleNorMoves()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var morale = 60;

        for (var turn = 1; turn <= 3; turn++)
        {
            var (newMorale, movesPenalty) = SupplyMoraleRule.ApplyToMorale(morale, supplyPercent: 12, ruleset);

            Assert.Equal(morale, newMorale); // "does not move" -- morale is unchanged.
            Assert.Equal(0, movesPenalty); // "does not move" -- no moves penalty ever applies in the dead band.

            morale = newMorale;
        }

        Assert.Equal(60, morale);
    }
}
