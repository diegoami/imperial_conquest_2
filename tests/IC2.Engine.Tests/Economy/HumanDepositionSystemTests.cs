using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays, mercenary desertion, and deposition
/// for debt", Done-when 6's human clause: "a human nation in debt at the start of its turn is deposed
/// and its seat passes to the AI." Run through the real <see cref="IC2.Engine.Core.TurnCoordinator"/>,
/// narrowed to just this system, so the trigger is genuinely <see cref="IC2.Engine.Core.TurnPhase.SeatStart"/>
/// — "the start of its turn" — not a direct call.
/// </summary>
public sealed class HumanDepositionSystemTests
{
    [Fact]
    public void RunTurn_HumanNationInDebt_IsDeposedAndItsSeatPassesToTheAi()
    {
        // The toy scenario's active seat defaults to "north", the human seat.
        var state = EconomyTestbed.InitialState();
        Assert.Equal("north", state.ActiveNationId);
        Assert.Equal(SeatControl.Human, state.NationById("north")!.Control);

        var nations = state.Nations.Select(n => n.Id == "north" ? n with { Treasury = -50_000, Unity = 300 } : n);
        state = state with { Nations = ValueList.From(nations) };

        var coordinator = EconomyTestbed.CoordinatorOnly(sink: null, typeof(HumanDepositionSystem));
        var after = coordinator.RunTurn(state).State;
        var north = after.NationById("north")!;

        Assert.Equal(SeatControl.Ai, north.Control); // the seat passes to the AI.
        Assert.Equal(0, north.Treasury); // -50,000 -> floored to zero.
        Assert.Equal(450, north.Unity); // min(550, 300 + 150).
        Assert.Equal(state.NationById("north")!.LeaderName, north.LeaderName); // Done-when 7: unchanged, not renamed.
    }

    [Fact]
    public void RunTurn_HumanNationNotInDebt_IsUntouched()
    {
        var state = EconomyTestbed.InitialState();
        var before = state.NationById("north")!;

        var coordinator = EconomyTestbed.CoordinatorOnly(sink: null, typeof(HumanDepositionSystem));
        var after = coordinator.RunTurn(state).State;

        Assert.Equal(before, after.NationById("north"));
        Assert.Equal(SeatControl.Human, after.NationById("north")!.Control);
    }

    [Fact]
    public void RunTurn_TheActiveSeatIsAiControlled_IsNeverConsideredHere_EvenDeeplyInDebt()
    {
        var state = EconomyTestbed.InitialState() with { ActiveSeatIndex = 1 }; // "south", the AI seat.
        Assert.Equal("south", state.ActiveNationId);

        var nations = state.Nations.Select(n => n.Id == "south" ? n with { Treasury = -50_000, Unity = 100 } : n);
        state = state with { Nations = ValueList.From(nations) };
        var before = state.NationById("south")!;

        var coordinator = EconomyTestbed.CoordinatorOnly(sink: null, typeof(HumanDepositionSystem));
        var after = coordinator.RunTurn(state).State;

        Assert.Equal(before, after.NationById("south")); // AiDepositionHandler's concern, not this one's.
    }

    [Fact]
    public void RunTurn_ResetsOnlyTheDeposedNationsCloseCooldowns()
    {
        var state = EconomyTestbed.InitialState();
        state = state with { Relations = state.Relations.WithRelation("north", "south", -2) };
        var nations = state.Nations.Select(n => n.Id == "north" ? n with { Treasury = -50_000, Unity = 300 } : n);
        state = state with { Nations = ValueList.From(nations) };

        var coordinator = EconomyTestbed.CoordinatorOnly(sink: null, typeof(HumanDepositionSystem));
        var after = coordinator.RunTurn(state).State;

        Assert.Equal(0, after.Relations.Get("north", "south"));
    }
}
