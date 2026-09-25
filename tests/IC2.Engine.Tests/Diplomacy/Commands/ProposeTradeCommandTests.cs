using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// Bug #199 (T69), Done-when 2: <see cref="ProposeTradeCommandHandler"/> rejects an eliminated
/// counterparty with the one shared code, before any state change — checked directly here on a minimal
/// fixture; the cross-task scenario (a real elimination through <c>BesiegeCityCommand</c>) lives in
/// <c>EliminationDiplomacyTests</c>.
/// </summary>
public sealed class ProposeTradeCommandTests
{
    private const string Proposer = "proposer";
    private const string Target = "target";

    [Fact]
    public void RefusedWhenTheTargetIsEliminated()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target", eliminated: true));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeTradeCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }
}
