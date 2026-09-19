using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 10: the pending offer is state, cleared and re-rolled at every
/// human turn start, accepting waives the 3-partner cap, and the announcement is a presentation event
/// with no news line.
/// </summary>
public sealed class PendingOfferSystemTests
{
    private const string Human = "human-nation";
    private const string CandidateAi = "candidate-ai";
    private const string OtherAi = "other-ai";

    private static GameState HumanTurnState(bool candidateEliminated = false) =>
        DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation(CandidateAi, "CandidateAi", eliminated: candidateEliminated),
            DiplomacyTestbed.Nation(OtherAi, "OtherAi"));

    /// <summary>An existing pending offer is cleared at the human seat's turn start, whatever it was.</summary>
    [Fact]
    public void DoD10_AnExistingOffer_IsClearedAtHumanTurnStart()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState() with
        {
            PendingOffer = new PendingDiplomaticOffer(OtherAi, ruleset.Diplomacy.StateCodes.Trade),
        };

        var (after, _) = PendingOfferSystem.Apply(state, ruleset, DiplomacyTestbed.Rng());

        // Either cleared outright, or replaced by a freshly rolled one -- never the SAME stale offer this
        // run started with (the whole point of "re-rolled", not "kept").
        if (after.PendingOffer is not null)
        {
            Assert.NotEqual(state.PendingOffer, after.PendingOffer);
        }
    }

    /// <summary>An AI seat's turn touches the pending offer not at all.</summary>
    [Fact]
    public void DoD10_AnAiSeatsTurn_LeavesThePendingOfferUntouched()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var existing = new PendingDiplomaticOffer(Human, ruleset.Diplomacy.StateCodes.Trade);
        var state = HumanTurnState() with
        {
            ActiveSeatIndex = 1, // CandidateAi's turn, not the human's
            PendingOffer = existing,
        };

        var (after, announcement) = PendingOfferSystem.Apply(state, ruleset, DiplomacyTestbed.Rng());

        Assert.Equal(existing, after.PendingOffer);
        Assert.Null(announcement);
    }

    /// <summary>
    /// A candidate on a cooldown, or already eliminated, never becomes an offer, over many seeds (the
    /// relation/alive gates are checked before the chance roll, per the source's own short-circuit order).
    /// </summary>
    [Fact]
    public void DoD10_AnEliminatedCandidate_NeverBecomesAnOffer()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState(candidateEliminated: true);

        for (ulong seed = 1; seed < 500; seed++)
        {
            var (after, _) = PendingOfferSystem.Apply(state, ruleset, new SplitMix64Rng(seed));
            if (after.PendingOffer is { } offer)
            {
                Assert.NotEqual(CandidateAi, offer.ProposingNationId);
            }
        }
    }

    /// <summary>
    /// A candidate on a cooldown never becomes an offer (the confirmed gate: <c>relation[human][r] == 0</c>).
    /// </summary>
    [Fact]
    public void DoD10_ACandidateOnACooldown_NeverBecomesAnOffer()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState();
        state = state with { Relations = state.Relations.WithRelation(Human, CandidateAi, -4) };

        for (ulong seed = 1; seed < 500; seed++)
        {
            var (after, _) = PendingOfferSystem.Apply(state, ruleset, new SplitMix64Rng(seed));
            if (after.PendingOffer is { } offer)
            {
                Assert.NotEqual(CandidateAi, offer.ProposingNationId);
            }
        }
    }

    /// <summary>
    /// When every gate passes, the offer is the confirmed relation code for trade, and its dialog text is
    /// the exact period-terminated literal from Q5, not the corpus's own paraphrase.
    /// </summary>
    [Fact]
    public void DoD10_WhenRolled_TheOfferIsTrade_WithTheExactDialogText()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState();

        PendingDiplomaticOfferAnnounced? found = null;
        for (ulong seed = 1; seed < 2000 && found is null; seed++)
        {
            var (_, announcement) = PendingOfferSystem.Apply(state, ruleset, new SplitMix64Rng(seed));
            found = announcement;
        }

        Assert.NotNull(found);
        Assert.Equal(ruleset.Diplomacy.StateCodes.Trade, found!.ProposedRelationCode);
        Assert.Equal($"{found.ProposingNationName} wants to trade with {found.TargetNationName}.", found.DialogText);
    }

    /// <summary>DoD 10: the offer is a presentation event, never a news line.</summary>
    [Fact]
    public void DoD10_TheAnnouncement_WritesNoNewsLine()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState();

        GameState after = state;
        for (ulong seed = 1; seed < 2000; seed++)
        {
            var (result, announcement) = PendingOfferSystem.Apply(state, ruleset, new SplitMix64Rng(seed));
            if (announcement is not null)
            {
                after = result;
                break;
            }
        }

        Assert.Empty(after.NewsLog.Slots);
        Assert.Equal(-1, after.NewsLog.MostRecentSlot);
    }

    /// <summary>DoD 10: accepting a trade offer waives the 3-partner cap.</summary>
    [Fact]
    public void DoD10_AcceptingATradeOffer_WaivesTheThreePartnerCap()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;

        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation("p1", "P1", taxBase: 100),
            DiplomacyTestbed.Nation("p2", "P2", taxBase: 200),
            DiplomacyTestbed.Nation("p3", "P3", taxBase: 300),
            DiplomacyTestbed.Nation(CandidateAi, "CandidateAi", taxBase: 50));

        var relations = state.Relations
            .WithRelation(Human, "p1", codes.Trade)
            .WithRelation(Human, "p2", codes.Trade)
            .WithRelation(Human, "p3", codes.Trade);
        state = state with
        {
            Relations = relations,
            PendingOffer = new PendingDiplomaticOffer(CandidateAi, codes.Trade),
        };

        var dispatcher = DiplomacyTestbed.Dispatcher();
        var result = dispatcher.Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsAccepted);
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, CandidateAi));

        // The waiver means none of the three existing partners was dropped, even though this is a fourth.
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, "p1"));
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, "p2"));
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, "p3"));
    }

    /// <summary>DoD 10: accepting does NOT clear the pending offer -- only the next human turn start does.</summary>
    [Fact]
    public void DoD10_Accepting_DoesNotClearThePendingOffer()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState() with
        {
            PendingOffer = new PendingDiplomaticOffer(CandidateAi, ruleset.Diplomacy.StateCodes.Trade),
        };

        var dispatcher = DiplomacyTestbed.Dispatcher();
        var result = dispatcher.Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.State.PendingOffer);
        Assert.Equal(CandidateAi, result.State.PendingOffer!.ProposingNationId);
    }

    [Fact]
    public void DoD10_AcceptingWithNoPendingOffer_IsRejected()
    {
        var state = HumanTurnState();
        var dispatcher = DiplomacyTestbed.Dispatcher();

        var result = dispatcher.Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.NoPendingOffer, result.Code);
    }
}
