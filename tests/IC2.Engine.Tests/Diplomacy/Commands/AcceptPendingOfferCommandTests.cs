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

    /// <summary>
    /// Rework round 1, B3: <c>decompiled-ai-offers-to-human-seats.md</c> §3 -- "the human's working row
    /// already has 3 partners" is a plain refusal, never waived by a pending offer. Before the fix, the
    /// trade branch skipped every cap and this scenario ended with 4 partners instead of being refused.
    /// </summary>
    [Fact]
    public void Trade_RefusedWhenTheHumanIsAlreadyAtTheTradeCap()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var cap = ruleset.Diplomacy.MaxTradePartners;

        var nations = new List<NationState>
        {
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
        };
        for (var i = 0; i < cap; i++)
        {
            nations.Add(DiplomacyTestbed.Nation($"o{i}", $"O{i}"));
        }

        var state = DiplomacyTestbed.StateOf(nations.ToArray());
        for (var i = 0; i < cap; i++)
        {
            state = state with { Relations = state.Relations.WithRelation(Human, $"o{i}", codes.Trade) };
        }

        state = state with { PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.TradeCapReached, result.Code);

        // The cap partners are untouched, and no trade was written with the proposer.
        for (var i = 0; i < cap; i++)
        {
            Assert.Equal(codes.Trade, result.State.Relations.Get(Human, $"o{i}"));
        }

        Assert.Equal(codes.Peace, result.State.Relations.Get(Human, Proposer));
    }

    /// <summary>
    /// Rework round 1, B3: accepting a pending offer FROM a proposer already at its own trade cap is
    /// exactly the "pending trade offer from that target" case §3 exempts from a flat refusal -- but the
    /// cap is not silently ignored: "on OK, if the target has 3 partners, it drops its lowest-tax-base
    /// partner to peace, a -8 cooldown" [confirmed: code]. Before the fix, the proposer ended with 4
    /// partners and nothing was dropped.
    /// </summary>
    [Fact]
    public void Trade_AcceptedWhenTheProposerIsAtTheTradeCap_DropsItsPoorestPartner()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var cap = ruleset.Diplomacy.MaxTradePartners;

        var nations = new List<NationState>
        {
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
        };
        for (var i = 0; i < cap; i++)
        {
            // Strictly increasing tax bases, so "o0" is unambiguously the proposer's poorest partner.
            nations.Add(DiplomacyTestbed.Nation($"o{i}", $"O{i}", taxBase: 100 + i));
        }

        var state = DiplomacyTestbed.StateOf(nations.ToArray());
        for (var i = 0; i < cap; i++)
        {
            state = state with { Relations = state.Relations.WithRelation(Proposer, $"o{i}", codes.Trade) };
        }

        state = state with { PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsAccepted);
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, Proposer));

        // The proposer's poorest partner (o0) was dropped to the broken-trade cooldown, not left as 4.
        Assert.Equal(ruleset.Diplomacy.CooldownAfterBrokenTrade, result.State.Relations.Get(Proposer, "o0"));
        for (var i = 1; i < cap; i++)
        {
            Assert.Equal(codes.Trade, result.State.Relations.Get(Proposer, $"o{i}"));
        }
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

    /// <summary>
    /// Rework round 1, B2: the second of §3's human-to-AI refusal conditions, "any nation the working row
    /// marks allied is at war with anyone" (<c>FUN_00449CD8</c>) -- the human is not itself at war, but an
    /// existing ally of the human is.
    /// </summary>
    [Fact]
    public void Alliance_RefusedWhenAnAllyOfTheHumanIsAtWarWithAnyone()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation("ally", "Ally"),
            DiplomacyTestbed.Nation(ThirdParty, "ThirdParty"));
        state = state with
        {
            Relations = state.Relations
                .WithRelation(Human, "ally", codes.Alliance)
                .WithRelation("ally", ThirdParty, codes.War),
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Alliance),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.SideAtWar, result.Code);
    }

    /// <summary>
    /// T82 (#359, bug #357 item 3): <c>decompiled-ai-offers-to-human-seats.md</c> §3's correction --
    /// against an AI target, <c>TPolitics_MakeAlliance</c> checks only the accepting human's own side.
    /// The proposer being at war with a third party (not the human) must <strong>not</strong> refuse the
    /// acceptance -- the human is dragged into that war through the alliance cascade instead. Restoring
    /// the old two-sided check (proposer OR issuer) would refuse this and fail the test.
    /// </summary>
    [Fact]
    public void Alliance_AcceptedWhenOnlyTheProposerIsAtWarWithAnyone()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState();
        state = state with
        {
            Relations = state.Relations.WithRelation(Proposer, ThirdParty, codes.War),
            PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Alliance),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsAccepted);
        Assert.Equal(codes.Alliance, result.State.Relations.Get(Human, Proposer));

        // The setter's cascade (RelationTransitions.FormAlliance): allying with a nation at war drags
        // the human into that war too.
        Assert.Equal(codes.War, result.State.Relations.Get(Human, ThirdParty));
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

    // ---- Bug #199 (T69), Done-when 2: rejected before any state change, with the one shared code ----

    [Fact]
    public void Trade_RefusedWhenTheProposerIsEliminated()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Proposer, "Proposer", eliminated: true));
        state = state with { PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(IC2.Engine.Diplomacy.DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Alliance_RefusedWhenTheProposerIsEliminated()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation(Proposer, "Proposer", eliminated: true));
        state = state with { PendingOffer = new PendingDiplomaticOffer(Proposer, codes.Alliance) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(IC2.Engine.Diplomacy.DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    // ---- #200 R2-F4: UnknownProposer had no test ----

    /// <summary>
    /// Defensive gate (<see cref="AcceptPendingOfferRejections.UnknownProposer"/>'s own remark: "never
    /// expected"): a pending offer whose proposer no longer resolves to any nation in the state is
    /// rejected rather than throwing.
    /// </summary>
    [Fact]
    public void RefusedWhenTheProposerIsUnknown()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = HumanState() with
        {
            PendingOffer = new PendingDiplomaticOffer("no-such-nation", codes.Trade),
        };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.UnknownProposer, result.Code);
        Assert.Same(state, result.State);
    }
}
