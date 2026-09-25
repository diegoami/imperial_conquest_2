using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// T82 (#359, bug #357): <c>AiFormAllianceCommand</c> -- the AI's own direct alliance write
/// (<c>FUN_0044FB7C</c> §1a), distinct from <see cref="ProposeAllianceCommand"/> (the human
/// Politics-screen path). The partner-selection gates (busy, protected, neighbour, war cap, city count)
/// are <c>AiOwnDiplomacyRuleTests</c>' job; this is the handler's own, narrower gate -- the target must
/// still be a live, computer-controlled nation.
/// </summary>
public sealed class AiFormAllianceCommandTests
{
    private const string Me = "me";
    private const string Partner = "partner";

    [Fact]
    public void AcceptedBetweenTwoAiNations_AtPeace()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"), DiplomacyTestbed.Nation(Partner, "Partner"));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormAllianceCommand(Me, Partner));

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.StateCodes.Alliance, result.State.Relations.Get(Me, Partner));
    }

    /// <summary>Done-when 3's own line: "every write still requires a computer partner".</summary>
    [Fact]
    public void RefusedWhenThePartnerIsHuman()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"),
            DiplomacyTestbed.Nation(Partner, "Partner", control: SeatControl.Human));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormAllianceCommand(Me, Partner));

        Assert.True(result.IsRejected);
        Assert.Equal(AiFormAllianceRejections.PartnerNotAi, result.Code);
    }

    [Fact]
    public void RefusedAgainstAnEliminatedPartner()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"), DiplomacyTestbed.Nation(Partner, "Partner", eliminated: true));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormAllianceCommand(Me, Partner));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
    }

    [Fact]
    public void RefusedAgainstItself()
    {
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(Me, "Me"));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormAllianceCommand(Me, Me));

        Assert.True(result.IsRejected);
        Assert.Equal(AiFormAllianceRejections.SelfTarget, result.Code);
    }

    /// <summary>
    /// No consent step: unlike <see cref="ProposeAllianceCommand"/>'s AI-target gates, an existing war
    /// on either side does not refuse this -- the partner search that proposes it
    /// (<see cref="Ai.AiOwnDiplomacyRule.FindAlliancePartner"/>) is what selects a partner already at
    /// war with a shared neighbour in the first place.
    /// </summary>
    [Fact]
    public void AcceptedEvenWhileTheIssuerIsAtWarWithAThirdNation()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"),
            DiplomacyTestbed.Nation(Partner, "Partner"),
            DiplomacyTestbed.Nation("third", "Third"));
        state = state with { Relations = state.Relations.WithRelation(Me, "third", ruleset.Diplomacy.StateCodes.War) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormAllianceCommand(Me, Partner));

        Assert.True(result.IsAccepted);
    }
}
