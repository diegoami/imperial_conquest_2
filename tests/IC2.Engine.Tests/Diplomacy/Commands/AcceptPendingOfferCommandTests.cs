using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// Rework round 1, B2: DoD 10 says accepting a trade offer <strong>"only waives the three-partner
/// limit"</strong>. Every other gate a fresh proposal would apply still applies to an acceptance — a
/// pending offer is rolled at turn start and can go stale within the same turn (the player declares war
/// on the proposer, for instance), so "a pending offer exists" is not by itself proof the relation it
/// proposes is still legal.
/// </summary>
public sealed class AcceptPendingOfferCommandTests
{
    private const string Human = "human-nation";
    private const string Proposer = "proposer-nation";
    private const string ThirdParty = "third-party";

    private static GameState HumanState(int? proposerRelation = null) =>
        DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(ThirdParty, "ThirdParty"));

    // ---- The exact reachable-in-one-turn scenario the review found ----

    /// <summary>
    /// The offer rolls (trade, Proposer -> Human) -> the human declares war on the proposer, in the same
    /// turn -> the human then tries to accept the now-stale offer. It must be refused, not silently
    /// overwrite War with Trade.
    /// </summary>
    [Fact]
    public void B2_AcceptingATradeOffer_AfterDeclaringWarOnTheProposer_InTheSameTurn_IsRejected()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState() with
        {
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Trade),
        };

        // The human declares war on the proposer within the same turn the offer is still showing.
        state = RelationTransitions.DeclareWar(state, ruleset, Human, Proposer);
        Assert.Equal(codes.War, state.Relations.Get(Human, Proposer));

        var dispatcher = DiplomacyTestbed.Dispatcher();
        var result = dispatcher.Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.AlliedOrAtWar, result.Code);

        // War survives, untouched -- the defect this test exists to close would have overwritten it.
        Assert.Equal(codes.War, result.State.Relations.Get(Human, Proposer));
    }

    /// <summary>The mirrored case: an alliance offer accepted after the human declares war on the proposer.</summary>
    [Fact]
    public void B2_AcceptingAnAllianceOffer_AfterDeclaringWarOnTheProposer_InTheSameTurn_IsRejected()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState() with
        {
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Alliance),
        };

        state = RelationTransitions.DeclareWar(state, ruleset, Human, Proposer);

        var dispatcher = DiplomacyTestbed.Dispatcher();
        var result = dispatcher.Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.SideAtWar, result.Code);
        Assert.Equal(codes.War, result.State.Relations.Get(Human, Proposer));
    }

    // ---- One rejection test per gate, trade ----

    [Fact]
    public void Trade_RefusedDuringACooldown()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState();
        state = state with
        {
            Relations = state.Relations.WithRelation(Human, Proposer, -4),
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Trade),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.Cooldown, result.Code);
    }

    [Fact]
    public void Trade_RefusedWhenAlreadyTrading()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState();
        state = state with
        {
            Relations = state.Relations.WithRelation(Human, Proposer, codes.Trade),
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Trade),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.AlreadyTrading, result.Code);
    }

    [Fact]
    public void Trade_RefusedWhenAlreadyAllied()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState();
        state = state with
        {
            Relations = state.Relations.WithRelation(Human, Proposer, codes.Alliance),
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Trade),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.AlliedOrAtWar, result.Code);
    }

    // ---- One rejection test per gate, alliance ----

    [Fact]
    public void Alliance_RefusedDuringACooldown()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState();
        state = state with
        {
            Relations = state.Relations.WithRelation(Human, Proposer, -4),
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Alliance),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.Cooldown, result.Code);
    }

    [Fact]
    public void Alliance_RefusedWhenAlreadyAllied()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState();
        state = state with
        {
            Relations = state.Relations.WithRelation(Human, Proposer, codes.Alliance),
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Alliance),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.AlreadyAllied, result.Code);
    }

    [Fact]
    public void Alliance_RefusedWhenTheIssuerIsAtWarWithAnyone()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState();
        state = state with
        {
            Relations = state.Relations.WithRelation(Human, ThirdParty, codes.War),
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Alliance),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.SideAtWar, result.Code);
    }

    // ---- Still accepted at peace ----

    [Fact]
    public void Trade_AtPeace_IsAccepted()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState() with { PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsAccepted);
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, Proposer));
    }

    [Fact]
    public void Alliance_AtPeace_IsAccepted()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState() with { PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Alliance) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsAccepted);
        Assert.Equal(codes.Alliance, result.State.Relations.Get(Human, Proposer));
    }
}
