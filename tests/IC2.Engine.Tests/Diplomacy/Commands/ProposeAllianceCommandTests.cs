using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19: <c>ProposeAllianceCommand</c>'s confirmed legality —
/// <c>TPolitics_MakeAlliance</c>: refused against an AI target if either side is at war with anyone, or
/// the relation is negative; always accepted from a human target.
/// </summary>
public sealed class ProposeAllianceCommandTests
{
    private const string Proposer = "proposer";
    private const string Target = "target";
    private const string ThirdParty = "third-party";

    [Fact]
    public void RefusedDuringACooldown_AgainstAnAiTarget()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Proposer, "Proposer"), DiplomacyTestbed.Nation(Target, "Target"));
        state = state with { Relations = state.Relations.WithRelation(Proposer, Target, -5) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeAllianceCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(ProposeAllianceRejections.Cooldown, result.Code);
    }

    [Fact]
    public void RefusedWhenTheProposerIsAtWarWithAnyone_AgainstAnAiTarget()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target"),
            DiplomacyTestbed.Nation(ThirdParty, "ThirdParty"));
        state = state with
        {
            Relations = state.Relations.WithRelation(Proposer, ThirdParty, ruleset.Diplomacy.StateCodes.War),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeAllianceCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(ProposeAllianceRejections.SideAtWar, result.Code);
    }

    [Fact]
    public void AlwaysAccepted_FromAHumanTarget_EvenWhileTheProposerIsAtWar()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target", control: SeatControl.Human),
            DiplomacyTestbed.Nation(ThirdParty, "ThirdParty"));
        state = state with
        {
            Relations = state.Relations.WithRelation(Proposer, ThirdParty, ruleset.Diplomacy.StateCodes.War),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeAllianceCommand(Proposer, Target));

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.StateCodes.Alliance, result.State.Relations.Get(Proposer, Target));
    }

    [Fact]
    public void Accepted_AtPeace_FormsTheAlliance()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Proposer, "Proposer"), DiplomacyTestbed.Nation(Target, "Target"));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeAllianceCommand(Proposer, Target));

        Assert.True(result.IsAccepted);
        Assert.Equal(DiplomacyTestbed.Ruleset.Diplomacy.StateCodes.Alliance, result.State.Relations.Get(Proposer, Target));
    }
}
