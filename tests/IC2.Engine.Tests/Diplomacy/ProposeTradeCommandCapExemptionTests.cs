using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// T85 Done-when 6, NB1 (#392, T82's follow-up): the pending-trade-offer exception in
/// <see cref="ProposeTradeCommandHandler"/> is narrow in two ways that
/// <c>tests/IC2.Engine.Tests/Diplomacy/Commands/ProposeTradeCommandTests.cs</c> does not yet cover --
/// it exempts only the <em>target's</em> own cap, never the issuer's (the issuer's cap check runs
/// first, unconditionally, before the pending-offer check even exists); and only a pending
/// <em>trade</em> offer from that exact target counts, never a pending alliance offer from the same
/// nation. Both are already true of the handler's own code
/// (<see cref="ProposeTradeCommand"/>'s own remarks, rework round 2, R2) -- these tests are new
/// coverage for behaviour that already exists, not a behaviour change.
/// </summary>
public sealed class ProposeTradeCommandCapExemptionTests
{
    private const string Proposer = "proposer";
    private const string Target = "target";

    /// <summary>
    /// A pending trade offer from the target exempts only the target's own cap
    /// (<see cref="ProposeTradeRejections.TargetCapReached"/>'s own waiver) -- the issuer's cap check
    /// (<see cref="ProposeTradeRejections.IssuerCapReached"/>) runs first and unconditionally, with no
    /// pending-offer gate at all, so the issuer is refused even though a pending trade offer from the
    /// target is showing. Fails if the issuer's own cap check is ever made conditional on
    /// <c>hasPendingTradeOfferFromTarget</c> the way the target's is.
    /// </summary>
    [Fact]
    public void APendingTradeOfferFromTheTargetDoesNotExemptTheIssuersOwnCap()
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
        // The issuer (not the target) is at its own trade-partner cap.
        for (var i = 0; i < cap; i++)
        {
            state = state with { Relations = state.Relations.WithRelation(Proposer, $"o{i}", codes.Trade) };
        }

        // A genuine pending trade offer from the target -- exactly the shape that waives the target's
        // own cap in the sibling accepted-path test -- showing here only to prove it does NOT also
        // waive the issuer's.
        state = state with { PendingOffer = new PendingDiplomaticOffer(Target, codes.Trade) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeTradeCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(ProposeTradeRejections.IssuerCapReached, result.Code);

        // Refused before any state change: the issuer's own cap partners are untouched, and no trade
        // was written with the target.
        for (var i = 0; i < cap; i++)
        {
            Assert.Equal(codes.Trade, result.State.Relations.Get(Proposer, $"o{i}"));
        }

        Assert.Equal(codes.Peace, result.State.Relations.Get(Proposer, Target));
    }

    /// <summary>
    /// Only a pending <em>trade</em> offer from the target waives the target's own cap -- a pending
    /// <em>alliance</em> offer from that same target does not. Fails if the
    /// <c>offer.ProposedRelationCode == codes.Trade</c> half of <c>hasPendingTradeOfferFromTarget</c>'s
    /// condition is ever dropped (leaving only the "from that exact target" half), which would let an
    /// alliance offer waive a trade cap it has nothing to do with.
    /// </summary>
    [Fact]
    public void APendingAllianceOfferFromTheTargetDoesNotExemptTheTargetsTradeCap()
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
            state = state with { Relations = state.Relations.WithRelation(Target, $"o{i}", codes.Trade) };
        }

        // A pending offer genuinely from the target -- so "from that exact target" alone would pass --
        // but proposing alliance, not trade.
        state = state with { PendingOffer = new PendingDiplomaticOffer(Target, codes.Alliance) };

        var result = DiplomacyTestbed.Dispatcher().Dispatch(state, new ProposeTradeCommand(Proposer, Target));

        Assert.True(result.IsRejected);
        Assert.Equal(ProposeTradeRejections.TargetCapReached, result.Code);

        for (var i = 0; i < cap; i++)
        {
            Assert.Equal(codes.Trade, result.State.Relations.Get(Target, $"o{i}"));
        }

        Assert.Equal(codes.Peace, result.State.Relations.Get(Proposer, Target));
    }
}
