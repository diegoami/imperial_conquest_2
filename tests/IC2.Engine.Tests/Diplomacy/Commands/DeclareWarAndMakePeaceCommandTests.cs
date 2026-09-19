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
}
