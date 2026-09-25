using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19: <c>ProposeAllianceCommand</c>'s confirmed legality —
/// <c>TPolitics_MakeAlliance</c>: refused against an AI target if the <em>proposer's own side</em>
/// (itself, or a nation it is allied with) is at war with anyone, or the relation is negative; the AI
/// target's own wars are never checked (T82 rework round 1, B2 corrects an earlier "either side"
/// reading — <c>decompiled-ai-offers-to-human-seats.md</c> §3). Always accepted from a human target.
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

    /// <summary>
    /// Rework round 1, B2: <c>decompiled-ai-offers-to-human-seats.md</c> §3 is explicit that the AI
    /// target's own wars are <em>not</em> checked -- "The AI target's own wars are not checked" (listing
    /// 0x00453101-0x0045317A). This is the reconciliation the review asked for: before the fix, this
    /// exact scenario was refused (<see cref="ProposeAllianceRejections.SideAtWar"/>), which disagreed
    /// with <c>AcceptPendingOfferCommand</c>'s own accept path for the identical AI-at-war scenario.
    /// </summary>
    [Fact]
    public void AcceptedWhenOnlyTheTargetIsAtWarWithAnyone_AgainstAnAiTarget()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target"),
            DiplomacyTestbed.Nation(ThirdParty, "ThirdParty"));
        state = state with
        {
            Relations = state.Relations.WithRelation(Target, ThirdParty, ruleset.Diplomacy.StateCodes.War),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeAllianceCommand(Proposer, Target));

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.StateCodes.Alliance, result.State.Relations.Get(Proposer, Target));
    }

    /// <summary>
    /// Rework round 1, B2: the second of §3's human-to-AI refusal conditions, "any nation the working row
    /// marks allied is at war with anyone" (<c>FUN_00449CD8</c>) -- the proposer is not itself at war, but
    /// an existing ally of the proposer is.
    /// </summary>
    [Fact]
    public void RefusedWhenAnAllyOfTheProposerIsAtWarWithAnyone_AgainstAnAiTarget()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target"),
            DiplomacyTestbed.Nation("ally", "Ally"),
            DiplomacyTestbed.Nation(ThirdParty, "ThirdParty"));
        state = state with
        {
            Relations = state.Relations
                .WithRelation(Proposer, "ally", ruleset.Diplomacy.StateCodes.Alliance)
                .WithRelation("ally", ThirdParty, ruleset.Diplomacy.StateCodes.War),
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

    /// <summary>Bug #199 (T69), Done-when 2: rejected before any state change, with the one shared code.</summary>
    [Fact]
    public void RefusedWhenTheTargetIsEliminated()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target", eliminated: true));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeAllianceCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(IC2.Engine.Diplomacy.DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }
}
