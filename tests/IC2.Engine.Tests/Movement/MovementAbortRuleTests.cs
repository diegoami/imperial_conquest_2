using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using Xunit;

namespace IC2.Engine.Tests.Movement;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T09 Movement and terrain", Done When 4: "An unaffordable step aborts
/// the move; under <c>classical-faithful</c> the army's remaining moves are unchanged for a human seat
/// and zeroed for a computer-controlled seat; under <c>improved</c> moves are zeroed for every seat —
/// asserted as separate tests per ruleset."
/// </summary>
public sealed class MovementAbortRuleTests
{
    [Fact]
    public void ClassicalFaithful_HumanSeat_MovesRemainUnchanged()
    {
        var result = MovementAbortRule.MovesAfterAbortedStep(7, SeatControl.Human, SeatAsymmetryModel.Faithful);
        Assert.Equal(7, result);
    }

    [Fact]
    public void ClassicalFaithful_AiSeat_MovesAreZeroed()
    {
        var result = MovementAbortRule.MovesAfterAbortedStep(7, SeatControl.Ai, SeatAsymmetryModel.Faithful);
        Assert.Equal(0, result);
    }

    [Fact]
    public void Improved_HumanSeat_MovesAreZeroed()
    {
        var result = MovementAbortRule.MovesAfterAbortedStep(7, SeatControl.Human, SeatAsymmetryModel.Normalized);
        Assert.Equal(0, result);
    }

    [Fact]
    public void Improved_AiSeat_MovesAreZeroed()
    {
        var result = MovementAbortRule.MovesAfterAbortedStep(7, SeatControl.Ai, SeatAsymmetryModel.Normalized);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ZeroMovesRemaining_IsAlreadyZeroEitherWay()
    {
        Assert.Equal(0, MovementAbortRule.MovesAfterAbortedStep(0, SeatControl.Human, SeatAsymmetryModel.Faithful));
        Assert.Equal(0, MovementAbortRule.MovesAfterAbortedStep(0, SeatControl.Ai, SeatAsymmetryModel.Faithful));
    }

    [Fact]
    public void NegativeMovesRemaining_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementAbortRule.MovesAfterAbortedStep(-1, SeatControl.Human, SeatAsymmetryModel.Faithful));
    }

    /// <summary>
    /// End-to-end: a walk that aborts for insufficient moves, followed by the abort rule, reproduces the
    /// four DoD combinations directly against what a caller would actually store back on the mover.
    /// </summary>
    [Theory]
    [InlineData(SeatAsymmetryModel.Faithful, SeatControl.Human, 3)] // unchanged
    [InlineData(SeatAsymmetryModel.Faithful, SeatControl.Ai, 0)] // zeroed
    [InlineData(SeatAsymmetryModel.Normalized, SeatControl.Human, 0)] // zeroed
    [InlineData(SeatAsymmetryModel.Normalized, SeatControl.Ai, 0)] // zeroed
    public void WalkThenAbortRule_UnaffordableStep_MatchesEachRulesetCombination(
        SeatAsymmetryModel model, SeatControl control, int expectedMovesAfterAbort)
    {
        // Row y=2 from (1,2): (2,2) is River, cost 4. A budget of 3 cannot afford it.
        var walkResult = MovementWalker.Walk(
            new GridPoint(1, 2), new GridPoint(4, 2), movesAvailable: 3,
            MovementTestbed.Ruleset.Terrain, MovementTestbed.TileTypeIdAt, static _ => false,
            new RecordingEventSink());

        Assert.Equal(MovementStopReason.InsufficientMoves, walkResult.StopReason);
        Assert.Equal(3, walkResult.MovesRemaining);

        var finalMoves = MovementAbortRule.MovesAfterAbortedStep(walkResult.MovesRemaining, control, model);

        Assert.Equal(expectedMovesAfterAbort, finalMoves);
    }
}
