using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy.Commands;

/// <summary>
/// Bug #199 (T69), Done-when 2: <see cref="ProposeTradeCommandHandler"/> rejects an eliminated
/// counterparty with the one shared code, before any state change — checked directly here on a minimal
/// fixture; the cross-task scenario (a real elimination through <c>BesiegeCityCommand</c>) lives in
/// <c>EliminationDiplomacyTests</c>.
/// </summary>
public sealed class ProposeTradeCommandTests
{
    private const string Proposer = "proposer";
    private const string Target = "target";

    [Fact]
    public void RefusedWhenTheTargetIsEliminated()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target", eliminated: true));

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeTradeCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(DiplomacyRejections.CounterpartyEliminated, result.Code);
        Assert.Same(state, result.State);
    }

    // ---- Rework round 2, R2: TPolitics_MakeTrade's own two cap refusals, previously ignored outright ----

    /// <summary>
    /// Report §"TPolitics_MakeTrade": "the human's working row already has 3 partners" is a plain
    /// refusal -- never waived, not even by a pending offer (that exception is the target's own, see
    /// below). Before this fix, the handler called <see cref="TradePartnerCap.MakeRoomForOneMorePartner"/>
    /// unconditionally and this scenario ended with the issuer at 4 partners instead of being refused.
    /// </summary>
    [Fact]
    public void RefusedWhenTheIssuerIsAlreadyAtTheTradeCap()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var cap = ruleset.Diplomacy.MaxTradePartners;

        var nations = new List<NationState>
        {
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target"),
        };
        for (var i = 0; i < cap; i++)
        {
            nations.Add(DiplomacyTestbed.Nation($"o{i}", $"O{i}"));
        }

        var state = DiplomacyTestbed.StateOf(nations.ToArray());
        for (var i = 0; i < cap; i++)
        {
            state = state with { Relations = state.Relations.WithRelation(Proposer, $"o{i}", codes.Trade) };
        }

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeTradeCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(ProposeTradeRejections.IssuerCapReached, result.Code);

        // Refused before any state change: the issuer's own cap partners are untouched, and no trade was
        // written with the target.
        for (var i = 0; i < cap; i++)
        {
            Assert.Equal(codes.Trade, result.State.Relations.Get(Proposer, $"o{i}"));
        }

        Assert.Equal(codes.Peace, result.State.Relations.Get(Proposer, Target));
    }

    /// <summary>
    /// Report §"TPolitics_MakeTrade": "the target already has 3 partners and there is no pending trade
    /// offer from that target" is refused too. Before this fix, the handler dropped the target's poorest
    /// partner unconditionally and this scenario ended with the target at 4 partners instead of being
    /// refused.
    /// </summary>
    [Fact]
    public void RefusedWhenTheTargetIsAlreadyAtTheTradeCap_AndNoPendingOfferFromTheTarget()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var cap = ruleset.Diplomacy.MaxTradePartners;

        var nations = new List<NationState>
        {
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target", taxBase: 50),
        };
        for (var i = 0; i < cap; i++)
        {
            nations.Add(DiplomacyTestbed.Nation($"o{i}", $"O{i}"));
        }

        var state = DiplomacyTestbed.StateOf(nations.ToArray());
        for (var i = 0; i < cap; i++)
        {
            state = state with { Relations = state.Relations.WithRelation(Target, $"o{i}", codes.Trade) };
        }

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeTradeCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(ProposeTradeRejections.TargetCapReached, result.Code);

        for (var i = 0; i < cap; i++)
        {
            Assert.Equal(codes.Trade, result.State.Relations.Get(Target, $"o{i}"));
        }

        Assert.Equal(codes.Peace, result.State.Relations.Get(Proposer, Target));
    }

    /// <summary>
    /// The one exception the report names: a pending trade offer from that exact target waives its own
    /// cap refusal, and <c>TPolitics_OK</c> drops its poorest partner instead -- the same drop
    /// <see cref="AcceptPendingOfferCommandHandler"/>'s own trade branch performs for the mirrored accept
    /// path. Distinguishes the exception from a general waiver: a pending offer from a third nation, or
    /// no pending offer at all, must not trigger it (the sibling refusal test above already covers "no
    /// pending offer").
    /// </summary>
    [Fact]
    public void AcceptedWhenTheTargetIsAtTheTradeCap_ButAPendingOfferFromTheTargetIsShowing_DropsItsPoorestPartner()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var cap = ruleset.Diplomacy.MaxTradePartners;

        var nations = new List<NationState>
        {
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target"),
        };
        for (var i = 0; i < cap; i++)
        {
            // Strictly increasing tax bases, so "o0" is unambiguously the target's poorest partner.
            nations.Add(DiplomacyTestbed.Nation($"o{i}", $"O{i}", taxBase: 100 + i));
        }

        var state = DiplomacyTestbed.StateOf(nations.ToArray());
        for (var i = 0; i < cap; i++)
        {
            state = state with { Relations = state.Relations.WithRelation(Target, $"o{i}", codes.Trade) };
        }

        state = state with { PendingOffer = new PendingDiplomaticOffer(Target, codes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeTradeCommand(Proposer, Target));

        Assert.True(result.IsAccepted);
        Assert.Equal(codes.Trade, result.State.Relations.Get(Proposer, Target));
        Assert.Equal(ruleset.Diplomacy.CooldownAfterBrokenTrade, result.State.Relations.Get(Target, "o0"));
        for (var i = 1; i < cap; i++)
        {
            Assert.Equal(codes.Trade, result.State.Relations.Get(Target, $"o{i}"));
        }
    }

    /// <summary>A pending offer from a third nation is not "from that target" and must not waive the cap.</summary>
    [Fact]
    public void RefusedWhenTheTargetIsAtTheTradeCap_AndThePendingOfferIsFromSomeoneElse()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var cap = ruleset.Diplomacy.MaxTradePartners;

        var nations = new List<NationState>
        {
            DiplomacyTestbed.Nation(Proposer, "Proposer"),
            DiplomacyTestbed.Nation(Target, "Target"),
            DiplomacyTestbed.Nation("someone-else", "SomeoneElse"),
        };
        for (var i = 0; i < cap; i++)
        {
            nations.Add(DiplomacyTestbed.Nation($"o{i}", $"O{i}"));
        }

        var state = DiplomacyTestbed.StateOf(nations.ToArray());
        for (var i = 0; i < cap; i++)
        {
            state = state with { Relations = state.Relations.WithRelation(Target, $"o{i}", codes.Trade) };
        }

        state = state with { PendingOffer = new PendingDiplomaticOffer("someone-else", codes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeTradeCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(ProposeTradeRejections.TargetCapReached, result.Code);
    }
}
