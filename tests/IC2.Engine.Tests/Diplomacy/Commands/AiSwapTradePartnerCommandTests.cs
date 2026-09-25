using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// T82 (#359, bug #357): <c>AiSwapTradePartnerCommand</c> -- <c>FUN_0044FB7C</c> §1a's closing loop,
/// "swap a poorer partner for a richer one".
/// </summary>
public sealed class AiSwapTradePartnerCommandTests
{
    private const string Me = "me";
    private const string Poorer = "poorer";
    private const string Richer = "richer";

    private static GameState BaseState(int poorerTaxBase = 100, int richerTaxBase = 500)
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"),
            DiplomacyTestbed.Nation(Poorer, "Poorer", taxBase: poorerTaxBase),
            DiplomacyTestbed.Nation(Richer, "Richer", taxBase: richerTaxBase));

        return state with { Relations = state.Relations.WithRelation(Me, Poorer, ruleset.Diplomacy.StateCodes.Trade) };
    }

    [Fact]
    public void BreaksThePoorerPartnerToACooldown_AndTradesWithTheRicherOne()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = BaseState();

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiSwapTradePartnerCommand(Me, Poorer, Richer));

        Assert.True(result.IsAccepted);
        Assert.Equal(ruleset.Diplomacy.CooldownAfterBrokenTrade, result.State.Relations.Get(Me, Poorer));
        Assert.Equal(ruleset.Diplomacy.StateCodes.Trade, result.State.Relations.Get(Me, Richer));
    }

    [Fact]
    public void RefusedWhenTheRicherCandidateIsNotActuallyRicher()
    {
        var state = BaseState(poorerTaxBase: 300, richerTaxBase: 300);

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiSwapTradePartnerCommand(Me, Poorer, Richer));

        Assert.True(result.IsRejected);
        Assert.Equal(AiSwapTradePartnerRejections.CandidateNotRicher, result.Code);
    }

    [Fact]
    public void RefusedWhenThePoorerNationIsNotACurrentTradePartner()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"),
            DiplomacyTestbed.Nation(Poorer, "Poorer", taxBase: 100),
            DiplomacyTestbed.Nation(Richer, "Richer", taxBase: 500));
        // No trade relation set up between Me and Poorer.

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiSwapTradePartnerCommand(Me, Poorer, Richer));

        Assert.True(result.IsRejected);
        Assert.Equal(AiSwapTradePartnerRejections.NotCurrentlyTrading, result.Code);
    }

    [Fact]
    public void RefusedWhenTheRicherCandidateIsNotAtPeace()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = BaseState();
        state = state with { Relations = state.Relations.WithRelation(Me, Richer, ruleset.Diplomacy.StateCodes.Alliance) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiSwapTradePartnerCommand(Me, Poorer, Richer));

        Assert.True(result.IsRejected);
        Assert.Equal(AiSwapTradePartnerRejections.CandidateNotAtPeace, result.Code);
    }

    [Fact]
    public void RefusedWhenTheRicherCandidateIsHuman()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me"),
            DiplomacyTestbed.Nation(Poorer, "Poorer", taxBase: 100),
            DiplomacyTestbed.Nation(Richer, "Richer", taxBase: 500, control: SeatControl.Human));
        state = state with { Relations = state.Relations.WithRelation(Me, Poorer, ruleset.Diplomacy.StateCodes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiSwapTradePartnerCommand(Me, Poorer, Richer));

        Assert.True(result.IsRejected);
        Assert.Equal(AiSwapTradePartnerRejections.PartnerNotAi, result.Code);
    }

    [Fact]
    public void RefusedWhenTheThreeIdsAreNotDistinct()
    {
        var state = BaseState();

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiSwapTradePartnerCommand(Me, Poorer, Poorer));

        Assert.True(result.IsRejected);
        Assert.Equal(AiSwapTradePartnerRejections.NotDistinct, result.Code);
    }

    /// <summary>
    /// Rework round 1, B1: this command is the AI's own direct write, with no consent step -- a human
    /// issuer would otherwise be able to drop and re-form trade relations directly, bypassing every
    /// human-facing trade command's own gates.
    /// </summary>
    [Fact]
    public void RefusedWhenTheIssuerIsHuman()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Me, "Me", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Poorer, "Poorer", taxBase: 100),
            DiplomacyTestbed.Nation(Richer, "Richer", taxBase: 500));
        state = state with { Relations = state.Relations.WithRelation(Me, Poorer, ruleset.Diplomacy.StateCodes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AiSwapTradePartnerCommand(Me, Poorer, Richer));

        Assert.True(result.IsRejected);
        Assert.Equal(AiSwapTradePartnerRejections.IssuerNotAi, result.Code);
    }
}
