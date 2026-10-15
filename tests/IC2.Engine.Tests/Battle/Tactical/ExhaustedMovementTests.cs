using IC2.Engine.Battle.Tactical;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

public class ExhaustedMovementTests
{
    // FUN_00438D24 (T174/#1105): the melee-target branch requires positive moves.
    // The mover is stronger so the separate last-move danger check permits its step.
    [Theory]
    [InlineData(true, 1, 3, 4, 0, false)]
    [InlineData(true, 0, 4, 4, 0, false)]
    [InlineData(true, 1, 4, 4, 1, true)]
    [InlineData(false, 1, 3, 4, 0, false)]
    [InlineData(false, 0, 4, 4, 0, false)]
    [InlineData(false, 1, 4, 4, 1, false)]
    public void Exhaustion_is_checked_before_computer_targeting(
        bool computer, int moves, int startY, int expectedY, int expectedMoves, bool targets)
    {
        var state = Arena(
            new[] { (0, Unit(HI, 5, startY, 15000, moves: moves)),
                    (D0, Unit(HI, 5, 5, 1000)) },
            attackerComputer: computer);
        var draws = new ScriptedDraws();

        var after = TacticalMovement.MoveUnit(state, 0, 5, 5, Context, draws);

        Assert.Equal((5, expectedY, expectedMoves, targets ? D0 : TacticalSlot.NoTarget),
            (after.Slots[0].X, after.Slots[0].Y, after.Slots[0].Moves, after.Slots[0].Target));
        Assert.Equal(expectedY - startY, after.Log.OfType<TacticalMovedEvent>().Count());
        Assert.Equal(targets ? 1 : 0, after.Log.OfType<TacticalTargetSetEvent>().Count());
        Assert.Empty(after.Log.OfType<TacticalShotEvent>());
        Assert.Empty(draws.Calls);
        AssertGridMatchesSlots(after);
    }
}

