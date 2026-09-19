using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 3: the 3-trade-partner cap drops the weakest existing partner
/// (lowest <see cref="NationState.TaxBase"/>), and trade is refused during a cooldown or while allied or
/// at war.
/// </summary>
public sealed class TradePartnerCapTests
{
    private const string Hub = "hub";
    private const string Weak = "weak";
    private const string Medium = "medium";
    private const string Strong = "strong";
    private const string NewPartner = "new-partner";

    private static GameState FourPartnerCandidateState()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Hub, "Hub"),
            DiplomacyTestbed.Nation(Weak, "Weak", taxBase: 100),
            DiplomacyTestbed.Nation(Medium, "Medium", taxBase: 500),
            DiplomacyTestbed.Nation(Strong, "Strong", taxBase: 900),
            DiplomacyTestbed.Nation(NewPartner, "NewPartner", taxBase: 1000));

        var codes = DiplomacyTestbed.Ruleset.Diplomacy.StateCodes;
        var relations = state.Relations
            .WithRelation(Hub, Weak, codes.Trade)
            .WithRelation(Hub, Medium, codes.Trade)
            .WithRelation(Hub, Strong, codes.Trade);

        return state with { Relations = relations };
    }

    /// <summary>
    /// DoD 3: opening a fourth trade partner drops the existing one with the lowest tax base — here,
    /// <c>Weak</c> (100), not <c>Medium</c> (500) or <c>Strong</c> (900).
    /// </summary>
    [Fact]
    public void DoD03_OpeningAFourthPartner_DropsTheLowestTaxBasePartner()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = TradePartnerCap.MakeRoomForOneMorePartner(FourPartnerCandidateState(), ruleset, Hub, NewPartner);

        Assert.Equal(ruleset.Diplomacy.CooldownAfterBrokenTrade, state.Relations.Get(Hub, Weak));

        // The other two partners survive record-identical -- the two-entity probe (build-process.md §4.2):
        // a single-partner fixture would not catch an over-broad drop that clears every partner.
        Assert.Equal(codes.Trade, state.Relations.Get(Hub, Medium));
        Assert.Equal(codes.Trade, state.Relations.Get(Hub, Strong));
    }

    /// <summary>Mutation proof: swapping the comparison direction would drop the strongest, not the weakest.</summary>
    [Fact]
    public void DoD03_MutationProof_TheDroppedPartnerIsSpecificallyTheWeakest_NotJustAny()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = TradePartnerCap.MakeRoomForOneMorePartner(FourPartnerCandidateState(), ruleset, Hub, NewPartner);

        Assert.NotEqual(codes.Trade, state.Relations.Get(Hub, Weak));
        Assert.Equal(codes.Trade, state.Relations.Get(Hub, Medium));
        Assert.Equal(codes.Trade, state.Relations.Get(Hub, Strong));
    }

    /// <summary>Below the cap, opening one more partner is a no-op on the existing three... two... one.</summary>
    [Fact]
    public void DoD03_BelowTheCap_NoPartnerIsDropped()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Hub, "Hub"),
            DiplomacyTestbed.Nation(Weak, "Weak", taxBase: 100),
            DiplomacyTestbed.Nation(NewPartner, "NewPartner", taxBase: 1000));
        state = state with { Relations = state.Relations.WithRelation(Hub, Weak, codes.Trade) };

        state = TradePartnerCap.MakeRoomForOneMorePartner(state, ruleset, Hub, NewPartner);

        Assert.Equal(codes.Trade, state.Relations.Get(Hub, Weak));
    }

    // ---- Command-level legality (refusal during cooldown / allied / at war) ----

    [Fact]
    public void DoD03_ProposeTrade_RefusedDuringACooldown()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Hub, "Hub"), DiplomacyTestbed.Nation(NewPartner, "NewPartner"));
        state = RelationTransitions.BreakToPeace(
            state with { Relations = state.Relations.WithRelation(Hub, NewPartner, ruleset.Diplomacy.StateCodes.Trade) },
            ruleset, Hub, NewPartner);
        Assert.True(state.Relations.Get(Hub, NewPartner) < 0);

        var dispatcher = DiplomacyTestbed.Dispatcher();
        var result = dispatcher.Dispatch(state, new ProposeTradeCommand(Hub, NewPartner));

        Assert.True(result.IsRejected);
        Assert.Equal(ProposeTradeRejections.Cooldown, result.Code);
        Assert.Same(state, result.State);
    }

    [Theory]
    [InlineData("alliance")]
    [InlineData("war")]
    public void DoD03_ProposeTrade_RefusedWhileAlliedOrAtWar(string relationKind)
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Hub, "Hub"), DiplomacyTestbed.Nation(NewPartner, "NewPartner"));
        var relationCode = relationKind == "alliance" ? codes.Alliance : codes.War;
        state = state with { Relations = state.Relations.WithRelation(Hub, NewPartner, relationCode) };

        var dispatcher = DiplomacyTestbed.Dispatcher();
        var result = dispatcher.Dispatch(state, new ProposeTradeCommand(Hub, NewPartner));

        Assert.True(result.IsRejected);
        Assert.Equal(ProposeTradeRejections.AlliedOrAtWar, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void DoD03_ProposeTrade_AtPeace_IsAccepted()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Hub, "Hub"), DiplomacyTestbed.Nation(NewPartner, "NewPartner"));

        var dispatcher = DiplomacyTestbed.Dispatcher();
        var result = dispatcher.Dispatch(state, new ProposeTradeCommand(Hub, NewPartner));

        Assert.True(result.IsAccepted);
        Assert.Equal(
            DiplomacyTestbed.Ruleset.Diplomacy.StateCodes.Trade, result.State.Relations.Get(Hub, NewPartner));
    }
}
