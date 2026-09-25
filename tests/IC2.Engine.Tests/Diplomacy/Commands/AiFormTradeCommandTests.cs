using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// T82 (#359, bug #357): <c>AiFormTradeCommand</c> -- the AI's own direct trade write
/// (<c>FUN_0044FB7C</c> §1a), distinct from <see cref="ProposeTradeCommand"/> (the human
/// Politics-screen path, which makes room by dropping a poorer partner instead of refusing at the cap).
/// </summary>
public sealed class AiFormTradeCommandTests
{
    private const string Me = "me";
    private const string Partner = "partner";

    [Fact]
    public void AcceptedBetweenTwoAiNations_AtPeace()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"), DiplomacyTestbed.Nation(Partner, "Partner"));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormTradeCommand(Me, Partner));

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.StateCodes.Trade, result.State.Relations.Get(Me, Partner));
    }

    [Fact]
    public void RefusedWhenThePartnerIsHuman()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"),
            DiplomacyTestbed.Nation(Partner, "Partner", control: SeatControl.Human));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormTradeCommand(Me, Partner));

        Assert.True(result.IsRejected);
        Assert.Equal(AiFormTradeRejections.PartnerNotAi, result.Code);
    }

    [Fact]
    public void RefusedAgainstAnEliminatedPartner()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"), DiplomacyTestbed.Nation(Partner, "Partner", eliminated: true));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormTradeCommand(Me, Partner));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
    }

    [Fact]
    public void RefusedAgainstItself()
    {
        var state = DiplomacyTestbed.StateOf(DiplomacyTestbed.Nation(Me, "Me"));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormTradeCommand(Me, Me));

        Assert.True(result.IsRejected);
        Assert.Equal(AiFormTradeRejections.SelfTarget, result.Code);
    }

    [Fact]
    public void RefusedWhenNotAtPeace()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"), DiplomacyTestbed.Nation(Partner, "Partner"));
        state = state with { Relations = state.Relations.WithRelation(Me, Partner, ruleset.Diplomacy.StateCodes.Alliance) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormTradeCommand(Me, Partner));

        Assert.True(result.IsRejected);
        Assert.Equal(AiFormTradeRejections.NotAtPeace, result.Code);
    }

    /// <summary>
    /// The AI-to-AI rule skips a partner already at the cap -- it never drops an existing partner the
    /// way <see cref="ProposeTradeCommand"/>'s human-facing handler does.
    /// </summary>
    [Fact]
    public void RefusedWhenTheIssuerIsAlreadyAtTheTradeCap()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var cap = ruleset.Diplomacy.MaxTradePartners;
        var nations = new List<NationState> { DiplomacyTestbed.Nation(Me, "Me"), DiplomacyTestbed.Nation(Partner, "Partner") };
        for (var i = 0; i < cap; i++)
        {
            nations.Add(DiplomacyTestbed.Nation($"o{i}", $"O{i}"));
        }

        var state = DiplomacyTestbed.StateOf(nations.ToArray());
        for (var i = 0; i < cap; i++)
        {
            state = state with { Relations = state.Relations.WithRelation(Me, $"o{i}", ruleset.Diplomacy.StateCodes.Trade) };
        }

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormTradeCommand(Me, Partner));

        Assert.True(result.IsRejected);
        Assert.Equal(AiFormTradeRejections.PartnerCapReached, result.Code);
    }

    /// <summary>
    /// Rework round 1, B1: this command is the AI's own direct write, with no consent step -- a human
    /// issuer would otherwise write trade directly, skipping <see cref="ProposeTradeCommand"/>'s own
    /// gates entirely.
    /// </summary>
    [Fact]
    public void RefusedWhenTheIssuerIsHuman()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Partner, "Partner"));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiFormTradeCommand(Me, Partner));

        Assert.True(result.IsRejected);
        Assert.Equal(AiFormTradeRejections.IssuerNotAi, result.Code);
    }
}
