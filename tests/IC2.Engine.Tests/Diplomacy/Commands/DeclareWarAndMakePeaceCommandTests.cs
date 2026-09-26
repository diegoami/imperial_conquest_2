using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19: <c>DeclareWarCommand</c> ("War: set directly, no check") and
/// <c>MakePeaceCommand</c> (<c>TPolitics_MakePeace</c>: refused against an AI target currently at war;
/// always accepted from a human).
/// </summary>
public sealed class DeclareWarAndMakePeaceCommandTests
{
    private const string A = "a";
    private const string B = "b";

    [Fact]
    public void DeclareWar_SetsWarDirectly_NoLegalityCheck()
    {
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B"));

        // Even from an allied state -- "no check" means no check, not "except when allied".
        state = state with
        {
            Relations = state.Relations.WithRelation(A, B, DiplomacyTestbed.Ruleset.Diplomacy.StateCodes.Alliance),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new DeclareWarCommand(A, B));

        Assert.True(result.IsAccepted);
        Assert.Equal(DiplomacyTestbed.Ruleset.Diplomacy.StateCodes.War, result.State.Relations.Get(A, B));
    }

    [Fact]
    public void DeclareWar_UnknownTarget_IsRejected()
    {
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(A, "A"));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new DeclareWarCommand(A, "nobody"));

        Assert.True(result.IsRejected);
        Assert.Equal(DeclareWarRejections.UnknownTarget, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void MakePeace_RefusedAgainstAnAiTarget_StillAtWar()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B"));
        state = state with { Relations = state.Relations.WithRelation(A, B, ruleset.Diplomacy.StateCodes.War) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new MakePeaceCommand(A, B));

        Assert.True(result.IsRejected);
        Assert.Equal(MakePeaceRejections.Refused, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void MakePeace_AlwaysAccepted_FromAHumanTarget()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B", control: SeatControl.Human));
        state = state with { Relations = state.Relations.WithRelation(A, B, ruleset.Diplomacy.StateCodes.War) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new MakePeaceCommand(A, B));

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.CooldownAfterEndedWar, result.State.Relations.Get(A, B));
    }

    [Fact]
    public void MakePeace_WhenNotAtWar_IsRejected()
    {
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B"));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new MakePeaceCommand(A, B));

        Assert.True(result.IsRejected);
        Assert.Equal(MakePeaceRejections.NotAtWar, result.Code);
    }

    /// <summary>
    /// T88, DoD 2: "Hotseat peace between two humans is still always accepted" -- both seats human this
    /// time, not only the target, to leave no doubt this is genuine hotseat play, not merely "the target
    /// happens to be human."
    /// </summary>
    [Fact]
    public void MakePeace_AlwaysAccepted_HotseatBothHuman()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(A, "A", control: SeatControl.Human),
            DiplomacyTestbed.Nation(B, "B", control: SeatControl.Human));
        state = state with { Relations = state.Relations.WithRelation(A, B, ruleset.Diplomacy.StateCodes.War) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new MakePeaceCommand(A, B));

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.CooldownAfterEndedWar, result.State.Relations.Get(A, B));
    }

    // ---- T88, DoD 5: a human can end a trade or an alliance too, not only a war ----
    //
    // decompiled-diplomacy-peace-terms-and-instant-battles.md §2.1: TPolitics_MakePeace's refusal fires
    // only when the target is AI AND the committed relation is war; any other current relation falls
    // straight through to the working-value reset TPolitics_OK later commits through the setter. Before
    // this task MakePeaceCommandHandler rejected everything but a live war (MakePeaceRejections.NotAtWar),
    // leaving no engine path for a human to end a trade or an alliance at all.

    [Fact]
    public void MakePeace_EndsATrade_ToItsCooldown_EvenAgainstAnAi()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B"));
        state = state with { Relations = state.Relations.WithRelation(A, B, ruleset.Diplomacy.StateCodes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new MakePeaceCommand(A, B));

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.CooldownAfterBrokenTrade, result.State.Relations.Get(A, B));
    }

    [Fact]
    public void MakePeace_EndsAnAlliance_ToItsCooldown_EvenAgainstAnAi()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B"));
        state = state with { Relations = state.Relations.WithRelation(A, B, ruleset.Diplomacy.StateCodes.Alliance) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new MakePeaceCommand(A, B));

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.CooldownAfterBrokenAlliance, result.State.Relations.Get(A, B));
    }

    /// <summary>Mutation proof: an AI-target war is still refused -- widening the gate to trade/alliance did not also waive the war refusal.</summary>
    [Fact]
    public void MakePeace_MutationProof_WideningToTradeAndAlliance_DidNotWeakenTheWarRefusal()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B"));
        state = state with { Relations = state.Relations.WithRelation(A, B, ruleset.Diplomacy.StateCodes.War) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new MakePeaceCommand(A, B));

        Assert.True(result.IsRejected);
        Assert.Equal(MakePeaceRejections.Refused, result.Code);
    }

    // ---- Rework round 1, N2: DeclareWar and MakePeace also reject an eliminated counterparty ----

    /// <summary>Bug #199, N2: rejected before any state change, with the one shared code.</summary>
    [Fact]
    public void DeclareWar_RefusedWhenTheTargetIsEliminated()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B", eliminated: true));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new DeclareWarCommand(A, B));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    /// <summary>Bug #199, N2: rejected before any state change, with the one shared code.</summary>
    [Fact]
    public void MakePeace_RefusedWhenTheTargetIsEliminated()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(A, "A"), DiplomacyTestbed.Nation(B, "B", eliminated: true));
        state = state with { Relations = state.Relations.WithRelation(A, B, ruleset.Diplomacy.StateCodes.War) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new MakePeaceCommand(A, B));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }
}
